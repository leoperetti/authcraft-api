CREATE TABLE [dbo].[invitations]
(
	[id] BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
	[tenant_id] BIGINT NOT NULL,
	[email] NVARCHAR(200) NOT NULL,
	[code] NVARCHAR(128) NOT NULL,
	[created_at] DATETIMEOFFSET NOT NULL CONSTRAINT [DF_invitations_created_at] DEFAULT SYSUTCDATETIME(),
	[expires_at] DATETIMEOFFSET NULL,
	[used] BIT NOT NULL CONSTRAINT [DF_invitations_used] DEFAULT 0,
	[purpose] NVARCHAR(32) NULL,
	[student_id] BIGINT NULL,
	[first_name] NVARCHAR(150) NULL,
	[last_name] NVARCHAR(150) NULL,
	[full_name] NVARCHAR(150) NULL,
	[phone_number] NVARCHAR(30) NULL,
	[dni] NVARCHAR(16) NULL,
	[accepted_identity_id] BIGINT NULL,
	[accepted_at] DATETIMEOFFSET NULL,
	[registered_first_name] NVARCHAR(150) NULL,
	[registered_last_name] NVARCHAR(150) NULL,
	[registered_phone_number] NVARCHAR(30) NULL,
	[registered_dni] NVARCHAR(16) NULL,
	[row_version] ROWVERSION NOT NULL,
	CONSTRAINT [FK_invitations_tenants] FOREIGN KEY ([tenant_id]) REFERENCES [dbo].[tenants]([id]) ON DELETE CASCADE,
	CONSTRAINT [FK_invitations_accepted_identity] FOREIGN KEY ([accepted_identity_id]) REFERENCES [dbo].[identities]([id]),
	CONSTRAINT [UQ_invitations_code] UNIQUE ([code]),
	CONSTRAINT [CK_invitations_purpose] CHECK ([purpose] IS NULL OR [purpose] = 'gym_student'),
	CONSTRAINT [CK_invitations_student_profile] CHECK ([purpose] IS NULL OR ([student_id] > 0 AND [full_name] IS NOT NULL AND LEN([full_name]) > 0 AND (([first_name] IS NULL AND [last_name] IS NULL) OR ([first_name] IS NOT NULL AND [last_name] IS NOT NULL)))),
	CONSTRAINT [CK_invitations_student_dni] CHECK ([dni] IS NULL OR (len(replace(replace([dni],N'.',N''),N' ',N''))=(8) OR len(replace(replace([dni],N'.',N''),N' ',N''))=(7)) AND NOT [dni] like N'%[^0-9. ]%'),
	CONSTRAINT [CK_invitations_registered_dni] CHECK ([registered_dni] IS NULL OR (len(replace(replace([registered_dni],N'.',N''),N' ',N''))=(8) OR len(replace(replace([registered_dni],N'.',N''),N' ',N''))=(7)) AND NOT [registered_dni] like N'%[^0-9. ]%')
);

GO
CREATE INDEX [IX_invitations_tenant] ON [dbo].[invitations] ([tenant_id]);
GO
CREATE INDEX [IX_invitations_email] ON [dbo].[invitations] ([email]);
GO
