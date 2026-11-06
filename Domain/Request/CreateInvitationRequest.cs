namespace Domain.Request
{
    public class CreateInvitationRequest
    {
        public string Email { get; set; } = string.Empty;
        public StudentInvitationProfileRequest? StudentProfile { get; set; }
    }
}
