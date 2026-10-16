using System.Collections.Generic;

namespace Domain.Entities
{
    public class Role
    {
        public int Id { get; set; }
        public long TenantId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsSystem { get; set; } = false;

        public Tenant Tenant { get; set; } = null!;
        public ICollection<MembershipRole> MembershipRoles { get; set; } = new List<MembershipRole>();
    }
}
