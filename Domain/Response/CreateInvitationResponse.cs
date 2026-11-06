namespace Domain.Response
{
    public class CreateInvitationResponse
    {
        public CreateInvitationResponse(long invitationId, DateTimeOffset expiresAt)
        {
            InvitationId = invitationId;
            ExpiresAt = expiresAt;
        }

        public long InvitationId { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }
}
