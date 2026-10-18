using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class UserProfileUpdateDto
{
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(30)]
    public string? Username { get; set; }

    [MaxLength(250)]
    public string? Bio { get; set; }

    public DateOnly Birthdate { get; set; }
}
