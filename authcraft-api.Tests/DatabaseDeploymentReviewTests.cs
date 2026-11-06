using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace authcraft_api.Tests;

public sealed class DatabaseDeploymentReviewTests
{
    [Fact]
    public void Publish_RevalidatesExactAuthenticatedReviewAndPlanBeforePublishing()
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run("approved");

        Assert.True(result.ExitCode == 0, result.Error);
        using var published = JsonDocument.Parse(result.Output);
        Assert.Equal("published", published.RootElement.GetProperty("state").GetString());
        Assert.Equal(42, published.RootElement.GetProperty("review_comment_id").GetInt32());
        Assert.Equal(
            ["/Action:DeployReport", "/Action:Script", "/Action:DeployReport", "/Action:Script", "/Action:Publish"],
            sandbox.Actions);
        Assert.DoesNotContain("native diagnostic", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("missing-review")]
    [InlineData("wrong-author")]
    [InlineData("rejected")]
    [InlineData("duplicate-decision")]
    [InlineData("wrong-release")]
    [InlineData("wrong-destination")]
    [InlineData("closed-issue")]
    [InlineData("review-api-error")]
    [InlineData("review-revoked")]
    [InlineData("plan-drift")]
    [InlineData("missing-recheck-output")]
    [InlineData("dacpac-changed")]
    [InlineData("dacpac-changed-on-recheck")]
    [InlineData("destructive-plan")]
    [InlineData("reviewer-override")]
    public void Publish_BlocksInvalidOrChangedReviewWithoutModification(string scenario)
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run(scenario);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("/Action:Publish", sandbox.Actions);
        Assert.DoesNotContain(sandbox.SyntheticToken, result.Output + result.Error, StringComparison.Ordinal);
        if (scenario == "plan-drift")
        {
            Assert.Contains("changed after review", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(4, sandbox.Actions.Length);
        }
        if (scenario == "review-revoked")
        {
            Assert.Contains("does not approve", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(4, sandbox.Actions.Length);
        }
    }

    [Fact]
    public void Publish_FailureDoesNotAttemptRollbackOrExposeNativeDiagnostics()
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run("publish-fails");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal("/Action:Publish", sandbox.Actions.Last());
        Assert.Contains("no rollback", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("native diagnostic", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sandbox.SyntheticToken, result.Output + result.Error, StringComparison.Ordinal);
    }

    private sealed class ReviewSandbox : IDisposable
    {
        private readonly string _directory;
        private readonly string _harnessPath;
        private readonly string _actionLog;

        public ReviewSandbox()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"authcraft-sql-review-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            _actionLog = Path.Combine(_directory, "actions.log");
            _harnessPath = Path.Combine(_directory, "review-harness.ps1");
            var dacpacPath = Path.Combine(_directory, "database.dacpac");
            var manifestPath = Path.Combine(_directory, "manifest.json");
            var sqlPackagePath = Path.Combine(_directory, "sqlpackage.ps1");
            File.WriteAllBytes(dacpacPath, [1, 2, 3]);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
            {
                commit_sha = new string('a', 40),
                run_id = "12345",
                database = new
                {
                    artifact = "dacpac",
                    sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dacpacPath))).ToLowerInvariant()
                }
            }));
            File.WriteAllText(sqlPackagePath, """
                $ErrorActionPreference = 'Stop'
                $action = @($args | Where-Object { $_ -like '/Action:*' })[0]
                Add-Content -LiteralPath $env:REVIEW_TEST_ACTION_LOG -Value $action
                foreach ($required in @('/p:BlockOnPossibleDataLoss=True', '/p:DropObjectsNotInSource=False',
                    '/TargetTrustServerCertificate:False',
                    '/TargetDatabaseName:authcraft-db-staging',
                    '/TargetServerName:tcp:authcraftdbbrasil.database.windows.net,1433')) {
                    if ($required -notin $args) { throw 'Required SQL publication option is missing.' }
                }
                Write-Output "native diagnostic $env:AUTHCRAFT_SQL_ACCESS_TOKEN"
                if ($action -eq '/Action:DeployReport') {
                    $path = @($args | Where-Object { $_ -like '/OutputPath:*' })[0].Substring('/OutputPath:'.Length)
                    [IO.File]::WriteAllText($path, '<DeploymentReport />')
                }
                if ($action -eq '/Action:Script') {
                    $path = @($args | Where-Object { $_ -like '/DeployScriptPath:*' })[0].Substring('/DeployScriptPath:'.Length)
                    if ($env:REVIEW_TEST_SCENARIO -eq 'missing-recheck-output' -and $path -like '*recheck.sql') { exit 0 }
                    $sql = 'CREATE TABLE dbo.synthetic_test (id INT);'
                    if ($env:REVIEW_TEST_SCENARIO -eq 'destructive-plan') { $sql = 'DROP TABLE dbo.existing_data;' }
                    if (Test-Path -LiteralPath $env:REVIEW_TEST_DRIFT_PATH) { $sql += ' ALTER TABLE dbo.synthetic_test ADD other_id INT;' }
                    [IO.File]::WriteAllText($path, $sql)
                }
                if ($action -eq '/Action:Publish' -and $env:REVIEW_TEST_SCENARIO -eq 'publish-fails') {
                    Write-Error "native diagnostic $env:AUTHCRAFT_SQL_ACCESS_TOKEN"
                    exit 1
                }
                exit 0
                """);
            var publisherPath = Path.Combine(FindRepositoryRoot(), "scripts", "Invoke-DatabaseDeployment.ps1");
            File.WriteAllText(_harnessPath, $$"""
                $ErrorActionPreference = 'Stop'
                $script:commentsRead = 0
                function Invoke-RestMethod {
                    param($Uri, $Headers, $Method, $TimeoutSec)
                    if ($Headers.Authorization -cne "Bearer $env:GITHUB_TOKEN") {
                        throw 'Expected authenticated request.'
                    }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'review-api-error') {
                        throw "private API diagnostic $env:GITHUB_TOKEN"
                    }
                    if ($Uri -eq 'https://api.github.com/repos/leoperetti/authcraft-api/issues/10') {
                        $state = 'open'
                        if ($env:REVIEW_TEST_SCENARIO -eq 'closed-issue') { $state = 'closed' }
                        return @{ state = $state }
                    }
                    if ($Uri -ne 'https://api.github.com/repos/leoperetti/authcraft-api/issues/10/comments?per_page=100') {
                        throw 'Unexpected remote request.'
                    }
                    $script:commentsRead++
                    if ($env:REVIEW_TEST_SCENARIO -eq 'missing-review') { return @() }
                    $author = 'leoperetti'
                    if ($env:REVIEW_TEST_SCENARIO -in @('wrong-author', 'reviewer-override')) { $author = 'other-user' }
                    $decision = 'approved'
                    if ($env:REVIEW_TEST_SCENARIO -eq 'rejected' -or
                        ($env:REVIEW_TEST_SCENARIO -eq 'review-revoked' -and $script:commentsRead -gt 1)) {
                        $decision = 'rejected'
                    }
                    $commit = '{{new string('a', 40)}}'
                    if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-release') { $commit = '{{new string('b', 40)}}' }
                    $database = 'authcraft-db-staging'
                    if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-destination') { $database = 'authcraft-db' }
                    $dacpacHash = (Get-FileHash -LiteralPath '{{dacpacPath}}' -Algorithm SHA256).Hash.ToLowerInvariant()
                    $reportHash = (Get-FileHash -LiteralPath '{{Path.Combine(_directory, "staging-deploy-report.xml")}}' -Algorithm SHA256).Hash.ToLowerInvariant()
                    $sqlHash = (Get-FileHash -LiteralPath '{{Path.Combine(_directory, "staging-deploy.sql")}}' -Algorithm SHA256).Hash.ToLowerInvariant()
                    $body = @"
                deployment_review=$decision
                run_id=12345
                commit_sha=$commit
                target_environment=staging
                database_server=authcraftdbbrasil.database.windows.net
                database_name=$database
                dacpac_sha256=$dacpacHash
                block_on_possible_data_loss=true
                drop_objects_not_in_source=false
                deploy_report_sha256=$reportHash
                deploy_script_sha256=$sqlHash
                "@
                    if ($env:REVIEW_TEST_SCENARIO -eq 'duplicate-decision') { $body += "`ndeployment_review=rejected" }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'plan-drift') { Set-Content -LiteralPath $env:REVIEW_TEST_DRIFT_PATH -Value 'changed' }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'dacpac-changed' -or
                        ($env:REVIEW_TEST_SCENARIO -eq 'dacpac-changed-on-recheck' -and $script:commentsRead -gt 1)) {
                        Add-Content -LiteralPath '{{dacpacPath}}' -Value 'changed'
                    }
                    return @(@{ id = 42; user = @{ login = $author }; body = $body; created_at = '2026-11-06T15:00:00Z' })
                }
                $parameters = @{
                    Mode = 'Publish'
                    TargetEnvironment = 'staging'
                    DacpacPath = '{{dacpacPath}}'
                    ManifestPath = '{{manifestPath}}'
                    ExpectedCommitSha = '{{new string('a', 40)}}'
                    ExpectedRunId = '12345'
                    SqlPackagePath = '{{sqlPackagePath}}'
                    ReviewIssueNumber = '10'
                }
                if ($env:REVIEW_TEST_SCENARIO -eq 'reviewer-override') { $parameters.AllowedReviewer = 'other-user' }
                & '{{publisherPath}}' @parameters
                """);
        }

        public string SyntheticToken { get; } = $"synthetic-{Guid.NewGuid():N}";
        public string[] Actions => File.Exists(_actionLog) ? File.ReadAllLines(_actionLog) : [];

        public ProcessResult Run(string scenario)
        {
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", _harnessPath })
            {
                processInfo.ArgumentList.Add(argument);
            }
            processInfo.Environment["AUTHCRAFT_SQL_ACCESS_TOKEN"] = SyntheticToken;
            processInfo.Environment["GITHUB_TOKEN"] = SyntheticToken;
            processInfo.Environment["GITHUB_REPOSITORY"] = "leoperetti/authcraft-api";
            processInfo.Environment["REVIEW_TEST_SCENARIO"] = scenario;
            processInfo.Environment["REVIEW_TEST_ACTION_LOG"] = _actionLog;
            processInfo.Environment["REVIEW_TEST_DRIFT_PATH"] = Path.Combine(_directory, "drift");
            using var process = Process.Start(processInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("SQL review test exceeded 60 seconds.");
            }
            return new ProcessResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);

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
