using System;

namespace Domain.Entities
{
    public class OAuthAccount
    {
        public long Id { get; set; }

        public long UserId { get; set; }
        public int ProviderId { get; set; }
        public string ProviderUserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool EmailVerified { get; set; }
        public string? Hd { get; set; }
        public byte[]? RefreshToken { get; set; }
        public byte[]? AccessToken { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public User? User { get; set; }
        public Provider? Provider { get; set; }
    }
}
