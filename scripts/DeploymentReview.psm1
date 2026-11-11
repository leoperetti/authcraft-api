function Test-DeploymentReviewRecord {
    param(
        [string]$Body,
        [string]$Author,
        [string]$AllowedReviewer,
        [string]$RunId,
        [string]$CommitSha,
        [string]$TargetEnvironment,
        [string]$DatabaseServer,
        [string]$DatabaseName,
        [string]$DacpacSha256,
        [string]$ReportSha256,
        [string]$ScriptSha256,
        [string]$StagingBaselineSha,
        [string]$RecoveryDeploymentRunId
    )

    if ($Author -cne $AllowedReviewer) {
        return $false
    }
    if ($StagingBaselineSha -cne 'unknown' -and $StagingBaselineSha -notmatch '^[A-Fa-f0-9]{40}$') {
        return $false
    }

    $expectedFields = [ordered]@{
        deployment_review = 'approved'
        run_id = $RunId
        commit_sha = $CommitSha.ToLowerInvariant()
        staging_baseline_sha = $StagingBaselineSha.ToLowerInvariant()
        target_environment = $TargetEnvironment
        database_server = $DatabaseServer
        database_name = $DatabaseName
        dacpac_sha256 = $DacpacSha256.ToLowerInvariant()
        api_compatibility = 'accepted'
        data_impact = 'accepted'
        recovery_plan = 'accepted'
        block_on_possible_data_loss = 'true'
        drop_objects_not_in_source = 'false'
        script_database_options = 'false'
        include_transactional_scripts = 'true'
        postdeploy_transaction = 'true'
        deploy_report_sha256 = $ReportSha256.ToLowerInvariant()
        deploy_script_sha256 = $ScriptSha256.ToLowerInvariant()
    }
    if (-not [string]::IsNullOrWhiteSpace($RecoveryDeploymentRunId)) {
        if ($RecoveryDeploymentRunId -notmatch '^[1-9][0-9]*$') {
            return $false
        }
        $expectedFields.recovery_deployment_run_id = $RecoveryDeploymentRunId
        $expectedFields.live_state_reconciled = 'accepted'
    }

    foreach ($field in $expectedFields.GetEnumerator()) {
        $pattern = '(?im)^[ \t]*' + [Regex]::Escape($field.Key) + '[ \t]*=[ \t]*([^\r\n]*)\r?$'
        $entries = [Regex]::Matches($Body, $pattern)
        if ($entries.Count -ne 1 -or
            $entries[0].Groups[1].Value.Trim() -cne [string]$field.Value) {
            return $false
        }
    }

    return $true
}

function Test-LocalDeploymentScriptSafety {
    param([string]$ScriptContent)

    $destructiveStatement = '(?im)\bDROP\s+(?:DATABASE|TABLE(?!\s+\[?#tmpErrors\]?[ \t]*(?:;|\r?$))|SCHEMA|COLUMN)\b|\bDELETE\s+FROM\b|\bTRUNCATE\s+TABLE\b'
    return -not [Regex]::IsMatch($ScriptContent, $destructiveStatement)
}

