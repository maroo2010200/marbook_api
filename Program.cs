using MarbookApi;
using MarbookApi.Data;
using MarbookApi.Exceptions;
using MarbookApi.Hubs;
using MarbookApi.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Reflection.Metadata;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection("Jwt")
);

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,

        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,

        ValidateLifetime = true,

        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token below."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend",
        policy =>
        {
            policy.WithOrigins("http://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        });
});

// Rate Limiting Configuration (.NET 10 Built-in)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/problem+json";

        var retryAfter = 60;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterSpan))
        {
            retryAfter = (int)Math.Ceiling(retryAfterSpan.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = StatusCodes.Status429TooManyRequests,
            title = "Too Many Requests",
            detail = $"Rate limit exceeded. Please try again after {retryAfter} seconds.",
            retryAfterSeconds = retryAfter
        }, cancellationToken: token);
    };

    // Policy 1: Auth endpoints - 10 requests / 1 min per IP (Brute-force & email quota protection)
    options.AddPolicy("auth-policy", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString()
            ?? httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? "unknown_ip";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Policy 2: Chat messaging - Token Bucket per User ID (Allows bursts up to 15, replenishes 1 token every 2s)
    options.AddPolicy("chat-policy", httpContext =>
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetTokenBucketLimiter(userId, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 15,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = TimeSpan.FromSeconds(2),
            AutoReplenishment = true,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Policy 3: Post creation - Sliding Window per User ID (Max 5 posts / 1 min)
    options.AddPolicy("post-policy", httpContext =>
    {
        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(userId, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 3,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IEmailVerificationService, EmailVerificationService>();

// Bind Cloudinary settings
builder.Services.Configure<CloudinarySettings>(
    builder.Configuration.GetSection("Cloudinary"));

var cloudinaryConfig = builder.Configuration.GetSection("Cloudinary");
if (!string.IsNullOrEmpty(cloudinaryConfig["CloudName"]))
{
    builder.Services.AddScoped<IFileStorageService, CloudinaryFileStorageService>();
}
else
{
    builder.Services.AddScoped<IFileStorageService, LocalFileStorageService>();
}

builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHttpClient<IBrevoEmailSender, BrevoEmailSender>(client =>
{
    client.BaseAddress = new Uri("https://api.brevo.com/");
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseExceptionHandler();

app.UseCors("AllowFrontend");

app.UseStaticFiles();

app.UseAuthentication();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
