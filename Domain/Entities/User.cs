using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class User
    {
        public long Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Picture { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public ICollection<OAuthAccount> OAuthAccounts { get; set; } = new List<OAuthAccount>();
    }
}
