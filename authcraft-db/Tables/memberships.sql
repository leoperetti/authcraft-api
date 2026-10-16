CREATE TABLE [dbo].[memberships]
(
    [identity_id] BIGINT NOT NULL,
    [tenant_id] BIGINT NOT NULL,
    [status] NVARCHAR(50) NOT NULL CONSTRAINT [DF_memberships_status] DEFAULT ('active'),
    [invited_by] BIGINT NULL,
    [invited_at] DATETIMEOFFSET NULL,
    [joined_at] DATETIMEOFFSET NULL,
    [created_at] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_memberships_created_at] DEFAULT SYSUTCDATETIME(),
    [updated_at] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_memberships_updated_at] DEFAULT SYSUTCDATETIME(),

    CONSTRAINT [PK_memberships] PRIMARY KEY ([identity_id], [tenant_id]),
    CONSTRAINT [FK_memberships_identity] FOREIGN KEY ([identity_id])
        REFERENCES [dbo].[identities]([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_memberships_tenant] FOREIGN KEY ([tenant_id])
        REFERENCES [dbo].[tenants]([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_memberships_invited_by] FOREIGN KEY ([invited_by])
        REFERENCES [dbo].[identities]([id]) ON DELETE NO ACTION,
    CONSTRAINT [CK_memberships_status]
        CHECK ([status] IN ('invited','active','suspended','removed'))
);