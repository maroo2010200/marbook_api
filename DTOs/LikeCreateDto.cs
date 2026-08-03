using System.ComponentModel.DataAnnotations;

namespace MarbookApi.DTOs;

public class LikeCreateDto
{
    [Required]
    public int UserId { get; set; }
}