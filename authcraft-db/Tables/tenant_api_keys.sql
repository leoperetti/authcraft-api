CREATE TABLE [dbo].[tenant_api_keys]
(
	[id] INT NOT NULL PRIMARY KEY IDENTITY, 
    [tenant_id] BIGINT NOT NULL, 
    [key_hash] NVARCHAR(255) NULL, 
    [scopes] NVARCHAR(MAX) NULL, 
    CONSTRAINT [FK_tenant_api_keys_tenants] FOREIGN KEY ([tenant_id]) REFERENCES [tenants]([id])
)
