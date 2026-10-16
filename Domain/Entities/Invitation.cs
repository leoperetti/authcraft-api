using System;

namespace Domain.Entities
{
    public class Invitation
    {
        public long Id { get; set; }
        public long TenantId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool Used { get; set; } = false;

        public Tenant Tenant { get; set; } = null!;
    }
}
