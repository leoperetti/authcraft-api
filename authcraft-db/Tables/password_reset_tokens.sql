CREATE TABLE [dbo].[password_reset_tokens]
(
    [id]           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT [PK_password_reset_tokens] PRIMARY KEY,
    [identity_id]  BIGINT NOT NULL,
    [code]         NVARCHAR(64) NOT NULL,
    [created_at]   DATETIMEOFFSET NOT NULL CONSTRAINT [DF_prt_created_at] DEFAULT SYSUTCDATETIME(),
    [expires_at]   DATETIMEOFFSET NOT NULL,
    [used]         BIT NOT NULL CONSTRAINT [DF_prt_used] DEFAULT 0,

    CONSTRAINT [FK_prt_identity] FOREIGN KEY ([identity_id])
        REFERENCES [dbo].[identities]([id]) ON DELETE CASCADE,
    CONSTRAINT [UQ_prt_code] UNIQUE ([code])
);
GO
CREATE INDEX [IX_prt_identity] ON [dbo].[password_reset_tokens] ([identity_id]);
GO