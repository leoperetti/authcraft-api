using System.ComponentModel.DataAnnotations;

namespace Entities.Request
{
    public class SignupRequest
    {
        [Required]
        public string Email { get; set; } = null!;

        [Required]
        public string FirstName { get; set; } = null!;

        [Required]
        public string LastName { get; set; } = null!;

        [Required]
        public string Password { get; set; } = null!;

        public string? InvitationCode { get; set; }
    }
}
