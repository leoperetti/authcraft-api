using System.ComponentModel.DataAnnotations;

namespace Entities.Request
{
    public class LoginRequest
    {
        [Required]
        public string Identifier { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;
    }
}
