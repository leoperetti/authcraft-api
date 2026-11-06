namespace Domain.Response
{
    public class AcceptedInvitationProfileResponse
    {
        public long InvitationId { get; set; }
        public long StudentId { get; set; }
        public string IdentityId { get; set; } = string.Empty;
        public DateTimeOffset AcceptedAt { get; set; }
        public AcceptedInvitationPrefillResponse Prefill { get; set; } = new();
        public AcceptedInvitationRegisteredResponse Registered { get; set; } = new();
    }

    public class AcceptedInvitationPrefillResponse
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Dni { get; set; }
    }

    public class AcceptedInvitationRegisteredResponse
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string Dni { get; set; } = string.Empty;
    }
}
