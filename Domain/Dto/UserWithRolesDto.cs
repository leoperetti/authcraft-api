using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Domain.Dto
{
    public class UserWithRolesDto
    {
        [Required]
        public string Id { get; set; } = null!;

        [Required]
        public string Email { get; set; } = null!;

        public string? PhoneNumber { get; set; }

        public string? Name { get; set; }

        public IList<string> Roles { get; set; } = new List<string>();
    }
}
