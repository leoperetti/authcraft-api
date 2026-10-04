using System.ComponentModel.DataAnnotations;

namespace Entities.Request
{
    public class GoogleLoginRequest
    {
        [Required]
        public string IdToken { get; set; } = null!;
    }
}
