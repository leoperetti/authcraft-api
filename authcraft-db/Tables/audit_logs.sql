CREATE TABLE [dbo].[audit_logs]
(
	[id] BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_audit_logs] PRIMARY KEY,
	[actor_id] BIGINT NULL,
	[actor_email] NVARCHAR(256) NULL,
	[actor_role] NVARCHAR(32) NULL,
	[action] NVARCHAR(64) NOT NULL,
	[entity_type] NVARCHAR(64) NOT NULL,
	[entity_id] NVARCHAR(64) NULL,
	[old_values] NVARCHAR(MAX) NULL,
	[new_values] NVARCHAR(MAX) NULL,
	[ip_address] NVARCHAR(45) NULL,
	[user_agent] NVARCHAR(512) NULL,
	[correlation_id] NVARCHAR(128) NULL,
	[success] BIT NOT NULL,
	[failure_reason] NVARCHAR(512) NULL,
	[timestamp_utc] DATETIME2(3) NOT NULL
)

GO
CREATE INDEX [IX_AuditLogs_Entity_Timestamp] ON [dbo].[audit_logs] ([entity_type] ASC, [entity_id] ASC, [timestamp_utc] DESC);
GO
CREATE INDEX [IX_AuditLogs_Actor_Timestamp] ON [dbo].[audit_logs] ([actor_id] ASC, [timestamp_utc] DESC);
GO
CREATE INDEX [IX_AuditLogs_Action_Timestamp] ON [dbo].[audit_logs] ([action] ASC, [timestamp_utc] DESC);
GO
CREATE INDEX [IX_AuditLogs_Timestamp] ON [dbo].[audit_logs] ([timestamp_utc] DESC);
GO
CREATE INDEX [IX_AuditLogs_CorrelationId] ON [dbo].[audit_logs] ([correlation_id]) WHERE [correlation_id] IS NOT NULL;
