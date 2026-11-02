using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class Identity
    {
        public long Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Name { get; set; }
        public string? Picture { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public ICollection<OAuthAccount> OAuthAccounts { get; set; } = new List<OAuthAccount>();
        public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
        public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = new List<PasswordResetToken>();
    }
}
