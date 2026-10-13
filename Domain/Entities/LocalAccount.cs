using System;

namespace Domain.Entities
{
    public class LocalAccount
    {
        public long Id { get; set; }

        public long IdentityId { get; set; }
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>();
        public byte[] Salt { get; set; } = Array.Empty<byte>();
        // Store algorithm and parameters as a simple string (e.g. "argon2id$m=65536,t=4,p=2")
        public string Algorithm { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public Identity? Identity { get; set; }
    }
}