function Get-DeploymentTransactionVerification {
    param(
        [string]$Path,
        [string]$SqlPackagePath
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw 'Executed transaction verification evidence is required before Publish.'
    }
    $proofDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $Path).Path
    $trxPath = Join-Path $proofDirectory 'transaction-tests.trx'
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf) -or
        (Get-Item -LiteralPath $Path).Length -gt 65536 -or
        (Get-Item -LiteralPath $trxPath).Length -gt 1048576) {
        throw 'Transaction verification evidence is missing or exceeds its size limit.'
    }
    try {
        $proof = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        [xml]$testRun = Get-Content -LiteralPath $trxPath -Raw -Encoding UTF8
    } catch {
        throw 'Transaction verification evidence could not be parsed.'
    }
    $counters = $testRun.SelectSingleNode('//*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
    $results = @($testRun.SelectNodes('//*[local-name()="UnitTestResult"]'))
    if ($null -eq $counters -or $counters.total -cne '1' -or
        $counters.executed -cne '1' -or $counters.passed -cne '1' -or $counters.failed -cne '0' -or
        $results.Count -ne 1 -or $results[0].outcome -cne 'Passed' -or
        $results[0].testName -cne 'authcraft_api.Tests.LocalEnvironmentSqlTests.TransactionalPublishDocumentsSeparateSchemaAndPostdeploymentCoverage' -or
        $proof.schema_version -ne 1 -or $proof.sqlpackage_version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or
        @($proof.cases).Count -ne 6) {
        throw 'Transaction verification must contain the exact executed, successful phase test and all six cases.'
    }
    $repositoryRoot = Split-Path -Parent $PSScriptRoot
    $boundFiles = @{
        sqlpackage_sha256 = $SqlPackagePath
        postdeployment_sha256 = Join-Path $repositoryRoot 'authcraft-db\postdeploys\Script.PostDeployment.sql'
        validation_sha256 = Join-Path $repositoryRoot 'authcraft-db\postdeploys\ValidateConstraints.sql'
        fixture_sha256 = Join-Path $repositoryRoot 'authcraft-api.Tests\LocalEnvironmentSqlTests.cs'
    }
    foreach ($field in $boundFiles.Keys) {
        if ($proof.$field -cne (Get-FileHash -LiteralPath $boundFiles[$field] -Algorithm SHA256).Hash.ToLowerInvariant()) {
            throw 'Transaction verification does not match the current SqlPackage, postdeployment, or test source.'
        }
    }
    try {
        $versionOutput = if ([IO.Path]::GetExtension($SqlPackagePath) -ieq '.ps1') {
            & powershell.exe -NoProfile -NonInteractive -File $SqlPackagePath /Version 2>&1
        } else {
            & $SqlPackagePath /Version 2>&1
        }
        $versionExitCode = $LASTEXITCODE
    } catch {
        throw 'SqlPackage version could not be verified against transaction evidence.'
    }
    if ($versionExitCode -ne 0 -or ($versionOutput -join "`n").Trim() -cne $proof.sqlpackage_version) {
        throw 'SqlPackage version differs from the executed transaction evidence.'
    }
    $caseReferences = @(
        foreach ($protected in @($false, $true)) {
            foreach ($scenario in @('success', '547', '51000')) {
                $matchingCases = @($proof.cases | Where-Object {
                    $_.protected_postdeployment -is [bool] -and
                    $_.protected_postdeployment -eq $protected -and $_.scenario -ceq $scenario
                })
                if ($matchingCases.Count -ne 1) { throw 'Transaction verification cases are missing or duplicated.' }
                $case = $matchingCases[0]
                $trusted = if ($scenario -eq 'success' -or (-not $protected -and $scenario -eq '51000')) { 1 } else { 0 }
                $seed = if ($protected -and $scenario -ne 'success') { 0 } else { 1 }
                $expectedExit = if ($scenario -eq 'success') { 0 } else { 1 }
                $prefix = if ($protected) { 'protected' } else { 'control' }
                $sqlFile = "$prefix-$scenario.sql"
                $sqlPath = Join-Path $proofDirectory $sqlFile
                if ($case.exit_code -ne $expectedExit -or
                    ($case.state -join ',') -cne "1,1,$trusted,$seed,1" -or
                    $case.sql_file -cne $sqlFile -or -not (Test-Path -LiteralPath $sqlPath -PathType Leaf)) {
                    throw 'Transaction verification does not demonstrate the required phased state.'
                }
                if ((Get-Item -LiteralPath $sqlPath).Length -gt 1048576 -or
                    $case.sql_sha256 -cne (Get-FileHash -LiteralPath $sqlPath -Algorithm SHA256).Hash.ToLowerInvariant()) {
                    throw 'Transaction verification SQL has changed or exceeds its size limit.'
                }
                $sql = (Get-Content -LiteralPath $sqlPath -Raw -Encoding UTF8).Replace("`r`n", "`n")
                # DacFx also emits indented conditional restarts after error handling.
                $begins = @([Regex]::Matches($sql, '(?im)^BEGIN TRANSACTION;?[ \t]*$'))
                $commits = @([Regex]::Matches($sql, '(?im)^COMMIT TRANSACTION;?[ \t]*$'))
                $seedIndex = $sql.IndexOf('INSERT dbo.transaction_seeds', [StringComparison]::Ordinal)
                $expectedCount = if ($protected) { 2 } else { 1 }
                if ($begins.Count -ne $expectedCount -or $commits.Count -ne $expectedCount -or
                    $begins[0].Index -ge $commits[0].Index -or $commits[0].Index -ge $seedIndex -or
                    $sql -notmatch '(?im)^SET XACT_ABORT ON;?[ \t]*$') {
                    throw 'Transaction verification SQL has unrecognized schema transaction boundaries.'
                }
                if ($protected -and ($begins[1].Index -le $commits[0].Index -or
                    $begins[1].Index -ge $seedIndex -or $commits[1].Index -le $seedIndex)) {
                    throw 'Transaction verification SQL has unrecognized postdeployment transaction boundaries.'
                }
                [pscustomobject]@{
                    scenario = $scenario
                    protected_postdeployment = $protected
                    sql_file = $sqlFile
                    sql_sha256 = $case.sql_sha256
                    begin_lines = @($begins | ForEach-Object { 1 + ($sql.Substring(0, $_.Index).ToCharArray() | Where-Object { $_ -eq "`n" }).Count })
                    commit_lines = @($commits | ForEach-Object { 1 + ($sql.Substring(0, $_.Index).ToCharArray() | Where-Object { $_ -eq "`n" }).Count })
                }
            }
        }
    )
    [pscustomobject]@{
        proof_sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        test_report_sha256 = (Get-FileHash -LiteralPath $trxPath -Algorithm SHA256).Hash.ToLowerInvariant()
        sqlpackage_version = $proof.sqlpackage_version
        sqlpackage_sha256 = $proof.sqlpackage_sha256
        postdeployment_sha256 = $proof.postdeployment_sha256
        validation_sha256 = $proof.validation_sha256
        cases = $caseReferences
    }
}

Export-ModuleMember -Function Test-DeploymentReviewRecord, Test-LocalDeploymentScriptSafety, Get-DeploymentTransactionVerification
