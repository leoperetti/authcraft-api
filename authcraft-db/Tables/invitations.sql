CREATE TABLE [dbo].[invitations]
(
	[id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
	[tenant_id] BIGINT NOT NULL,
	[email] NVARCHAR(200) NOT NULL,
	[code] NVARCHAR(128) NOT NULL,
	[created_at] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_invitations_created_at] DEFAULT SYSUTCDATETIME(),
	[expires_at] DATETIMEOFFSET NULL,
	[used] BIT NOT NULL CONSTRAINT [DF_invitations_used] DEFAULT 0,
	CONSTRAINT [FK_invitations_tenants] FOREIGN KEY ([tenant_id]) REFERENCES [dbo].[tenants]([id]) ON DELETE CASCADE,
	CONSTRAINT [UQ_invitations_code] UNIQUE ([code])
);

GO
CREATE INDEX [IX_invitations_tenant] ON [dbo].[invitations] ([tenant_id]);
GO
CREATE INDEX [IX_invitations_email] ON [dbo].[invitations] ([email]);
GO
