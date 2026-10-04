using Domain.Dto;
using System.ComponentModel.DataAnnotations;

namespace Domain.Response
{
    public class AuthResponse
    {
        [Required]
        public string AccessToken { get; set; } = null!;

        [Required]
        public string TokenType { get; set; } = "Bearer";

        [Required]
        public int ExpiresIn { get; set; }

        [Required]
        public AuthUserDto User { get; set; } = null!;
    }
}
