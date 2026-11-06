using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Infrastructure.Configuration;

public static class DatabaseConfiguration
{
    private const string AzureSqlServer = "authcraftdbbrasil.database.windows.net";
    private const string StagingDatabase = "authcraft-db-staging";
    private const string ProductionDatabase = "authcraft-db";

    public static string ResolveAndValidate(
        IConfiguration configuration,
        string? connectionStringOverride,
        string environmentName)
    {
        var connectionString = ResolveConnectionString(configuration, connectionStringOverride);
        if (environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(configuration["Testing:AllowInMemoryDatabase"], "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Testing requires an explicit in-memory test host.");
            }

            return connectionString;
        }

        SqlConnectionStringBuilder connection;
        try
        {
            connection = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("DatabaseConnectionString is not a valid SQL Server connection string.");
        }

        var expectedServer = configuration["Database:ExpectedServer"];
        var expectedDatabase = configuration["Database:ExpectedName"];
        if (string.IsNullOrWhiteSpace(expectedServer))
        {
            throw new InvalidOperationException("Database:ExpectedServer is required.");
        }

        if (string.IsNullOrWhiteSpace(expectedDatabase))
        {
            throw new InvalidOperationException("Database:ExpectedName is required.");
        }

        var actualServer = NormalizeServer(connection.DataSource);
        var configuredServer = NormalizeServer(expectedServer);
        if (!string.Equals(actualServer, configuredServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.InitialCatalog, expectedDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DatabaseConnectionString does not match Database:ExpectedServer and Database:ExpectedName.");
        }

        if (environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLocalServer(actualServer) ||
                expectedDatabase.Equals(StagingDatabase, StringComparison.OrdinalIgnoreCase) ||
                expectedDatabase.Equals(ProductionDatabase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Development requires a dedicated local SQL database.");
            }

            return connectionString;
        }

        var approvedDatabase = environmentName.Equals("Staging", StringComparison.OrdinalIgnoreCase)
            ? StagingDatabase
            : environmentName.Equals("Production", StringComparison.OrdinalIgnoreCase)
                ? ProductionDatabase
                : null;
        if (approvedDatabase is null ||
            !string.Equals(actualServer, AzureSqlServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedServer, AzureSqlServer, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedDatabase, approvedDatabase, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(connection.InitialCatalog, approvedDatabase, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"DatabaseConnectionString does not match the approved {environmentName} SQL target.");
        }

        return connectionString;
    }

    private static string ResolveConnectionString(IConfiguration configuration, string? connectionStringOverride)
    {
        var configuredValues = new[]
        {
            ("DatabaseConnectionString", configuration["DatabaseConnectionString"]),
            ("ConnectionStrings:DefaultConnection", configuration["ConnectionStrings:DefaultConnection"]),
            ("ConnectionStrings:AZURE_SQL_CONNECTIONSTRING", configuration["ConnectionStrings:AZURE_SQL_CONNECTIONSTRING"]),
            ("connectionStringOverride", connectionStringOverride)
        }.Where(item => !string.IsNullOrWhiteSpace(item.Item2)).ToArray();

        if (configuredValues.Length == 0)
        {
            throw new InvalidOperationException(
                "Database connection string is required in DatabaseConnectionString or a supported ConnectionStrings alias.");
        }

        var selectedValue = configuredValues[0].Item2!;
        if (configuredValues.Any(item => !string.Equals(item.Item2, selectedValue, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Conflicting database configuration aliases are set: {string.Join(", ", configuredValues.Select(item => item.Item1))}.");
        }

        return selectedValue;
    }

    private static string NormalizeServer(string server)
    {
        var normalized = server.Trim();
        if (normalized.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..];
        }

        var portSeparator = normalized.IndexOf(',');
        if (portSeparator >= 0)
        {
            normalized = normalized[..portSeparator];
        }

        return normalized.TrimEnd('.').ToLowerInvariant();
    }

    private static bool IsLocalServer(string server)
    {
        return server.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || server.Equals(".", StringComparison.Ordinal)
            || server.Equals("(local)", StringComparison.OrdinalIgnoreCase)
            || server.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)
            || server.Equals("127.0.0.1", StringComparison.Ordinal)
            || server.Equals("::1", StringComparison.Ordinal)
            || server.Equals("[::1]", StringComparison.Ordinal);
    }
}
