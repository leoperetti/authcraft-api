CREATE TABLE [dbo].[roles]
(
    [id] INT NOT NULL PRIMARY KEY IDENTITY,
    [tenant_id] BIGINT NOT NULL, 
    [name] NVARCHAR(50) NOT NULL,
    [is_system] BIT NOT NULL DEFAULT 0,
    CONSTRAINT [UQ_roles_name] UNIQUE ([tenant_id],[name]), 
    CONSTRAINT [FK_roles_tenants] FOREIGN KEY ([tenant_id]) REFERENCES [tenants]([id]),
    CONSTRAINT [UQ_roles_id_tenant] UNIQUE ([id], [tenant_id])
);

