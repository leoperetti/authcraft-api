namespace Domain.Entities
{
    public class Membership
    {
        public long IdentityId { get; set; }
        public long TenantId { get; set; }
        public string Status { get; set; } = string.Empty;
        public long? InvitedBy { get; set; }
        public DateTimeOffset? InvitedAt { get; set; }
        public DateTimeOffset? JoinedAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public Identity Identity { get; set; } = null!;
        public Tenant Tenant { get; set; } = null!;
        public Identity? InvitedByIdentity { get; set; }
        public ICollection<MembershipRole> MembershipRoles { get; set; } = new List<MembershipRole>();
    }
}
