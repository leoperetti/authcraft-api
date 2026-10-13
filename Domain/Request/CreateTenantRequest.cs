using Domain.Dto;
using System.ComponentModel.DataAnnotations;

namespace Domain.Request
{
    public class CreateTenantRequest
    {
        [Required, MaxLength(200)]
        public string ClientId { get; set; } = default!;

        [Required, MaxLength(100), RegularExpression("^[a-z0-9-]+$")]
        public string Slug { get; set; } = default!;

        [Required, MaxLength(200)]
        public string Name { get; set; } = default!;

        [RegularExpression("^(active|suspended|archived)$")]
        public string Status { get; set; } = "active";

        public TenantSettingsDto? Settings { get; set; }

        public List<string> AllowedOrigins { get; set; } = new();
    }
}
