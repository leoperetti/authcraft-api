CREATE TABLE [dbo].[tenants]
(
	[id]            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_tenants] PRIMARY KEY,
    [client_id] NVARCHAR(200) NOT NULL,
    [slug]          NVARCHAR(100) NOT NULL CONSTRAINT [UQ_tenants_slug] UNIQUE,
    [name]          NVARCHAR(200) NOT NULL,
    [status]        NVARCHAR(20)  NOT NULL CONSTRAINT [DF_tenants_status] DEFAULT ('active'),
    [settings]      NVARCHAR(MAX) NULL,  -- JSON: mfa_required, password_policy...
    [allowed_origins] VARCHAR(2083) NULL, 
    CONSTRAINT [CK_tenants_status]        CHECK ([status] IN ('active','suspended','archived')),
    CONSTRAINT [CK_tenants_settings_json] CHECK ([settings] IS NULL OR ISJSON([settings]) = 1),
    CONSTRAINT [CK_tenants_origins_json]  CHECK ([allowed_origins] IS NULL OR ISJSON([allowed_origins]) = 1)
)
