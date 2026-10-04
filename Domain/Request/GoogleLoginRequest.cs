using System.ComponentModel.DataAnnotations;

namespace Domain.Request
{
    public class GoogleLoginRequest
    {
        [Required]
        public string IdToken { get; set; } = null!;
    }
}
