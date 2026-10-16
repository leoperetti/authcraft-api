using System.Collections.Generic;

namespace Domain.Entities
{
    public class Tenant
    {
        public long Id { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PrimaryDomain { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Settings { get; set; }
        public string? AllowedOrigins { get; set; }

        public ICollection<Role> Roles { get; set; } = new List<Role>();
        public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
        public ICollection<TenantApiKey> TenantApiKeys { get; set; } = new List<TenantApiKey>();
        public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
    }
}
