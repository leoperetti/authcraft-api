CREATE TABLE [dbo].[users_roles]
(
    [user_id] BIGINT NOT NULL,
    [role_id] INT NOT NULL,
    CONSTRAINT [PK_users_roles] PRIMARY KEY ([user_id], [role_id]),
    CONSTRAINT [FK_users_roles_roles] FOREIGN KEY ([role_id]) REFERENCES [dbo].[roles]([id]) ON DELETE CASCADE,
    CONSTRAINT [FK_users_roles_users] FOREIGN KEY ([user_id]) REFERENCES [dbo].[users]([id]) ON DELETE CASCADE
);

-- Ejemplo para asignar un rol a un usuario:
-- INSERT INTO [dbo].[users_roles] ([user_id], [role_id])
-- VALUES (@userId, (SELECT [id] FROM [dbo].[roles] WHERE [name] = 'admin'));
