CREATE TABLE [dbo].[local_accounts]
(
	id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_local_accounts PRIMARY KEY,
	user_id BIGINT NOT NULL,
	password_hash VARBINARY(MAX) NOT NULL,
	salt VARBINARY(MAX) NOT NULL,
	algorithm NVARCHAR(400) NOT NULL,
	created_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_local_accounts_created_at DEFAULT SYSUTCDATETIME(),
	updated_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_local_accounts_updated_at DEFAULT SYSUTCDATETIME(),
	CONSTRAINT FK_local_accounts_user FOREIGN KEY (user_id) REFERENCES dbo.users(id) ON DELETE CASCADE,
	CONSTRAINT UQ_local_accounts_user UNIQUE (user_id)
)

GO
CREATE INDEX [IX_local_accounts_user] ON [dbo].[local_accounts] ([user_id]);
GO
