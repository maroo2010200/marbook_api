using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class UserProfileUpdateDto
{
    [MaxLength(50)]
    public string? Name { get; set; }

    [MaxLength(30)]
    public string? Username { get; set; }

    [MaxLength(250)]
    public string? Bio { get; set; }

    [MaxLength(500)]
    public string? AvatarUrl { get; set; }

    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    public DateOnly? Birthdate { get; set; }
    public bool? IsBirthdatePrivate { get; set; }
}
