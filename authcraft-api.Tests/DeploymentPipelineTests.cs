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
    public void Preflight_VerifiesArtifactsAndLiveStagingGatesWithoutAuthorizingPublish()
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);

        var result = sandbox.RunPreflight("refs/heads/main", "staging", sandbox.ParentCommitSha);

        Assert.Equal(0, result.ExitCode);
        using var preflight = JsonDocument.Parse(result.Output);
        Assert.Equal("ready_for_review", preflight.RootElement.GetProperty("state").GetString());
        Assert.Equal("verified", preflight.RootElement.GetProperty("artifact_validation").GetString());
        Assert.False(preflight.RootElement.GetProperty("deployment_authorized").GetBoolean());
        Assert.Equal("authcraft-db-staging", preflight.RootElement.GetProperty("database_name").GetString());
        Assert.Equal("confirmed", preflight.RootElement.GetProperty("staging_preflight").GetProperty("verification_state").GetString());
    }

    [Fact]
    public void Preflight_RecordsUnknownInitialBaselineWithoutAuthorizingDeployment()
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);

        var result = sandbox.RunPreflight(
            "refs/heads/main", "staging", "unknown", scenario: "initial-bootstrap", allowInitialBootstrap: true);

        Assert.Equal(0, result.ExitCode);
        using var preflight = JsonDocument.Parse(result.Output);
        Assert.Equal("unknown", preflight.RootElement.GetProperty("baseline_state").GetString());
        Assert.Equal("unknown", preflight.RootElement.GetProperty("baseline_commit_sha").GetString());
        Assert.True(preflight.RootElement.GetProperty("bootstrap_review_required").GetBoolean());
        Assert.False(preflight.RootElement.GetProperty("deployment_authorized").GetBoolean());
    }

    [Theory]
    [InlineData("wrong-live-runtime")]
    [InlineData("wrong-live-publisher")]
    [InlineData("missing-live-reader")]
    [InlineData("production-workflow-active")]
    public void Preflight_BlocksLiveStagingOrProductionRouteMismatch(string scenario)
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);

        var result = sandbox.RunPreflight("refs/heads/main", "staging", sandbox.ParentCommitSha, scenario: scenario);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("\"deployment_authorized\":true", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_RejectsUnknownBaselineWithoutExplicitBootstrapSwitch()
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);

        var result = sandbox.RunPreflight("refs/heads/main", "staging", "unknown");

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("\"artifact_validation\":\"verified\"", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_RejectsInitialBootstrapWhenSuccessfulStagingDeploymentExists()
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);

        var result = sandbox.RunPreflight(
            "refs/heads/main", "staging", "unknown", scenario: "bootstrap-with-deployment", allowInitialBootstrap: true);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("\"artifact_validation\":\"verified\"", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong-repository")]
    [InlineData("wrong-run")]
    [InlineData("wrong-baseline")]
    [InlineData("expired-artifact")]
    [InlineData("expired-at-date")]
    [InlineData("duplicate-artifact")]
    [InlineData("truncated-artifacts")]
    [InlineData("truncated-jobs")]
    [InlineData("missing-baseline")]
    [InlineData("build-only-baseline")]
    [InlineData("failed-build")]
    [InlineData("failed-publisher-verification")]
    [InlineData("untrusted-event")]
    [InlineData("download-tamper")]
    [InlineData("metadata-error")]
    public void Preflight_BlocksUnverifiedActionsProvenance(string scenario)
    {
        using var sandbox = new PipelineSandbox();
        Assert.Equal(0, sandbox.CreateManifest().ExitCode);

        var result = sandbox.RunPreflight("refs/heads/main", "staging", sandbox.ParentCommitSha, scenario: scenario);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("\"artifact_validation\":\"verified\"", result.Output, StringComparison.Ordinal);
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
            staging_baseline_sha=unknown
            target_environment=staging
            database_server=authcraftdbbrasil.database.windows.net
            database_name=authcraft-db-staging
            dacpac_sha256={{new string('b', 64)}}
            api_compatibility=accepted
            data_impact=accepted
            recovery_plan=accepted
            block_on_possible_data_loss=true
            drop_objects_not_in_source=false
            script_database_options=false
            deploy_report_sha256={{new string('c', 64)}}
            deploy_script_sha256={{new string('d', 64)}}
            '@
            $parameters = @{
                Body = $body
                Author = 'leoperetti'
                AllowedReviewer = 'leoperetti'
                RunId = '12345'
                CommitSha = '{{new string('a', 40)}}'
                TargetEnvironment = 'staging'
                DatabaseServer = 'authcraftdbbrasil.database.windows.net'
                DatabaseName = 'authcraft-db-staging'
                DacpacSha256 = '{{new string('b', 64)}}'
                ReportSha256 = '{{new string('c', 64)}}'
                ScriptSha256 = '{{new string('d', 64)}}'
                StagingBaselineSha = 'unknown'
            }
            $valid = Test-DeploymentReviewRecord @parameters
            $wrongAuthorParameters = $parameters.Clone()
            $wrongAuthorParameters.Author = 'other-user'
            $wrongAuthor = Test-DeploymentReviewRecord @wrongAuthorParameters
            $wrongPlanParameters = $parameters.Clone()
            $wrongPlanParameters.ReportSha256 = '{{new string('e', 64)}}'
            $wrongPlan = Test-DeploymentReviewRecord @wrongPlanParameters
            $wrongBaselineParameters = $parameters.Clone()
            $wrongBaselineParameters.StagingBaselineSha = '{{new string('e', 40)}}'
            $wrongBaseline = Test-DeploymentReviewRecord @wrongBaselineParameters
            $missingDataImpactParameters = $parameters.Clone()
            $missingDataImpactParameters.Body = $body -replace '(?im)^data_impact=accepted\r?\n', ''
            $missingDataImpact = Test-DeploymentReviewRecord @missingDataImpactParameters
            $wrongOptionsBody = $body.Replace('script_database_options=false', 'script_database_options=true')
            $wrongOptionsParameters = $parameters.Clone()
            $wrongOptionsParameters.Body = $wrongOptionsBody
            $wrongOptions = Test-DeploymentReviewRecord @wrongOptionsParameters
            $missingOptionsBody = $body -replace '(?im)^script_database_options=false\r?\n', ''
            $missingOptionsParameters = $parameters.Clone()
            $missingOptionsParameters.Body = $missingOptionsBody
            $missingOptions = Test-DeploymentReviewRecord @missingOptionsParameters
            if ($valid -and -not $wrongAuthor -and -not $wrongPlan -and -not $wrongBaseline -and
                -not $missingDataImpact -and -not $wrongOptions -and -not $missingOptions) { exit 0 }
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

        public ProcessResult RunPreflight(
            string gitHubRef,
            string target,
            string lastCommitSha,
            string runId = "12345",
            string scenario = "",
            bool allowInitialBootstrap = false)
        {
            var harnessPath = Path.Combine(DirectoryPath, "preflight-harness.ps1");
            File.WriteAllText(harnessPath, $$"""
                $ErrorActionPreference = 'Stop'
                function az {
                    $global:LASTEXITCODE = 0
                    if ($args[0] -eq 'account') {
                        $userName = 'a2382f63-d653-47f3-82ca-9f2696f63f0a'
                        if ('{{scenario}}' -eq 'wrong-live-publisher') { $userName = 'unexpected-client' }
                        return (@{ subscription_id = '948a8f49-fd90-40d9-8fa9-12f722768a7a';
                            user_type = 'servicePrincipal'; user_name = $userName } | ConvertTo-Json -Compress)
                    }
                    if ($args[0] -eq 'webapp' -and $args[1] -eq 'show') {
                        return (@{ id = '/subscriptions/948a8f49-fd90-40d9-8fa9-12f722768a7a/resourceGroups/AuthCraft/providers/Microsoft.Web/sites/authcraft-api-staging';
                            name = 'authcraft-api-staging'; state = 'Running'; host = 'authcraft-api-staging.azurewebsites.net';
                            runtime_principal = 'staging-runtime' } | ConvertTo-Json -Compress)
                    }
                    if ($args[0] -eq 'webapp' -and $args[1] -eq 'config') {
                        $dotnetEnvironment = 'Staging'
                        if ('{{scenario}}' -eq 'wrong-live-runtime') { $dotnetEnvironment = 'Production' }
                        return (@(
                            @{ name = 'ASPNETCORE_ENVIRONMENT'; value = 'Staging' },
                            @{ name = 'DOTNET_ENVIRONMENT'; value = $dotnetEnvironment },
                            @{ name = 'KeyVault__Name'; value = 'authcraft-vault-staging' }
                        ) | ConvertTo-Json -Depth 3 -Compress)
                    }
                    if ($args[0] -eq 'sql' -and $args[1] -eq 'db') {
                        return (@{ id = '/subscriptions/948a8f49-fd90-40d9-8fa9-12f722768a7a/resourceGroups/AuthCraft/providers/Microsoft.Sql/servers/authcraftdbbrasil/databases/authcraft-db-staging';
                            name = 'authcraft-db-staging'; status = 'Online' } | ConvertTo-Json -Compress)
                    }
                    if ($args[0] -eq 'keyvault') {
                        return (@{ id = '/subscriptions/948a8f49-fd90-40d9-8fa9-12f722768a7a/resourceGroups/AuthCraft/providers/Microsoft.KeyVault/vaults/authcraft-vault-staging';
                            name = 'authcraft-vault-staging'; rbac_enabled = $true } | ConvertTo-Json -Compress)
                    }
                    if ($args[0] -eq 'role' -and $args[1] -eq 'assignment') {
                        $scopeIndex = [Array]::IndexOf($args, '--scope')
                        $scope = $args[$scopeIndex + 1]
                        if ('{{scenario}}' -eq 'missing-live-reader' -and $scope -like '*vaults/authcraft-vault-staging') {
                            return '[]'
                        }
                        $assignments = @()
                        if ($scope -like '*sites/authcraft-api-staging') {
                            $assignments = @(
                                @{ principalId = '2a446033-801f-444c-985e-6519c4661632'; roleDefinitionName = 'Reader'; scope = $scope },
                                @{ principalId = '2a446033-801f-444c-985e-6519c4661632'; roleDefinitionName = 'Website Contributor'; scope = $scope }
                            )
                        } elseif ($scope -like '*databases/authcraft-db-staging') {
                            $assignments = @(
                                @{ principalId = '2a446033-801f-444c-985e-6519c4661632'; roleDefinitionName = 'Reader'; scope = $scope }
                            )
                        } else {
                            $assignments = @(
                                @{ principalId = '2a446033-801f-444c-985e-6519c4661632'; roleDefinitionName = 'Reader'; scope = $scope },
                                @{ principalId = 'staging-runtime'; roleDefinitionName = 'Key Vault Secrets User'; scope = $scope }
                            )
                        }
                        return ($assignments | ConvertTo-Json -Depth 4 -Compress)
                    }
                    throw 'Unexpected Azure metadata command.'
                }
                function gh {
                    $global:LASTEXITCODE = 0
                    if ('{{scenario}}' -eq 'metadata-error') { $global:LASTEXITCODE = 1; return }
                    if ($args[0] -eq 'run' -and $args[1] -eq 'download') {
                        $name = $args[[Array]::IndexOf($args, '--name') + 1]
                        $destination = $args[[Array]::IndexOf($args, '--dir') + 1]
                        New-Item -ItemType Directory -Path $destination -Force | Out-Null
                        switch ($name) {
                            'api' { Copy-Item -LiteralPath '{{ApiPath}}' -Destination (Join-Path $destination 'api.zip') }
                            'dacpac' { Copy-Item -LiteralPath '{{DacpacPath}}' -Destination (Join-Path $destination 'database.dacpac') }
                            'release-manifest' { Copy-Item -LiteralPath '{{ManifestPath}}' -Destination (Join-Path $destination 'manifest.json') }
                            default { throw 'Unexpected artifact download.' }
                        }
                        if ('{{scenario}}' -eq 'download-tamper' -and $name -eq 'api') {
                            Add-Content -LiteralPath (Join-Path $destination 'api.zip') -Value 'changed'
                        }
                        return
                    }
                    if ($args[0] -ne 'api') { throw 'Unexpected GitHub command.' }
                    switch ($args[1]) {
                        'repos/leoperetti/authcraft-api/actions/workflows' {
                            $workflowState = 'disabled_manually'
                            if ('{{scenario}}' -eq 'production-workflow-active') { $workflowState = 'active' }
                            return (@{ total_count = 1; workflows = @(@{ path = '.github/workflows/main_authcraft-api.yml'; state = $workflowState }) } | ConvertTo-Json -Depth 4 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/12345' {
                            $repository = 'leoperetti/authcraft-api'
                            if ('{{scenario}}' -eq 'wrong-repository') { $repository = 'other/repository' }
                            $sha = '{{CommitSha}}'
                            if ('{{scenario}}' -eq 'wrong-run') { $sha = '{{ParentCommitSha}}' }
                            $event = 'workflow_dispatch'
                            if ('{{scenario}}' -eq 'untrusted-event') { $event = 'pull_request' }
                            return (@{ id = 12345; head_sha = $sha; head_branch = 'main'; event = $event;
                                path = '.github/workflows/main_authcraft-api-staging.yml';
                                repository = @{ full_name = $repository }; head_repository = @{ full_name = $repository }
                            } | ConvertTo-Json -Depth 4 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/12345/jobs?per_page=100' {
                            $conclusion = 'success'
                            if ('{{scenario}}' -eq 'failed-build') { $conclusion = 'failure' }
                            $publisherConclusion = 'success'
                            if ('{{scenario}}' -eq 'failed-publisher-verification') { $publisherConclusion = 'failure' }
                            $total = 2
                            if ('{{scenario}}' -eq 'truncated-jobs') { $total = 3 }
                            return (@{ total_count = $total; jobs = @(
                                @{ name = 'validate'; status = 'completed'; conclusion = $conclusion },
                                @{ name = 'verify-staging-publisher'; status = 'completed'; conclusion = $publisherConclusion }
                            ) } | ConvertTo-Json -Depth 4 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/12345/artifacts?per_page=100' {
                            $names = @('api', 'dacpac', 'release-manifest')
                            if ('{{scenario}}' -eq 'duplicate-artifact') { $names += 'api' }
                            $total = $names.Count
                            if ('{{scenario}}' -eq 'truncated-artifacts') { $total++ }
                            $expires = [DateTime]::UtcNow.AddDays(1)
                            if ('{{scenario}}' -eq 'expired-at-date') { $expires = [DateTime]::UtcNow.AddMinutes(-1) }
                            return (@{ total_count = $total; artifacts = $names | ForEach-Object {
                                @{ name = $_; expired = ('{{scenario}}' -eq 'expired-artifact');
                                    expires_at = $expires.ToString('o'); workflow_run = @{ id = 12345; head_sha = '{{CommitSha}}' } }
                            } } | ConvertTo-Json -Depth 5 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/workflows/main_authcraft-api-staging.yml/runs?branch=main&status=success&per_page=100' {
                            if ('{{scenario}}' -eq 'missing-baseline') { return '{"total_count":0,"workflow_runs":[]}' }
                            $sha = '{{ParentCommitSha}}'
                            if ('{{scenario}}' -eq 'wrong-baseline') { $sha = '{{RemoteMainSha}}' }
                            return (@{ total_count = 1; workflow_runs = @(@{ id = 12344; head_sha = $sha; head_branch = 'main';
                                created_at = '2026-11-09T12:00:00Z';
                                path = '.github/workflows/main_authcraft-api-staging.yml'; status = 'completed'; conclusion = 'success';
                                event = 'workflow_dispatch'; repository = @{ full_name = 'leoperetti/authcraft-api' };
                                head_repository = @{ full_name = 'leoperetti/authcraft-api' } }) } | ConvertTo-Json -Depth 5 -Compress)
                        }
                        'repos/leoperetti/authcraft-api/actions/runs/12344/jobs?per_page=100' {
                            if ('{{scenario}}' -eq 'initial-bootstrap') {
                                return '{"total_count":1,"jobs":[{"name":"validate","status":"completed","conclusion":"success"}]}'
                            }
                            if ('{{scenario}}' -eq 'bootstrap-with-deployment') {
                                return '{"total_count":1,"jobs":[{"name":"deploy-staging","status":"completed","conclusion":"success"}]}'
                            }
                            if ('{{scenario}}' -eq 'build-only-baseline') {
                                return '{"total_count":1,"jobs":[{"name":"build","status":"completed","conclusion":"success"}]}'
                            }
                            return '{"total_count":1,"jobs":[{"name":"deploy-staging","status":"completed","conclusion":"success"}]}'
                        }
                        default { throw 'Unexpected GitHub API request.' }
                    }
                }
                $preflightParameters = @{
                    ManifestPath = '{{ManifestPath}}'
                    ApiArtifactPath = '{{ApiPath}}'
                    DatabaseArtifactPath = '{{DacpacPath}}'
                    ExpectedCommitSha = '{{CommitSha}}'
                    ExpectedRunId = '{{runId}}'
                    GitHubRef = '{{gitHubRef}}'
                    TargetEnvironment = '{{target}}'
                    LastSuccessfulCommitSha = '{{lastCommitSha}}'
                    RepositoryPath = '{{_gitRepositoryPath}}'
                }
                if ('{{allowInitialBootstrap}}' -eq 'True') {
                    $preflightParameters.AllowInitialStagingBootstrap = $true
                }
                & '{{Path.Combine(_scriptsDirectory, "Test-ReleasePreflight.ps1")}}' @preflightParameters
                """);
            return RunPowerShell(harnessPath);
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
