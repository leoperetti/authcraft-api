using System;

namespace Domain.Entities
{
    public class Invitation
    {
        public long Id { get; set; }
        public long TenantId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public bool Used { get; set; } = false;
        public string? Purpose { get; set; }
        public long? StudentId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Dni { get; set; }
        public long? AcceptedIdentityId { get; set; }
        public DateTimeOffset? AcceptedAt { get; set; }
        public string? RegisteredFirstName { get; set; }
        public string? RegisteredLastName { get; set; }
        public string? RegisteredPhoneNumber { get; set; }
        public string? RegisteredDni { get; set; }
        public byte[] RowVersion { get; set; } = Array.Empty<byte>();

        public Tenant Tenant { get; set; } = null!;
        public Identity? AcceptedIdentity { get; set; }
    }
}
