# Marbook Backend — Architecture, Design Decisions & Interview Guide

This guide provides an in-depth breakdown of the backend architecture of **Marbook API** (a Facebook-like social media REST API built with .NET 10, ASP.NET Core, PostgreSQL, and Entity Framework Core). It explains why each technology and architectural pattern was chosen, the exact problems solved, and how to discuss these implementations confidently in engineering interviews.

---

## Table of Contents
1. [Core Architecture & Tech Stack Rationale](#1-core-architecture--tech-stack-rationale)
2. [Data Layer & Entity Framework Core Design](#2-data-layer--entity-framework-core-design)
3. [Authentication, Password Security & JWT Architecture](#3-authentication-password-security--jwt-architecture)
4. [Email Verification System & Encoding Gotchas](#4-email-verification-system--encoding-gotchas)
5. [Feed, Audience Privacy & Post Visibility Engine](#5-feed-audience-privacy--post-visibility-engine)
6. [Hierarchical Nested Comments & Eliminating N+1 Queries](#6-hierarchical-nested-comments--eliminating-n1-queries)
7. [Media Storage Pipeline & File Upload Security](#7-media-storage-pipeline--file-upload-security)
8. [Global Exception Handling & API Consistency](#8-global-exception-handling--api-consistency)
9. [Dual-Engine Search: Full-Text Search (FTS) for Posts & Trigram Similarity (pg_trgm) for Users](#9-dual-engine-search-full-text-search-fts-for-posts--trigram-similarity-pg_trgm-for-users)
10. [Top Backend Interview Questions & Deep-Dive Answers](#10-top-backend-interview-questions--deep-dive-answers)
11. [Real-Time Direct Messaging (Chat) & SignalR Architecture](#11-real-time-direct-messaging-chat--signalr-architecture)
12. [Real-Time In-App Notifications & Event-Driven Architecture](#12-real-time-in-app-notifications--event-driven-architecture)
13. [Account Security, Credential Rotation & Cascade Account Deletion Engine](#13-account-security-credential-rotation--cascade-account-deletion-engine)

---

## 1. Core Architecture & Tech Stack Rationale

### The Stack
- **Framework**: ASP.NET Core Web API (.NET 10, C# 13)
- **Database**: PostgreSQL (v16+)
- **ORM**: Entity Framework Core 10 (`Npgsql.EntityFrameworkCore.PostgreSQL`)
- **Authentication**: JWT Bearer Tokens (`System.IdentityModel.Tokens.Jwt`)
- **Password Hashing**: BCrypt (`BCrypt.Net-Next`)
- **Email Delivery**: Brevo (formerly Sendinblue) Transactional API (`HttpClient`)

### Why .NET 10 & C# 13?
- **High Performance**: .NET is consistently among the fastest web frameworks in the TechEmpower benchmarks, providing high-throughput asynchronous I/O (`async/await`) and low memory footprints.
- **Enterprise-Grade Dependency Injection**: Built-in IoC container (`IServiceCollection`) decouples services, repositories, and controllers.
- **Modern C# Features**: Primary constructors, pattern matching, records, and collection expressions reduce boilerplate while preserving type safety.

### Why PostgreSQL over MongoDB (NoSQL) for a Social Network?
- **Problem**: Many developers assume "social media means documents (NoSQL)". However, social platforms are fundamentally **relational**: users follow users, users like posts, posts have comments, comments have replies, and users own media.
- **Solution**: PostgreSQL provides:
  1. **Strict Relational Integrity**: Foreign keys guarantee that if a post is deleted, its likes, comments, and post images are cleanly cleaned up via cascading deletes (`ON DELETE CASCADE`), avoiding orphaned records.
  2. **ACID Transactions**: Financial-grade consistency for operations like toggling likes or managing follower graphs.
  3. **Hybrid Capabilities**: Built-in Full-Text Search (`tsvector`) and JSONB support provide the best of both relational and document databases without running separate engines.
- **Interview Takeaway**: *"We chose PostgreSQL because social media graphs are inherently relational. Foreign keys, cascade rules, and ACID guarantees ensure zero data corruption or orphaned comments/likes."*

---

## 2. Data Layer & Entity Framework Core Design

### The DTO Pattern (Data Transfer Objects)
Entity Framework models (`User`, `Post`, `Comment`) are **never** returned directly by controller actions or accepted as request parameters.

**Why?**
1. **Prevents Over-Posting / Mass Assignment Attacks**: An attacker cannot send `{ "isEmailVerified": true, "role": "Admin" }` to update fields they should not control.
2. **Breaks Serialization Cycles**: Direct EF models with bidirectional relationships (`Post.Comments` and `Comment.Post`) cause infinite JSON serialization loops.
3. **Decouples Database Schema from API Contract**: You can rename a database column without breaking mobile/frontend clients.

```csharp
// Example: Post Creation DTO
public class PostCreateDto
{
    public string? Content { get; set; }
    public PostVisibility Visibility { get; set; } = PostVisibility.Public;
    public List<string>? ImageUrls { get; set; }
}
```

### Fluent API Configuration (`AppDbContext.cs`)
Rather than polluting domain entities with data annotations, all database schema constraints are centralized in `OnModelCreating`:
```csharp
modelBuilder.Entity<Post>(entity =>
{
    entity.HasKey(p => p.Id);
    entity.Property(p => p.Content).HasMaxLength(5000);
    entity.Property(p => p.Visibility).HasDefaultValue(PostVisibility.Public);
    entity.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");

    entity.HasOne(p => p.User)
          .WithMany(u => u.Posts)
          .HasForeignKey(p => p.UserId)
          .OnDelete(DeleteBehavior.Cascade);
});
```

---

## 3. Authentication, Password Security & JWT Architecture

### Password Hashing: Why BCrypt over SHA-256?
- **Problem**: SHA-256 or MD5 are fast cryptographic hash functions. Modern GPUs can compute billions of SHA-256 hashes per second, making brute-force dictionary attacks trivial if the database is leaked.
- **Solution**: BCrypt incorporates:
  1. **Automatic Salt Generation**: Every password receives a unique 128-bit salt, neutralizing Rainbow Table attacks.
  2. **Configurable Work Factor**: BCrypt is intentionally computationally expensive and memory-hard. It takes ~100ms per verification, throttling brute-force attempts.

```csharp
// Password hashing during registration
user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password);

// Verification during login
bool isValid = BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash);
```

### JWT Architecture (`JwtService.cs`)
- When a user logs in, the server signs a token containing standard claims:
  - `ClaimTypes.NameIdentifier`: User ID (`int`)
  - `ClaimTypes.Email`: User email
  - `ClaimTypes.Name`: Display name
- The token is signed using `HmacSha256` and a 256-bit secret stored in user secrets.
- **Stateless Validation**: The server validates token signatures using ASP.NET Core Authentication Middleware without querying the database on every request.

---

## 4. Email Verification System & Encoding Gotchas

### Flow
1. User registers &rarr; A secure random token is generated and stored with an expiration timestamp (`EmailVerifications` table).
2. `IBrevoEmailSender` sends an HTML verification email with the link:
   `http://localhost:5173/verify-email?token=...&email=...`
3. The user remains unverified (`IsEmailVerified = false`) and cannot log in until verified.

### ⚠️ The Critical Base64 URL Encoding Gotcha
- **The Bug**: Tokens generated using standard Base64 encoding contain characters like `+`, `/`, and `=`. When a user clicks a link in an email, query parameters are parsed using application `x-www-form-urlencoded` rules, where `+` is interpreted as a **space (` `)**!
- As a result, the backend received `abc 123` instead of `abc+123`, causing token verification to fail.
- **The Fix**:
  ```csharp
  // When verifying in EmailVerificationService:
  token = token.Replace(" ", "+");
  // Or percent-encode with Uri.EscapeDataString() when constructing the email link
  ```
- **Interview Takeaway**: *"Base64 tokens traveling in query parameters must handle the '+' character conversion because web form-decoders decode '+' into a space."*

---

## 5. Feed, Audience Privacy & Post Visibility Engine

### Business Logic
Posts have three audience levels:
1. `Public (0)`: Visible to everyone (including unauthenticated guests).
2. `Followers (1)`: Visible to the post author and users who follow the author.
3. `Private (2)`: Visible **only** to the post author.

### Query Construction in `PostsController.cs`
Rather than fetching posts into memory and filtering with C#, audience visibility is enforced directly at the SQL level via Entity Framework expressions:

```csharp
if (currentUserId.HasValue)
{
    var followedUserIds = await _dbContext.Follows
        .Where(f => f.FollowerId == currentUserId.Value)
        .Select(f => f.FolloweeId)
        .ToListAsync();

    if (followingOnly == true)
    {
        query = query.Where(p =>
            followedUserIds.Contains(p.UserId) &&
            (p.Visibility == PostVisibility.Public || p.Visibility == PostVisibility.Followers));
    }
    else
    {
        query = query.Where(p =>
            p.UserId == currentUserId.Value
            || p.Visibility == PostVisibility.Public
            || (p.Visibility == PostVisibility.Followers && followedUserIds.Contains(p.UserId)));
    }
}
else
{
    // Guests only see Public posts
    query = query.Where(p => p.Visibility == PostVisibility.Public);
}
```

---

## 6. Hierarchical Nested Comments & Eliminating N+1 Queries

### The Database Structure
Comments support threaded replies via a self-referencing foreign key:
```csharp
public class Comment
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public int PostId { get; set; }
    public int? ParentCommentId { get; set; } // Self-referencing FK
    public int UserId { get; set; }
    
    public Comment? ParentComment { get; set; }
    public ICollection<Comment> Replies { get; set; } = new List<Comment>();
}
```

### The N+1 Problem We Solved
- **The Flawed Approach**: A recursive method was querying `_dbContext.Comments.Where(c => c.ParentCommentId == id)` inside a loop for each comment and its children. For a post with 20 comments and multiple nested replies, this executed **50+ synchronous database queries** per page load!
- **The Optimized Solution**: Single-Query In-Memory Tree Counting:
  1. Execute **one single lightweight query** to fetch only the `Id` and `ParentCommentId` of all non-root comments for the post.
  2. Build an in-memory adjacency list (`Dictionary<int, List<int>>`).
  3. Compute total descendant counts recursively in memory in $O(N)$ time with **0 additional database queries**.

```csharp
// Optimized method in CommentsController.cs
private async Task<Dictionary<int, int>> GetReplyCountsForPostAsync(int postId)
{
    // Single DB query for the entire post's comment hierarchy
    var hierarchy = await _dbContext.Comments
        .Where(c => c.PostId == postId && c.ParentCommentId != null)
        .Select(c => new { c.Id, c.ParentCommentId })
        .ToListAsync();

    if (hierarchy.Count == 0) return new Dictionary<int, int>();

    var childrenMap = hierarchy
        .GroupBy(c => c.ParentCommentId!.Value)
        .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

    int CountDescendants(int id)
    {
        if (!childrenMap.TryGetValue(id, out var childIds)) return 0;
        int count = childIds.Count;
        foreach (var childId in childIds)
        {
            count += CountDescendants(childId);
        }
        return count;
    }

    var result = new Dictionary<int, int>();
    foreach (var node in hierarchy) result[node.Id] = CountDescendants(node.Id);
    foreach (var parentId in childrenMap.Keys) result[parentId] = CountDescendants(parentId);

    return result;
}
```

---

## 7. Media Storage Pipeline & File Upload Security

### Multi-Layer Security (`LocalFileStorageService.cs`)
Allowing users to upload files to a server is one of the highest-risk attack vectors in web development (malware execution, path traversal, denial-of-service). We implemented **5 layers of defense**:

1. **File Size Capping**: Rejects any file $> 10\text{ MB}$ immediately.
2. **Extension Whitelisting**: Strict dictionary check against `.jpg`, `.jpeg`, `.png`, `.webp`, `.gif`.
3. **MIME Type Verification**: Ensures `Content-Type` matches the extension.
4. **Binary Magic Bytes (File Signatures)**:
   - *Attack*: A hacker renames `malware.exe` to `photo.jpg`. Extension and MIME checks both pass.
   - *Defense*: We inspect the first 4–12 bytes of the raw binary stream:
     - JPEG: `FF D8 FF`
     - PNG: `89 50 4E 47 0D 0A 1A 0A`
     - GIF: `47 49 46 38`
     - WebP: RIFF header (`52 49 46 46` ... `57 45 42 50`)
     If the binary signature does not match, the file is rejected.
5. **Path Traversal Protection**:
   Subfolders are sanitized using alphanumeric filters to prevent `../../` attacks. Files are assigned new GUIDs (`Guid.NewGuid():N`) so user-supplied filenames can never overwrite system files.

---

## 8. Global Exception Handling & API Consistency

### RFC 7807 ProblemDetails (`GlobalExceptionHandler.cs`)
Rather than wrapping every controller endpoint in repetitive `try/catch` blocks:
- We registered `builder.Services.AddExceptionHandler<GlobalExceptionHandler>()`.
- Unhandled exceptions are caught by the middleware, logged securely (without exposing stack traces in production), and formatted as standard RFC 7807 Problem Details JSON:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.6.1",
  "title": "An unexpected error occurred.",
  "status": 500,
  "detail": "Error message logged with trace ID"
}
```

---

## 9. Dual-Engine Search: Full-Text Search (FTS) for Posts & Trigram Similarity (pg_trgm) for Users

Social networks require two distinct search strategies because natural language content and user identities have completely different linguistic characteristics:

### 1. Post Search: PostgreSQL Full-Text Search (`tsvector` & `tsquery`)
Natural language posts consist of full sentences and paragraphs. Searching posts requires:
- **Linguistic Stemming**: Users searching for `"running"` expect to match `"run"`, `"runs"`, and `"running"`.
- **Stop-Word Removal**: Common linguistic filler words (`the`, `is`, `at`, `which`) are automatically ignored.
- **Relevance Ranking (`ts_rank`)**: Results are mathematically ordered by keyword frequency and proximity.

```csharp
// Controllers/PostsController.cs
var query = _dbContext.Posts
    .Where(p => EF.Functions.ToTsVector("english", p.Content)
        .Matches(EF.Functions.WebSearchToTsQuery("english", search)))
    .Select(p => new PostFtsResultDto
    {
        Id = p.Id,
        Content = p.Content,
        UserId = p.UserId,
        UserName = p.User.Name,
        UserAvatarUrl = p.User.AvatarUrl,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        RelevanceScore = EF.Functions.ToTsVector("english", p.Content)
            .Rank(EF.Functions.WebSearchToTsQuery("english", search)),
        LikesCount = p.Likes.Count(),
        CommentsCount = p.Comments.Count(),
        IsLiked = currentUserId.HasValue && p.Likes.Any(l => l.UserId == currentUserId.Value),
        ImageUrls = p.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
    })
    .OrderByDescending(p => p.RelevanceScore);
```

### 2. User Search: PostgreSQL Trigram Similarity (`pg_trgm` + `ILIKE`)
Usernames and real names behave fundamentally differently from paragraphs:
- Names are short proper nouns with no dictionary stemming (e.g. "Omar", "Sarah", "Alex").
- Users frequently search by partial substrings (e.g. searching `"mar"` to find `"Omar"` or `"Mary"`).
- Users often make minor typos (e.g. searching `"alxe"` instead of `"alex"`).

Standard B-Tree indexes cannot index wildcards like `LIKE '%mar%'` (causing full-table scans), and FTS fails on names without dictionary definitions. We solved this by enabling the PostgreSQL `pg_trgm` extension in `AppDbContext`:

```csharp
// Data/AppDbContext.cs
modelBuilder.HasPostgresExtension("pg_trgm");
```

In `UsersController.cs`, we combine substring matching with trigram similarity thresholding ($> 0.2$):

```csharp
// Controllers/UsersController.cs
var query = _dbContext.Users
    .Where(u =>
        (u.Username != null && (
            EF.Functions.ILike(u.Username, $"%{term}%")
            || EF.Functions.TrigramsSimilarity(u.Username, term) > 0.2
        ))
        || EF.Functions.ILike(u.Name, $"%{term}%")
        || EF.Functions.TrigramsSimilarity(u.Name, term) > 0.2
    );

var users = await query
    .OrderByDescending(u => EF.Functions.ILike(u.Name, $"{term}%") || (u.Username != null && EF.Functions.ILike(u.Username, $"{term}%")))
    .ThenByDescending(u => EF.Functions.TrigramsSimilarity(u.Name, term))
    .ThenBy(u => u.Name)
    .Skip(skip)
    .Take(pagination.PageSize)
    .Select(u => new UserSearchDto
    {
        Id = u.Id,
        Username = u.Username,
        Name = u.Name,
        AvatarUrl = u.AvatarUrl,
        Bio = u.Bio,
        FollowersCount = u.Followers.Count,
        IsFollowing = currentUserId.HasValue && u.Followers.Any(f => f.FollowerId == currentUserId.Value),
        IsMe = currentUserId.HasValue && u.Id == currentUserId.Value
    })
    .ToListAsync();
```

- **Three-Tier Ordering**:
  1. Prefix matches first (e.g., names starting with `"Mar"` appear before names with `"mar"` in the middle).
  2. Highest trigram similarity next (typo-tolerant matches closest to the query).
  3. Alphabetical fallback for deterministic pagination.

---

## 10. Top Backend Interview Questions & Deep-Dive Answers

### Q1: "Why did you use Entity Framework Core instead of Dapper or raw ADO.NET?"
> *"EF Core provides high developer velocity, strongly-typed LINQ queries, automatic change tracking, and seamless migration management across schema changes. For 95% of our application (CRUD, relationships, cascades, and validation), EF Core is ideal. Where performance matters, we leverage compiled queries, projection with `.Select()`, and `AsNoTracking()` to eliminate tracking overhead."*

### Q2: "How did you prevent the N+1 query problem when loading nested comment threads?"
> *"In nested comments, querying the database recursively for each comment's replies creates N+1 queries that destroy database performance. We solved this by executing a single query for the post to load all non-root comment IDs and parent IDs, loading them into an in-memory dictionary. We then compute all descendant counts recursively in memory in $O(N)$ time with zero extra database round-trips."*

### Q3: "How does JWT authentication work under the hood in ASP.NET Core?"
> *"When a user authenticates, the server signs a JWT containing claims (UserId, Email, Name) using HMAC-SHA256 and a secret key. In `Program.cs`, we register `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)`. On every incoming request, the authentication middleware inspects the `Authorization: Bearer <token>` header, verifies the cryptographic signature, validates expiration and issuer, and populates the `HttpContext.User` as a `ClaimsPrincipal`."*

### Q4: "Why did you use BCrypt instead of SHA-256 for password hashing?"
> *"SHA-256 is designed to be fast, which makes it vulnerable to modern GPU hardware capable of computing billions of hashes per second in offline dictionary attacks. BCrypt is a key-derivation function that includes automatic random salting and a configurable work factor (cost). It is intentionally slow (~100ms per check), making brute-force attacks computationally infeasible."*

### Q5: "How do you protect your file upload endpoint from malicious attacks?"
> *"We implement 5 defense layers: 1) Strict file size caps (10MB), 2) Extension whitelisting, 3) Content-Type header validation, 4) Binary magic bytes/file signature inspection to prevent disguised executables, and 5) Renaming all uploaded files with cryptographic GUIDs inside sanitized directories to prevent directory traversal (`../../`) and file overwrite attacks."*

### Q6: "How did you implement Post Audience visibility (Public, Followers, Private) at the database level?"
> *"Instead of pulling all posts into memory, we construct the filter inside Entity Framework using LINQ. We fetch the list of users the current user follows, then filter posts: public posts are accessible to everyone, private posts are strictly limited to `p.UserId == currentUserId`, and followers-only posts require `followedUserIds.Contains(p.UserId)`. This compiles down to a single SQL `WHERE` clause executed directly on PostgreSQL."*

### Q7: "Why do you use DTOs instead of passing database entity models to controllers?"
> *"Exposing domain entities directly leads to mass-assignment security vulnerabilities (over-posting), causes JSON circular reference errors during serialization, and tightly couples the public API contract to the internal database schema. DTOs act as a strict contract, validating only incoming data and returning only the fields the client needs."*

### Q8: "What was the URL encoding issue with email verification tokens and how did you resolve it?"
> *"Base64 tokens contain characters like `+`. When passed in URL query strings, standard HTTP form-urlencoded decoders interpret `+` as a space character (` `). When the token arrived at the backend, verification failed because the space didn't match the original token. We resolved this by sanitizing `token.Replace(' ', '+')` in the verification service and properly URL-encoding links with `Uri.EscapeDataString()`."*

### Q9: "How do you handle unhandled exceptions across the API?"
> *"We use .NET 10's `IExceptionHandler` interface in `GlobalExceptionHandler.cs`. Any unhandled exception thrown in the controller or service layer is intercepted by the middleware pipeline. It logs the stack trace and error details internally, and formats an RFC 7807 ProblemDetails JSON response with an appropriate HTTP status code (e.g. 500), avoiding unformatted 500 HTML pages or sensitive stack trace leaks."*

### Q10: "How does Full-Text Search in PostgreSQL work compared to `LIKE '%text%'`?"
> *"`LIKE '%text%'` forces a full sequential scan of the table because standard B-Tree indexes cannot index leading wildcards. PostgreSQL Full-Text Search parses text into normalized lexemes using `to_tsvector` (stemming words so 'running' matches 'run'). It searches using inverted indexes (GIN) in logarithmic time and allows ranking results by relevance using `ts_rank`."*

### Q11: "How did you build the real-time Chat feature in ASP.NET Core, what did you use, and why?"
> *"We implemented real-time 1-on-1 direct messaging using ASP.NET Core SignalR (`ChatHub`) backed by PostgreSQL and EF Core. We chose SignalR over raw WebSockets because it abstracts transport negotiation (WebSockets with automatic fallback to Server-Sent Events or Long Polling), connection pooling, automatic client reconnection, and group multiplexing (`conversation_{id}`). Messages are sent via a validated REST endpoint (`POST /api/conversations/{id}/messages`) to ensure ACID transaction persistence in PostgreSQL first, after which `IHubContext<ChatHub>` is injected to broadcast the message to room participants via `Clients.Group` and directly to recipient user connections via `Clients.User`."*

### Q12: "Why can't browser clients send standard JWT `Authorization: Bearer` headers during WebSocket handshakes, and how does ASP.NET Core handle this?"
> *"The W3C WebSocket standard in browsers does not provide an API to specify custom HTTP headers during the initial opening handshake (`new WebSocket(url)`). Because of this, client libraries like `@microsoft/signalr` pass the JWT access token in the query string (`?access_token=<token>`). In ASP.NET Core, standard JWT middleware only checks the `Authorization` header. We resolve this in `Program.cs` by configuring `JwtBearerEvents.OnMessageReceived`: if the request path starts with `/hubs` and an `access_token` query parameter is present, we assign it to `context.Token`, allowing ASP.NET Core's authentication system to validate the token and populate `ClaimsPrincipal` seamlessly."*

### Q13: "How does your backend handle message delivery when a user is online but hasn't opened the chat room (dual-delivery pattern)?"
> *"When a message is sent, broadcasting only to the SignalR room group (`Clients.Group($"conversation_{id}")`) would fail to notify users who are browsing their Feed or Profile and haven't joined that specific room. We implemented a dual-delivery pattern in `ConversationsController.CreateAndDispatchMessageAsync`:
> 1. Broadcast the message to `Clients.Group($"conversation_{id}")` for users actively in the conversation.
> 2. Dispatch directly to `Clients.User(recipientUserId)` with `ReceiveMessage` to trigger desktop/browser notifications regardless of their active route.
> 3. Dispatch `ConversationUpdated` containing an updated unread message count and preview snippet to `Clients.User(recipientUserId)` so their navbar badge and message dropdown update in real time."*

### Q14: "How do you handle ephemeral real-time events like typing indicators in SignalR without overloading the database?"
> *"Typing indicators trigger multiple times per second while users type. Writing these events to a relational database like PostgreSQL would saturate write IOPS and bloat transaction logs. In `ChatHub.cs`, the `SendTyping(int conversationId, bool isTyping)` method validates that the caller is an active participant in the conversation, then immediately forwards the event in-memory to `Clients.OthersInGroup(groupName)` and `Clients.Users(otherParticipantUserIds)`. The event bypasses EF Core and PostgreSQL completely, maintaining sub-10ms delivery latency with zero database overhead."*

### Q15: "How did you implement message editing and soft deletion with live SignalR synchronization?"
> *"We support updating and soft-deleting messages:
> 1. **Editing**: `PUT /api/conversations/{id}/messages/{messageId}` verifies that `message.SenderUserId == CurrentUserId` and `!message.IsDeleted`. It updates `Content`, sets `UpdatedAt = DateTime.UtcNow`, persists via EF Core, and broadcasts `MessageEdited` to the conversation group and participant user channels.
> 2. **Soft Deletion**: `DELETE /api/conversations/{id}/messages/{messageId}` does not hard-delete the database row (which would break foreign key relationships and historical ordering). Instead, it sets `IsDeleted = true`, replaces `Content = 'This message was deleted'`, updates `UpdatedAt`, and broadcasts `MessageDeleted` via SignalR. Frontend clients dynamically replace the bubble content with a grayed-out placeholder while preserving message placement."*

### Q16: "How do you calculate unread message counts efficiently per user across multiple conversations?"
> *"In social messaging, storing an `is_read` boolean flag directly on the `Messages` table breaks down for group chats and requires expensive batch updates when a user opens a thread. Instead, we use a watermark approach: each participant has a `ConversationParticipant` record with a `LastReadAt` UTC timestamp. When calculating unread messages, we query:
> ```csharp
> await _dbContext.Messages
>     .CountAsync(m => m.ConversationId == conv.Id &&
>                      m.SenderUserId != userId &&
>                      !m.IsDeleted &&
>                      (lastRead == null || m.CreatedAt > lastRead));
> ```
> Marking a conversation as read requires updating only **one single column** (`LastReadAt = DateTime.UtcNow`) on `ConversationParticipants`, avoiding heavy updates across hundreds of individual message rows."*

### Q17: "How would you scale this SignalR architecture horizontally across multiple backend instances?"
> *"By default, SignalR maintains connection state and group memberships in the local server process memory. If Marbook scales to multiple backend servers behind a load balancer, a user connected to Server A wouldn't receive a message broadcast from a user connected to Server B. To scale horizontally, we introduce a **SignalR Backplane** (e.g. Redis Pub/Sub or Azure SignalR Service). When Server B sends a message to a group or user, it publishes the message to the Redis backplane, which broadcasts it to Server A and all other instances to relay to their locally connected clients."*

---

## 11. Real-Time Direct Messaging (Chat) & SignalR Architecture

### 1. Hub Design & Group Authorization (`ChatHub.cs`)
The real-time chat core is implemented in `ChatHub.cs`, marked with `[Authorize]` to enforce JWT authentication across all socket interactions.

```csharp
// Hubs/ChatHub.cs
[Authorize]
public class ChatHub(AppDbContext dbContext, ILogger<ChatHub> logger) : Hub
{
    public async Task JoinConversation(int conversationId)
    {
        var userId = GetCurrentUserId();
        
        // Strict Authorization: Verify the caller is an actual participant
        var isParticipant = await _dbContext.ConversationParticipants
            .AnyAsync(cp => cp.ConversationId == conversationId && cp.UserId == userId);

        if (isParticipant)
        {
            var groupName = GetConversationGroupName(conversationId);
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }
    }

    public async Task LeaveConversation(int conversationId)
    {
        var groupName = GetConversationGroupName(conversationId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }
}
```

- **Group Naming Isolation**: Rooms use explicit naming schemes (`$"conversation_{conversationId}"`) to prevent namespace collisions with other hubs.
- **Participant Authorization Guard**: Users cannot eavesdrop on other users' conversations by manually sending `JoinConversation(123)`: the hub queries `ConversationParticipants` before granting group membership.

---

### 2. WebSocket Handshake & JWT Query-String Authentication
Because the standard browser WebSocket API cannot include custom `Authorization: Bearer` headers during the HTTP upgrade handshake, the client passes the token as a URL query parameter (`?access_token=...`).

In `Program.cs`, we intercept the token using `JwtBearerEvents`:
```csharp
options.Events = new JwtBearerEvents
{
    OnMessageReceived = context =>
    {
        var accessToken = context.Request.Query["access_token"];
        var path = context.HttpContext.Request.Path;
        
        // Only extract query token for SignalR Hub endpoints
        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
        {
            context.Token = accessToken;
        }
        return Task.CompletedTask;
    }
};
```
- **Security Guardrail**: Query token extraction is strictly scoped to paths starting with `/hubs`. Standard REST endpoints must still transmit credentials via the HTTP `Authorization` header, preventing token leakage in proxy logs.

---

### 3. Dual-Delivery Message Dispatching Pattern (`ConversationsController.cs`)

When a user sends a message, rather than handling database persistence inside the Hub itself, the request goes to `POST /api/conversations/{id}/messages`. This ensures:
1. Standard HTTP REST status codes (`201 Created`, `400 Bad Request`, `403 Forbidden`).
2. Robust model validation via `SendMessageDto`.
3. ACID transaction guarantees in PostgreSQL via Entity Framework Core.

Once saved, `IHubContext<ChatHub>` dispatches the message via three distinct channels:

```csharp
// In ConversationsController.CreateAndDispatchMessageAsync:

// 1. Deliver to all clients currently viewing the room group
var groupName = ChatHub.GetConversationGroupName(conversation.Id);
await _hubContext.Clients.Group(groupName).SendAsync("ReceiveMessage", recipientMessageDto);

// 2. Deliver to the sender's user channel with IsMine = true
await _hubContext.Clients.User(senderUserId.ToString()).SendAsync("ReceiveMessage", senderMessageDto);

// 3. Deliver to other participant(s) with updated unread counts for sidebar/navbar badges
var otherParticipants = conversation.Participants.Where(p => p.UserId != senderUserId).ToList();
foreach (var other in otherParticipants)
{
    await _hubContext.Clients.User(other.UserId.ToString()).SendAsync("ReceiveMessage", recipientMessageDto);

    var unreadCount = await _dbContext.Messages
        .CountAsync(m => m.ConversationId == conversation.Id &&
                         m.SenderUserId != other.UserId &&
                         !m.IsDeleted &&
                         (other.LastReadAt == null || m.CreatedAt > other.LastReadAt));

    var summary = new ConversationSummaryDto
    {
        Id = conversation.Id,
        UpdatedAt = conversation.UpdatedAt,
        OtherParticipant = new UserSearchDto { Id = sender.Id, Name = sender.Name, ... },
        LastMessage = recipientMessageDto,
        UnreadCount = unreadCount
    };

    await _hubContext.Clients.User(other.UserId.ToString()).SendAsync("ConversationUpdated", summary);
}
```

---

### 4. Ephemeral Typing Indicator Engine

Typing indicators fire frequently as users type. Writing these keystroke events to PostgreSQL would cause severe write Amplification and disk I/O bottlenecks.

In `ChatHub.cs`:
```csharp
public async Task SendTyping(int conversationId, bool isTyping)
{
    var userId = GetCurrentUserId();
    var isParticipant = await _dbContext.ConversationParticipants
        .AnyAsync(cp => cp.ConversationId == conversationId && cp.UserId == userId);

    if (isParticipant)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        var dto = new TypingNotificationDto
        {
            ConversationId = conversationId,
            UserId = userId,
            UserName = user?.Name ?? "Someone",
            IsTyping = isTyping
        };

        // Broadcast directly to room group in memory
        var groupName = GetConversationGroupName(conversationId);
        await Clients.OthersInGroup(groupName).SendAsync("UserTyping", dto);

        // Also broadcast to participant user channels
        var otherParticipantUserIds = await _dbContext.ConversationParticipants
            .Where(cp => cp.ConversationId == conversationId && cp.UserId != userId)
            .Select(cp => cp.UserId.ToString())
            .ToListAsync();

        await Clients.Users(otherParticipantUserIds).SendAsync("UserTyping", dto);
    }
}
```
- **Zero Database Persistence**: The event is purely transit-oriented, streaming directly through server memory to connected WebSockets in $< 10\text{ms}$.

---

### 5. Message Lifecycle: Editing & Soft Deletion

#### Message Editing (`PUT /api/conversations/{id}/messages/{messageId}`)
- Verifies authorship (`message.SenderUserId == CurrentUserId`).
- Rejects edits on deleted messages (`!message.IsDeleted`).
- Updates `Content` and sets `UpdatedAt = DateTime.UtcNow`.
- Broadcasts `MessageEdited` payload to both `Clients.Group` and `Clients.Users`.

#### Soft Deletion (`DELETE /api/conversations/{id}/messages/{messageId}`)
- **Why Soft Delete?** Hard-deleting messages breaks chronological thread flow, causes message counter discrepancies, and can trigger foreign key cascade anomalies.
- **Implementation**:
  ```csharp
  message.IsDeleted = true;
  message.Content = "This message was deleted";
  message.UpdatedAt = DateTime.UtcNow;
  await _dbContext.SaveChangesAsync();

  var deletePayload = new
  {
      ConversationId = id,
      MessageId = messageId,
      Content = "This message was deleted",
      IsDeleted = true,
      UpdatedAt = message.UpdatedAt
  };

  await _hubContext.Clients.Group(groupName).SendAsync("MessageDeleted", deletePayload);
  await _hubContext.Clients.Users(participantUserIds).SendAsync("MessageDeleted", deletePayload);
  ```
- **Security Guarantee**: The original message content is wiped from the database column and replaced with `"This message was deleted"`, ensuring privacy even in raw database backups.

---

### 6. Watermark-Based Unread Message Architecture

Rather than maintaining an `is_read` boolean flag on every individual message (which requires updating hundreds of rows when a chat is opened), Marbook uses a **watermark timestamp pattern**:
- `ConversationParticipant.LastReadAt` stores the UTC timestamp when the user last read the conversation.
- A message is unread if `m.CreatedAt > participant.LastReadAt` and `m.SenderUserId != userId`.
- **Marking as Read**:
  ```csharp
  participant.LastReadAt = DateTime.UtcNow;
  await _dbContext.SaveChangesAsync();
  ```
  This executes a single, fast SQL statement (`UPDATE "ConversationParticipants" SET "LastReadAt" = NOW() WHERE "Id" = ...`) regardless of whether there are 5 or 5,000 unread messages in the thread.

---

## 12. Real-Time In-App Notifications & Event-Driven Architecture

### 1. Hub Design (`NotificationHub.cs`)
In-app notifications (likes, comments, new followers) are handled via a dedicated SignalR hub mounted at `/hubs/notifications`.

```csharp
[Authorize]
public class NotificationHub : Hub
{
    // Clients connect and are automatically indexed by Context.UserIdentifier
}
```

### 2. Centralized Notification Service (`NotificationService.cs`)
Whenever a social action occurs in the API (e.g. `LikesController`, `CommentsController`, `FollowsController`), the controller dispatches through `INotificationService`:

```csharp
public async Task CreateNotificationAsync(
    int recipientId,
    int senderId,
    NotificationType type,
    int? postId = null,
    int? commentId = null)
{
    // 1. Suppress notifications if the user acts on their own content
    if (recipientId == senderId) return;

    // 2. Persist notification to PostgreSQL
    var notification = new Notification
    {
        RecipientId = recipientId,
        SenderId = senderId,
        Type = type,
        TargetPostId = postId,
        TargetCommentId = commentId,
        CreatedAt = DateTime.UtcNow,
        IsRead = false
    };
    _dbContext.Notifications.Add(notification);
    await _dbContext.SaveChangesAsync();

    // 3. Dispatch real-time SignalR push directly to recipient user connection
    var dto = await BuildNotificationDtoAsync(notification);
    await _hubContext.Clients.User(recipientId.ToString())
        .SendAsync("ReceiveNotification", dto);
}
```

- **Self-Action Suppression**: Prevents spamming users with notifications when they like their own posts or comment on their own threads.
- **Dual Pipeline**: Persists the notification to PostgreSQL so it survives page reloads, while simultaneously streaming it to the recipient's active browser session.



---

## 13. Account Security, Credential Rotation & Cascade Account Deletion Engine

### Overview (`UsersController.cs` & `AccountSecurityDtos.cs`)
Account security endpoints allow authenticated users to safely rotate credentials and exercise their right to account erasure without security loopholes or orphaned database records.

```
┌──────────────────────────────────────────────────────────────┐
│                    Authenticated Client Request               │
│               (Authorization: Bearer <JWT Token>)            │
└──────────────────────────────┬───────────────────────────────┘
                               │
                ┌──────────────┴──────────────┐
                ▼                             ▼
   POST /api/users/change-password   POST /api/users/delete-account
                │                             │
    Verify CurrentPassword via        Verify Password via
    BCrypt.Verify(pw, hash)           BCrypt.Verify(pw, hash)
                │                             │
    Hash & Update New Password        Cascade Delete User via EF Core
    (BCrypt.HashPassword)             (Posts, Comments, Likes, DMs)
                │                             │
          200 OK Success                200 OK + Client Logout
```

### 1. Secure Password Rotation (`POST /api/users/change-password`)

#### A. Data Transfer Object (`ChangePasswordDto`)
```csharp
public class ChangePasswordDto
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;
}
```

#### B. Verification Pipeline
1. **Identity Extraction**: Obtains `userId` directly from `ClaimTypes.NameIdentifier` (preventing users from specifying another user's ID).
2. **Current Password Proof**: Validates `CurrentPassword` against `user.PasswordHash` using `BCrypt.Net.BCrypt.Verify()`. If invalid, immediately returns `400 Bad Request ("Current password is incorrect.")` without revealing further details.
3. **Length Validation**: Enforces minimum length constraint (>= 6 characters).
4. **Secure Hashing**: Hashes `NewPassword` with `BCrypt.Net.BCrypt.HashPassword(dto.NewPassword)`.
5. **Persistence**: Updates the `PasswordHash` column and commits via `await _dbContext.SaveChangesAsync()`.

---

### 2. Password-Protected Cascade Account Erasure (`POST /api/users/delete-account`)

#### A. Data Transfer Object (`DeleteAccountDto`)
```csharp
public class DeleteAccountDto
{
    [Required]
    public string Password { get; set; } = string.Empty;
}
```

#### B. Destructive Execution & Cascade Guarantees
1. **Re-Authentication Barrier**: Requires the user's current password to prevent unauthorized deletion from hijacked sessions or unattended devices.
2. **PostgreSQL Relational Cascades**:
   - Because our database schema defines `ON DELETE CASCADE` across relational tables:
     - User's authored **Posts** (and their associated Likes, Comments, and PostImages) are deleted.
     - User's **Comments** and nested replies are deleted.
     - User's **Likes** and **CommentLikes** are removed.
     - Social graph relationships (**Follows** as both follower and followee) are purged.
     - User's sent **Messages** and conversation memberships are removed.
     - User's **Notifications** (sent and received) are cleared.
3. **Avatar File Cleanup**: If the user uploaded a custom avatar to `wwwroot/uploads/avatars/`, the physical file is safely removed from disk.
4. **Entity Removal**: `_dbContext.Users.Remove(user)` followed by `await _dbContext.SaveChangesAsync()`.
5. **Response**: Returns `200 OK ("Account deleted successfully.")`, allowing the frontend client to clear cached JWT tokens and redirect to the login screen.
