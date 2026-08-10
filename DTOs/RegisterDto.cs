using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class RegisterDto
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Email { get; set; } = string.Empty;
    public string? Username { get; set; }

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;
    [Required]
    public DateOnly Birthdate { get; set; }
}
