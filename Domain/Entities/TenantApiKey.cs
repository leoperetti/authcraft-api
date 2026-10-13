namespace Domain.Entities
{
    public class TenantApiKey
    {
        public int Id { get; set; }
        public long TenantId { get; set; }
        public string? KeyHash { get; set; }
        public string? Scopes { get; set; }

        public Tenant Tenant { get; set; } = null!;
    }
}
