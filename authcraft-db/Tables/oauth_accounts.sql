CREATE TABLE [dbo].[oauth_accounts]
(
	id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_oauth_accounts PRIMARY KEY,
	[identity_id] BIGINT NOT NULL,
	provider_id INT NOT NULL,
	provider_user_id NVARCHAR(200) NOT NULL,
	email NVARCHAR(320) NOT NULL,
	email_verified BIT NOT NULL DEFAULT(0),
	hd NVARCHAR(253) NULL,
	refresh_token VARBINARY(MAX) NULL,
	access_token VARBINARY(MAX) NULL,
	expires_at DATETIMEOFFSET NULL,
	last_login_at DATETIMEOFFSET NULL,
	created_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_oauth_accounts_created_at DEFAULT SYSUTCDATETIME(),
	updated_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_oauth_accounts_updated_at DEFAULT SYSUTCDATETIME(),
	CONSTRAINT FK_oauth_accounts_identity FOREIGN KEY ([identity_id]) REFERENCES dbo.identities(id) ON DELETE CASCADE,
	CONSTRAINT FK_oauth_accounts_provider FOREIGN KEY (provider_id) REFERENCES dbo.providers(id) ON DELETE NO ACTION, 
    CONSTRAINT UQ_oauth_accounts_provider_provideruserid UNIQUE ([provider_id], [provider_user_id])
)

GO
CREATE INDEX [IX_oauth_accounts_identity] ON [dbo].[oauth_accounts] ([identity_id]);
GO
CREATE INDEX [IX_oauth_accounts_provideruserid] ON [dbo].[oauth_accounts] ([provider_user_id]);