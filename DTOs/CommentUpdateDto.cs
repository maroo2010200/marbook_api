using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class CommentUpdateDto
{
    [Required]
    [MaxLength(300, ErrorMessage = "Content cannot exceed 300 characters.")]
    public string Content { get; set; } = string.Empty;
}
