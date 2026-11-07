using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace authcraft_api.Tests;

public sealed class LocalEnvironmentSqlTests
{
    private const string ConnectionVariable = "AUTHCRAFT_LOCAL_ENVIRONMENT_SQL_CONNECTION";
    private const string SqlPackageFallback =
        @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe";

    [LocalEnvironmentSqlFact]
    public async Task BuiltDacpacPublishesPostdeployDataAndSupportsExistingRepository()
    {
        var sourceConnectionString = GetLocalDevelopmentConnectionString();
        var sqlPackagePath = Environment.GetEnvironmentVariable("SQLPACKAGE_PATH") ?? SqlPackageFallback;
        Assert.True(File.Exists(sqlPackagePath), "Set SQLPACKAGE_PATH to an installed SqlPackage.exe.");

        var repositoryRoot = FindRepositoryRoot();
        var dacpacDirectory = Path.Combine(repositoryRoot, "authcraft-db", "bin", "Release");
        var dacpacs = Directory.Exists(dacpacDirectory)
            ? Directory.GetFiles(dacpacDirectory, "*.dacpac")
            : [];
        Assert.True(dacpacs.Length == 1, $"Expected exactly one MSBuild DACPAC under {dacpacDirectory}.");

        var targetDatabaseName = $"authcraft_environment_test_{Guid.NewGuid():N}";
        var targetConnectionString = new SqlConnectionStringBuilder(sourceConnectionString)
        {
            InitialCatalog = targetDatabaseName
        }.ConnectionString;
        var evidenceDirectory = Path.Combine(Path.GetTempPath(), $"authcraft-local-sql-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(evidenceDirectory);
        var databaseCreated = false;

        try
        {
            await CreateEmptyDatabaseAsync(sourceConnectionString, targetDatabaseName);
            databaseCreated = true;

            var initializerPath = Path.Combine(repositoryRoot, "scripts", "Initialize-LocalEnvironment.ps1");
            var report = RunPowerShell(initializerPath,
                "-TargetServerName", new SqlConnectionStringBuilder(sourceConnectionString).DataSource,
                "-TargetDatabaseName", targetDatabaseName,
                "-SqlPackagePath", sqlPackagePath,
                "-EvidenceDirectory", evidenceDirectory);
            Assert.Equal(0, report.ExitCode);

            var reportPath = Path.Combine(evidenceDirectory, "local-deploy-report.xml");
            var scriptPath = Path.Combine(evidenceDirectory, "local-deploy.sql");
            var reportXml = System.Xml.Linq.XDocument.Load(reportPath);
            XNamespace reportNamespace = "http://schemas.microsoft.com/sqlserver/dac/DeployReport/2012/02";
            var operations = reportXml.Descendants(reportNamespace + "Operation").ToArray();
            Assert.NotEmpty(operations);
            Assert.Contains(operations, operation => (string?)operation.Attribute("Name") == "Create");
            Assert.All(operations,
                operation => Assert.Equal("Create", (string?)operation.Attribute("Name")));
            var deploymentScript = await File.ReadAllTextAsync(scriptPath);
            Assert.DoesNotMatch(
                new Regex(@"(?i)\bDROP\s+(?:DATABASE|TABLE|SCHEMA|COLUMN)\b|\bDELETE\s+FROM\b|\bTRUNCATE\s+TABLE\b"),
                deploymentScript);
            Assert.Contains("INSERT INTO dbo.providers", deploymentScript, StringComparison.OrdinalIgnoreCase);

            var reportHash = GetHash(report.Output, "Report");
            var scriptHash = GetHash(report.Output, "Script");
            var publish = RunPowerShell(initializerPath,
                "-TargetServerName", new SqlConnectionStringBuilder(sourceConnectionString).DataSource,
                "-TargetDatabaseName", targetDatabaseName,
                "-SqlPackagePath", sqlPackagePath,
                "-EvidenceDirectory", evidenceDirectory,
                "-Publish",
                "-ReviewedReportSha256", reportHash,
                "-ReviewedScriptSha256", scriptHash,
                "-ConfirmedTargetDatabase", targetDatabaseName);
            Assert.Equal(0, publish.ExitCode);

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(targetConnectionString)
                .Options;
            await using var context = new AppDbContext(options);
            var provider = await new ProviderRepository(context).GetByNameAsync("google");
            Assert.NotNull(provider);

            var tableCount = await context.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*) AS [Value] FROM sys.tables WHERE is_ms_shipped = 0")
                .SingleAsync();
            Assert.Equal(13, tableCount);

            await VerifySchemaReviewPermissionsAsync(targetConnectionString);
        }
        finally
        {
            if (databaseCreated)
            {
                await DropDatabaseAsync(sourceConnectionString, targetDatabaseName);
            }

            Directory.Delete(evidenceDirectory, recursive: true);
        }
    }

