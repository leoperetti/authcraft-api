namespace Domain.Audit
{
    public class AuditEntry
    {
        public string Action { get; set; } = default!;
        public string EntityType { get; set; } = default!;
        public string? EntityId { get; set; }
        public object? OldValues { get; set; }
        public object? NewValues { get; set; }
        public bool Success { get; set; } = true;
        public string? FailureReason { get; set; }
    }
}
