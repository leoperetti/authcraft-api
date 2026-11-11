using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace authcraft_api.Tests;

public sealed class DeploymentRecoveryTests
{
    [Fact]
    public void StagingRecovery_ReviewOnlyEvaluatesSqlWithoutRunningWriteStages()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "deploy_authcraft-api-staging.yml"));
        var inputsIndex = workflow.IndexOf("      recovery_deployment_run_id:", StringComparison.Ordinal);
        var reviewOnlyIndex = workflow.IndexOf("      review_only:", StringComparison.Ordinal);
        var preflightIndex = workflow.IndexOf("- name: Verify release provenance and current Staging gates", StringComparison.Ordinal);
        var evaluationIndex = workflow.IndexOf("- name: Evaluate the exact Staging SQL plan without publishing", StringComparison.Ordinal);
        var permissionManagerIndex = workflow.IndexOf("- name: Log in to the Staging permission manager", StringComparison.Ordinal);
        var recordIndex = workflow.IndexOf("- name: Record Staging deployment attempt", StringComparison.Ordinal);
        var candidateCheckoutIndex = workflow.IndexOf("- name: Check out the exact candidate source for provenance validation", StringComparison.Ordinal);
        var transactionVerificationIndex = workflow.IndexOf("- name: Verify transaction coverage using this runner's deployment tool", StringComparison.Ordinal);

        Assert.True(inputsIndex >= 0 && reviewOnlyIndex > inputsIndex);
        Assert.Contains("default: true", workflow[reviewOnlyIndex..]);
        Assert.True(preflightIndex >= 0 && evaluationIndex > preflightIndex);
        Assert.True(permissionManagerIndex > evaluationIndex && recordIndex > permissionManagerIndex);
        Assert.True(candidateCheckoutIndex >= 0 && transactionVerificationIndex > candidateCheckoutIndex);
        Assert.Contains("fetch-depth: 0", workflow[candidateCheckoutIndex..transactionVerificationIndex], StringComparison.Ordinal);
        Assert.Contains("$preflightParameters.RecoveryDeploymentRunId", workflow[preflightIndex..evaluationIndex], StringComparison.Ordinal);
        Assert.Contains("recovery_review_required", workflow[preflightIndex..evaluationIndex], StringComparison.Ordinal);
        Assert.Contains("-Mode Evaluate", workflow[evaluationIndex..permissionManagerIndex], StringComparison.Ordinal);
        Assert.DoesNotContain("Set-StagingPublisherRole.ps1", workflow[evaluationIndex..permissionManagerIndex], StringComparison.Ordinal);
        Assert.DoesNotContain("/Action:Publish", workflow[evaluationIndex..permissionManagerIndex], StringComparison.Ordinal);
        Assert.DoesNotContain("az webapp deploy", workflow[evaluationIndex..permissionManagerIndex], StringComparison.Ordinal);

        var writeStages = workflow[permissionManagerIndex..recordIndex];
        foreach (var stepName in new[]
        {
            "Log in to the Staging permission manager",
            "Grant the temporary Staging publisher role",
            "Restore the Staging publisher session before SQL",
            "Publish the reviewed Staging database plan",
            "Restore the Staging application publisher session",
            "Deploy the same API package to Staging",
            "Verify Staging health, synthetic login, and tenant roles"
        })
        {
            var stepIndex = writeStages.IndexOf($"- name: {stepName}", StringComparison.Ordinal);
            Assert.True(stepIndex >= 0, $"Missing Staging deployment step: {stepName}");
            var nextStepIndex = writeStages.IndexOf("\n      - name:", stepIndex + 1, StringComparison.Ordinal);
            var step = nextStepIndex < 0 ? writeStages[stepIndex..] : writeStages[stepIndex..nextStepIndex];
            Assert.Contains("if: inputs.review_only != true && success()", step, StringComparison.Ordinal);
        }
        Assert.Contains("$deploymentParameters.RecoveryDeploymentRunId = $env:RECOVERY_DEPLOYMENT_RUN_ID",
            writeStages, StringComparison.Ordinal);
        var recordStep = workflow[recordIndex..];
        Assert.Contains("SCHEMA_EVALUATION_RESULT: ${{ steps.recovery-evaluation.outcome }}", recordStep, StringComparison.Ordinal);
        Assert.Contains("$attempt.schema_evaluation_result = Get-StageResult $env:SCHEMA_EVALUATION_RESULT",
            recordStep, StringComparison.Ordinal);
        Assert.Contains("if ($attempt.review_only -and $env:PREFLIGHT_RESULT -eq 'success')", recordStep,
            StringComparison.Ordinal);
        Assert.Contains("$attempt.failure_stage = $null", recordStep, StringComparison.Ordinal);
        Assert.Contains("Staging review-only: preflight=", recordStep, StringComparison.Ordinal);
    }

    [Fact]
    public void StagingDeployment_DoesNotContinueApiOrSmokeAfterSqlFailure()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "deploy_authcraft-api-staging.yml"));
        var publishIndex = workflow.IndexOf("- name: Publish the reviewed Staging database plan", StringComparison.Ordinal);
        Assert.True(publishIndex >= 0);
        var evidenceIndex = workflow.IndexOf("- name: Record Staging deployment attempt", StringComparison.Ordinal);
        Assert.True(evidenceIndex > publishIndex);
        var deploymentSteps = workflow[publishIndex..evidenceIndex];
        Assert.DoesNotContain("continue-on-error:", deploymentSteps, StringComparison.OrdinalIgnoreCase);
        var applicationIndex = deploymentSteps.IndexOf("- name: Deploy the same API package to Staging", StringComparison.Ordinal);
        var smokeIndex = deploymentSteps.IndexOf("- name: Verify Staging health, synthetic login, and tenant roles", StringComparison.Ordinal);
        Assert.True(applicationIndex > 0 && smokeIndex > applicationIndex);
        var applicationSteps = deploymentSteps[applicationIndex..];
        Assert.Contains("if: inputs.review_only != true && success() && steps.database.outcome == 'success' && steps.permission-revoke.outcome == 'success'",
            applicationSteps, StringComparison.Ordinal);
        Assert.DoesNotContain("always()", applicationSteps, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if: inputs.review_only != true && success()", deploymentSteps[smokeIndex..], StringComparison.Ordinal);
        Assert.Contains("if: always() && steps.permission-cleanup-login.outcome == 'success'", deploymentSteps, StringComparison.Ordinal);
        Assert.Contains("throw 'The reviewed Staging database deployment failed.'", deploymentSteps, StringComparison.Ordinal);
        Assert.DoesNotContain("RESTORE DATABASE", deploymentSteps, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", deploymentSteps, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StagingDeployment_StopsAfterApiFailureAndAlwaysPreservesAttemptEvidence()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "deploy_authcraft-api-staging.yml"));
        var databaseIndex = workflow.IndexOf("- name: Publish the reviewed Staging database plan", StringComparison.Ordinal);
        var applicationIndex = workflow.IndexOf("- name: Deploy the same API package to Staging", StringComparison.Ordinal);
        var smokeIndex = workflow.IndexOf("- name: Verify Staging health, synthetic login, and tenant roles", StringComparison.Ordinal);
        var recordIndex = workflow.IndexOf("- name: Record Staging deployment attempt", StringComparison.Ordinal);
        var uploadIndex = workflow.IndexOf("- name: Preserve Staging deployment evidence", StringComparison.Ordinal);

        Assert.True(databaseIndex >= 0);
        Assert.True(applicationIndex > databaseIndex);
        Assert.True(smokeIndex > applicationIndex);
        Assert.True(recordIndex > smokeIndex);
        Assert.True(uploadIndex > recordIndex);

        var applicationStep = workflow[applicationIndex..smokeIndex];
        var smokeStep = workflow[smokeIndex..recordIndex];
        var recordStep = workflow[recordIndex..uploadIndex];
        var evidenceUpload = workflow[uploadIndex..];

        Assert.Contains(
            "if: inputs.review_only != true && success() && steps.database.outcome == 'success' && steps.permission-revoke.outcome == 'success'",
            applicationStep,
            StringComparison.Ordinal);
        Assert.Contains("staging-release\\api\\api.zip", applicationStep, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(
            applicationStep, @"(?m)^\s+az webapp deploy ").Cast<System.Text.RegularExpressions.Match>());
        Assert.DoesNotContain("previous", applicationStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE DATABASE", applicationStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", applicationStep, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if: inputs.review_only != true && success()", smokeStep, StringComparison.Ordinal);
        Assert.Contains("APPLICATION_RESULT: ${{ steps.application.outcome }}", recordStep, StringComparison.Ordinal);
        Assert.Contains("SMOKE_RESULT: ${{ steps.smoke.outcome }}", recordStep, StringComparison.Ordinal);
        Assert.Contains("$attempt.failure_stage = if ($env:APPLICATION_RESULT", recordStep, StringComparison.Ordinal);
        Assert.Contains("{ 'application_publish' }", recordStep, StringComparison.Ordinal);
        Assert.Contains("if: always()", recordStep, StringComparison.Ordinal);
        Assert.Contains("if: always()", evidenceUpload, StringComparison.Ordinal);
        Assert.Contains("name: staging-deployment-attempt", evidenceUpload, StringComparison.Ordinal);
        Assert.Contains("retention-days: 30", evidenceUpload, StringComparison.Ordinal);
        Assert.DoesNotContain("RESTORE DATABASE", evidenceUpload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP DATABASE", evidenceUpload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("report")]
    [InlineData("script")]
    public void DatabaseDeployment_StopsAfterEvaluationFailureWithoutRollbackOrPublish(string failureStage)
    {
        using var sandbox = new RecoverySandbox();
        var protectedFiles = sandbox.SnapshotProtectedFiles();

        var result = sandbox.RunDatabaseDeployment(failureStage);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(
            failureStage == "report" ? ["/Action:DeployReport"] : ["/Action:DeployReport", "/Action:Script"],
            File.ReadAllLines(sandbox.SqlPackageLogPath));
        Assert.DoesNotContain("ROLLBACK", File.ReadAllText(sandbox.SqlPackageLogPath), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RESTORE", File.ReadAllText(sandbox.SqlPackageLogPath), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(protectedFiles, sandbox.SnapshotProtectedFiles());
    }

    [Fact]
    public void DatabaseDeployment_BlocksProductionBeforeInvokingSqlPackage()
    {
        using var sandbox = new RecoverySandbox();
        var protectedFiles = sandbox.SnapshotProtectedFiles();

        var result = sandbox.RunDatabaseDeployment(failureStage: "", targetEnvironment: "production", mode: "Publish");

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(sandbox.SqlPackageLogPath));
        Assert.Contains("blocked", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(protectedFiles, sandbox.SnapshotProtectedFiles());
    }

    [Fact]
    public void DatabaseDeployment_RequiresAuthenticatedReviewBeforePublishing()
    {
        using var sandbox = new RecoverySandbox();
        var result = sandbox.RunDatabaseDeployment(failureStage: "", mode: "Publish");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("authenticated deployment review", result.Error, StringComparison.OrdinalIgnoreCase);
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
        private readonly string _apiPackagePath;
        private readonly string _productionVersionPath;
        private readonly string _recentWritesPath;

        public RecoverySandbox()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"authcraft-recovery-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            _scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "Invoke-DatabaseDeployment.ps1");
            _sqlPackagePath = Path.Combine(DirectoryPath, "mock-sqlpackage.ps1");
            _manifestPath = Path.Combine(DirectoryPath, "manifest.json");
            _dacpacPath = Path.Combine(DirectoryPath, "database.dacpac");
            _apiPackagePath = Path.Combine(DirectoryPath, "api.zip");
            _productionVersionPath = Path.Combine(DirectoryPath, "production-version.txt");
            _recentWritesPath = Path.Combine(DirectoryPath, "production-recent-writes.txt");
            SqlPackageLogPath = Path.Combine(DirectoryPath, "sqlpackage-actions.log");
            File.WriteAllBytes(_dacpacPath, [1, 2, 3, 4]);
            File.WriteAllText(_apiPackagePath, "synthetic-api-package");
            File.WriteAllText(_productionVersionPath, new string('b', 40));
            File.WriteAllText(_recentWritesPath, "synthetic-recent-write-must-be-preserved");
            var postdeploymentPath = Path.Combine(FindRepositoryRoot(), "authcraft-db", "postdeploys", "Script.PostDeployment.sql");
            File.WriteAllText(_sqlPackagePath, $$"""
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
                    $postdeployment = Get-Content -LiteralPath '{{postdeploymentPath}}' -Raw -Encoding UTF8
                    $constraints = Get-Content -LiteralPath (Join-Path (Split-Path '{{postdeploymentPath}}') 'ValidateConstraints.sql') -Raw -Encoding UTF8
                    $postdeployment = $postdeployment.Replace(':r .\ValidateConstraints.sql', $constraints)
                    [IO.File]::WriteAllText($output, "CREATE TABLE dbo.synthetic_test (id INT);`nGO`n$postdeployment`nGO`n")
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

        public string[] SnapshotProtectedFiles()
        {
            return new[] { _apiPackagePath, _productionVersionPath, _recentWritesPath }
                .Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                .ToArray();
        }

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
                    "-StagingBaselineSha", "unknown",
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
