namespace Domain.Request
{
    public class ResendInvitationRequest
    {
        public string Email { get; set; } = string.Empty;
        public StudentInvitationProfileRequest StudentProfile { get; set; } = null!;
    }
}
