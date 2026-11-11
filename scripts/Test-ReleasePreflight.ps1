[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$ApiArtifactPath,
    [Parameter(Mandatory = $true)]
    [string]$DatabaseArtifactPath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommitSha,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedRunId,
    [Parameter(Mandatory = $true)]
    [string]$GitHubRef,
    [Parameter(Mandatory = $true)]
    [ValidateSet('staging', 'production')]
    [string]$TargetEnvironment,
    [string]$LastSuccessfulCommitSha,
    [string]$RecoveryDeploymentRunId,
    [switch]$AllowInitialStagingBootstrap,
    [string]$RepositoryPath = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
function Stop-Preflight {
    param([string]$Reason)
    Write-Error $Reason
    exit 1
}

function Get-ActionsMetadata {
    param([string]$Endpoint)

    $output = & gh api "repos/leoperetti/authcraft-api/$Endpoint" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($output -join "`n"))) {
        throw 'Authenticated Actions metadata is unavailable.'
    }
    return ($output -join "`n" | ConvertFrom-Json)
}

$evidenceDirectory = $null
try {
    if ($TargetEnvironment -ne 'staging') {
        Stop-Preflight 'Production publication is blocked until protected approval is available.'
    }
    if ($GitHubRef -cne 'refs/heads/main') {
        Stop-Preflight 'Release publication requires refs/heads/main.'
    }
    $hasRecoveryCheckpoint = -not [string]::IsNullOrWhiteSpace($RecoveryDeploymentRunId)
    $hasKnownBaseline = $LastSuccessfulCommitSha -match '^[A-Fa-f0-9]{40}$'
    if ($ExpectedCommitSha -notmatch '^[A-Fa-f0-9]{40}$' -or
        $ExpectedRunId -notmatch '^[1-9][0-9]*$' -or
        ($hasRecoveryCheckpoint -and
            ($RecoveryDeploymentRunId -notmatch '^[1-9][0-9]*$' -or $hasKnownBaseline -or $AllowInitialStagingBootstrap)) -or
        (-not $hasRecoveryCheckpoint -and -not $hasKnownBaseline -and
            ($LastSuccessfulCommitSha -cne 'unknown' -or -not $AllowInitialStagingBootstrap))) {
        Stop-Preflight 'Commit or run metadata is missing or malformed.'
    }

    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.commit_sha -cne $ExpectedCommitSha.ToLowerInvariant() -or
        [string]$manifest.run_id -cne $ExpectedRunId -or
        $manifest.api.artifact -ne 'api' -or
        $manifest.database.artifact -ne 'dacpac' -or
        $manifest.api.sha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        $manifest.database.sha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        Stop-Preflight 'Manifest is incomplete or does not match the authorized run.'
    }
    if ([IO.Path]::GetExtension($ApiArtifactPath) -ne '.zip' -or
        [IO.Path]::GetExtension($DatabaseArtifactPath) -ne '.dacpac') {
        Stop-Preflight 'Release artifact file types do not match the manifest contract.'
    }

    foreach ($artifact in @(
            @{ Path = $ApiArtifactPath; ExpectedHash = $manifest.api.sha256 },
            @{ Path = $DatabaseArtifactPath; ExpectedHash = $manifest.database.sha256 })) {
        if (-not (Test-Path -LiteralPath $artifact.Path -PathType Leaf)) {
            Stop-Preflight 'A release artifact is missing.'
        }
        $actualHash = (Get-FileHash -LiteralPath $artifact.Path -Algorithm SHA256).Hash
        if (-not [string]::Equals($actualHash, $artifact.ExpectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Preflight 'A release artifact hash does not match the manifest.'
        }
    }

    if ($hasKnownBaseline) {
        if ([string]::Equals(
                $ExpectedCommitSha,
                $LastSuccessfulCommitSha,
                [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Preflight 'Candidate release is not newer than the last successful release.'
        }
    }

    $evidenceDirectory = Join-Path ([IO.Path]::GetTempPath()) "authcraft-preflight-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $evidenceDirectory | Out-Null
    $repositoryRoot = (Resolve-Path -LiteralPath $RepositoryPath).Path
    $checkedOutCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or
        -not [string]::Equals($checkedOutCommit, $ExpectedCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Preflight 'Checked-out source does not match the release commit.'
    }
    $baselineCommitSha = if ($hasKnownBaseline) { $LastSuccessfulCommitSha } else { $null }
    if ($hasRecoveryCheckpoint) {
        $checkpointDirectory = Join-Path $evidenceDirectory 'recovery-checkpoint'
        $checkpointParameters = @{
            DeploymentRunId = $RecoveryDeploymentRunId
            OutputDirectory = $checkpointDirectory
        }
        if ($env:GITHUB_RUN_ID -match '^[1-9][0-9]*$') {
            $checkpointParameters.CurrentDeploymentRunId = $env:GITHUB_RUN_ID
        }
        $checkpointOutput = & (Join-Path $PSScriptRoot 'Get-StagingRecoveryCheckpoint.ps1') @checkpointParameters
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($checkpointOutput -join "`n"))) {
            Stop-Preflight 'Authenticated partial Staging recovery evidence is unavailable or invalid.'
        }
        try {
            $recoveryCheckpoint = ($checkpointOutput -join "`n" | ConvertFrom-Json -ErrorAction Stop)
        } catch {
            Stop-Preflight 'Authenticated partial Staging recovery evidence is malformed.'
        }
        if ($recoveryCheckpoint.baseline_state -cne 'partial_recovery' -or
            $recoveryCheckpoint.deployment_run_id -cne $RecoveryDeploymentRunId -or
            $recoveryCheckpoint.live_state -cne 'not_reconciled' -or
            $recoveryCheckpoint.baseline_eligible -ne $false -or
            $recoveryCheckpoint.deployment_authorized -ne $false -or
            $recoveryCheckpoint.promotion_authorized -ne $false -or
            $recoveryCheckpoint.baseline_commit_sha -notmatch '^[a-f0-9]{40}$') {
            Stop-Preflight 'Recovery checkpoint does not represent a non-authorizing partial deployment.'
        }
        $baselineCommitSha = $recoveryCheckpoint.baseline_commit_sha
    } elseif ($hasKnownBaseline) {
        $baselineCommitSha = $LastSuccessfulCommitSha
    }
    if ($baselineCommitSha) {
        if ([string]::Equals(
                $ExpectedCommitSha,
                $baselineCommitSha,
                [StringComparison]::OrdinalIgnoreCase)) {
            Stop-Preflight 'Candidate release is not newer than the recorded Staging state.'
        }
        & git -C $repositoryRoot merge-base --is-ancestor $baselineCommitSha $ExpectedCommitSha *> $null
        if ($LASTEXITCODE -ne 0) {
            Stop-Preflight 'Candidate release is older than or diverged from the recorded Staging state.'
        }
    }

    $run = Get-ActionsMetadata "actions/runs/$ExpectedRunId"
    if ([string]$run.id -cne $ExpectedRunId -or
        $run.repository.full_name -cne 'leoperetti/authcraft-api' -or
        $run.head_repository.full_name -cne 'leoperetti/authcraft-api' -or
        $run.head_sha -cne $ExpectedCommitSha.ToLowerInvariant() -or
        $run.head_branch -cne 'main' -or
        $run.event -notin @('push', 'workflow_dispatch') -or
        $run.path -cne '.github/workflows/main_authcraft-api-staging.yml') {
        Stop-Preflight 'Actions candidate does not match an authorized main build from this repository.'
    }
    $jobs = Get-ActionsMetadata "actions/runs/$ExpectedRunId/jobs?per_page=100"
    $buildJobName = 'validate'
    $buildJobs = @($jobs.jobs | Where-Object { $_.name -ceq $buildJobName })
    if ($jobs.total_count -ne @($jobs.jobs).Count -or
        $buildJobs.Count -ne 1 -or $buildJobs[0].status -cne 'completed' -or $buildJobs[0].conclusion -cne 'success') {
        Stop-Preflight 'Actions build completion could not be verified.'
    }
    $publisherVerificationJobs = @($jobs.jobs | Where-Object { $_.name -ceq 'verify-staging-publisher' })
    if ($publisherVerificationJobs.Count -ne 1 -or
        $publisherVerificationJobs[0].status -cne 'completed' -or
        $publisherVerificationJobs[0].conclusion -cne 'success') {
        Stop-Preflight 'Staging OIDC publisher verification did not complete successfully in the release run.'
    }

    $artifactMetadata = Get-ActionsMetadata "actions/runs/$ExpectedRunId/artifacts?per_page=100"
    if ($artifactMetadata.total_count -ne @($artifactMetadata.artifacts).Count) {
        Stop-Preflight 'Actions artifact metadata is truncated; provenance cannot be verified.'
    }
    foreach ($artifact in @(
            @{ Name = 'api'; LocalPath = $ApiArtifactPath },
            @{ Name = 'dacpac'; LocalPath = $DatabaseArtifactPath },
            @{ Name = 'release-manifest'; LocalPath = $ManifestPath })) {
        $matches = @($artifactMetadata.artifacts | Where-Object { $_.name -ceq $artifact.Name })
        if ($matches.Count -ne 1 -or $matches[0].expired -ne $false -or
            [DateTimeOffset]::Parse($matches[0].expires_at) -le [DateTimeOffset]::UtcNow -or
            [string]$matches[0].workflow_run.id -cne $ExpectedRunId -or
            $matches[0].workflow_run.head_sha -cne $ExpectedCommitSha.ToLowerInvariant()) {
            Stop-Preflight 'Actions artifact is missing, duplicated, expired, or belongs to another release.'
        }
        $downloadDirectory = Join-Path $evidenceDirectory $artifact.Name
        & gh run download $ExpectedRunId --repo leoperetti/authcraft-api --name $artifact.Name --dir $downloadDirectory *> $null
        if ($LASTEXITCODE -ne 0) {
            Stop-Preflight 'Actions artifact download failed; no deployment is authorized.'
        }
        $files = @(Get-ChildItem -LiteralPath $downloadDirectory -File -Recurse)
        if ($files.Count -ne 1 -or
            (Get-FileHash -LiteralPath $files[0].FullName -Algorithm SHA256).Hash -cne
            (Get-FileHash -LiteralPath $artifact.LocalPath -Algorithm SHA256).Hash) {
            Stop-Preflight 'Local release file does not match the artifact retained by Actions.'
        }
    }

    $lastStagingRun = $null
    if (-not $hasRecoveryCheckpoint) {
        $history = Get-ActionsMetadata 'actions/workflows/main_authcraft-api-staging.yml/runs?branch=main&status=success&per_page=100'
        if ($history.total_count -ne @($history.workflow_runs).Count) {
            Stop-Preflight 'Actions staging history is truncated; the initial baseline cannot be verified.'
        }
        $lastStagingRunCreatedAt = [DateTimeOffset]::MinValue
        foreach ($previousRun in $history.workflow_runs) {
            if ($previousRun.repository.full_name -cne 'leoperetti/authcraft-api' -or
                $previousRun.head_repository.full_name -cne 'leoperetti/authcraft-api' -or
                $previousRun.path -cne '.github/workflows/main_authcraft-api-staging.yml' -or
                $previousRun.head_branch -cne 'main' -or
                $previousRun.event -notin @('push', 'workflow_dispatch') -or
                $previousRun.status -cne 'completed' -or $previousRun.conclusion -cne 'success' -or
                [string]$previousRun.id -notmatch '^[1-9][0-9]*$') {
                Stop-Preflight 'Actions release history contains unverifiable metadata.'
            }
            $previousJobs = Get-ActionsMetadata "actions/runs/$($previousRun.id)/jobs?per_page=100"
            if ($previousJobs.total_count -ne @($previousJobs.jobs).Count) {
                Stop-Preflight 'Actions job history is truncated; successful staging deployment cannot be verified.'
            }
            $stagingJobs = @($previousJobs.jobs | Where-Object {
                $_.name -ceq 'deploy-staging' -and $_.status -ceq 'completed' -and $_.conclusion -ceq 'success'
            })
            if ($stagingJobs.Count -eq 1) {
                $previousRunCreatedAt = [DateTimeOffset]::MinValue
                if (-not [DateTimeOffset]::TryParse(
                        [string]$previousRun.created_at,
                        [Globalization.CultureInfo]::InvariantCulture,
                        [Globalization.DateTimeStyles]::AssumeUniversal,
                        [ref]$previousRunCreatedAt)) {
                    Stop-Preflight 'Actions staging deployment timestamp is missing or malformed.'
                }
                if ($null -eq $lastStagingRun -or
                    $previousRunCreatedAt -gt $lastStagingRunCreatedAt -or
                    ($previousRunCreatedAt -eq $lastStagingRunCreatedAt -and
                        [long]$previousRun.id -gt [long]$lastStagingRun.id)) {
                    $lastStagingRun = $previousRun
                    $lastStagingRunCreatedAt = $previousRunCreatedAt
                }
            }
            if ($stagingJobs.Count -gt 1) {
                Stop-Preflight 'Actions history contains ambiguous successful staging deployment jobs.'
            }
        }
        if ($hasKnownBaseline) {
            if ($null -eq $lastStagingRun -or
                $lastStagingRun.head_sha -cne $LastSuccessfulCommitSha.ToLowerInvariant()) {
                Stop-Preflight 'Last successful staging release is missing or does not match authenticated Actions history.'
            }
        } elseif ($null -ne $lastStagingRun) {
            Stop-Preflight 'Initial Staging bootstrap is allowed only when no successful deployment is recorded.'
        } else {
            $deploymentHistory = Get-ActionsMetadata 'actions/workflows/deploy_authcraft-api-staging.yml/runs?branch=main&per_page=100'
            if ($deploymentHistory.total_count -ne @($deploymentHistory.workflow_runs).Count) {
                Stop-Preflight 'Staging deployment history is truncated; initial bootstrap is not safe.'
            }
            if (@($deploymentHistory.workflow_runs).Count -gt 0) {
                Stop-Preflight 'A Staging deployment attempt exists; initial bootstrap cannot replace its recovery checkpoint.'
            }
        }
    }

    $stagingPreflightOutput = & (Join-Path $PSScriptRoot 'Test-StagingPreflightMetadata.ps1') 2>$null
    $stagingPreflightExitCode = $LASTEXITCODE
    if ($stagingPreflightExitCode -ne 0 -or
        [string]::IsNullOrWhiteSpace(($stagingPreflightOutput -join "`n"))) {
        Stop-Preflight 'Live Staging resource, configuration, OIDC scope, or Production-disablement checks failed.'
    }
    try {
        $stagingPreflight = ($stagingPreflightOutput -join "`n" | ConvertFrom-Json -ErrorAction Stop)
    } catch {
        Stop-Preflight 'Live Staging preflight metadata is malformed.'
    }
    if ($stagingPreflight.verification_state -cne 'confirmed' -or
        $stagingPreflight.target_environment -cne 'staging' -or
        $stagingPreflight.application -cne 'authcraft-api-staging' -or
        $stagingPreflight.database -cne 'authcraft-db-staging' -or
        $stagingPreflight.vault_name -cne 'authcraft-vault-staging' -or
        $stagingPreflight.publisher_roles_verified -ne $true -or
        $stagingPreflight.production_workflow_disabled -ne $true) {
        Stop-Preflight 'Live Staging preflight does not confirm the expected isolated destination and publisher.'
    }

    $baselineState = 'verified'
    $previousRunId = [string]$lastStagingRun.id
    $preflightState = 'ready_for_review'
    if ($hasRecoveryCheckpoint) {
        $baselineState = 'partial_recovery'
        $previousRunId = $RecoveryDeploymentRunId
        $preflightState = 'recovery_review_required'
    } elseif (-not $hasKnownBaseline) {
        $baselineState = 'unknown'
        $previousRunId = $null
    }
    [pscustomobject]@{
        state = $preflightState
        artifact_validation = 'verified'
        deployment_authorized = $false
        blocking_reason = if ($hasRecoveryCheckpoint) {
            'The partial deployment is evidence only; live state reconciliation, exact SQL review, and separate authorization are still required.'
        } else {
            'Authenticated SQL review and temporary publish permission verification are still required before Publish.'
        }
        commit_sha = $ExpectedCommitSha.ToLowerInvariant()
        run_id = $ExpectedRunId
        baseline_state = $baselineState
        baseline_commit_sha = if ($hasRecoveryCheckpoint) {
            $recoveryCheckpoint.baseline_commit_sha
        } elseif ($hasKnownBaseline) {
            $LastSuccessfulCommitSha.ToLowerInvariant()
        } else {
            'unknown'
        }
        bootstrap_review_required = (-not $hasKnownBaseline -and -not $hasRecoveryCheckpoint)
        previous_run_id = $previousRunId
        recovery_checkpoint = if ($hasRecoveryCheckpoint) { $recoveryCheckpoint } else { $null }
        live_state_reconciliation_required = $hasRecoveryCheckpoint
        target = 'staging'
        database_server = 'authcraftdbbrasil.database.windows.net'
        database_name = 'authcraft-db-staging'
        staging_preflight = $stagingPreflight
    } | ConvertTo-Json -Compress
}
catch {
    if ($_.Exception.Message -notmatch '^(Production publication|Release publication|Commit or run|Manifest|A release|Candidate release)') {
        Write-Error "Release preflight failed. No deployment command was run. $($_.Exception.Message)"
        exit 1
    }
    throw
}
finally {
    if ($evidenceDirectory -and (Test-Path -LiteralPath $evidenceDirectory)) {
        Remove-Item -LiteralPath $evidenceDirectory -Recurse -Force
    }
}
