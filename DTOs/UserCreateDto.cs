using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;
    public class UserCreateDto
    {
        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;
        public string? Username { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MinLength(6)]
        public string Password { get; set; } = string.Empty;
        public DateOnly Birthdate { get; set; }
    }