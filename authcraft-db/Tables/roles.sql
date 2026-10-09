CREATE TABLE [dbo].[roles]
(
    [id] INT NOT NULL PRIMARY KEY IDENTITY,
    [name] NVARCHAR(50) NOT NULL,
    CONSTRAINT [UQ_roles_name] UNIQUE ([name])
);

