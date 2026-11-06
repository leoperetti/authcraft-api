namespace Domain.Dto
{
    public class InvitationDto
    {
        public long Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }
}
