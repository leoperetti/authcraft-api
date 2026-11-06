using System.Diagnostics;
using System.Text.Json;

namespace authcraft_api.Tests;

public sealed class DeploymentPipelineTests
{
    [Fact]
    public void Manifest_ContainsSameRunAndSha256ForBothPackages()
    {
        using var sandbox = new PipelineSandbox();
        var result = sandbox.CreateManifest();

        Assert.Equal(0, result.ExitCode);
        using var manifest = JsonDocument.Parse(File.ReadAllText(sandbox.ManifestPath));
        Assert.Equal(sandbox.CommitSha, manifest.RootElement.GetProperty("commit_sha").GetString());
        Assert.Equal("12345", manifest.RootElement.GetProperty("run_id").GetString());
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sandbox.ApiPath))).ToLowerInvariant(),
            manifest.RootElement.GetProperty("api").GetProperty("sha256").GetString());
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sandbox.DacpacPath))).ToLowerInvariant(),
            manifest.RootElement.GetProperty("database").GetProperty("sha256").GetString());
    }

    [Fact]
    public void Manifest_RejectsMissingArtifactsAndDuplicateFiles()
    {
        using var sandbox = new PipelineSandbox();
        var missing = sandbox.RunManifest(sandbox.ApiPath, Path.Combine(sandbox.DirectoryPath, "missing.dacpac"));
        var duplicate = sandbox.RunManifest(sandbox.ApiPath, sandbox.ApiPath);

        Assert.NotEqual(0, missing.ExitCode);
        Assert.NotEqual(0, duplicate.ExitCode);
    }

    [Fact]
    public void Preflight_AllowsOnlyNewerMainReleaseWithMatchingManifestHashes()
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);

        var result = sandbox.RunPreflight("refs/heads/main", "staging", sandbox.ParentCommitSha);

        Assert.Equal(0, result.ExitCode);
        using var preflight = JsonDocument.Parse(result.Output);
        Assert.Equal("ready", preflight.RootElement.GetProperty("state").GetString());
        Assert.Equal("authcraft-db-staging", preflight.RootElement.GetProperty("database_name").GetString());
    }

    [Theory]
    [InlineData("refs/heads/feature", "staging", "newer_commit")]
    [InlineData("refs/heads/main", "production", "newer_commit")]
    [InlineData("refs/heads/main", "staging", "same_commit")]
    [InlineData("refs/heads/main", "staging", "newer_commit")]
    public void Preflight_BlocksUnauthorizedTargetReferenceOrSupersededRelease(
        string gitHubRef,
        string target,
        string releaseState)
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);
        var lastSuccessfulCommit = releaseState switch
        {
            "same_commit" => sandbox.CommitSha,
            "newer_commit" => sandbox.RemoteMainSha,
            _ => sandbox.ParentCommitSha
        };

        var result = sandbox.RunPreflight(gitHubRef, target, lastSuccessfulCommit);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("AZURE_SQL", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Preflight_BlocksChangedHashAndRunIdMismatchWithoutAzureCommands()
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);
        File.AppendAllText(sandbox.ApiPath, "tampered");

        var changedHash = sandbox.RunPreflight("refs/heads/main", "staging", sandbox.ParentCommitSha);
        var wrongRun = sandbox.RunPreflight("refs/heads/main", "staging", sandbox.ParentCommitSha, "99999");

        Assert.NotEqual(0, changedHash.ExitCode);
        Assert.NotEqual(0, wrongRun.ExitCode);
        Assert.DoesNotContain("az login", changedHash.Output + changedHash.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatabaseReview_RequiresAuthorizedAuthorAndExactReleasePlanHashes()
    {
        using var sandbox = new PipelineSandbox();
        var modulePath = Path.Combine(sandbox.RepositoryRoot, "scripts", "DeploymentReview.psm1");
        var scriptPath = Path.Combine(sandbox.DirectoryPath, "verify-review.ps1");
        var script = $$"""
            Import-Module '{{modulePath}}' -Force
            $body = @'
            deployment_review=approved
            run_id=12345
            commit_sha={{new string('a', 40)}}
            target_environment=staging
            database_server=authcraftdbbrasil.database.windows.net
            database_name=authcraft-db-staging
            dacpac_sha256={{new string('b', 64)}}
            block_on_possible_data_loss=true
            drop_objects_not_in_source=false
            deploy_report_sha256={{new string('c', 64)}}
            deploy_script_sha256={{new string('d', 64)}}
            '@
            $valid = Test-DeploymentReviewRecord -Body $body -Author 'leoperetti' -AllowedReviewer 'leoperetti' -RunId '12345' -CommitSha '{{new string('a', 40)}}' -TargetEnvironment 'staging' -DatabaseServer 'authcraftdbbrasil.database.windows.net' -DatabaseName 'authcraft-db-staging' -DacpacSha256 '{{new string('b', 64)}}' -ReportSha256 '{{new string('c', 64)}}' -ScriptSha256 '{{new string('d', 64)}}'
            $wrongAuthor = Test-DeploymentReviewRecord -Body $body -Author 'other-user' -AllowedReviewer 'leoperetti' -RunId '12345' -CommitSha '{{new string('a', 40)}}' -TargetEnvironment 'staging' -DatabaseServer 'authcraftdbbrasil.database.windows.net' -DatabaseName 'authcraft-db-staging' -DacpacSha256 '{{new string('b', 64)}}' -ReportSha256 '{{new string('c', 64)}}' -ScriptSha256 '{{new string('d', 64)}}'
            $wrongPlan = Test-DeploymentReviewRecord -Body $body -Author 'leoperetti' -AllowedReviewer 'leoperetti' -RunId '12345' -CommitSha '{{new string('a', 40)}}' -TargetEnvironment 'staging' -DatabaseServer 'authcraftdbbrasil.database.windows.net' -DatabaseName 'authcraft-db-staging' -DacpacSha256 '{{new string('b', 64)}}' -ReportSha256 '{{new string('e', 64)}}' -ScriptSha256 '{{new string('d', 64)}}'
            if ($valid -and -not $wrongAuthor -and -not $wrongPlan) { exit 0 }
            exit 1
            """;
        File.WriteAllText(scriptPath, script);

        var result = sandbox.RunPowerShellFile(scriptPath);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void LocalDeploymentScriptSafety_RejectsDatabaseRecreationAndDataDeletion()
    {
        using var sandbox = new PipelineSandbox();
        var modulePath = Path.Combine(sandbox.RepositoryRoot, "scripts", "DeploymentReview.psm1");
        var scriptPath = Path.Combine(sandbox.DirectoryPath, "verify-local-sql-safety.ps1");
        var script = $$"""
            Import-Module '{{modulePath}}' -Force
            $safe = Test-LocalDeploymentScriptSafety -ScriptContent 'CREATE TABLE dbo.users (id INT);'
            $dropDatabase = Test-LocalDeploymentScriptSafety -ScriptContent 'DROP DATABASE [authcraft_development];'
            $deleteRows = Test-LocalDeploymentScriptSafety -ScriptContent 'DELETE FROM dbo.users;'
            $truncateTable = Test-LocalDeploymentScriptSafety -ScriptContent 'TRUNCATE TABLE dbo.users;'
            $dropColumn = Test-LocalDeploymentScriptSafety -ScriptContent 'ALTER TABLE dbo.users DROP COLUMN secret;'
            $replaceConstraint = Test-LocalDeploymentScriptSafety -ScriptContent 'ALTER TABLE dbo.users DROP CONSTRAINT CK_users_name;'
            if ($safe -and -not $dropDatabase -and -not $deleteRows -and -not $truncateTable -and -not $dropColumn -and $replaceConstraint) { exit 0 }
            exit 1
            """;
        File.WriteAllText(scriptPath, script);

        var result = sandbox.RunPowerShellFile(scriptPath);

        Assert.Equal(0, result.ExitCode);
    }

    private sealed class PipelineSandbox : IDisposable
    {
        private readonly string _repositoryRoot;
        private readonly string _scriptsDirectory;
        private readonly string _gitRepositoryPath;

        public PipelineSandbox()
        {
            _repositoryRoot = FindRepositoryRoot();
            _scriptsDirectory = Path.Combine(_repositoryRoot, "scripts");
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"authcraft-pipeline-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            ApiPath = Path.Combine(DirectoryPath, "api.zip");
            DacpacPath = Path.Combine(DirectoryPath, "database.dacpac");
            ManifestPath = Path.Combine(DirectoryPath, "manifest.json");
            File.WriteAllBytes(ApiPath, [1, 2, 3, 4]);
            File.WriteAllBytes(DacpacPath, [5, 6, 7, 8]);
            _gitRepositoryPath = Path.Combine(DirectoryPath, "repository");
            Directory.CreateDirectory(_gitRepositoryPath);
            RunGitAt(_gitRepositoryPath, "init", "-b", "main");
            RunGitAt(_gitRepositoryPath, "config", "user.name", "Pipeline test");
            RunGitAt(_gitRepositoryPath, "config", "user.email", "pipeline-test@example.test");
            File.WriteAllText(Path.Combine(_gitRepositoryPath, "state.txt"), "baseline");
            RunGitAt(_gitRepositoryPath, "add", "state.txt");
            RunGitAt(_gitRepositoryPath, "commit", "-m", "baseline");
            ParentCommitSha = RunGitAt(_gitRepositoryPath, "rev-parse", "HEAD");
            File.WriteAllText(Path.Combine(_gitRepositoryPath, "state.txt"), "candidate");
            RunGitAt(_gitRepositoryPath, "commit", "-am", "candidate");
            CommitSha = RunGitAt(_gitRepositoryPath, "rev-parse", "HEAD");
            File.WriteAllText(Path.Combine(_gitRepositoryPath, "state.txt"), "newer");
            RunGitAt(_gitRepositoryPath, "commit", "-am", "newer");
            RemoteMainSha = RunGitAt(_gitRepositoryPath, "rev-parse", "HEAD");
            RunGitAt(_gitRepositoryPath, "checkout", "--detach", CommitSha);
        }

        public string DirectoryPath { get; }
        public string RepositoryRoot => _repositoryRoot;
        public string ApiPath { get; }
        public string DacpacPath { get; }
        public string ManifestPath { get; }
        public string CommitSha { get; }
        public string ParentCommitSha { get; }
        public string RemoteMainSha { get; }

        public ProcessResult CreateManifest() => RunManifest(ApiPath, DacpacPath);

        public ProcessResult RunManifest(string apiPath, string dacpacPath)
        {
            return RunPowerShell(Path.Combine(_scriptsDirectory, "New-ReleaseManifest.ps1"),
                "-ApiArtifactPath", apiPath,
                "-DatabaseArtifactPath", dacpacPath,
                "-CommitSha", CommitSha,
                "-RunId", "12345",
                "-OutputPath", ManifestPath);
        }

        public ProcessResult RunPreflight(string gitHubRef, string target, string lastCommitSha, string runId = "12345")
        {
            return RunPowerShell(Path.Combine(_scriptsDirectory, "Test-ReleasePreflight.ps1"),
                "-ManifestPath", ManifestPath,
                "-ApiArtifactPath", ApiPath,
                "-DatabaseArtifactPath", DacpacPath,
                "-ExpectedCommitSha", CommitSha,
                "-ExpectedRunId", runId,
                "-GitHubRef", gitHubRef,
                "-TargetEnvironment", target,
                "-LastSuccessfulCommitSha", lastCommitSha,
                "-RepositoryPath", _gitRepositoryPath);
        }

        public void Dispose()
        {
            foreach (var filePath in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            foreach (var directoryPath in Directory.EnumerateDirectories(DirectoryPath, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(directoryPath, FileAttributes.Normal);
            }

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

        private string RunGit(params string[] arguments)
        {
            return RunGitAt(_repositoryRoot, arguments);
        }

        private string RunGitAt(string workingDirectory, params string[] arguments)
        {
            var processInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                processInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(processInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"git failed: {error}");
            }

            return output.Trim();
        }

        private ProcessResult RunPowerShell(string scriptPath, params string[] arguments)
        {
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = _repositoryRoot,
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

        public ProcessResult RunPowerShellFile(string scriptPath)
        {
            return RunPowerShell(scriptPath);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