    private static async Task VerifySchemaReviewPermissionsAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            CREATE USER [authcraft_schema_review_test] WITHOUT LOGIN;
            GRANT CONNECT TO [authcraft_schema_review_test];
            GRANT VIEW DEFINITION TO [authcraft_schema_review_test];
            EXECUTE AS USER = N'authcraft_schema_review_test';
            """ + "\n" + StagingPublisherSqlTests.ReviewPermissionQuery + "\nREVERT;";
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(reader.GetOrdinal("can_read_dependencies")));
            Assert.Equal(0, reader.GetInt32(reader.GetOrdinal("can_read_refactorlog")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => StagingPublisherSqlTests.AssertReviewPermissions(reader));
        }

        command.CommandText = """
            GRANT SELECT ON OBJECT::sys.sql_expression_dependencies TO [authcraft_schema_review_test];
            EXECUTE AS USER = N'authcraft_schema_review_test';
            """ + "\n" + StagingPublisherSqlTests.ReviewPermissionQuery + "\nREVERT;";
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(reader.GetOrdinal("can_read_refactorlog")));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => StagingPublisherSqlTests.AssertReviewPermissions(reader));
        }

        command.CommandText = """
            GRANT SELECT ON OBJECT::dbo.__RefactorLog TO [authcraft_schema_review_test];
            EXECUTE AS USER = N'authcraft_schema_review_test';
            """ + "\n" + StagingPublisherSqlTests.ReviewPermissionQuery + "\nREVERT;";
        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            StagingPublisherSqlTests.AssertReviewPermissions(reader);
            Assert.Equal(13, reader.GetInt32(reader.GetOrdinal("visible_table_count")));
        }

        command.CommandText = """
            GRANT INSERT ON OBJECT::dbo.providers TO [authcraft_schema_review_test];
            EXECUTE AS USER = N'authcraft_schema_review_test';
            """ + "\n" + StagingPublisherSqlTests.ReviewPermissionQuery + "\nREVERT;";
        await using var elevatedPermissions = await command.ExecuteReaderAsync();
        Assert.True(await elevatedPermissions.ReadAsync());
        Assert.Equal(1, elevatedPermissions.GetInt32(elevatedPermissions.GetOrdinal("can_run_postdeploy")));
        Assert.Equal(1, elevatedPermissions.GetInt32(elevatedPermissions.GetOrdinal("unapproved_grant_count")));
        Assert.Equal(1, elevatedPermissions.GetInt32(elevatedPermissions.GetOrdinal("writable_table_count")));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => StagingPublisherSqlTests.AssertReviewPermissions(elevatedPermissions));
    }

    private static string GetLocalDevelopmentConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        Assert.False(string.IsNullOrWhiteSpace(connectionString),
            $"Set {ConnectionVariable} to the dedicated local development connection string.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        var isLocalDb = builder.DataSource.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase);
        Assert.True(
            string.Equals(builder.DataSource, "localhost", StringComparison.OrdinalIgnoreCase) || isLocalDb,
            $"{ConnectionVariable} must use localhost or LocalDB.");
        Assert.Equal("authcraft_development", builder.InitialCatalog);
        return builder.ConnectionString;
    }

    private static async Task CreateEmptyDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID(N'{databaseName}') IS NOT NULL THROW 51000, 'Test database already exists.', 1; CREATE DATABASE [{databaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string connectionString, string databaseName)
    {
        await using var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        }.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}];";
        await command.ExecuteNonQueryAsync();
    }

    private static string GetHash(string output, string artifact)
    {
        var match = Regex.Match(output, $@"(?m)^{artifact}: .+ \(SHA256 ([A-Fa-f0-9]{{64}})\)\r?$");
        Assert.True(match.Success, $"Could not read the {artifact} SHA-256 from initializer output: {output}");
        return match.Groups[1].Value;
    }

    private static ProcessResult RunPowerShell(string scriptPath, params string[] arguments)
    {
        var processInfo = new ProcessStartInfo("powershell.exe")
        {
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", scriptPath }.Concat(arguments))
        {
            processInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(processInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "authcraft-api.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class LocalEnvironmentSqlFactAttribute : FactAttribute
{
    public LocalEnvironmentSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AUTHCRAFT_LOCAL_ENVIRONMENT_SQL_CONNECTION")))
        {
            Skip = "Set AUTHCRAFT_LOCAL_ENVIRONMENT_SQL_CONNECTION to run the LocalDB DACPAC integration test.";
        }
    }
}
