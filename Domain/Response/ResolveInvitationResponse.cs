namespace Domain.Response
{
    public class ResolveInvitationResponse
    {
        public ResolveInvitationResponse(string email, string tenantName, string tenantSlug, DateTimeOffset? expiresAt)
        {
            Email = email;
            TenantName = tenantName;
            TenantSlug = tenantSlug;
            ExpiresAt = expiresAt;
        }

        public string Email { get; set; }
        public string TenantName { get; set; }
        public string TenantSlug { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
