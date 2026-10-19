using System.ComponentModel.DataAnnotations;
using MarbookApi.Models;

namespace MarbookApi.DTOs;

public class PostCreateDto
{
    [MaxLength(500, ErrorMessage = "Content cannot exceed 500 characters.")]
    public string? Content { get; set; }

    public PostVisibility Visibility { get; set; } = PostVisibility.Public;

    public List<string> ImageUrls { get; set; } = new();
}
