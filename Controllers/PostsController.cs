using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PostsController(AppDbContext dbContext, ILogger<PostsController> logger) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly ILogger _logger = logger;

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<PostResponseDto>>> GetPosts([FromQuery] PaginationParams pagination, [FromQuery] string? search)
        {

            var query = _dbContext.Posts.Include(p => p.User).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p => EF.Functions.ILike(p.Content, $"%{search}%"));
            }

            query = query.OrderByDescending(p => p.CreatedAt);

            var totalCount = await query.CountAsync();

            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var posts = await query
            .Skip(skip)
            .Take(pagination.PageSize)
            .Select(p => new PostResponseDto
            {
                Id = p.Id,
                Content = p.Content,
                UserName = p.User.Name,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                UserId = p.UserId
            })
            .ToListAsync();

            return Ok(new PagedResult<PostResponseDto>
            {
                Items = posts,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }
        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<PostFtsResultDto>>> SearchPosts([FromQuery] PaginationParams pagination, [FromQuery] string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return BadRequest("Search term cannot be empty.");
            }

            var query = _dbContext.Posts
            .Include(p => p.User)
            .Where(p => EF.Functions.ToTsVector("english", p.Content)
            .Matches(EF.Functions.WebSearchToTsQuery("english", search)));

            var totalCount = await query.CountAsync();

            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var posts = await query
                .Select(p => new PostFtsResultDto
                {
                    Id = p.Id,
                    Content = p.Content,
                    UserName = p.User.Name,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    UserId = p.UserId,
                    RelevanceScore = EF.Functions.ToTsVector("english", p.Content)
                        .Rank(EF.Functions.WebSearchToTsQuery("english", search))

                })
                .OrderByDescending(p => p.RelevanceScore)
                .ThenByDescending(p => p.CreatedAt)
                .Skip(skip)
                .Take(pagination.PageSize)
                .ToListAsync();

            return Ok(new PagedResult<PostFtsResultDto>
            {
                Items = posts,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageNumber
            });
        }

        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<PostResponseDto>> GetPost(int id)
        {
            var post = await _dbContext.Posts
                .Where(p => p.Id == id)
                .Select(p => new PostResponseDto
                {
                    Id = p.Id,
                    Content = p.Content,
                    UserName = p.User.Name,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    UserId = p.UserId
                })
                .FirstOrDefaultAsync();

            if (post == null)
            {
                return NotFound();
            }

            return Ok(post);
        }

        [HttpPost]
        public async Task<ActionResult<PostResponseDto>> CreatePost(PostCreateDto postDto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            _logger.LogInformation("User {UserId} is creating a post.", userId);

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning(
                    "User {UserId} not found when creating a post.",
                    userId);

                return NotFound($"User with ID {userId} not found.");
            }

            var post = new Post
            {
                Content = postDto.Content,
                UserId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Posts.Add(post);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Post {PostId} created successfully by user {UserId}.",
                post.Id,
                userId);

            var responseDto = new PostResponseDto
            {
                Id = post.Id,
                Content = post.Content,
                UserName = user.Name,
                CreatedAt = post.CreatedAt,
                UpdatedAt = post.UpdatedAt,
                UserId = post.UserId
            };

            return CreatedAtAction(nameof(GetPost), new { id = post.Id }, responseDto);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<Post>> UpdatePost(int id, PostUpdateDto postDto)
        {
            var post = await _dbContext.Posts.FindAsync(id);
            if (post == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (post.UserId != userId)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to update post {PostId} owned by another user.",
                    userId,
                    post.UserId);

                return Forbid();
            }

            post.Content = postDto.Content;
            post.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Post {PostId} updated successfully by user {UserId}",
                post.Id,
                userId);

            return Ok(post);
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> DeletePost(int id)
        {
            var post = await _dbContext.Posts.FindAsync(id);
            if (post == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (post.UserId != userId)
            {
                return Forbid();
            }

            _dbContext.Posts.Remove(post);
            await _dbContext.SaveChangesAsync();

            return Ok();
        }
    }
}