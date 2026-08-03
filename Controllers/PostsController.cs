using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PostsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;
        public PostsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }
    
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Post>>> GetPosts()
        {
            var posts = await _dbContext.Posts
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PostResponseDto
            {
                Id = p.Id,
                Content = p.Content,
                UserName = p.User.Name,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                UserId = p.UserId
            }).ToListAsync();

            return Ok(posts);
        }

        [HttpGet("{id:int}")]
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
        public async Task<ActionResult<Post>> CreatePost(PostCreateDto postDto)
        {
            var user = await _dbContext.Users.FindAsync(postDto.UserId);
            if (user == null)
            {
                return NotFound($"User with ID {postDto.UserId} not found.");
            }

            var post = new Post
            {
                Content = postDto.Content,
                UserId = postDto.UserId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Posts.Add(post);
            await _dbContext.SaveChangesAsync();

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

            post.Content = postDto.Content;
            post.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

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

            _dbContext.Posts.Remove(post);
            await _dbContext.SaveChangesAsync();

            return Ok();
        }
    }
}