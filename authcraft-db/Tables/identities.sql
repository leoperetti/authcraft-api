CREATE TABLE [dbo].[identities]
(
	id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_identities PRIMARY KEY,
	email NVARCHAR(320) NOT NULL,
	phone_number NVARCHAR(20) NULL,
	name NVARCHAR(200) NULL,
	picture NVARCHAR(1000) NULL,
	created_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_identities_created_at DEFAULT SYSUTCDATETIME(),
	updated_at DATETIMEOFFSET NOT NULL CONSTRAINT DF_identities_updated_at DEFAULT SYSUTCDATETIME(), 
)

GO
CREATE INDEX [IX_identities_email] ON [dbo].[identities] ([email]);
