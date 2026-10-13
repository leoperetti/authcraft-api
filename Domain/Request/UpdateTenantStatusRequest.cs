using System.ComponentModel.DataAnnotations;

namespace Domain.Request
{
    public class UpdateTenantStatusRequest
    {
        [Required, RegularExpression("^(active|suspended|archived)$")]
        public string Status { get; set; } = default!;
    }
}
