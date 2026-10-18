using System.ComponentModel.DataAnnotations;
using MarbookApi.Models;

namespace MarbookApi.DTOs;

public class PostCreateDto
{
    [Required]
    [MaxLength(500, ErrorMessage = "Content cannot exceed 500 characters.")]
    public string Content { get; set; } = string.Empty;

    public PostVisibility Visibility { get; set; } = PostVisibility.Public;
}
