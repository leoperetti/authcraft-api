namespace Domain.Entities
{
    public class PasswordResetToken
    {
        public long Id { get; set; }
        public long IdentityId { get; set; }
        public string Code { get; set; } = default!;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public bool Used { get; set; }

        public Identity Identity { get; set; } = null!;
    }
}
