namespace Domain.Response
{
    public class ResolveInvitationResponse
    {
        public ResolveInvitationResponse(
            string email,
            string tenantName,
            string tenantSlug,
            DateTimeOffset? expiresAt,
            ResolvedStudentInvitationProfileResponse? studentProfile = null,
            bool dniRequired = false)
        {
            Email = email;
            TenantName = tenantName;
            TenantSlug = tenantSlug;
            ExpiresAt = expiresAt;
            StudentProfile = studentProfile;
            DniRequired = dniRequired;
        }

        public string Email { get; set; }
        public string TenantName { get; set; }
        public string TenantSlug { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public ResolvedStudentInvitationProfileResponse? StudentProfile { get; set; }
        public bool DniRequired { get; set; }
    }
}
