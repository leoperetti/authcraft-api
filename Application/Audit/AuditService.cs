using Domain.Audit;
using Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Application.Audit
{
    public class AuditService: IAuditService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<AuditService> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public AuditService(AppDbContext db, IHttpContextAccessor http, ILogger<AuditService> logger)
        {
            _db = db; _http = http; _logger = logger;
        }

        public Task LogAsync(AuditEntry entry, CancellationToken ct = default)
        {
            var ctx = _http.HttpContext;
            var user = ctx?.User;

            long? actorId = null;
            var actorIdRaw = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? user?.FindFirst("sub")?.Value;

            if (long.TryParse(actorIdRaw, out var parsed))
                actorId = parsed;

            var log = new AuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                ActorId = actorId,
                ActorEmail = user?.FindFirst(ClaimTypes.Email)?.Value,
                ActorRole = user?.FindFirst(ClaimTypes.Role)?.Value,

                Action = entry.Action,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,

                OldValues = entry.OldValues is null ? null : JsonSerializer.Serialize(entry.OldValues, JsonOpts),
                NewValues = entry.NewValues is null ? null : JsonSerializer.Serialize(entry.NewValues, JsonOpts),

                IpAddress = ctx?.Connection.RemoteIpAddress?.ToString(),
                UserAgent = ctx?.Request.Headers["User-Agent"].ToString(),
                CorrelationId = ctx?.TraceIdentifier,

                Success = entry.Success,
                FailureReason = entry.FailureReason
            };

            _db.AuditLogs.Add(log);

            // Además, opcionalmente al logger estructurado
            _logger.LogInformation(
                "AUDIT {Action} actor={ActorId} target={EntityType}:{EntityId} success={Success}",
                entry.Action, actorId, entry.EntityType, entry.EntityId, entry.Success);

            return Task.CompletedTask;
        }

        public async Task LogIndependentAsync(AuditEntry entry, CancellationToken ct = default)
        {
            await LogAsync(entry, ct);
            await _db.SaveChangesAsync(ct);
        }
    }
}
