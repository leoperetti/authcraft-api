CREATE TABLE [dbo].[membership_roles]
(
    [membership_id] BIGINT NOT NULL,
    [role_id] INT NOT NULL,
    [tenant_id] BIGINT NOT NULL,
    CONSTRAINT [PK_membership_roles] PRIMARY KEY ([membership_id], [role_id]),
    CONSTRAINT [FK_membership_roles_role_tenant]
        FOREIGN KEY ([role_id], [tenant_id]) REFERENCES [dbo].[roles]([id], [tenant_id]),
    CONSTRAINT [FK_membership_roles_membership_tenant]
        FOREIGN KEY ([membership_id], [tenant_id]) REFERENCES [dbo].[memberships]([identity_id], [tenant_id]) ON DELETE CASCADE
);
GO
CREATE INDEX [IX_membership_roles_role] ON [dbo].[membership_roles] ([role_id]);