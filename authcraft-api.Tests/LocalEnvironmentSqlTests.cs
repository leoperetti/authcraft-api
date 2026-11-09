using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace authcraft_api.Tests;

public sealed class LocalEnvironmentSqlTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const string ConnectionVariable = "AUTHCRAFT_LOCAL_ENVIRONMENT_SQL_CONNECTION";
    private const string SqlPackageFallback =
        @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe";

    [LocalEnvironmentSqlFact]
    public async Task TransactionalPublishDocumentsSeparateSchemaAndPostdeploymentCoverage()
    {
        var evidenceDirectory = Environment.GetEnvironmentVariable("AUTHCRAFT_TRANSACTION_EVIDENCE_DIRECTORY");
        if (evidenceDirectory is not null)
        {
            Assert.True(Path.IsPathFullyQualified(evidenceDirectory));
            Assert.False(Path.GetFullPath(evidenceDirectory).StartsWith(
                FindRepositoryRoot() + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            Directory.CreateDirectory(evidenceDirectory);
            Assert.False(File.Exists(Path.Combine(evidenceDirectory, "transaction-verification.json")),
                "Transaction evidence must be generated in a new directory.");
        }
        var cases = new List<object>();
        cases.AddRange(await VerifyTransactionalPublishAsync(false, evidenceDirectory));
        cases.AddRange(await VerifyTransactionalPublishAsync(true, evidenceDirectory));
        if (evidenceDirectory is not null)
        {
            var sqlPackagePath = Environment.GetEnvironmentVariable("SQLPACKAGE_PATH") ?? SqlPackageFallback;
            var version = await RunToolAsync(sqlPackagePath, "/Version");
            Assert.Equal(0, version.ExitCode);
            Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", version.Output.Trim());
            var repositoryRoot = FindRepositoryRoot();
            var proof = new
            {
                schema_version = 1,
                sqlpackage_version = version.Output.Trim(),
                sqlpackage_sha256 = HashFile(sqlPackagePath),
                postdeployment_sha256 = HashFile(Path.Combine(repositoryRoot, "authcraft-db", "postdeploys", "Script.PostDeployment.sql")),
                validation_sha256 = HashFile(Path.Combine(repositoryRoot, "authcraft-db", "postdeploys", "ValidateConstraints.sql")),
                fixture_sha256 = HashFile(Path.Combine(repositoryRoot, "authcraft-api.Tests", "LocalEnvironmentSqlTests.cs")),
                cases
            };
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "transaction-verification.json"),
                JsonSerializer.Serialize(proof));
        }
    }

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private async Task<List<object>> VerifyTransactionalPublishAsync(bool protectedPostdeployment, string? evidenceDirectory)
    {
        var sourceConnectionString = GetLocalDevelopmentConnectionString();
        var sqlPackagePath = Environment.GetEnvironmentVariable("SQLPACKAGE_PATH") ?? SqlPackageFallback;
        var directory = Path.Combine(Path.GetTempPath(), $"authcraft-transaction-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var outcomes = new List<string>();
        var cases = new List<object>();
        try
        {
            var baseline = await BuildTransactionFixtureAsync(directory, "baseline", false);
            var candidate = await BuildTransactionFixtureAsync(directory, "candidate", true, protectedPostdeployment);
            var version = await RunToolAsync(sqlPackagePath, "/Version");
            output.WriteLine(version.Output);
            foreach (var scenario in new[] { "success", "547", "51000" })
            {
                var databaseName = $"authcraft_environment_test_{Guid.NewGuid():N}";
                var targetConnectionString = new SqlConnectionStringBuilder(sourceConnectionString)
                {
                    InitialCatalog = databaseName
                }.ConnectionString;
                await CreateEmptyDatabaseAsync(sourceConnectionString, databaseName);
                try
                {
                    string[] Arguments(string action, string package) =>
                    [
                        $"/Action:{action}", $"/SourceFile:{package}",
                        $"/TargetConnectionString:{targetConnectionString}",
                        "/p:BlockOnPossibleDataLoss=True", "/p:DropObjectsNotInSource=False",
                        "/p:ScriptDatabaseOptions=False", "/p:IncludeTransactionalScripts=True"
                    ];
                    var initialPublish = await RunToolAsync(sqlPackagePath, Arguments("Publish", baseline));
                    Assert.True(initialPublish.ExitCode == 0, initialPublish.Output + initialPublish.Error);
                    await ExecuteFixtureSqlAsync(targetConnectionString,
                        $"INSERT dbo.transaction_probe (id, status) VALUES (1, {(scenario == "547" ? "-1" : scenario == "51000" ? "51000" : "1")});");
                    var scriptPath = Path.Combine(directory, $"{scenario}.sql");
                    var script = await RunToolAsync(sqlPackagePath,
                        [.. Arguments("Script", candidate), $"/OutputPath:{scriptPath}"]);
                    Assert.True(script.ExitCode == 0, script.Output + script.Error);
                    var sql = await File.ReadAllTextAsync(scriptPath);
                    output.WriteLine($"Scenario {scenario}; SQL SHA256 {Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(scriptPath)))}");
                    var scriptLines = sql.Split('\n');
                    for (var lineIndex = 0; lineIndex < scriptLines.Length; lineIndex++)
                    {
                        if (Regex.IsMatch(scriptLines[lineIndex],
                            @"(?i)BEGIN TRAN|COMMIT|ROLLBACK|XACT_ABORT|THROW|WITH CHECK CHECK|INSERT dbo.transaction_seeds"))
                            output.WriteLine($"{lineIndex + 1}: {scriptLines[lineIndex].Trim()}");
                    }
                    var publish = await RunToolAsync(sqlPackagePath, Arguments("Publish", candidate));
                    await using var connection = new SqlConnection(targetConnectionString);
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = """
                        SELECT
                            (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.transaction_probe') AND name = N'new_value'),
                            (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_transaction_probe_status'),
                            (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N'CK_transaction_probe_status' AND is_disabled = 0 AND is_not_trusted = 0),
                            (SELECT COUNT(*) FROM dbo.transaction_seeds WHERE id = 2),
                            (SELECT COUNT(*) FROM dbo.transaction_probe WHERE id = 1);
                        """;
                    await using var reader = await command.ExecuteReaderAsync();
                    Assert.True(await reader.ReadAsync());
                    var state = Enumerable.Range(0, 5).Select(reader.GetInt32).ToArray();
                    output.WriteLine($"protected={protectedPostdeployment}; {scenario}: exit={publish.ExitCode}; column/check/trusted/seed/original={string.Join(",", state)}");
                    Assert.Equal(1, state[4]);
                    if (scenario == "success")
                    {
                        Assert.True(publish.ExitCode == 0, publish.Output + publish.Error);
                        Assert.Equal(new[] { 1, 1, 1, 1, 1 }, state);
                    }
                    else
                    {
                        Assert.NotEqual(0, publish.ExitCode);
                        Assert.Contains(scenario, publish.Output + publish.Error, StringComparison.Ordinal);
                        var expected = protectedPostdeployment
                            ? new[] { 1, 1, 0, 0, 1 }
                            : new[] { 1, 1, scenario == "51000" ? 1 : 0, 1, 1 };
                        if (!state.SequenceEqual(expected))
                            outcomes.Add($"{scenario}: unexpected phased state ({string.Join(",", state)})");
                    }
                    var sqlFile = $"{(protectedPostdeployment ? "protected" : "control")}-{scenario}.sql";
                    if (evidenceDirectory is not null)
                        File.Copy(scriptPath, Path.Combine(evidenceDirectory, sqlFile), overwrite: false);
                    cases.Add(new
                    {
                        protected_postdeployment = protectedPostdeployment,
                        scenario,
                        exit_code = publish.ExitCode,
                        state,
                        sql_file = sqlFile,
                        sql_sha256 = HashFile(scriptPath),
                        boundaries = scriptLines.Select((line, index) => new { line = index + 1, statement = line.Trim() })
                            .Where(boundary => Regex.IsMatch(boundary.statement,
                                @"(?i)BEGIN TRAN|COMMIT|ROLLBACK|XACT_ABORT|THROW|WITH CHECK CHECK|INSERT dbo.transaction_seeds"))
                            .ToArray()
                    });
                }
                finally
                {
                    await DropDatabaseAsync(sourceConnectionString, databaseName);
                }
            }
            Assert.True(outcomes.Count == 0,
                "Transactional coverage gate failed: " + string.Join("; ", outcomes));
            return cases;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<string> BuildTransactionFixtureAsync(
        string directory, string name, bool candidate, bool protectedPostdeployment = false)
    {
        var fixtureDirectory = Path.Combine(directory, name);
        Directory.CreateDirectory(fixtureDirectory);
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), "authcraft-db", "authcraft-db.sqlproj"));
        XNamespace projectNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
        project.Root!.Elements(projectNamespace + "ItemGroup").Remove();
        project.Root.Add(new XElement(projectNamespace + "ItemGroup",
            new XElement(projectNamespace + "Build", new XAttribute("Include", "schema.sql")),
            new XElement(projectNamespace + "PostDeploy", new XAttribute("Include", "postdeploy.sql"))));
        var projectPath = Path.Combine(fixtureDirectory, "fixture.sqlproj");
        project.Save(projectPath);
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "schema.sql"),
            "CREATE TABLE dbo.transaction_probe (id int NOT NULL PRIMARY KEY, status int NOT NULL" +
            (candidate ? ", new_value int NULL, CONSTRAINT CK_transaction_probe_status CHECK (status > 0)" : "") +
            ");\nGO\nCREATE TABLE dbo.transaction_seeds (id int NOT NULL PRIMARY KEY);");
        var postdeployment = candidate ? """
            INSERT dbo.transaction_seeds (id) VALUES (2);
            ALTER TABLE dbo.transaction_probe WITH CHECK CHECK CONSTRAINT CK_transaction_probe_status;
            IF EXISTS (SELECT 1 FROM dbo.transaction_probe WHERE status = 51000)
                THROW 51000, 'Synthetic postdeployment guard failure.', 1;
            """ : "PRINT N'Synthetic baseline';";
        if (protectedPostdeployment)
        {
            var template = await File.ReadAllTextAsync(Path.Combine(
                FindRepositoryRoot(), "authcraft-db", "postdeploys", "Script.PostDeployment.sql"));
            const string begin = "BEGIN TRANSACTION;";
            const string commit = "COMMIT TRANSACTION;";
            var beginIndex = template.IndexOf(begin, StringComparison.Ordinal);
            var commitIndex = template.IndexOf(commit, StringComparison.Ordinal);
            Assert.True(beginIndex >= 0 && commitIndex > beginIndex, "Versioned postdeploy transaction is missing.");
            postdeployment = template[..(beginIndex + begin.Length)] + "\n" +
                postdeployment + "\n" + template[commitIndex..];
        }
        await File.WriteAllTextAsync(Path.Combine(fixtureDirectory, "postdeploy.sql"), postdeployment);
        var discovery = await RunToolAsync(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft Visual Studio", "Installer", "vswhere.exe"),
            "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.SQL.SSDT", "-property", "installationPath");
        Assert.True(discovery.ExitCode == 0 && !string.IsNullOrWhiteSpace(discovery.Output),
            "An SSDT-enabled Visual Studio installation is required.");
        var installation = discovery.Output.Trim();
        var dacDirectory = Path.Combine(installation, "Common7", "IDE", "Extensions", "Microsoft", "SQLDB", "DAC");
        var ssdtDirectory = Directory.GetDirectories(Path.Combine(installation, "MSBuild", "Microsoft", "VisualStudio"), "v*")
            .Select(versionDirectory => Path.Combine(versionDirectory, "SSDT")).Single(Directory.Exists);
        var build = await RunToolAsync(Path.Combine(installation, "MSBuild", "Current", "Bin", "MSBuild.exe"),
            projectPath, "/p:Configuration=Release", "/verbosity:minimal",
            $"/p:SQLDBExtensionsRefPath={ssdtDirectory}",
            $"/p:SQLDBExtensionsRefPath64={dacDirectory}",
            $"/p:SSDTPath={dacDirectory}",
            $"/p:DevEnvDir={Path.Combine(installation, "Common7", "IDE")}{Path.DirectorySeparatorChar}");
        Assert.True(build.ExitCode == 0, build.Output + build.Error);
        return Path.Combine(fixtureDirectory, "bin", "Release", "fixture.dacpac");
    }

    private static async Task ExecuteFixtureSqlAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<ProcessResult> RunToolAsync(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await standardOutput, await standardError);
    }

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

            var argumentsLogPath = Path.Combine(evidenceDirectory, "sqlpackage-arguments.log");
            var realSqlPackagePath = sqlPackagePath;
            sqlPackagePath = Path.Combine(evidenceDirectory, "sqlpackage-wrapper.ps1");
            await File.WriteAllTextAsync(sqlPackagePath, $$"""
                [IO.File]::AppendAllText('{{argumentsLogPath}}', ($args -join '|') + "`n")
                & '{{realSqlPackagePath}}' @args
                exit $LASTEXITCODE
                """);
            var initializerPath = Path.Combine(repositoryRoot, "scripts", "Initialize-LocalEnvironment.ps1");
            var report = RunPowerShell(initializerPath,
                "-TargetServerName", new SqlConnectionStringBuilder(sourceConnectionString).DataSource,
                "-TargetDatabaseName", targetDatabaseName,
                "-SqlPackagePath", sqlPackagePath,
                "-EvidenceDirectory", evidenceDirectory);
            Assert.True(report.ExitCode == 0, report.Output + report.Error);

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
                new Regex(@"(?im)\bDROP\s+(?:DATABASE|TABLE(?!\s+\[?#tmpErrors\]?[ \t]*(?:;|\r?$))|SCHEMA|COLUMN)\b|\bDELETE\s+FROM\b|\bTRUNCATE\s+TABLE\b"),
                deploymentScript);
            Assert.Contains("INSERT INTO dbo.providers", deploymentScript, StringComparison.OrdinalIgnoreCase);
            var gateHarnessPath = Path.Combine(evidenceDirectory, "verify-generated-gate.ps1");
            await File.WriteAllTextAsync(gateHarnessPath, """
                param([string]$RepositoryRoot, [string]$ScriptPath)
                $ErrorActionPreference = 'Stop'
                $publisher = Join-Path $RepositoryRoot 'scripts\Invoke-DatabaseDeployment.ps1'
                $tokens = $null
                $errors = $null
                $ast = [System.Management.Automation.Language.Parser]::ParseFile($publisher, [ref]$tokens, [ref]$errors)
                if ($errors.Count -gt 0) { throw 'Publisher syntax error.' }
                $gate = $ast.Find({
                    param($node)
                    $node -is [System.Management.Automation.Language.IfStatementAst] -and
                    $node.Extent.Text.StartsWith("if (`$Mode -eq 'Publish') {") -and
                    $node.Extent.Text.Contains('$postdeploymentPath = Join-Path')
                }, $true)
                if ($null -eq $gate) { throw 'Publisher postdeployment gate not found.' }
                $Mode = 'Publish'
                $attempt = @{ postdeploy_transaction = $false }
                $deploymentSql = Get-Content -LiteralPath $ScriptPath -Raw -Encoding UTF8
                $scriptsRoot = Join-Path $RepositoryRoot 'scripts'
                . ([ScriptBlock]::Create($gate.Extent.Text.Replace('$PSScriptRoot', "'$scriptsRoot'")))
                """);
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
            Assert.True(publish.ExitCode == 0, publish.Output + publish.Error);
            var recheck = RunPowerShell(initializerPath,
                "-TargetServerName", new SqlConnectionStringBuilder(sourceConnectionString).DataSource,
                "-TargetDatabaseName", targetDatabaseName,
                "-SqlPackagePath", sqlPackagePath,
                "-EvidenceDirectory", evidenceDirectory);
            Assert.True(recheck.ExitCode == 0, recheck.Output + recheck.Error);
            var gateResult = RunPowerShell(gateHarnessPath,
                "-RepositoryRoot", repositoryRoot, "-ScriptPath", scriptPath);
            Assert.True(gateResult.ExitCode == 0, gateResult.Output + gateResult.Error);
            var invocations = await File.ReadAllLinesAsync(argumentsLogPath);
            Assert.Equal(7, invocations.Length);
            Assert.All(invocations, invocation =>
            {
                Assert.Contains("/p:IncludeTransactionalScripts=True", invocation.Split('|'));
                Assert.Contains("/p:ScriptDatabaseOptions=False", invocation.Split('|'));
            });

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

            await VerifyPostdeploymentRejectsExistingTransactionAsync(targetConnectionString, repositoryRoot);
            await VerifyConstraintValidationAsync(targetConnectionString, repositoryRoot);
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

    private static async Task VerifyPostdeploymentRejectsExistingTransactionAsync(
        string connectionString, string repositoryRoot)
    {
        var postdeployDirectory = Path.Combine(repositoryRoot, "authcraft-db", "postdeploys");
        var postdeployment = (await File.ReadAllTextAsync(Path.Combine(postdeployDirectory, "Script.PostDeployment.sql")))
            .Replace(":r .\\ValidateConstraints.sql",
                await File.ReadAllTextAsync(Path.Combine(postdeployDirectory, "ValidateConstraints.sql")));
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = (SqlTransaction)transaction;
        command.CommandText = "DELETE FROM dbo.providers WHERE name = 'github';";
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        command.CommandText = postdeployment;
        var failure = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(51001, failure.Number);
        command.CommandText = "SELECT COUNT(*) FROM dbo.providers WHERE name = 'github';";
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
        await transaction.RollbackAsync();
    }

    private static async Task VerifyConstraintValidationAsync(string connectionString, string repositoryRoot)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        const string untrustedConstraintCount = """
            SELECT (SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled = 1 OR is_not_trusted = 1)
                 + (SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled = 1 OR is_not_trusted = 1);
            """;
        command.CommandText = untrustedConstraintCount;
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));

        command.CommandText = """
            ALTER TABLE dbo.tenants NOCHECK CONSTRAINT CK_tenants_status;
            INSERT INTO dbo.tenants (client_id, slug, name, status)
            VALUES ('constraint-validation-test', 'constraint-validation-test', 'Constraint validation test', 'invalid');
            """;
        await command.ExecuteNonQueryAsync();
        var validationSql = await File.ReadAllTextAsync(
            Path.Combine(repositoryRoot, "authcraft-db", "postdeploys", "ValidateConstraints.sql"));
        command.CommandText = validationSql;
        var exception = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(547, exception.Number);

        command.CommandText = "DELETE FROM dbo.tenants WHERE client_id = 'constraint-validation-test';";
        await command.ExecuteNonQueryAsync();
        command.CommandText = validationSql;
        await command.ExecuteNonQueryAsync();
        command.CommandText = untrustedConstraintCount;
        Assert.Equal(0, Convert.ToInt32(await command.ExecuteScalarAsync()));
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
