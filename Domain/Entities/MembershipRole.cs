namespace Domain.Entities
{
    public class MembershipRole
    {
        public long MembershipId { get; set; }
        public int RoleId { get; set; }
        public long TenantId { get; set; }

        public Membership Membership { get; set; } = null!;
        public Role Role { get; set; } = null!;
    }
}
