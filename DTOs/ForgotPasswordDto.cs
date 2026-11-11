using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class ForgotPasswordDto
{
    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    public string Email { get; set; } = string.Empty;
}

