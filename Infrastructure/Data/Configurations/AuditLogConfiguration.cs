using Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class AuditLogConfiguration: IEntityTypeConfiguration<AuditLog>
    {
        public void Configure(EntityTypeBuilder<AuditLog> builder)
        {
            // Tabla
            builder.ToTable("audit_logs");

            // PK
            builder.HasKey(a => a.Id);

            // Id autogenerado (long, identity)
            builder.Property(a => a.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            // ===== Actor =====
            builder.Property(a => a.ActorId)
                .HasColumnName("actor_id")
                .IsRequired(false);

            builder.Property(a => a.ActorEmail)
                .HasColumnName("actor_email")
                .HasMaxLength(256)
                .IsRequired(false);

            builder.Property(a => a.ActorRole)
                .HasColumnName("actor_role")
                .HasMaxLength(32)
                .IsRequired(false);

            // ===== Acción =====
            builder.Property(a => a.Action)
                .HasColumnName("action")
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(a => a.EntityType)
                .HasColumnName("entity_type")
                .HasMaxLength(64)
                .IsRequired();

            builder.Property(a => a.EntityId)
                .HasColumnName("entity_id")
                .HasMaxLength(64)
                .IsRequired(false);

            // ===== Cambios (JSON) =====
            // Si estás en SQL Server 2016+ puedes usar nvarchar(max)
            builder.Property(a => a.OldValues)
                .HasColumnName("old_values")
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            builder.Property(a => a.NewValues)
                .HasColumnName("new_values")
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            // ===== Contexto =====
            builder.Property(a => a.IpAddress)
                .HasColumnName("ip_address")
                .HasMaxLength(45) // IPv6 cabe en 45
                .IsRequired(false);

            builder.Property(a => a.UserAgent)
                .HasColumnName("user_agent")
                .HasMaxLength(512)
                .IsRequired(false);

            builder.Property(a => a.CorrelationId)
                .HasColumnName("correlation_id")
                .HasMaxLength(128)
                .IsRequired(false);

            // ===== Resultado =====
            builder.Property(a => a.Success)
                .HasColumnName("success")
                .IsRequired();

            builder.Property(a => a.FailureReason)
                .HasColumnName("failure_reason")
                .HasMaxLength(512)
                .IsRequired(false);

            // ===== Timestamp =====
            builder.Property(a => a.TimestampUtc)
                .HasColumnName("timestamp_utc")
                .HasColumnType("datetime2(3)")
                .IsRequired();

            // ===== Índices =====
            // Historial de una entidad concreta (el más usado)
            builder.HasIndex(a => new { a.EntityType, a.EntityId, a.TimestampUtc })
                .HasDatabaseName("IX_AuditLogs_Entity_Timestamp")
                .IsDescending(false, false, true); // el timestamp descendente ayuda

            // Qué ha hecho un actor
            builder.HasIndex(a => new { a.ActorId, a.TimestampUtc })
                .HasDatabaseName("IX_AuditLogs_Actor_Timestamp")
                .IsDescending(false, true);

            // Filtrar por acción (dashboards, alertas)
            builder.HasIndex(a => new { a.Action, a.TimestampUtc })
                .HasDatabaseName("IX_AuditLogs_Action_Timestamp")
                .IsDescending(false, true);

            // Filtrar por rango de fechas global
            builder.HasIndex(a => a.TimestampUtc)
                .HasDatabaseName("IX_AuditLogs_Timestamp")
                .IsDescending(true);

            // CorrelationId para agrupar operaciones compuestas
            builder.HasIndex(a => a.CorrelationId)
                .HasDatabaseName("IX_AuditLogs_CorrelationId")
                .HasFilter("[correlation_id] IS NOT NULL");
        }
    }
}
