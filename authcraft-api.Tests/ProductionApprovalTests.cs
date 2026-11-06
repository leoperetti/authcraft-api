using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace authcraft_api.Tests;

public sealed class ProductionApprovalTests
{
    [Theory]
    [InlineData("protection-missing")]
    [InlineData("protection-incompatible")]
    [InlineData("approval-rejected")]
    [InlineData("approval-other-release")]
    [InlineData("non-main-reference")]
    [InlineData("reviewed-report-changed")]
    [InlineData("artifact-expired")]
    [InlineData("workflow-dispatch")]
    [InlineData("approval-boolean")]
    [InlineData("main-pull-request")]
    public void Production_RemainsBlockedBeforeExternalCommandsRegardlessOfUntrustedApprovalInputs(string scenario)
    {
        using var sandbox = new ApprovalSandbox(scenario);
        var originalFiles = sandbox.SnapshotFiles();

        foreach (var entryPoint in new[] { "preflight", "database-publish" })
        {
            var result = sandbox.Run(entryPoint);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Production", result.Error, StringComparison.Ordinal);
            Assert.Contains("blocked until protected approval is available", result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("EXTERNAL_COMMAND_ATTEMPTED", result.Output + result.Error, StringComparison.Ordinal);
            Assert.DoesNotContain(sandbox.SyntheticToken, result.Output + result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(sandbox.ExternalCommandLogPath));
            Assert.Equal(originalFiles, sandbox.SnapshotFiles());
        }
    }

    private sealed class ApprovalSandbox : IDisposable
    {
        private readonly string _directory;
        private readonly string _repository;
        private readonly string _harnessPath;
        private readonly string _scenario;

        public ApprovalSandbox(string scenario)
        {
            _scenario = scenario;
            _repository = FindRepositoryRoot();
            _directory = Path.Combine(Path.GetTempPath(), $"authcraft-production-approval-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            _harnessPath = Path.Combine(_directory, "invoke-production.ps1");
            ExternalCommandLogPath = Path.Combine(_directory, "external-commands.log");

            File.WriteAllText(Path.Combine(_directory, "api.zip"), "synthetic-api-package");
            File.WriteAllText(Path.Combine(_directory, "database.dacpac"), "synthetic-dacpac");
            File.WriteAllText(Path.Combine(_directory, "production-version.txt"), new string('b', 40));
            File.WriteAllText(Path.Combine(_directory, "production-data.txt"), "recent-write-must-survive");
            File.WriteAllText(Path.Combine(_directory, "production-deploy-report.xml"), "<DeployReport />");
            File.WriteAllText(Path.Combine(_directory, "production-deploy.sql"), "CREATE TABLE dbo.synthetic_test (id INT);");
            File.WriteAllText(Path.Combine(_directory, "sqlpackage.ps1"), """
                [IO.File]::AppendAllText($env:AUTHCRAFT_APPROVAL_TEST_LOG, "SqlPackage`n")
                throw 'EXTERNAL_COMMAND_ATTEMPTED'
                """);

            var manifest = new JsonObject
            {
                ["commit_sha"] = new string('a', 40),
                ["run_id"] = "12345",
                ["api"] = new JsonObject { ["artifact"] = "api", ["sha256"] = HashFile("api.zip") },
                ["database"] = new JsonObject { ["artifact"] = "dacpac", ["sha256"] = HashFile("database.dacpac") }
            };
            File.WriteAllText(Path.Combine(_directory, "manifest.json"), manifest.ToJsonString());

            var approval = new JsonObject
            {
                ["protection"] = "missing",
                ["decision"] = "approved",
                ["commit_sha"] = new string('a', 40),
                ["run_id"] = "12345",
                ["target_environment"] = "production",
                ["deploy_report_sha256"] = HashFile("production-deploy-report.xml")
            };
            switch (scenario)
            {
                case "protection-incompatible":
                    approval["protection"] = "required-reviewers-unavailable";
                    break;
                case "approval-rejected":
                    approval["decision"] = "rejected";
                    break;
                case "approval-other-release":
                    approval["commit_sha"] = new string('c', 40);
                    approval["run_id"] = "99999";
                    break;
                case "reviewed-report-changed":
                    File.AppendAllText(Path.Combine(_directory, "production-deploy-report.xml"), "<ChangedPlan />");
                    break;
            }
            File.WriteAllText(Path.Combine(_directory, "untrusted-approval.json"), approval.ToJsonString());
            File.WriteAllText(Path.Combine(_directory, "actions-artifacts.json"), new JsonObject
            {
                ["artifacts"] = new JsonArray(new JsonObject
                {
                    ["name"] = "api",
                    ["expired"] = scenario == "artifact-expired",
                    ["expires_at"] = DateTimeOffset.UtcNow
                        .AddDays(scenario == "artifact-expired" ? -1 : 1).ToString("O")
                })
            }.ToJsonString());

            File.WriteAllText(_harnessPath, """
                param([string]$Repository, [string]$EvidenceDirectory, [string]$EntryPoint)
                $ErrorActionPreference = 'Stop'
                function Stop-ExternalCommand {
                    param([string]$Command)
                    [IO.File]::AppendAllText($env:AUTHCRAFT_APPROVAL_TEST_LOG, "$Command`n")
                    throw 'EXTERNAL_COMMAND_ATTEMPTED'
                }
                function gh { Stop-ExternalCommand 'gh' }
                function az { Stop-ExternalCommand 'az' }
                function git { Stop-ExternalCommand 'git' }
                function Invoke-RestMethod { Stop-ExternalCommand 'Invoke-RestMethod' }
                function Invoke-WebRequest { Stop-ExternalCommand 'Invoke-WebRequest' }
                $parameters = @{
                    TargetEnvironment = 'production'
                    ManifestPath = Join-Path $EvidenceDirectory 'manifest.json'
                    ExpectedCommitSha = 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
                    ExpectedRunId = '12345'
                }
                if ($EntryPoint -eq 'preflight') {
                    $parameters.ApiArtifactPath = Join-Path $EvidenceDirectory 'api.zip'
                    $parameters.DatabaseArtifactPath = Join-Path $EvidenceDirectory 'database.dacpac'
                    $parameters.GitHubRef = $env:GITHUB_REF
                    $parameters.LastSuccessfulCommitSha = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
                    $parameters.RepositoryPath = $Repository
                    & (Join-Path $Repository 'scripts\Test-ReleasePreflight.ps1') @parameters
                } else {
                    $parameters.Mode = 'Publish'
                    $parameters.DacpacPath = Join-Path $EvidenceDirectory 'database.dacpac'
                    $parameters.SqlPackagePath = Join-Path $EvidenceDirectory 'sqlpackage.ps1'
                    $parameters.ReviewIssueNumber = '10'
                    & (Join-Path $Repository 'scripts\Invoke-DatabaseDeployment.ps1') @parameters
                }
                """);
        }

        public string ExternalCommandLogPath { get; }
        public string SyntheticToken => "synthetic-production-approval-test-token";

        public string[] SnapshotFiles() => Directory.GetFiles(_directory)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{Path.GetFileName(path)}:{HashFile(Path.GetFileName(path))}")
            .ToArray();

        public (int ExitCode, string Output, string Error) Run(string entryPoint)
        {
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = _directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File", _harnessPath,
                         "-Repository", _repository, "-EvidenceDirectory", _directory, "-EntryPoint", entryPoint
                     })
            {
                processInfo.ArgumentList.Add(argument);
            }

            processInfo.Environment["AUTHCRAFT_APPROVAL_TEST_LOG"] = ExternalCommandLogPath;
            processInfo.Environment["AUTHCRAFT_SQL_ACCESS_TOKEN"] = SyntheticToken;
            processInfo.Environment["GITHUB_TOKEN"] = SyntheticToken;
            processInfo.Environment["GH_TOKEN"] = SyntheticToken;
            processInfo.Environment["GITHUB_REPOSITORY"] = "leoperetti/authcraft-api";
            processInfo.Environment["GITHUB_SHA"] = new string('a', 40);
            processInfo.Environment["GITHUB_REF"] = _scenario == "non-main-reference"
                ? "refs/heads/feature" : "refs/heads/main";
            processInfo.Environment["GITHUB_EVENT_NAME"] = _scenario switch
            {
                "workflow-dispatch" => "workflow_dispatch",
                "main-pull-request" => "pull_request",
                _ => "push"
            };
            processInfo.Environment["GITHUB_BASE_REF"] = _scenario == "main-pull-request" ? "main" : "";
            processInfo.Environment["INPUT_APPROVED"] = _scenario == "approval-boolean" ? "true" : "false";
            processInfo.Environment["INPUT_APPROVAL_REFERENCE"] = Path.Combine(_directory, "untrusted-approval.json");
            processInfo.Environment.Remove("ACTIONS_ID_TOKEN_REQUEST_URL");
            processInfo.Environment.Remove("ACTIONS_ID_TOKEN_REQUEST_TOKEN");

            using var process = Process.Start(processInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Production approval test exceeded 30 seconds.");
            }
            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);

        private string HashFile(string name) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(_directory, name)))).ToLowerInvariant();

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
}
