using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace authcraft_api.Tests;

public sealed class DatabaseDeploymentReviewTests
{
    [Fact]
    public void Evaluate_PreservesUnvalidatedPlanForDiagnosticsWithoutPublishing()
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run("evaluate-unvalidated-check");

        Assert.True(result.ExitCode == 0, result.Error);
        using var evaluated = JsonDocument.Parse(result.Output);
        Assert.Equal("evaluated", evaluated.RootElement.GetProperty("state").GetString());
        var scriptPath = evaluated.RootElement.GetProperty("deploy_script_path").GetString()!;
        Assert.Contains("WITH NOCHECK", File.ReadAllText(scriptPath), StringComparison.Ordinal);
        Assert.Equal(["/Action:DeployReport", "/Action:Script"], sandbox.Actions);
    }

    [Theory]
    [InlineData("validated-deferred-check")]
    [InlineData("validated-dacfx-tail")]
    public void Publish_AllowsDeferredValidationOnlyWithTheTrustedFinalValidationBlock(string scenario)
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run(scenario);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("/Action:Publish", sandbox.Actions);
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("json-array-comments")]
    public void Publish_RevalidatesExactAuthenticatedReviewAndPlanBeforePublishing(string scenario)
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run(scenario);

        Assert.True(result.ExitCode == 0, result.Error);
        using var published = JsonDocument.Parse(result.Output);
        Assert.Equal("published", published.RootElement.GetProperty("state").GetString());
        Assert.Equal(42, published.RootElement.GetProperty("review_comment_id").GetInt32());
        var reportPath = published.RootElement.GetProperty("deploy_report_path").GetString()!;
        var scriptPath = published.RootElement.GetProperty("deploy_script_path").GetString()!;
        Assert.NotEqual(sandbox.ReviewedReportPath, reportPath);
        Assert.NotEqual(sandbox.ReviewedScriptPath, scriptPath);
        Assert.True(File.Exists(reportPath));
        Assert.True(File.Exists(scriptPath));
        Assert.Equal(
            published.RootElement.GetProperty("deploy_report_sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(reportPath))).ToLowerInvariant());
        Assert.Equal(
            published.RootElement.GetProperty("deploy_script_sha256").GetString(),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(scriptPath))).ToLowerInvariant());
        Assert.Equal(
            ["/Action:DeployReport", "/Action:Script", "/Action:DeployReport", "/Action:Script", "/Action:Publish"],
            sandbox.Actions);
        Assert.Equal("previous reviewed report", File.ReadAllText(sandbox.ReviewedReportPath));
        Assert.Equal("previous reviewed SQL", File.ReadAllText(sandbox.ReviewedScriptPath));
        Assert.DoesNotContain("native diagnostic", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
        using var attempt = JsonDocument.Parse(File.ReadAllText(sandbox.AttemptPath));
        Assert.Equal("succeeded", attempt.RootElement.GetProperty("result").GetString());
        Assert.Equal("separate_schema_and_postdeploy", attempt.RootElement.GetProperty("transaction_scope").GetString());
        Assert.True(attempt.RootElement.GetProperty("include_transactional_scripts").GetBoolean());
        Assert.True(attempt.RootElement.GetProperty("postdeploy_transaction").GetBoolean());
        Assert.Equal("unknown", attempt.RootElement.GetProperty("postdeploy_result").GetString());
        Assert.Equal("pending", attempt.RootElement.GetProperty("failure_state_verification").GetString());
        Assert.Equal(6, attempt.RootElement.GetProperty("transaction_verification_reference").GetProperty("cases").GetArrayLength());
        Assert.Equal(published.RootElement.GetProperty("deploy_script_sha256").GetString(),
            attempt.RootElement.GetProperty("transaction_verification_reference").GetProperty("deploy_script_sha256").GetString());
    }

    [Theory]
    [InlineData("recovery-approved", true)]
    [InlineData("recovery-wrong-run", false)]
    [InlineData("recovery-missing-live-reconciliation", false)]
    public void Publish_RecoveryRequiresExactCheckpointAndLiveStateReview(string scenario, bool shouldPublish)
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run(scenario);

        Assert.True(shouldPublish ? result.ExitCode == 0 : result.ExitCode != 0, result.Error);
        var expectedActions = shouldPublish
            ? new[] { "/Action:DeployReport", "/Action:Script", "/Action:DeployReport", "/Action:Script", "/Action:Publish" }
            : new[] { "/Action:DeployReport", "/Action:Script" };
        Assert.Equal(expectedActions, sandbox.Actions);
    }

    [Theory]
    [InlineData("missing-proof")]
    [InlineData("invalid-proof")]
    [InlineData("wrong-tool-proof")]
    [InlineData("wrong-version-proof")]
    [InlineData("wrong-source-proof")]
    [InlineData("missing-case-proof")]
    [InlineData("wrong-state-proof")]
    [InlineData("skipped-proof")]
    [InlineData("changed-sql-proof")]
    [InlineData("wrong-boundaries-proof")]
    [InlineData("duplicate-case-proof")]
    [InlineData("missing-trx-proof")]
    public void Publish_RequiresToolAndSourceBoundExecutedTransactionProof(string scenario)
    {
        using var sandbox = new ReviewSandbox();
        var result = sandbox.Run(scenario);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("/Action:Publish", sandbox.Actions);
        Assert.DoesNotContain(sandbox.SyntheticToken, result.Output + result.Error, StringComparison.Ordinal);
        using var attempt = JsonDocument.Parse(File.ReadAllText(sandbox.AttemptPath));
        Assert.Equal("blocked", attempt.RootElement.GetProperty("result").GetString());
        Assert.Equal("unverified", attempt.RootElement.GetProperty("transaction_scope").GetString());
        Assert.Equal("pending", attempt.RootElement.GetProperty("postdeploy_result").GetString());
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
    [InlineData("unvalidated-check")]
    [InlineData("unvalidated-foreign-key")]
    [InlineData("disabled-constraint")]
    [InlineData("validation-not-final")]
    [InlineData("validation-followed-by-write")]
    [InlineData("validation-modified")]
    [InlineData("postdeploy-early-commit")]
    [InlineData("postdeploy-no-transaction")]
    [InlineData("uncovered-constraint")]
    [InlineData("reviewer-override")]
    [InlineData("wrong-options")]
    [InlineData("missing-options")]
    [InlineData("missing-transaction-option")]
    [InlineData("false-transaction-option")]
    [InlineData("duplicate-transaction-option")]
    [InlineData("missing-postdeploy-option")]
    [InlineData("false-postdeploy-option")]
    [InlineData("duplicate-postdeploy-option")]
    [InlineData("wrong-staging-baseline")]
    [InlineData("missing-api-compatibility")]
    [InlineData("missing-data-impact")]
    [InlineData("missing-recovery-plan")]
    public void Publish_BlocksInvalidOrChangedReviewWithoutModification(string scenario)
    {
        using var sandbox = new ReviewSandbox();

        var result = sandbox.Run(scenario);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("/Action:Publish", sandbox.Actions);
        Assert.DoesNotContain(sandbox.SyntheticToken, result.Output + result.Error, StringComparison.Ordinal);
        if (scenario is "unvalidated-check" or "unvalidated-foreign-key" or "disabled-constraint"
            or "validation-not-final" or "validation-followed-by-write" or "validation-modified" or "uncovered-constraint"
            or "postdeploy-early-commit" or "postdeploy-no-transaction")
        {
            Assert.Contains("NOCHECK", result.Error, StringComparison.Ordinal);
            Assert.Equal(2, sandbox.Actions.Length);
        }
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

    [Theory]
    [InlineData("547")]
    [InlineData("51000")]
    public void Publish_FailureDoesNotAttemptRollbackOrExposeNativeDiagnostics(string errorNumber)
    {
        using var sandbox = new ReviewSandbox();
        var protectedFiles = sandbox.SnapshotProtectedFiles();

        var result = sandbox.Run($"publish-fails-{errorNumber}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(
            ["/Action:DeployReport", "/Action:Script", "/Action:DeployReport", "/Action:Script", "/Action:Publish"],
            sandbox.Actions);
        Assert.Contains("no rollback", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("native diagnostic", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sandbox.SyntheticToken, result.Output + result.Error, StringComparison.Ordinal);
        Assert.Equal(protectedFiles, sandbox.SnapshotProtectedFiles());
        Assert.Equal("schema-partially-applied", File.ReadAllText(sandbox.SchemaStatePath));
        using var attempt = JsonDocument.Parse(File.ReadAllText(sandbox.AttemptPath));
        Assert.Equal("failed", attempt.RootElement.GetProperty("result").GetString());
        Assert.Equal("unknown", attempt.RootElement.GetProperty("failure_state_verification").GetString());
        Assert.Equal("unknown", attempt.RootElement.GetProperty("postdeploy_result").GetString());
        Assert.Equal("not_executed", attempt.RootElement.GetProperty("application_result").GetString());
        Assert.DoesNotContain(sandbox.SyntheticToken, File.ReadAllText(sandbox.AttemptPath), StringComparison.Ordinal);
    }

    private sealed class ReviewSandbox : IDisposable
    {
        private readonly string _directory;
        private readonly string _harnessPath;
        private readonly string _actionLog;
        private readonly string _apiPackagePath;
        private readonly string _activeApiVersionPath;
        private readonly string _recentWritesPath;

        public ReviewSandbox()
        {
            _directory = Path.Combine(Path.GetTempPath(), $"authcraft-sql-review-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_directory);
            _actionLog = Path.Combine(_directory, "actions.log");
            _harnessPath = Path.Combine(_directory, "review-harness.ps1");
            _apiPackagePath = Path.Combine(_directory, "candidate-api.zip");
            _activeApiVersionPath = Path.Combine(_directory, "active-api-version.txt");
            _recentWritesPath = Path.Combine(_directory, "recent-writes.txt");
            ReviewedReportPath = Path.Combine(_directory, "staging-deploy-report.xml");
            ReviewedScriptPath = Path.Combine(_directory, "staging-deploy.sql");
            SchemaStatePath = Path.Combine(_directory, "schema-state.txt");
            File.WriteAllText(_apiPackagePath, "synthetic-candidate-api-package");
            File.WriteAllText(_activeApiVersionPath, "synthetic-active-api-version");
            File.WriteAllText(_recentWritesPath, "synthetic-recent-user-write");
            File.WriteAllText(ReviewedReportPath, "previous reviewed report");
            File.WriteAllText(ReviewedScriptPath, "previous reviewed SQL");
            File.WriteAllText(SchemaStatePath, "schema-before-publish");
            var dacpacPath = Path.Combine(_directory, "database.dacpac");
            var manifestPath = Path.Combine(_directory, "manifest.json");
            var sqlPackagePath = Path.Combine(_directory, "sqlpackage.ps1");
            var validationSqlPath = Path.Combine(
                FindRepositoryRoot(), "authcraft-db", "postdeploys", "Script.PostDeployment.sql");
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
                if ($args[0] -eq '/Version') { Write-Output '170.4.53.0'; exit 0 }
                $action = @($args | Where-Object { $_ -like '/Action:*' })[0]
                Add-Content -LiteralPath $env:REVIEW_TEST_ACTION_LOG -Value $action
                foreach ($required in @('/p:BlockOnPossibleDataLoss=True', '/p:DropObjectsNotInSource=False',
                    '/p:ScriptDatabaseOptions=False', '/p:IncludeTransactionalScripts=True',
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
                    if ($env:REVIEW_TEST_SCENARIO -eq 'missing-recheck-output' -and $path -like '*recheck-*-script.sql') { exit 0 }
                    $validation = Get-Content -LiteralPath $env:REVIEW_TEST_VALIDATION_SQL -Raw -Encoding UTF8
                    $constraints = Get-Content -LiteralPath (Join-Path (Split-Path $env:REVIEW_TEST_VALIDATION_SQL) 'ValidateConstraints.sql') -Raw
                    $validation = $validation.Replace(':r .\ValidateConstraints.sql', $constraints)
                    $sql = "CREATE TABLE dbo.synthetic_test (id INT);`nGO`n" + $validation + "`nGO`n"
                    if ($env:REVIEW_TEST_SCENARIO -eq 'destructive-plan') { $sql = 'DROP TABLE dbo.existing_data;' }
                    if ($env:REVIEW_TEST_SCENARIO -in @('unvalidated-check', 'evaluate-unvalidated-check')) {
                        $sql = 'ALTER TABLE [dbo].[synthetic_test] WITH NOCHECK ADD CONSTRAINT [CK_test] CHECK (id > 0);'
                    }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'unvalidated-foreign-key') {
                        $sql = 'ALTER TABLE [dbo].[synthetic_test] WITH NOCHECK ADD CONSTRAINT [FK_test] FOREIGN KEY (id) REFERENCES dbo.parent(id);'
                    }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'disabled-constraint') {
                        $sql = 'ALTER TABLE [dbo].[synthetic_test] NOCHECK CONSTRAINT [CK_test];'
                    }
                    if ($env:REVIEW_TEST_SCENARIO -in @('validated-deferred-check', 'validated-dacfx-tail', 'validation-not-final', 'validation-followed-by-write', 'validation-modified', 'uncovered-constraint', 'postdeploy-early-commit', 'postdeploy-no-transaction')) {
                        $sql = "ALTER TABLE [dbo].[invitations] WITH NOCHECK ADD CONSTRAINT [CK_invitations_purpose] CHECK (purpose IS NULL);`nGO`n"
                        if ($env:REVIEW_TEST_SCENARIO -eq 'uncovered-constraint') {
                            $sql = $sql -replace 'CK_invitations_purpose', 'CK_uncovered'
                        }
                        if ($env:REVIEW_TEST_SCENARIO -eq 'validation-modified') {
                            $validation = $validation -replace 'WITH CHECK CHECK', 'WITH NOCHECK CHECK'
                        }
                        if ($env:REVIEW_TEST_SCENARIO -eq 'postdeploy-early-commit') {
                            $validation = $validation.Replace('ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [FK_invitations_accepted_identity];',
                                'COMMIT TRANSACTION; ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [FK_invitations_accepted_identity];')
                        }
                        if ($env:REVIEW_TEST_SCENARIO -eq 'postdeploy-no-transaction') {
                            $validation = $validation.Replace('BEGIN TRANSACTION;', '')
                        }
                        $sql += $validation + "`nGO`n"
                        if ($env:REVIEW_TEST_SCENARIO -eq 'validated-dacfx-tail') {
                            $sql += "PRINT N'Checking existing data';`nGO`nUSE [`$(DatabaseName)];`nGO`n"
                            $sql += "ALTER TABLE [dbo].[invitations] WITH CHECK CHECK CONSTRAINT [CK_invitations_purpose];`nGO`nPRINT N'Complete';`nGO`n"
                        }
                        if ($env:REVIEW_TEST_SCENARIO -eq 'validation-not-final') {
                            $sql += 'ALTER TABLE dbo.invitations NOCHECK CONSTRAINT CK_test;'
                        }
                        if ($env:REVIEW_TEST_SCENARIO -eq 'validation-followed-by-write') {
                            $sql += "INSERT INTO dbo.providers (name) VALUES ('unexpected');"
                        }
                    }
                    if (Test-Path -LiteralPath $env:REVIEW_TEST_DRIFT_PATH) { $sql += ' ALTER TABLE dbo.synthetic_test ADD other_id INT;' }
                    [IO.File]::WriteAllText($path, $sql)
                }
                if ($action -eq '/Action:Publish') {
                    $snapshot = Get-Content -LiteralPath (Join-Path (Split-Path $env:REVIEW_TEST_ACTION_LOG) 'deployment-attempt.json') -Raw | ConvertFrom-Json
                    if ($snapshot.result -cne 'pending' -or $snapshot.transaction_scope -cne 'separate_schema_and_postdeploy') {
                        throw 'Expected a persisted, verified pending attempt before Publish.'
                    }
                }
                if ($action -eq '/Action:Publish' -and $env:REVIEW_TEST_SCENARIO -like 'publish-fails-*') {
                    [IO.File]::WriteAllText($env:REVIEW_TEST_SCHEMA_STATE_PATH, 'schema-partially-applied')
                    Write-Output "SQL error $($env:REVIEW_TEST_SCENARIO.Substring('publish-fails-'.Length))"
                    Write-Error "native diagnostic $env:AUTHCRAFT_SQL_ACCESS_TOKEN"
                    exit 1
                }
                exit 0
                """);
            var publisherPath = Path.Combine(FindRepositoryRoot(), "scripts", "Invoke-DatabaseDeployment.ps1");
            ValidationSqlPath = validationSqlPath;
            AttemptPath = Path.Combine(_directory, "deployment-attempt.json");
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
                    $stagingBaseline = 'unknown'
                    if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-staging-baseline') { $stagingBaseline = '{{new string('b', 40)}}' }
                    $dacpacHash = (Get-FileHash -LiteralPath '{{dacpacPath}}' -Algorithm SHA256).Hash.ToLowerInvariant()
                    $reportFiles = @(Get-ChildItem -LiteralPath '{{_directory}}' -Filter 'staging-publish-*-report.xml')
                    $sqlFiles = @(Get-ChildItem -LiteralPath '{{_directory}}' -Filter 'staging-publish-*-script.sql')
                    if ($reportFiles.Count -ne 1 -or $sqlFiles.Count -ne 1) { throw 'Expected one isolated Publish evaluation.' }
                    $reportHash = (Get-FileHash -LiteralPath $reportFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    $sqlHash = (Get-FileHash -LiteralPath $sqlFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    $scriptDatabaseOptions = 'false'
                    if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-options') { $scriptDatabaseOptions = 'true' }
                    $body = @"
                deployment_review=$decision
                run_id=12345
                commit_sha=$commit
                staging_baseline_sha=$stagingBaseline
                target_environment=staging
                database_server=authcraftdbbrasil.database.windows.net
                database_name=$database
                dacpac_sha256=$dacpacHash
                api_compatibility=accepted
                data_impact=accepted
                recovery_plan=accepted
                block_on_possible_data_loss=true
                drop_objects_not_in_source=false
                script_database_options=$scriptDatabaseOptions
                include_transactional_scripts=true
                postdeploy_transaction=true
                deploy_report_sha256=$reportHash
                deploy_script_sha256=$sqlHash
                "@
                    if ($env:REVIEW_TEST_SCENARIO -eq 'missing-api-compatibility') {
                        $body = $body -replace '(?m)^api_compatibility=.*\r?\n', ''
                    }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'missing-data-impact') {
                        $body = $body -replace '(?m)^data_impact=.*\r?\n', ''
                    }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'missing-recovery-plan') {
                        $body = $body -replace '(?m)^recovery_plan=.*\r?\n', ''
                    }
                    if ($env:REVIEW_TEST_SCENARIO -like 'recovery-*') {
                        $recoveryRunId = '42737737446'
                        if ($env:REVIEW_TEST_SCENARIO -eq 'recovery-wrong-run') { $recoveryRunId = '42740000000' }
                        $body += "`nrecovery_deployment_run_id=$recoveryRunId"
                        if ($env:REVIEW_TEST_SCENARIO -ne 'recovery-missing-live-reconciliation') {
                            $body += "`nlive_state_reconciled=accepted"
                        }
                    }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'missing-options') {
                        $body = $body -replace '(?m)^script_database_options=.*\r?\n', ''
                    }
                    foreach ($option in @('transaction', 'postdeploy')) {
                        $field = if ($option -eq 'transaction') { 'include_transactional_scripts' } else { 'postdeploy_transaction' }
                        if ($env:REVIEW_TEST_SCENARIO -eq "missing-$option-option") {
                            $body = $body -replace "(?m)^$field=.*\r?\n", ''
                        }
                        if ($env:REVIEW_TEST_SCENARIO -eq "false-$option-option") {
                            $body = $body.Replace("$field=true", "$field=false")
                        }
                        if ($env:REVIEW_TEST_SCENARIO -eq "duplicate-$option-option") { $body += "`n$field=true" }
                    }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'duplicate-decision') { $body += "`ndeployment_review=rejected" }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'plan-drift') { Set-Content -LiteralPath $env:REVIEW_TEST_DRIFT_PATH -Value 'changed' }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'dacpac-changed' -or
                        ($env:REVIEW_TEST_SCENARIO -eq 'dacpac-changed-on-recheck' -and $script:commentsRead -gt 1)) {
                        Add-Content -LiteralPath '{{dacpacPath}}' -Value 'changed'
                    }
                    $currentReview = [pscustomobject]@{ id = 42; user = @{ login = $author }; body = $body; created_at = '2026-11-06T15:00:00Z' }
                    if ($env:REVIEW_TEST_SCENARIO -eq 'json-array-comments') {
                        $history = @(
                            [pscustomobject]@{ id = 41; user = @{ login = $author }; body = 'deployment_review=rejected'; created_at = '2026-11-05T15:00:00Z' },
                            $currentReview
                        )
                        Write-Output -NoEnumerate $history
                        return
                    }
                    return @($currentReview)
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
                    StagingBaselineSha = 'unknown'
                    AttemptPath = '{{AttemptPath}}'
                    TransactionVerificationPath = '{{Path.Combine(_directory, "transaction-verification.json")}}'
                }
                if ($env:REVIEW_TEST_SCENARIO -like 'recovery-*') {
                    $parameters.RecoveryDeploymentRunId = '42737737446'
                }
                $proofDirectory = '{{_directory}}'
                $cases = @(
                    foreach ($protected in @($false, $true)) {
                        foreach ($scenario in @('success', '547', '51000')) {
                            $modeName = if ($protected) { 'protected' } else { 'control' }
                            $sqlFile = "$modeName-$scenario.sql"
                            $sql = "SET XACT_ABORT ON;`nBEGIN TRANSACTION;`nCOMMIT TRANSACTION;`n"
                            if ($protected) { $sql += "BEGIN TRANSACTION;`n" }
                            $sql += "INSERT dbo.transaction_seeds (id) VALUES (2);`n"
                            if ($protected) { $sql += "COMMIT TRANSACTION;`n" }
                            Set-Content -LiteralPath (Join-Path $proofDirectory $sqlFile) -Value $sql
                            $trusted = if ($scenario -eq 'success' -or ($scenario -eq '51000' -and -not $protected)) { 1 } else { 0 }
                            $seed = if ($protected -and $scenario -ne 'success') { 0 } else { 1 }
                            @{
                                protected_postdeployment = $protected
                                scenario = $scenario
                                exit_code = $(if ($scenario -eq 'success') { 0 } else { 1 })
                                state = @(1, 1, $trusted, $seed, 1)
                                sql_file = $sqlFile
                                sql_sha256 = (Get-FileHash (Join-Path $proofDirectory $sqlFile) -Algorithm SHA256).Hash.ToLowerInvariant()
                            }
                        }
                    }
                )
                $proof = @{
                    schema_version = 1
                    sqlpackage_version = '170.4.53.0'
                    sqlpackage_sha256 = (Get-FileHash '{{sqlPackagePath}}' -Algorithm SHA256).Hash.ToLowerInvariant()
                    postdeployment_sha256 = (Get-FileHash '{{validationSqlPath}}' -Algorithm SHA256).Hash.ToLowerInvariant()
                    validation_sha256 = (Get-FileHash (Join-Path (Split-Path '{{validationSqlPath}}') 'ValidateConstraints.sql') -Algorithm SHA256).Hash.ToLowerInvariant()
                    fixture_sha256 = (Get-FileHash '{{Path.Combine(FindRepositoryRoot(), "authcraft-api.Tests", "LocalEnvironmentSqlTests.cs")}}' -Algorithm SHA256).Hash.ToLowerInvariant()
                    cases = $cases
                }
                $outcome = 'Passed'
                if ($env:REVIEW_TEST_SCENARIO -eq 'skipped-proof') { $outcome = 'NotExecuted' }
                Set-Content -LiteralPath (Join-Path $proofDirectory 'transaction-tests.trx') -Value "<TestRun><Results><UnitTestResult testName='authcraft_api.Tests.LocalEnvironmentSqlTests.TransactionalPublishDocumentsSeparateSchemaAndPostdeploymentCoverage' outcome='$outcome'/></Results><ResultSummary><Counters total='1' executed='1' passed='1' failed='0'/></ResultSummary></TestRun>"
                if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-tool-proof') { $proof.sqlpackage_sha256 = '{{new string('b', 64)}}' }
                if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-version-proof') { $proof.sqlpackage_version = '170.5.96.0' }
                if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-source-proof') { $proof.postdeployment_sha256 = '{{new string('b', 64)}}' }
                if ($env:REVIEW_TEST_SCENARIO -eq 'missing-case-proof') { $proof.cases = @($cases | Select-Object -First 5) }
                if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-state-proof') { $proof.cases[-1].state = @(1, 1, 1, 1, 1) }
                if ($env:REVIEW_TEST_SCENARIO -eq 'changed-sql-proof') { Add-Content (Join-Path $proofDirectory 'protected-51000.sql') -Value 'changed' }
                if ($env:REVIEW_TEST_SCENARIO -eq 'duplicate-case-proof') { $proof.cases[-1] = $proof.cases[0] }
                if ($env:REVIEW_TEST_SCENARIO -eq 'missing-trx-proof') { Remove-Item -LiteralPath (Join-Path $proofDirectory 'transaction-tests.trx') }
                if ($env:REVIEW_TEST_SCENARIO -eq 'wrong-boundaries-proof') {
                    $sqlPath = Join-Path $proofDirectory 'protected-51000.sql'
                    Set-Content -LiteralPath $sqlPath -Value "SET XACT_ABORT ON;`nBEGIN TRANSACTION;`nCOMMIT TRANSACTION;`nINSERT dbo.transaction_seeds (id) VALUES (2);"
                    $proof.cases[-1].sql_sha256 = (Get-FileHash $sqlPath -Algorithm SHA256).Hash.ToLowerInvariant()
                }
                $proof | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $parameters.TransactionVerificationPath -Encoding UTF8
                if ($env:REVIEW_TEST_SCENARIO -eq 'missing-proof') { Remove-Item -LiteralPath $parameters.TransactionVerificationPath }
                if ($env:REVIEW_TEST_SCENARIO -eq 'invalid-proof') { Set-Content -LiteralPath $parameters.TransactionVerificationPath -Value '{' }
                if ($env:REVIEW_TEST_SCENARIO -eq 'evaluate-unvalidated-check') { $parameters.Mode = 'Evaluate' }
                if ($env:REVIEW_TEST_SCENARIO -eq 'reviewer-override') { $parameters.AllowedReviewer = 'other-user' }
                & '{{publisherPath}}' @parameters
                """);
        }

        public string SyntheticToken { get; } = $"synthetic-{Guid.NewGuid():N}";
        public string[] Actions => File.Exists(_actionLog) ? File.ReadAllLines(_actionLog) : [];
        public string SchemaStatePath { get; }
        public string ReviewedReportPath { get; }
        public string ReviewedScriptPath { get; }
        public string AttemptPath { get; }
        private string ValidationSqlPath { get; }

        public string[] SnapshotProtectedFiles()
        {
            return new[] { _apiPackagePath, _activeApiVersionPath, _recentWritesPath }
                .Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))
                .ToArray();
        }

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
            processInfo.Environment["REVIEW_TEST_SCHEMA_STATE_PATH"] = SchemaStatePath;
            processInfo.Environment["REVIEW_TEST_DRIFT_PATH"] = Path.Combine(_directory, "drift");
            processInfo.Environment["REVIEW_TEST_VALIDATION_SQL"] = ValidationSqlPath;
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
