using System.Diagnostics;
using System.Text.Json;

namespace authcraft_api.Tests;

public sealed class StagingRecoveryCheckpointTests
{
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "authcraft-api.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    [Fact]
    public void RecoveryReview_IsAvailableOnlyThroughPackagingPreflightAndCannotAuthorizePublish()
    {
        var root = FindRepositoryRoot();
        var preflight = File.ReadAllText(Path.Combine(root, "scripts", "Test-ReleasePreflight.ps1"));
        var recoveryScript = File.ReadAllText(Path.Combine(root, "scripts", "Get-StagingRecoveryCheckpoint.ps1"));
        var packagingWorkflow = File.ReadAllText(Path.Combine(
            root, ".github", "workflows", "main_authcraft-api-staging.yml"));
        var deploymentWorkflow = File.ReadAllText(Path.Combine(
            root, ".github", "workflows", "deploy_authcraft-api-staging.yml"));

        Assert.Contains("Get-StagingRecoveryCheckpoint.ps1", preflight, StringComparison.Ordinal);
        Assert.Contains("state = $preflightState", preflight, StringComparison.Ordinal);
        Assert.Contains("'recovery_review_required'", preflight, StringComparison.Ordinal);
        Assert.Contains("baseline_eligible = $false", recoveryScript, StringComparison.Ordinal);
        Assert.Contains("recovery_deployment_run_id:", packagingWorkflow, StringComparison.Ordinal);
        Assert.Contains("preflight.state -cne $expectedPreflightState", packagingWorkflow, StringComparison.Ordinal);
        Assert.Contains("recovery_review_required", packagingWorkflow, StringComparison.Ordinal);
        var preflightJobIndex = packagingWorkflow.IndexOf("  preflight-staging:", StringComparison.Ordinal);
        Assert.True(preflightJobIndex >= 0);
        var preflightCheckoutIndex = packagingWorkflow.IndexOf("- uses: actions/checkout@v4", preflightJobIndex, StringComparison.Ordinal);
        var preflightArtifactDownloadIndex = packagingWorkflow.IndexOf("- name: Download this run's release artifacts", preflightCheckoutIndex, StringComparison.Ordinal);
        Assert.True(preflightCheckoutIndex >= 0 && preflightArtifactDownloadIndex > preflightCheckoutIndex);
        Assert.Contains("fetch-depth: 0", packagingWorkflow[preflightCheckoutIndex..preflightArtifactDownloadIndex], StringComparison.Ordinal);
        Assert.Contains("initial bootstrap cannot replace its recovery checkpoint", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("RecoveryDeploymentRunId", deploymentWorkflow, StringComparison.Ordinal);
        Assert.Contains("preflight.state -cne 'ready_for_review'", deploymentWorkflow, StringComparison.Ordinal);
        Assert.Contains("$preflight.deployment_authorized -ne $false", deploymentWorkflow, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("valid", true)]
    [InlineData("wrong-workflow", false)]
    [InlineData("successful-deployment", false)]
    [InlineData("missing-revocation", false)]
    [InlineData("api-not-deployed", false)]
    [InlineData("smoke-not-failed", false)]
    [InlineData("wrong-target", false)]
    [InlineData("wrong-release", false)]
    [InlineData("expired-artifact", false)]
    [InlineData("newer-deployment", false)]
    [InlineData("missing-sql-evidence", false)]
    public async Task RecoveryCheckpoint_RequiresExactAuthenticatedPartialDeploymentEvidence(
        string scenario,
        bool expectedSuccess)
    {
        using var sandbox = new RecoveryCheckpointSandbox(scenario);

        var result = await sandbox.RunAsync();

        Assert.True(expectedSuccess ? result.ExitCode == 0 : result.ExitCode != 0, result.Output);
        Assert.DoesNotContain("synthetic-secret", result.Output, StringComparison.Ordinal);
        if (!expectedSuccess)
            return;

        using var checkpoint = JsonDocument.Parse(result.Output);
        Assert.Equal("partial_recovery", checkpoint.RootElement.GetProperty("baseline_state").GetString());
        Assert.Equal(new string('6', 40), checkpoint.RootElement.GetProperty("baseline_commit_sha").GetString());
        Assert.Equal("42737737446", checkpoint.RootElement.GetProperty("deployment_run_id").GetString());
        Assert.Equal("42721985281", checkpoint.RootElement.GetProperty("release_run_id").GetString());
        Assert.Equal("smoke", checkpoint.RootElement.GetProperty("failure_stage").GetString());
        Assert.False(checkpoint.RootElement.GetProperty("baseline_eligible").GetBoolean());
        Assert.False(checkpoint.RootElement.GetProperty("deployment_authorized").GetBoolean());
        Assert.Equal("not_reconciled", checkpoint.RootElement.GetProperty("live_state").GetString());
        Assert.Matches("^[a-f0-9]{64}$", checkpoint.RootElement.GetProperty("attempt_sha256").GetString());
        Assert.Matches("^[a-f0-9]{64}$", checkpoint.RootElement.GetProperty("candidate_sha256").GetString());
        Assert.Matches("^[a-f0-9]{64}$", checkpoint.RootElement.GetProperty("sql_attempt_sha256").GetString());
    }

    private sealed class RecoveryCheckpointSandbox : IDisposable
    {
        private readonly string _scriptPath;
        private readonly string _artifactDirectory;
        private readonly string _releaseArtifactDirectory;
        private readonly string _scenario;

        public RecoveryCheckpointSandbox(string scenario)
        {
            _scenario = scenario;
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"authcraft-recovery-checkpoint-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            _artifactDirectory = Path.Combine(DirectoryPath, "artifact");
            Directory.CreateDirectory(_artifactDirectory);
            _releaseArtifactDirectory = Path.Combine(DirectoryPath, "release-artifact");
            Directory.CreateDirectory(_releaseArtifactDirectory);
            _scriptPath = Path.Combine(FindRepositoryRoot(), "scripts", "Get-StagingRecoveryCheckpoint.ps1");
            WriteAttemptEvidence();
            WriteOriginalReleaseEvidence();
        }

        public string DirectoryPath { get; }

        public async Task<ProcessResult> RunAsync()
        {
            var harnessPath = Path.Combine(DirectoryPath, "run.ps1");
            var expectedWorkflow = _scenario == "wrong-workflow"
                ? ".github/workflows/main_authcraft-api-staging.yml"
                : ".github/workflows/deploy_authcraft-api-staging.yml";
            var deploymentConclusion = _scenario == "successful-deployment" ? "success" : "failure";
            var revokeConclusion = _scenario == "missing-revocation" ? "failure" : "success";
            var apiConclusion = _scenario == "api-not-deployed" ? "failure" : "success";
            var smokeConclusion = _scenario == "smoke-not-failed" ? "success" : "failure";
            var targetDatabase = _scenario == "wrong-target" ? "authcraft-db" : "authcraft-db-staging";
            var releaseRunId = _scenario == "wrong-release" ? "99999" : "42721985281";
            var artifactExpired = _scenario == "expired-artifact";
            var runIds = _scenario == "newer-deployment"
                ? "[{\"id\":42737737446,\"created_at\":\"2026-11-10T01:50:00Z\",\"repository\":{\"full_name\":\"leoperetti/authcraft-api\"},\"head_repository\":{\"full_name\":\"leoperetti/authcraft-api\"},\"path\":\".github/workflows/deploy_authcraft-api-staging.yml\",\"head_branch\":\"main\"},{\"id\":42740000000,\"created_at\":\"2026-11-10T02:00:00Z\",\"repository\":{\"full_name\":\"leoperetti/authcraft-api\"},\"head_repository\":{\"full_name\":\"leoperetti/authcraft-api\"},\"path\":\".github/workflows/deploy_authcraft-api-staging.yml\",\"head_branch\":\"main\"}]"
                : "[{\"id\":42737737446,\"created_at\":\"2026-11-10T01:50:00Z\",\"repository\":{\"full_name\":\"leoperetti/authcraft-api\"},\"head_repository\":{\"full_name\":\"leoperetti/authcraft-api\"},\"path\":\".github/workflows/deploy_authcraft-api-staging.yml\",\"head_branch\":\"main\"}]";
            var sqlEvidence = _scenario == "missing-sql-evidence" ? "$null" : "'sql-attempt.json'";
            File.WriteAllText(harnessPath, $$"""
                $ErrorActionPreference = 'Stop'
                function gh {
                    $global:LASTEXITCODE = 0
                    if ($args[0] -eq 'run' -and $args[1] -eq 'download') {
                        $artifactName = $args[[Array]::IndexOf($args, '--name') + 1]
                        $destination = $args[[Array]::IndexOf($args, '--dir') + 1]
                        New-Item -ItemType Directory -Path $destination -Force | Out-Null
                        $sourceDirectory = if ($artifactName -eq 'staging-deployment-attempt') {
                            '{{_artifactDirectory}}'
                        } else {
                            '{{_releaseArtifactDirectory}}'
                        }
                        Get-ChildItem -LiteralPath $sourceDirectory | Copy-Item -Destination $destination -Recurse -Force
                        return
                    }
                    if ($args[0] -ne 'api') { throw 'Unexpected GitHub command.' }
                    switch ($args[1]) {
                        'repos/leoperetti/authcraft-api/actions/runs/42737737446' {
                            return (@{
                                id = 42737737446; path = '{{expectedWorkflow}}'; event = 'workflow_dispatch';
                                head_branch = 'main'; head_sha = '{{new string('7', 40)}}'; status = 'completed';
                                conclusion = '{{deploymentConclusion}}'; created_at = '2026-11-10T01:50:00Z';
                                repository = @{ full_name = 'leoperetti/authcraft-api' };
                                head_repository = @{ full_name = 'leoperetti/authcraft-api' }
                            } | ConvertTo-Json -Depth 4 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/42737737446/jobs?per_page=100' {
                            return (@{
                                total_count = 2; jobs = @(
                                    @{ name = 'resolve-release'; status = 'completed'; conclusion = 'success'; steps = @() },
                                    @{ name = 'deploy-reviewed-release'; status = 'completed'; conclusion = 'failure'; steps = @(
                                        @{ name = 'Publish the reviewed Staging database plan'; conclusion = 'success' },
                                        @{ name = 'Revoke and independently verify the Staging publisher role'; conclusion = '{{revokeConclusion}}' },
                                        @{ name = 'Deploy the same API package to Staging'; conclusion = '{{apiConclusion}}' },
                                        @{ name = 'Verify Staging health, synthetic login, and tenant roles'; conclusion = '{{smokeConclusion}}' }
                                    ) }
                                )
                            } | ConvertTo-Json -Depth 6 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/42737737446/artifacts?per_page=100' {
                            return (@{
                                total_count = 1; artifacts = @(@{
                                    id = 13727659079; name = 'staging-deployment-attempt';
                                    expired = ${{artifactExpired.ToString().ToLowerInvariant()}};
                                    expires_at = [DateTime]::UtcNow.AddDays(1).ToString('o');
                                    workflow_run = @{ id = 42737737446; head_sha = '{{new string('7', 40)}}' }
                                })
                            } | ConvertTo-Json -Depth 5 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/42721985281' {
                            return (@{
                                id = 42721985281; path = '.github/workflows/main_authcraft-api-staging.yml';
                                event = 'workflow_dispatch'; head_branch = 'main'; head_sha = '{{new string('6', 40)}}';
                                status = 'completed'; conclusion = 'success';
                                repository = @{ full_name = 'leoperetti/authcraft-api' };
                                head_repository = @{ full_name = 'leoperetti/authcraft-api' }
                            } | ConvertTo-Json -Depth 4 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/42721985281/jobs?per_page=100' {
                            return (@{
                                total_count = 3; jobs = @(
                                    @{ name = 'validate'; status = 'completed'; conclusion = 'success' },
                                    @{ name = 'verify-staging-publisher'; status = 'completed'; conclusion = 'success' },
                                    @{ name = 'preflight-staging'; status = 'completed'; conclusion = 'success' }
                                )
                            } | ConvertTo-Json -Depth 4 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/42721985281/artifacts?per_page=100' {
                            $artifactNames = @('api', 'dacpac', 'release-manifest', 'candidate-release', 'staging-schema-review')
                            return (@{
                                total_count = $artifactNames.Count;
                                artifacts = @($artifactNames | ForEach-Object {
                                    @{ name = $_; expired = $false; expires_at = [DateTime]::UtcNow.AddDays(1).ToString('o');
                                        workflow_run = @{ id = 42721985281; head_sha = '{{new string('6', 40)}}' } }
                                })
                            } | ConvertTo-Json -Depth 5 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/workflows/deploy_authcraft-api-staging.yml/runs?branch=main&per_page=100' {
                            return '{"total_count":{{(_scenario == "newer-deployment" ? 2 : 1)}},"workflow_runs":{{runIds}}}'
                        }
                        default { throw "Unexpected GitHub endpoint: $($args[1])" }
                    }
                }
                & '{{_scriptPath}}' -DeploymentRunId '42737737446' -OutputDirectory '{{DirectoryPath}}'
                """);
            var processInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = DirectoryPath
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", harnessPath })
                processInfo.ArgumentList.Add(argument);
            using var process = Process.Start(processInfo)!;
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new ProcessResult(process.ExitCode, await standardOutput + await standardError);
        }

        private void WriteAttemptEvidence()
        {
            var candidate = new
            {
                commit_sha = new string('6', 40),
                run_id = "42721985281",
                api_artifact_name = "api",
                api_sha256 = new string('a', 64),
                dacpac_artifact_name = "dacpac",
                dacpac_sha256 = new string('b', 64),
                state = "staging_failed",
                staging_validation_state = "not_validated",
                staging_validation_reference = (object?)null,
                promotion_authorized = false
            };
            var attempt = new
            {
                deployment_run_id = "42737737446",
                release_run_id = _scenario == "wrong-release" ? "99999" : "42721985281",
                commit_sha = new string('6', 40),
                target_environment = "staging",
                database_server = "authcraftdbbrasil.database.windows.net",
                database_name = _scenario == "wrong-target" ? "authcraft-db" : "authcraft-db-staging",
                result = "failed",
                schema_result = "succeeded",
                application_result = "succeeded",
                permission_grant_result = "succeeded",
                permission_revoke_result = "succeeded",
                smoke_result = "failed",
                failure_stage = "smoke",
                include_transactional_scripts = true,
                postdeploy_transaction = true,
                transaction_scope = "separate_schema_and_postdeploy",
                postdeploy_result = "unknown",
                failure_state_verification = "pending",
                dacpac_sha256 = new string('b', 64),
                deploy_report_sha256 = new string('c', 64),
                deploy_script_sha256 = new string('d', 64)
            };
            var sqlAttempt = new
            {
                release_run_id = "42721985281",
                commit_sha = new string('6', 40),
                target_environment = "staging",
                database_server = "authcraftdbbrasil.database.windows.net",
                database_name = "authcraft-db-staging",
                result = "succeeded",
                schema_result = "succeeded",
                dacpac_sha256 = new string('b', 64),
                deploy_report_sha256 = new string('c', 64),
                deploy_script_sha256 = new string('d', 64)
            };
            File.WriteAllText(Path.Combine(_artifactDirectory, "candidate-release.json"),
                JsonSerializer.Serialize(candidate));
            File.WriteAllText(Path.Combine(_artifactDirectory, "deployment-attempt.json"),
                JsonSerializer.Serialize(attempt));
            if (_scenario != "missing-sql-evidence")
            {
                File.WriteAllText(Path.Combine(_artifactDirectory, "sql-attempt.json"),
                    JsonSerializer.Serialize(sqlAttempt));
            }
        }

        private void WriteOriginalReleaseEvidence()
        {
            var apiHash = new string('a', 64);
            var dacpacHash = new string('b', 64);
            File.WriteAllText(Path.Combine(_releaseArtifactDirectory, "release-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    commit_sha = new string('6', 40),
                    run_id = "42721985281",
                    api = new { artifact = "api", sha256 = apiHash },
                    database = new { artifact = "dacpac", sha256 = dacpacHash }
                }));
            File.WriteAllText(Path.Combine(_releaseArtifactDirectory, "candidate-release.json"),
                JsonSerializer.Serialize(new
                {
                    commit_sha = new string('6', 40),
                    run_id = "42721985281",
                    api_artifact_name = "api",
                    api_sha256 = apiHash,
                    dacpac_artifact_name = "dacpac",
                    dacpac_sha256 = dacpacHash,
                    state = "built",
                    staging_validation_state = "not_validated",
                    staging_validation_reference = (object?)null,
                    promotion_authorized = false
                }));
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
