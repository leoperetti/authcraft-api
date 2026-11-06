using Microsoft.Data.SqlClient;
using Xunit.Sdk;

namespace authcraft_api.Tests;

public sealed class StagingPublisherSqlTests
{
    [StagingPublisherSqlFact]
    public async Task OidcPublisherHasOnlyApprovedReviewPermissions()
    {
        Assert.Equal("true", Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
        Assert.Equal("leoperetti/authcraft-api", Environment.GetEnvironmentVariable("GITHUB_REPOSITORY"));
        Assert.Equal("refs/heads/main", Environment.GetEnvironmentVariable("GITHUB_REF"));
        Assert.Equal("workflow_dispatch", Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME"));
        var token = Environment.GetEnvironmentVariable("AUTHCRAFT_SQL_ACCESS_TOKEN");
        Assert.False(string.IsNullOrWhiteSpace(token), "A process-scoped SQL token is required.");

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await VerifyPermissionsAsync(token!, timeout.Token);
                return;
            }
            catch (SqlException exception) when (exception.Number == 40613 && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), timeout.Token);
            }
            catch (SqlException exception)
            {
                throw new XunitException($"Staging SQL verification failed (SQL error {exception.Number}); native details withheld.");
            }
            catch (OperationCanceledException)
            {
                throw new XunitException("Staging SQL verification exceeded its bounded timeout.");
            }
        }
        throw new XunitException("Staging SQL verification did not complete.");
    }

    private static async Task VerifyPermissionsAsync(string token, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(
            "Server=tcp:authcraftdbbrasil.database.windows.net,1433;Database=authcraft-db-staging;Encrypt=True;TrustServerCertificate=False;Connect Timeout=30;ConnectRetryCount=0")
        {
            AccessToken = token
        };
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = ReviewPermissionQuery;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken), "Expected authenticated SQL principal metadata.");
        Assert.Equal("authcraft-db-staging", reader.GetString(reader.GetOrdinal("database_name")));
        Assert.Equal("authcraft-staging-schema-publisher", reader.GetString(reader.GetOrdinal("principal_name")));
        Assert.Equal(new Guid("a2382f63-d653-47f3-82ca-9f2696f63f0a"),
            Guid.Parse(reader.GetString(reader.GetOrdinal("application_id"))));
        AssertReviewPermissions(reader);
        Assert.False(await reader.ReadAsync(cancellationToken));
    }

    internal static void AssertReviewPermissions(SqlDataReader reader)
    {
        Assert.Equal(1, reader.GetInt32(reader.GetOrdinal("can_connect")));
        Assert.Equal(1, reader.GetInt32(reader.GetOrdinal("can_view_definition")));
        Assert.True(reader.GetInt32(reader.GetOrdinal("visible_table_count")) > 0);
        foreach (var permission in new[]
                 {
                     "can_alter", "can_control", "can_create_table", "can_alter_schema",
                     "can_read_data", "can_run_postdeploy", "is_db_owner", "explicit_role_count",
                     "unapproved_grant_count", "writable_table_count"
                 })
        {
            Assert.Equal(0, reader.GetInt32(reader.GetOrdinal(permission)));
        }
    }

    internal const string ReviewPermissionQuery = """
            SELECT DB_NAME() AS database_name, USER_NAME() AS principal_name,
                   CONVERT(varchar(36), CONVERT(uniqueidentifier, sid)) AS application_id,
                   HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONNECT') AS can_connect,
                   HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS can_view_definition,
                   HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER') AS can_alter,
                   HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') AS can_control,
                   HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') AS can_create_table,
                   HAS_PERMS_BY_NAME('dbo', 'SCHEMA', 'ALTER') AS can_alter_schema,
                   HAS_PERMS_BY_NAME('dbo.providers', 'OBJECT', 'SELECT') AS can_read_data,
                   HAS_PERMS_BY_NAME('dbo.providers', 'OBJECT', 'INSERT') AS can_run_postdeploy,
                   IS_ROLEMEMBER('db_owner') AS is_db_owner,
                   (SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0) AS visible_table_count,
                   (SELECT COUNT(*) FROM sys.database_role_members
                    WHERE member_principal_id = USER_ID()) AS explicit_role_count,
                   (SELECT COUNT(*) FROM sys.database_permissions
                    WHERE grantee_principal_id = USER_ID()
                      AND NOT (class = 0 AND state = 'G' AND permission_name IN ('CONNECT', 'VIEW DEFINITION'))) AS unapproved_grant_count,
                   (SELECT COUNT(*) FROM sys.tables AS table_metadata
                    WHERE table_metadata.is_ms_shipped = 0 AND
                      (HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(table_metadata.schema_id)) + '.' + QUOTENAME(table_metadata.name), 'OBJECT', 'INSERT') = 1 OR
                       HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(table_metadata.schema_id)) + '.' + QUOTENAME(table_metadata.name), 'OBJECT', 'UPDATE') = 1 OR
                       HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(table_metadata.schema_id)) + '.' + QUOTENAME(table_metadata.name), 'OBJECT', 'DELETE') = 1 OR
                       HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(table_metadata.schema_id)) + '.' + QUOTENAME(table_metadata.name), 'OBJECT', 'ALTER') = 1 OR
                       HAS_PERMS_BY_NAME(QUOTENAME(SCHEMA_NAME(table_metadata.schema_id)) + '.' + QUOTENAME(table_metadata.name), 'OBJECT', 'CONTROL') = 1)) AS writable_table_count
            FROM sys.database_principals WHERE principal_id = USER_ID();
            """;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class StagingPublisherSqlFactAttribute : FactAttribute
{
    public StagingPublisherSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("AUTHCRAFT_VERIFY_STAGING_PUBLISHER") != "true")
        {
            Skip = "Only the explicitly dispatched Staging publisher diagnostic enables this remote SQL check.";
        }
    }
}
