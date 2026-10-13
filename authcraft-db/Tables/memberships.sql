CREATE TABLE [dbo].[memberships]
(
	[identity_id] BIGINT NOT NULL PRIMARY KEY, 
    [tenant_id] BIGINT NOT NULL, 
    [status] NVARCHAR(50) NOT NULL CONSTRAINT [DF_memberships_status] DEFAULT ('active'),
    [role_id] INT NOT NULL,
    [invited_by]    BIGINT NULL,
    [invited_at]    DATETIMEOFFSET NULL,
    [joined_at]     DATETIMEOFFSET NULL,
    [created_at]    DATETIMEOFFSET NOT NULL CONSTRAINT [DF_memberships_created_at] DEFAULT SYSUTCDATETIME(),
    [updated_at]    DATETIMEOFFSET NOT NULL CONSTRAINT [DF_memberships_updated_at] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [FK_memberships_identity] FOREIGN KEY ([identity_id])
        REFERENCES [dbo].[identities]([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_memberships_tenant] FOREIGN KEY ([tenant_id])
        REFERENCES [dbo].[tenants]([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_memberships_invited_by] FOREIGN KEY ([invited_by])
        REFERENCES [dbo].[identities]([id]) ON DELETE NO ACTION,
    CONSTRAINT [UQ_memberships_identity_tenant] UNIQUE ([identity_id], [tenant_id]),
    CONSTRAINT [CK_memberships_status]
        CHECK ([status] IN ('invited','active','suspended','removed')),
);

GO
CREATE INDEX [IX_memberships_tenant_status]   ON [dbo].[memberships] ([tenant_id], [status])
GO
CREATE INDEX [IX_memberships_identity_status] ON [dbo].[memberships] ([identity_id], [status])