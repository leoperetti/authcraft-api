using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace authcraft_api.Tests;

public sealed class StagingPublisherVerificationTests
{
    [Fact]
    public void PermissionRehearsal_OnlyManagesRolesAndSerializesWithDeployment()
    {
        var workflow = File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            ".github", "workflows", "verify_staging_permission_manager.yml")).ReplaceLineEndings("\n");
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("if: github.ref == 'refs/heads/main'", workflow, StringComparison.Ordinal);
        Assert.Contains("group: authcraft-staging-deployment", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
        Assert.Contains("client-id: c376b9f5-2b0b-406f-8579-ef8787001115", workflow, StringComparison.Ordinal);
        Assert.Contains("finally {\n            & .\\scripts\\Set-StagingPublisherRole.ps1 -Mode Revoke", workflow, StringComparison.Ordinal);
        Assert.Contains("if: always() && steps.manager-login.outcome == 'success'", workflow, StringComparison.Ordinal);
        Assert.Contains("revocation_verified = $verified", workflow, StringComparison.Ordinal);
        Assert.Contains("published = $false", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-DatabaseDeployment", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("webapp deploy", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("production", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("publisher")]
    [InlineData("wrong-subscription")]
    [InlineData("wrong-tenant")]
    [InlineData("user-session")]
    public async Task PermissionManager_RejectsWrongAzureContextBeforeSql(string scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"authcraft-permission-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var helper = Path.Combine(FindRepositoryRoot(), "scripts", "Set-StagingPublisherRole.ps1").Replace("'", "''");
            var path = Path.Combine(directory, "test.ps1");
            File.WriteAllText(path, $$"""
                $ErrorActionPreference = 'Stop'
                function az {
                    if ($args[0] -ne 'account' -or $args[1] -ne 'show') { throw 'Unexpected token or SQL access' }
                    $global:LASTEXITCODE = 0
                    @{
                        id = '{{(scenario == "wrong-subscription" ? Guid.Empty.ToString() : "948a8f49-fd90-40d9-8fa9-12f722768a7a")}}'
                        tenantId = '{{(scenario == "wrong-tenant" ? Guid.Empty.ToString() : "b7bd0c07-88dc-4c47-81ba-608fd7607a82")}}'
                        user = @{
                            type = '{{(scenario == "user-session" ? "user" : "servicePrincipal")}}'
                            name = '{{(scenario == "publisher" ? "a2382f63-d653-47f3-82ca-9f2696f63f0a" : "c376b9f5-2b0b-406f-8579-ef8787001115")}}'
                        }
                    } | ConvertTo-Json
                }
                & '{{helper}}' -Mode Grant
                """);
            var start = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", path })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.NotEqual(0, process.ExitCode);
            Assert.Contains("Only the reviewed Staging permission manager", await output + await error, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void StagingPermissionWindow_SeparatesManagerAndRequiresCleanupBeforeApi()
    {
        var root = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "deploy_authcraft-api-staging.yml"));
        var grant = workflow.IndexOf("-Mode Grant", StringComparison.Ordinal);
        var publish = workflow.IndexOf("Mode = 'Publish'", StringComparison.Ordinal);
        var revoke = workflow.IndexOf("-Mode Revoke", StringComparison.Ordinal);
        var application = workflow.IndexOf("az webapp deploy", StringComparison.Ordinal);
        Assert.True(grant > 0 && publish > grant && revoke > publish && application > revoke);
        Assert.Contains("if: always() && steps.permission-cleanup-login.outcome == 'success'", workflow, StringComparison.Ordinal);
        Assert.Contains("steps.database.outcome == 'success' && steps.permission-revoke.outcome == 'success'", workflow, StringComparison.Ordinal);
        Assert.Contains("client-id: c376b9f5-2b0b-406f-8579-ef8787001115", workflow, StringComparison.Ordinal);
        var helper = File.ReadAllText(Path.Combine(root, "scripts", "Set-StagingPublisherRole.ps1"));
        Assert.Contains("$account.user.name -cne 'c376b9f5-2b0b-406f-8579-ef8787001115'", helper, StringComparison.Ordinal);
        Assert.Contains("DB_NAME() <> N'authcraft-db-staging'", helper, StringComparison.Ordinal);
        Assert.Contains("a2382f63-d653-47f3-82ca-9f2696f63f0a", helper, StringComparison.Ordinal);
        Assert.Contains("Assert-PermissionState (Invoke-PermissionSql $permissionQuery) $false", helper, StringComparison.Ordinal);
        Assert.Contains("ALTER ROLE [db_owner] DROP MEMBER", helper, StringComparison.Ordinal);
        Assert.Contains("finally { $script:permissionToken = $null }", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflow_KeepsVerificationOptInAndOutsideArtifactUploadSteps()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "main_authcraft-api-staging.yml")).ReplaceLineEndings("\n");
        var jobBoundary = workflow.IndexOf("\n  verify-staging-publisher:", StringComparison.Ordinal);
        Assert.True(jobBoundary > 0);
        var validation = workflow[..jobBoundary];
        var diagnostic = workflow[jobBoundary..];

        Assert.Contains("default: false", validation, StringComparison.Ordinal);
        Assert.DoesNotContain("id-token: write", validation, StringComparison.Ordinal);
        foreach (var artifact in new[] { "api", "dacpac", "release-manifest" })
        {
            Assert.Contains($"          name: {artifact}\n", validation, StringComparison.Ordinal);
        }
        Assert.Contains("inputs.verify_staging_publisher == true", diagnostic, StringComparison.Ordinal);
        Assert.Contains("github.event_name == 'workflow_dispatch'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("github.ref == 'refs/heads/main'", diagnostic, StringComparison.Ordinal);
        Assert.Contains("needs: validate", diagnostic, StringComparison.Ordinal);
        Assert.Contains("id-token: write", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Test-StagingPublisher.ps1", diagnostic, StringComparison.Ordinal);
        Assert.Contains("inputs.evaluate_staging_schema == true", diagnostic, StringComparison.Ordinal);
        Assert.Contains("name: staging-schema-review", diagnostic, StringComparison.Ordinal);
        Assert.Contains("success() && inputs.evaluate_staging_schema == true", diagnostic, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@v4", diagnostic, StringComparison.Ordinal);
        Assert.Contains("  preflight-staging:", diagnostic, StringComparison.Ordinal);
        Assert.Contains("Test-ReleasePreflight.ps1", diagnostic, StringComparison.Ordinal);
        Assert.Contains("staging-evaluation-*-report.xml", diagnostic, StringComparison.Ordinal);
        Assert.Contains("staging-evaluation-*-script.sql", diagnostic, StringComparison.Ordinal);
        Assert.Contains("evaluation-toolchain.json", diagnostic, StringComparison.Ordinal);
        Assert.Contains("sqlpackage_version", diagnostic, StringComparison.Ordinal);
        Assert.Contains("sqlpackage_sha256", diagnostic, StringComparison.Ordinal);
        Assert.Contains("-requires Microsoft.VisualStudio.Component.SQL.SSDT", diagnostic, StringComparison.Ordinal);
        Assert.Contains("allow_initial_staging_bootstrap", validation, StringComparison.Ordinal);
        Assert.Contains("last_successful_staging_commit_sha", validation, StringComparison.Ordinal);
        Assert.Contains("actions: read", validation, StringComparison.Ordinal);
        Assert.Contains("deployment_authorized -ne $false", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain("webapps-deploy", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sql-action", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invoke-DatabaseDeployment", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StagingResumeWorkflow_UsesReviewedArtifactsWithoutRebuildingTheRelease()
    {
        var workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), ".github", "workflows", "deploy_authcraft-api-staging.yml")).ReplaceLineEndings("\n");

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("release_run_id:", workflow, StringComparison.Ordinal);
        Assert.Contains("review_issue_number:", workflow, StringComparison.Ordinal);
        Assert.Contains("staging_baseline_sha:", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
        Assert.Contains("actions: read", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", workflow, StringComparison.Ordinal);
        Assert.Contains("id-token: write", workflow, StringComparison.Ordinal);
        var jobsBoundary = workflow.IndexOf("\njobs:", StringComparison.Ordinal);
        Assert.True(jobsBoundary > 0);
        Assert.DoesNotContain("id-token: write", workflow[..jobsBoundary], StringComparison.Ordinal);
        Assert.Contains("workflowRun.head_sha", workflow, StringComparison.Ordinal);
        Assert.Contains("workflowRun.id", workflow, StringComparison.Ordinal);
        Assert.Contains("staging-schema-review", workflow, StringComparison.Ordinal);
        Assert.Contains("run-id:", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-ReleasePreflight.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("deployment_authorized -ne $false", workflow, StringComparison.Ordinal);
        Assert.Contains("Invoke-DatabaseDeployment.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Mode = 'Publish'", workflow, StringComparison.Ordinal);
        Assert.Contains("az webapp deploy", workflow, StringComparison.Ordinal);
        Assert.Contains("Test-StagingRelease.ps1", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet build", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dotnet publish", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("msbuild ", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authcraft-api --src-path", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("production", workflow.Replace("production_blocked", "", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("production_blocked", workflow, StringComparison.Ordinal);
        Assert.Contains("candidate-release.json", workflow, StringComparison.Ordinal);
        var smokeCredentialsGate = workflow.IndexOf("Require synthetic Staging smoke credentials", StringComparison.Ordinal);
        var azureLogin = workflow.IndexOf("Log in to the Staging publisher through OIDC", StringComparison.Ordinal);
        var preflight = workflow.IndexOf("Verify release provenance and current Staging gates", StringComparison.Ordinal);
        var databasePublish = workflow.IndexOf("Publish the reviewed Staging database plan", StringComparison.Ordinal);
        var apiDeploy = workflow.IndexOf("Deploy the same API package to Staging", StringComparison.Ordinal);
        var smoke = workflow.IndexOf("Verify Staging health, synthetic login, and tenant roles", StringComparison.Ordinal);
        Assert.True(smokeCredentialsGate >= 0);
        Assert.True(azureLogin > smokeCredentialsGate);
        Assert.True(preflight > azureLogin);
        Assert.True(databasePublish > preflight);
        Assert.True(apiDeploy > databasePublish);
        Assert.True(smoke > apiDeploy);
        Assert.Contains("id: transaction-verification", workflow, StringComparison.Ordinal);
        Assert.Contains("TransactionalPublishDocumentsSeparateSchemaAndPostdeploymentCoverage", workflow, StringComparison.Ordinal);
        Assert.Contains("AUTHCRAFT_TRANSACTION_EVIDENCE_DIRECTORY", workflow, StringComparison.Ordinal);
        Assert.Contains("TransactionVerificationPath = Join-Path", workflow, StringComparison.Ordinal);
        Assert.Contains("AttemptPath = Join-Path", workflow, StringComparison.Ordinal);
        Assert.Contains("name: staging-deployment-attempt", workflow, StringComparison.Ordinal);
        Assert.Contains("retention-days: 30", workflow, StringComparison.Ordinal);
        Assert.Contains("if-no-files-found: error", workflow, StringComparison.Ordinal);
        var summary = workflow.IndexOf("- name: Record Staging deployment attempt", StringComparison.Ordinal);
        var upload = workflow.IndexOf("- name: Preserve Staging deployment evidence", StringComparison.Ordinal);
        Assert.True(summary > smoke);
        Assert.True(upload > summary);
        Assert.Contains("if: always()", workflow[summary..upload], StringComparison.Ordinal);
        Assert.Contains("if: always()", workflow[upload..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("before-sql", "blocked", "unverified", "pending", "not_executed")]
    [InlineData("review-blocked", "blocked", "unverified", "pending", "not_executed")]
    [InlineData("sql-failed", "failed", "separate_schema_and_postdeploy", "unknown", "not_executed")]
    [InlineData("sql-cancelled", "failed", "unverified", "unknown", "not_executed")]
    [InlineData("sql-interrupted", "failed", "separate_schema_and_postdeploy", "unknown", "not_executed")]
    [InlineData("api-failed", "failed", "separate_schema_and_postdeploy", "pending", "not_executed")]
    [InlineData("smoke-failed", "failed", "separate_schema_and_postdeploy", "pending", "unknown")]
    [InlineData("success", "succeeded", "separate_schema_and_postdeploy", "pending", "succeeded")]
    [InlineData("inconsistent-sql", "failed", "separate_schema_and_postdeploy", "unknown", "succeeded")]
    [InlineData("wrong-manifest", "failed", "separate_schema_and_postdeploy", "pending", "succeeded")]
    [InlineData("wrong-attempt", "failed", "separate_schema_and_postdeploy", "pending", "succeeded")]
    [InlineData("missing-manifest", "failed", "separate_schema_and_postdeploy", "pending", "succeeded")]
    [InlineData("revoke-failed", "failed", "separate_schema_and_postdeploy", "pending", "not_executed")]
    public async Task StagingAttempt_PreservesEvidenceWithoutInferringRollbackOrSkippedSuccess(
        string scenario, string result, string scope, string failureState, string healthResult)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"authcraft-attempt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var workflow = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), ".github", "workflows", "deploy_authcraft-api-staging.yml")).ReplaceLineEndings("\n");
            var summaryMatch = Regex.Match(workflow,
                @"(?s)- name: Record Staging deployment attempt\n.*?        run: \|\n(?<body>.*?)\n      - name: Preserve Staging deployment evidence");
            Assert.True(summaryMatch.Success);
            var scriptPath = Path.Combine(directory, "attempt.ps1");
            File.WriteAllText(scriptPath, string.Join("\n",
                summaryMatch.Groups["body"].Value.Split('\n').Select(line => line.Length >= 10 ? line[10..] : line)));
            var evidenceDirectory = Path.Combine(directory, "staging-deployment-evidence");
            Directory.CreateDirectory(evidenceDirectory);
            var manifestDirectory = Path.Combine(directory, "staging-release", "manifest");
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(Path.Combine(manifestDirectory, "release-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    commit_sha = new string(scenario == "wrong-manifest" ? 'd' : 'a', 40),
                    run_id = "12345",
                    api = new { artifact = "api", sha256 = new string('b', 64) },
                    database = new { artifact = "dacpac", sha256 = new string('c', 64) }
                }));
            if (scenario == "missing-manifest")
                File.Delete(Path.Combine(manifestDirectory, "release-manifest.json"));
            if (scenario is not ("before-sql" or "sql-cancelled"))
            {
                var sqlAttempt = new
                {
                    schema_version = 1,
                    release_run_id = scenario == "wrong-attempt" ? "99999" : "12345",
                    commit_sha = new string('a', 40),
                    target_environment = "staging",
                    started_at = "2026-11-09T20:00:00Z",
                    result = scenario == "review-blocked" ? "blocked" : scenario is "sql-failed" or "inconsistent-sql" ? "failed" : scenario == "sql-interrupted" ? "pending" : "succeeded",
                    schema_result = scenario == "review-blocked" ? "blocked" : scenario is "sql-failed" or "inconsistent-sql" ? "failed" : "succeeded",
                    include_transactional_scripts = true,
                    postdeploy_transaction = true,
                    transaction_scope = scope,
                    postdeploy_result = scenario == "review-blocked" ? "pending" : "unknown",
                    failure_state_verification = failureState
                };
                File.WriteAllText(Path.Combine(evidenceDirectory, "sql-attempt.json"), JsonSerializer.Serialize(sqlAttempt));
            }
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", scriptPath })
                processInfo.ArgumentList.Add(argument);
            processInfo.Environment["RUNNER_TEMP"] = directory;
            processInfo.Environment["GITHUB_STEP_SUMMARY"] = Path.Combine(directory, "summary.txt");
            processInfo.Environment["GITHUB_RUN_ID"] = "23456";
            processInfo.Environment["RELEASE_RUN_ID"] = "12345";
            processInfo.Environment["RELEASE_COMMIT_SHA"] = new string('a', 40);
            processInfo.Environment["STARTED_AT"] = "2026-11-09T20:00:00Z";
            processInfo.Environment["TRANSACTION_RESULT"] = scenario == "before-sql" ? "failure" : "success";
            processInfo.Environment["PREFLIGHT_RESULT"] = scenario == "before-sql" ? "skipped" : "success";
            processInfo.Environment["PERMISSION_GRANT_RESULT"] = scenario == "before-sql" ? "skipped" : "success";
            processInfo.Environment["PERMISSION_REVOKE_RESULT"] = scenario == "before-sql" ? "skipped" : scenario == "revoke-failed" ? "failure" : "success";
            processInfo.Environment["DATABASE_RESULT"] = scenario switch
            {
                "before-sql" => "skipped",
                "review-blocked" or "sql-failed" or "inconsistent-sql" => "failure",
                "sql-interrupted" => "cancelled",
                "sql-cancelled" => "cancelled",
                _ => "success"
            };
            var smokeSucceeds = scenario is "success" or "inconsistent-sql" or "wrong-manifest" or "wrong-attempt" or "missing-manifest";
            processInfo.Environment["APPLICATION_RESULT"] = smokeSucceeds || scenario == "smoke-failed"
                ? "success" : scenario == "api-failed" ? "failure" : "skipped";
            processInfo.Environment["SMOKE_RESULT"] = smokeSucceeds
                ? "success" : scenario == "smoke-failed" ? "failure" : "skipped";
            processInfo.Environment["AUTHCRAFT_SQL_ACCESS_TOKEN"] = "synthetic-secret-must-not-leak";
            using var process = Process.Start(processInfo)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await standardOutput + await standardError;
            var evidenceRejected = scenario is "inconsistent-sql" or "wrong-manifest" or "wrong-attempt" or "missing-manifest";
            Assert.True(evidenceRejected ? process.ExitCode != 0 : process.ExitCode == 0, output);
            if (evidenceRejected)
                Assert.Contains("Candidate evidence is incomplete or inconsistent", output, StringComparison.Ordinal);
            var json = File.ReadAllText(Path.Combine(evidenceDirectory, "deployment-attempt.json"));
            using var attempt = JsonDocument.Parse(json);
            Assert.Equal(result, attempt.RootElement.GetProperty("result").GetString());
            Assert.Equal(scope, attempt.RootElement.GetProperty("transaction_scope").GetString());
            Assert.Equal(failureState, attempt.RootElement.GetProperty("failure_state_verification").GetString());
            Assert.Equal(healthResult, attempt.RootElement.GetProperty("health_result").GetString());
            if (scenario == "revoke-failed")
                Assert.Equal("permission_revocation", attempt.RootElement.GetProperty("failure_stage").GetString());
            Assert.DoesNotContain("rolled_back", json, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic-secret-must-not-leak", json + File.ReadAllText(Path.Combine(directory, "summary.txt")), StringComparison.Ordinal);
            Assert.Equal(30, attempt.RootElement.GetProperty("report_retention_days").GetInt32());
            using var candidate = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(evidenceDirectory, "candidate-release.json")));
            var expectedCandidateState = scenario == "success" ? "production_blocked"
                : result == "failed" ? "staging_failed" : "built";
            Assert.Equal(expectedCandidateState, candidate.RootElement.GetProperty("state").GetString());
            Assert.Equal("12345", candidate.RootElement.GetProperty("run_id").GetString());
            Assert.Equal(new string('a', 40), candidate.RootElement.GetProperty("commit_sha").GetString());
            Assert.Equal(scenario is "wrong-manifest" or "missing-manifest" ? null : new string('b', 64),
                candidate.RootElement.GetProperty("api_sha256").GetString());
            Assert.Equal(scenario is "wrong-manifest" or "missing-manifest" ? null : new string('c', 64),
                candidate.RootElement.GetProperty("dacpac_sha256").GetString());
            Assert.False(candidate.RootElement.GetProperty("promotion_authorized").GetBoolean());
            if (scenario == "success")
            {
                Assert.Equal("staging_validated", candidate.RootElement.GetProperty("staging_validation_state").GetString());
                Assert.Equal("23456", candidate.RootElement.GetProperty("staging_validation_reference").GetProperty("deployment_run_id").GetString());
                var attemptHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(evidenceDirectory, "deployment-attempt.json")))).ToLowerInvariant();
                Assert.Equal(attemptHash, candidate.RootElement.GetProperty("staging_validation_reference").GetProperty("attempt_sha256").GetString());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, candidate.RootElement.GetProperty("staging_validation_reference").ValueKind);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("success", true)]
    [InlineData("wrong-ref", false)]
    [InlineData("pull-request", false)]
    [InlineData("wrong-repository", false)]
    [InlineData("not-actions", false)]
    [InlineData("wrong-account", false)]
    [InlineData("wrong-tenant", false)]
    [InlineData("wrong-subscription", false)]
    [InlineData("account-error", false)]
    [InlineData("token-error", false)]
    [InlineData("sql-failure", false)]
    [InlineData("missing-report", false)]
    [InlineData("skipped-test", false)]
    [InlineData("wrong-test", false)]
    [InlineData("evaluate-success", true)]
    [InlineData("evaluate-failure", false)]
    [InlineData("evaluate-tamper", false)]
    [InlineData("evaluate-missing-input", false)]
    public async Task Diagnostic_RequiresExpectedIdentityAndOneExecutedSqlCheck(string scenario, bool succeeds)
    {
        var repository = FindRepositoryRoot();
        var directory = Path.Combine(Path.GetTempPath(), $"authcraft-publisher-wrapper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var scriptPath = Path.Combine(directory, "harness.ps1");
            var logPath = Path.Combine(directory, "commands.log");
            File.WriteAllText(Path.Combine(directory, "sqlpackage.ps1"), """
                $action = @($args | Where-Object { $_ -like '/Action:*' })[0]
                [IO.File]::AppendAllText($env:AUTHCRAFT_WRAPPER_LOG, "$action`n")
                if ($action -notin @('/Action:DeployReport', '/Action:Script')) {
                    throw 'Modification must never be attempted'
                }
                if ($env:AUTHCRAFT_WRAPPER_SCENARIO -eq 'evaluate-failure') {
                    Write-Output "Error SQL72014: VIEW DATABASE STATE permission denied; The user does not have permission to alter the database. SELECT permission was denied on the object 'sql_expression_dependencies', database 'synthetic', schema 'sys'. sensitive-native-diagnostic synthetic.test.token"
                    exit 1
                }
                if ($action -eq '/Action:DeployReport') {
                    $path = @($args | Where-Object { $_ -like '/OutputPath:*' })[0].Substring('/OutputPath:'.Length)
                    [IO.File]::WriteAllText($path, '<DeployReport />')
                } else {
                    $path = @($args | Where-Object { $_ -like '/DeployScriptPath:*' })[0].Substring('/DeployScriptPath:'.Length)
                    [IO.File]::WriteAllText($path, 'CREATE TABLE dbo.synthetic_test (id INT);')
                }
                exit 0
                """);
            File.WriteAllText(scriptPath, """
                param([string]$Repository, [string]$Scenario)
                $ErrorActionPreference = 'Stop'
                function az {
                    $command = $args -join ' '
                    [IO.File]::AppendAllText($env:AUTHCRAFT_WRAPPER_LOG, "$command`n")
                    $global:LASTEXITCODE = 0
                    if ($command -eq 'account show --output json') {
                        if ($Scenario -eq 'account-error') { $global:LASTEXITCODE = 1; return }
                        $account = @{
                            id = '948a8f49-fd90-40d9-8fa9-12f722768a7a'
                            tenantId = 'b7bd0c07-88dc-4c47-81ba-608fd7607a82'
                            user = @{type = 'servicePrincipal';name = 'a2382f63-d653-47f3-82ca-9f2696f63f0a'}
                        }
                        if ($Scenario -eq 'wrong-account') { $account.user.name = 'unexpected' }
                        if ($Scenario -eq 'wrong-tenant') { $account.tenantId = 'unexpected' }
                        if ($Scenario -eq 'wrong-subscription') { $account.id = 'unexpected' }
                        return ($account | ConvertTo-Json)
                    }
                    if ($command -eq 'account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv') {
                        if ($Scenario -eq 'token-error') { $global:LASTEXITCODE = 1; return }
                        return 'synthetic.test.token'
                    }
                    throw 'Unexpected Azure command'
                }
                function dotnet {
                    [IO.File]::AppendAllText($env:AUTHCRAFT_WRAPPER_LOG, "dotnet test`n")
                    if ($env:AUTHCRAFT_SQL_ACCESS_TOKEN -cne 'synthetic.test.token' -or
                        $env:AUTHCRAFT_VERIFY_STAGING_PUBLISHER -cne 'true') { throw 'Missing process scope' }
                    if ($args -notcontains 'FullyQualifiedName=authcraft_api.Tests.StagingPublisherSqlTests.OidcPublisherHasOnlyApprovedReviewPermissions' -or
                        $args -notcontains '--no-build') { throw 'Wrong test command' }
                    $global:LASTEXITCODE = 0
                    if ($Scenario -eq 'sql-failure') {
                        Write-Output 'sensitive-native-diagnostic'
                        $global:LASTEXITCODE = 1
                        return
                    }
                    $resultIndex = [Array]::IndexOf($args, '--results-directory')
                    $resultPath = Join-Path $args[$resultIndex + 1] 'publisher.trx'
                    if ($Scenario -eq 'missing-report') { return }
                    $executed = '1'; $passed = '1'; $outcome = 'Passed'
                    $testName = 'authcraft_api.Tests.StagingPublisherSqlTests.OidcPublisherHasOnlyApprovedReviewPermissions'
                    if ($Scenario -eq 'skipped-test') { $executed = '0'; $passed = '0'; $outcome = 'NotExecuted' }
                    if ($Scenario -eq 'wrong-test') { $testName = 'UnexpectedTest' }
                    [IO.File]::WriteAllText($resultPath, @"
                <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <Results><UnitTestResult testName="$testName" outcome="$outcome" /></Results>
                  <ResultSummary><Counters total="1" executed="$executed" passed="$passed" failed="0" /></ResultSummary>
                </TestRun>
                "@)
                }
                $env:AUTHCRAFT_SQL_ACCESS_TOKEN = 'original-token'
                $env:AUTHCRAFT_VERIFY_STAGING_PUBLISHER = 'original-opt-in'
                $parameters = @{}
                if ($Scenario -like 'evaluate-*') {
                    $dacpac = Join-Path $PSScriptRoot 'database.dacpac'
                    $manifest = Join-Path $PSScriptRoot 'manifest.json'
                    [IO.File]::WriteAllText($dacpac, 'synthetic-dacpac')
                    @{
                        commit_sha = $env:GITHUB_SHA
                        run_id = $env:GITHUB_RUN_ID
                        database = @{artifact = 'dacpac';sha256 = (Get-FileHash -LiteralPath $dacpac -Algorithm SHA256).Hash.ToLowerInvariant()}
                    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifest
                    if ($Scenario -eq 'evaluate-tamper') { [IO.File]::AppendAllText($dacpac, 'tampered') }
                    $parameters = @{
                        EvaluateSchema = $true
                        DacpacPath = $dacpac
                        ManifestPath = $manifest
                        SqlPackagePath = Join-Path $PSScriptRoot 'sqlpackage.ps1'
                    }
                    if ($Scenario -eq 'evaluate-missing-input') { $parameters.Remove('ManifestPath') }
                }
                try {
                    & (Join-Path $Repository 'scripts\Test-StagingPublisher.ps1') @parameters
                } finally {
                    if ($env:AUTHCRAFT_SQL_ACCESS_TOKEN -cne 'original-token' -or
                        $env:AUTHCRAFT_VERIFY_STAGING_PUBLISHER -cne 'original-opt-in') {
                        throw 'Process environment was not restored'
                    }
                }
                """);
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory
            };
            using var diagnosticKey = RSA.Create(2048);
            var publicKey = diagnosticKey.ExportParameters(false);
            var encryptedDiagnosticPath = Path.Combine(directory, "diagnostic.encrypted.json");
            processInfo.Environment["AUTHCRAFT_SQL_DIAGNOSTIC_PUBLIC_KEY"] = JsonSerializer.Serialize(new
            {
                modulus = Convert.ToBase64String(publicKey.Modulus!),
                exponent = Convert.ToBase64String(publicKey.Exponent!)
            });
            processInfo.Environment["AUTHCRAFT_SQL_DIAGNOSTIC_PATH"] = encryptedDiagnosticPath;
            foreach (var argument in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File", scriptPath,
                         "-Repository", repository, "-Scenario", scenario
                     })
            {
                processInfo.ArgumentList.Add(argument);
            }
            processInfo.Environment["AUTHCRAFT_WRAPPER_LOG"] = logPath;
            processInfo.Environment["AUTHCRAFT_WRAPPER_SCENARIO"] = scenario;
            processInfo.Environment["TEMP"] = directory;
            processInfo.Environment["TMP"] = directory;
            processInfo.Environment["GITHUB_ACTIONS"] = scenario == "not-actions" ? "false" : "true";
            processInfo.Environment["GITHUB_REPOSITORY"] = scenario == "wrong-repository" ? "synthetic/other" : "leoperetti/authcraft-api";
            processInfo.Environment["GITHUB_REF"] = scenario == "wrong-ref" ? "refs/heads/feature" : "refs/heads/main";
            processInfo.Environment["GITHUB_EVENT_NAME"] = scenario == "pull-request" ? "pull_request" : "workflow_dispatch";
            processInfo.Environment["GITHUB_RUN_ID"] = "12345";
            processInfo.Environment["GITHUB_SHA"] = new string('a', 40);
            using var process = Process.Start(processInfo)!;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("Publisher wrapper test exceeded 30 seconds.");
            }
            var output = await outputTask;
            var error = await errorTask;
            Assert.Equal(succeeds, process.ExitCode == 0);
            Assert.DoesNotContain("sensitive-native-diagnostic", output + error, StringComparison.Ordinal);
            Assert.DoesNotContain("synthetic.test.token", error, StringComparison.Ordinal);
            if (scenario == "evaluate-failure")
            {
                Assert.Contains("diagnostic_category=database_state_permission", error, StringComparison.Ordinal);
                Assert.Contains("diagnostic_codes=SQL72014", error, StringComparison.Ordinal);
                var compactError = string.Concat(error.Where(character => !char.IsWhiteSpace(character)));
                Assert.Contains("mentioned_permissions=VIEWDATABASESTATE,ALTER,SELECT", compactError, StringComparison.Ordinal);
                Assert.Contains("permission_checks=sql_expression_dependencies_select_denied,alter_database_denied", compactError, StringComparison.Ordinal);
                Assert.Contains("denied_system_objects=sys.sql_expression_dependencies", compactError, StringComparison.Ordinal);
                var encryptedText = File.ReadAllText(encryptedDiagnosticPath);
                Assert.DoesNotContain("sensitive-native-diagnostic", encryptedText, StringComparison.Ordinal);
                Assert.DoesNotContain("synthetic.test.token", encryptedText, StringComparison.Ordinal);
                using var encryptedDiagnostic = JsonDocument.Parse(encryptedText);
                Assert.Equal("RSA-OAEP-SHA256", encryptedDiagnostic.RootElement.GetProperty("algorithm").GetString());
                var decryptedBytes = encryptedDiagnostic.RootElement.GetProperty("blocks").EnumerateArray()
                    .SelectMany(block => diagnosticKey.Decrypt(Convert.FromBase64String(block.GetString()!),
                        RSAEncryptionPadding.OaepSHA256)).ToArray();
                var decryptedText = Encoding.UTF8.GetString(decryptedBytes);
                Assert.Contains("sensitive-native-diagnostic", decryptedText, StringComparison.Ordinal);
                Assert.Contains("synthetic.test.token", decryptedText, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("Process environment was not restored", error, StringComparison.Ordinal);
            Assert.Empty(Directory.GetDirectories(directory, "authcraft-publisher-check-*"));
            if (succeeds)
            {
                Assert.Contains("\"deployment_authorized\":false", output, StringComparison.Ordinal);
                Assert.Equal(scenario == "evaluate-success" ? 5 : 3, File.ReadAllLines(logPath).Length);
                if (scenario == "evaluate-success")
                {
                    Assert.Contains("\"state\":\"evaluated\"", output, StringComparison.Ordinal);
                    Assert.Equal(
                        ["/Action:DeployReport", "/Action:Script"],
                        File.ReadAllLines(logPath).Where(command => command.StartsWith("/Action:", StringComparison.Ordinal)));
                }
            }
            if (File.Exists(logPath))
            {
                Assert.DoesNotContain("/Action:Publish", File.ReadAllText(logPath), StringComparison.Ordinal);
                if (scenario == "evaluate-tamper")
                {
                    Assert.DoesNotContain("/Action:", File.ReadAllText(logPath), StringComparison.Ordinal);
                }
            }
            if (scenario is "wrong-ref" or "pull-request" or "wrong-repository" or "not-actions")
            {
                Assert.False(File.Exists(logPath));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
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
}
