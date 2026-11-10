using Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace authcraft_api.Tests.Infrastructure;

public sealed class InvitationSignupSqlServerFixture : IAsyncLifetime, IAsyncDisposable
{
    public const string ConnectionVariable = "AUTHCRAFT_TEST_SQL_CONNECTION";
    private readonly string _connectionString;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly List<Func<AppDbContext, Task>> _cleanup = [];

    public InvitationSignupSqlServerFixture()
    {
        var parsed = new SqlConnectionStringBuilder(ValidateConnectionString(
            Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            InitialCatalog = $"authcraft_invitation_test_{Guid.NewGuid():N}"
        };
        _connectionString = parsed.ConnectionString;
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options;
    }

    public static string ValidateConnectionString(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
        {
            throw new InvalidOperationException($"Set {ConnectionVariable} to a dedicated local test database.");
        }

        var parsed = new SqlConnectionStringBuilder(connection);
        var localDb = parsed.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase);
        if ((!string.Equals(parsed.DataSource, "localhost", StringComparison.OrdinalIgnoreCase) && !localDb)
            || !parsed.InitialCatalog.StartsWith("authcraft_invitation_test_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{ConnectionVariable} must use localhost or SQL Server LocalDB and a database named authcraft_invitation_test_<suffix>.");
        }

        return parsed.ConnectionString;
    }

    public AppDbContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("""
            IF COL_LENGTH('dbo.invitations', 'purpose') IS NULL ALTER TABLE dbo.invitations ADD purpose NVARCHAR(32) NULL;
            IF COL_LENGTH('dbo.invitations', 'student_id') IS NULL ALTER TABLE dbo.invitations ADD student_id BIGINT NULL;
            IF COL_LENGTH('dbo.invitations', 'first_name') IS NULL ALTER TABLE dbo.invitations ADD first_name NVARCHAR(150) NULL;
            IF COL_LENGTH('dbo.invitations', 'last_name') IS NULL ALTER TABLE dbo.invitations ADD last_name NVARCHAR(150) NULL;
            IF COL_LENGTH('dbo.invitations', 'full_name') IS NULL ALTER TABLE dbo.invitations ADD full_name NVARCHAR(150) NULL;
            IF COL_LENGTH('dbo.invitations', 'phone_number') IS NULL ALTER TABLE dbo.invitations ADD phone_number NVARCHAR(30) NULL;
            IF COL_LENGTH('dbo.invitations', 'dni') IS NULL ALTER TABLE dbo.invitations ADD dni NVARCHAR(16) NULL;
            IF COL_LENGTH('dbo.invitations', 'accepted_identity_id') IS NULL ALTER TABLE dbo.invitations ADD accepted_identity_id BIGINT NULL;
            IF COL_LENGTH('dbo.invitations', 'accepted_at') IS NULL ALTER TABLE dbo.invitations ADD accepted_at DATETIMEOFFSET NULL;
            IF COL_LENGTH('dbo.invitations', 'registered_first_name') IS NULL ALTER TABLE dbo.invitations ADD registered_first_name NVARCHAR(150) NULL;
            IF COL_LENGTH('dbo.invitations', 'registered_last_name') IS NULL ALTER TABLE dbo.invitations ADD registered_last_name NVARCHAR(150) NULL;
            IF COL_LENGTH('dbo.invitations', 'registered_phone_number') IS NULL ALTER TABLE dbo.invitations ADD registered_phone_number NVARCHAR(30) NULL;
            IF COL_LENGTH('dbo.invitations', 'registered_dni') IS NULL ALTER TABLE dbo.invitations ADD registered_dni NVARCHAR(16) NULL;
            IF COL_LENGTH('dbo.invitations', 'row_version') IS NULL ALTER TABLE dbo.invitations ADD row_version ROWVERSION NOT NULL;
            """);
        await db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_invitations_accepted_identity')
                ALTER TABLE dbo.invitations ADD CONSTRAINT FK_invitations_accepted_identity FOREIGN KEY (accepted_identity_id) REFERENCES dbo.identities(id);
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_invitations_purpose')
                ALTER TABLE dbo.invitations ADD CONSTRAINT CK_invitations_purpose CHECK (purpose IS NULL OR purpose = 'gym_student');
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_invitations_student_profile')
                ALTER TABLE dbo.invitations ADD CONSTRAINT CK_invitations_student_profile CHECK (purpose IS NULL OR (student_id > 0 AND full_name IS NOT NULL AND LEN(full_name) > 0 AND ((first_name IS NULL AND last_name IS NULL) OR (first_name IS NOT NULL AND last_name IS NOT NULL))));
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_invitations_student_dni')
                ALTER TABLE dbo.invitations ADD CONSTRAINT CK_invitations_student_dni CHECK (dni IS NULL OR (len(replace(replace(dni,N'.',N''),N' ',N''))=(8) OR len(replace(replace(dni,N'.',N''),N' ',N''))=(7)) AND NOT [dni] like N'%[^0-9. ]%');
            IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_invitations_registered_dni')
                ALTER TABLE dbo.invitations ADD CONSTRAINT CK_invitations_registered_dni CHECK (registered_dni IS NULL OR (len(replace(replace(registered_dni,N'.',N''),N' ',N''))=(8) OR len(replace(replace(registered_dni,N'.',N''),N' ',N''))=(7)) AND NOT [registered_dni] like N'%[^0-9. ]%');
            """);
        await db.Database.OpenConnectionAsync();
    }

    public void RegisterCleanup(Func<AppDbContext, Task> cleanup) => _cleanup.Add(cleanup);

    public async Task CleanupAsync()
    {
        await using var db = CreateContext();
        while (_cleanup.Count > 0)
        {
            var last = _cleanup.Count - 1;
            await _cleanup[last](db);
            _cleanup.RemoveAt(last);
        }
    }

    public async ValueTask DisposeAsync() => await DisposeDatabaseAsync();

    Task IAsyncLifetime.DisposeAsync() => DisposeDatabaseAsync();

    private async Task DisposeDatabaseAsync()
    {
        await CleanupAsync();
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition("InvitationSignupSqlServer")]
public sealed class InvitationSignupSqlServerCollection : ICollectionFixture<InvitationSignupSqlServerFixture>;
