using System.ComponentModel.DataAnnotations;

namespace Domain.Request
{
    public class LoginRequest
    {
        [Required]
        public string Identifier { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;

        [Required]
        public string ClientId { get; set; } = string.Empty;
    }
}
