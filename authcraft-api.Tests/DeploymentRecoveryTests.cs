using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace authcraft_api.Tests;

public sealed class DeploymentRecoveryTests
{
    [Theory]
    [InlineData("report")]
    [InlineData("script")]
    public void DatabaseDeployment_StopsAfterEvaluationFailureWithoutRollbackOrPublish(string failureStage)
    {
        using var sandbox = new RecoverySandbox();
        var result = sandbox.RunDatabaseDeployment(failureStage);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(
            failureStage == "report" ? ["/Action:DeployReport"] : ["/Action:DeployReport", "/Action:Script"],
            File.ReadAllLines(sandbox.SqlPackageLogPath));
        Assert.DoesNotContain("ROLLBACK", File.ReadAllText(sandbox.SqlPackageLogPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE", File.ReadAllText(sandbox.SqlPackageLogPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatabaseDeployment_BlocksProductionBeforeInvokingSqlPackage()
    {
        using var sandbox = new RecoverySandbox();
        var result = sandbox.RunDatabaseDeployment(failureStage: "", targetEnvironment: "production", mode: "Publish");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(sandbox.SqlPackageLogPath));
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatabaseDeployment_RequiresAuthenticatedReviewBeforePublishing()
    {
        using var sandbox = new RecoverySandbox();
        var result = sandbox.RunDatabaseDeployment(failureStage: "", mode: "Publish");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(
            ["/Action:DeployReport", "/Action:Script"],
            File.ReadAllLines(sandbox.SqlPackageLogPath));
        Assert.DoesNotContain("ROLLBACK", File.ReadAllText(sandbox.SqlPackageLogPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE", File.ReadAllText(sandbox.SqlPackageLogPath), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecoverySandbox : IDisposable
    {
        private readonly Dictionary<string, string?> _originalEnvironment = new()
        {
            ["AUTHCRAFT_SQLPACKAGE_TEST_LOG"] = Environment.GetEnvironmentVariable("AUTHCRAFT_SQLPACKAGE_TEST_LOG"),
            ["AUTHCRAFT_SQLPACKAGE_TEST_FAILURE"] = Environment.GetEnvironmentVariable("AUTHCRAFT_SQLPACKAGE_TEST_FAILURE"),
            ["AUTHCRAFT_SQL_ACCESS_TOKEN"] = Environment.GetEnvironmentVariable("AUTHCRAFT_SQL_ACCESS_TOKEN"),
            ["GITHUB_TOKEN"] = Environment.GetEnvironmentVariable("GITHUB_TOKEN"),
            ["GITHUB_REPOSITORY"] = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
        };

        private readonly string _scriptPath;
        private readonly string _sqlPackagePath;
        private readonly string _manifestPath;
        private readonly string _dacpacPath;

        public RecoverySandbox()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"authcraft-recovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            _scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "Invoke-DatabaseDeployment.ps1");
            _sqlPackagePath = Path.Combine(DirectoryPath, "mock-sqlpackage.ps1");
            _manifestPath = Path.Combine(DirectoryPath, "manifest.json");
            _dacpacPath = Path.Combine(DirectoryPath, "database.dacpac");
            SqlPackageLogPath = Path.Combine(DirectoryPath, "sqlpackage-actions.log");
            File.WriteAllBytes(_dacpacPath, [1, 2, 3, 4]);
            File.WriteAllText(_sqlPackagePath, """
                $action = @($args | Where-Object { $_ -like '/Action:*' })[0]
                [IO.File]::AppendAllText($env:AUTHCRAFT_SQLPACKAGE_TEST_LOG, "$action`n")
                if ($env:AUTHCRAFT_SQLPACKAGE_TEST_FAILURE -eq 'report' -and $action -eq '/Action:DeployReport') {
                    exit 1
                }
                if ($env:AUTHCRAFT_SQLPACKAGE_TEST_FAILURE -eq 'script' -and $action -eq '/Action:Script') {
                    exit 1
                }
                if ($action -eq '/Action:DeployReport') {
                    $output = @($args | Where-Object { $_ -like '/OutputPath:*' })[0].Substring('/OutputPath:'.Length)
                    [IO.File]::WriteAllText($output, '<DeployReport />')
                }
                if ($action -eq '/Action:Script') {
                    $output = @($args | Where-Object { $_ -like '/DeployScriptPath:*' })[0].Substring('/DeployScriptPath:'.Length)
                    [IO.File]::WriteAllText($output, 'CREATE TABLE dbo.synthetic_test (id INT);')
                }
                """);

            var manifest = new
            {
                commit_sha = new string('a', 40),
                run_id = "12345",
                database = new
                {
                    artifact = "dacpac",
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_dacpacPath))).ToLowerInvariant()
                }
            };
            File.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest));
        }

        public string DirectoryPath { get; }
        public string SqlPackageLogPath { get; }

        public ProcessResult RunDatabaseDeployment(
            string failureStage,
            string targetEnvironment = "staging",
            string mode = "Evaluate")
        {
            Environment.SetEnvironmentVariable("AUTHCRAFT_SQLPACKAGE_TEST_LOG", SqlPackageLogPath);
            Environment.SetEnvironmentVariable("AUTHCRAFT_SQLPACKAGE_TEST_FAILURE", failureStage);
            Environment.SetEnvironmentVariable("AUTHCRAFT_SQL_ACCESS_TOKEN", "synthetic-token-for-tests");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            Environment.SetEnvironmentVariable("GITHUB_REPOSITORY", null);
            try
            {
                var processInfo = new ProcessStartInfo("powershell.exe")
                {
                    WorkingDirectory = FindRepositoryRoot(),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                };
                var arguments = new[]
                {
                    "-NoProfile", "-NonInteractive", "-File", _scriptPath,
                    "-Mode", mode,
                    "-TargetEnvironment", targetEnvironment,
                    "-DacpacPath", _dacpacPath,
                    "-ManifestPath", _manifestPath,
                    "-ExpectedCommitSha", new string('a', 40),
                    "-ExpectedRunId", "12345",
                    "-SqlPackagePath", _sqlPackagePath
                };
                foreach (var argument in arguments)
                {
                    processInfo.ArgumentList.Add(argument);
                }

                using var process = Process.Start(processInfo)!;
                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new ProcessResult(process.ExitCode, output, error);
            }
            finally
            {
                foreach (var (name, value) in _originalEnvironment)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
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
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
