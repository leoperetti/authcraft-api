using Domain.Dto;
using System.ComponentModel.DataAnnotations;

namespace Domain.Request
{
    public class UpdateTenantRequest
    {
        [MaxLength(200)]
        public string? Name { get; set; }

        [MaxLength(50)]
        public string? PrimaryDomain { get; set; }

        [RegularExpression("^(active|suspended|archived)$")]
        public string? Status { get; set; }

        public TenantSettingsDto? Settings { get; set; }

        public List<string>? AllowedOrigins { get; set; }
    }
}
