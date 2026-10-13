namespace Domain.Audit
{
    public class AuditLog
    {
        public long Id { get; set; } // long, no Guid: los logs crecen mucho
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        // Actor (quién)
        public long? ActorId { get; set; }
        public string? ActorEmail { get; set; }
        public string? ActorRole { get; set; }   // rol que tenía AL MOMENTO de la acción

        // Acción
        public string Action { get; set; } = default!;      // "role.changed"
        public string EntityType { get; set; } = default!;  // "User"
        public string? EntityId { get; set; }               // id afectado

        // Cambios (JSON serializado)
        public string? OldValues { get; set; }  // {"role":"user"}
        public string? NewValues { get; set; }  // {"role":"admin"}

        // Contexto
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? CorrelationId { get; set; }

        // Resultado
        public bool Success { get; set; }
        public string? FailureReason { get; set; }
    }
}
