using Domain.Audit;

namespace Application.Audit
{
    public interface IAuditService
    {
        Task LogAsync(AuditEntry entry, CancellationToken ct = default);
        Task LogIndependentAsync(AuditEntry entry, CancellationToken ct = default);
    }
}
