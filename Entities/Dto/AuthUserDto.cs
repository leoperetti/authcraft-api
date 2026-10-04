using System.ComponentModel.DataAnnotations;

namespace Entities.Dto
{
    public class AuthUserDto
    {
        [Required]
        public string Id { get; set; } = null!;

        [Required]
        public string Email { get; set; } = null!;

        public string? PhoneNumber { get; set; }

        [Required]
        public string FirstName { get; set; } = null!;

        [Required]
        public string LastName { get; set; } = null!;
    }
}
