namespace Domain.Dto
{
    public class ResolvedInvitationDto
    {
        public string Email { get; set; } = string.Empty;
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool Used { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string TenantSlug { get; set; } = string.Empty;
    }
}
