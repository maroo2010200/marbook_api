using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class PostUpdateDto
{
    [Required]
    [MaxLength(500, ErrorMessage = "Content cannot exceed 500 characters.")]
    public string Content { get; set; } = string.Empty;
}