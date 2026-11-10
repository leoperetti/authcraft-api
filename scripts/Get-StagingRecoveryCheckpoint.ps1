[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[1-9][0-9]*$')]
    [string]$DeploymentRunId,
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repository = 'leoperetti/authcraft-api'
$deploymentWorkflowPath = '.github/workflows/deploy_authcraft-api-staging.yml'
$releaseWorkflowPath = '.github/workflows/main_authcraft-api-staging.yml'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "authcraft-recovery-$([Guid]::NewGuid().ToString('N'))"

function Get-ActionsMetadata {
    param([string]$Endpoint)

    $output = & gh api "repos/$repository/$Endpoint" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($output -join "`n"))) {
        throw 'Authenticated Staging recovery metadata is unavailable.'
    }
    try {
        return ($output -join "`n" | ConvertFrom-Json -ErrorAction Stop)
    } catch {
        throw 'Authenticated Staging recovery metadata is malformed.'
    }
}

function Assert-Artifact {
    param(
        [object]$ArtifactMetadata,
        [string]$ArtifactName,
        [string]$RunId,
        [string]$CommitSha
    )

    $matchingArtifacts = @($ArtifactMetadata.artifacts | Where-Object { $_.name -ceq $ArtifactName })
    if ($matchingArtifacts.Count -ne 1 -or
        $matchingArtifacts[0].expired -ne $false -or
        [DateTimeOffset]::Parse($matchingArtifacts[0].expires_at) -le [DateTimeOffset]::UtcNow -or
        [string]$matchingArtifacts[0].workflow_run.id -cne $RunId -or
        $matchingArtifacts[0].workflow_run.head_sha -cne $CommitSha) {
        throw "Staging recovery artifact $ArtifactName is missing, duplicated, expired, or mismatched."
    }
}

function Assert-RequiredJob {
    param(
        [object[]]$Jobs,
        [string]$Name,
        [string]$Conclusion
    )

    $matchingJobs = @($Jobs | Where-Object { $_.name -ceq $Name })
    if ($matchingJobs.Count -ne 1 -or
        $matchingJobs[0].status -cne 'completed' -or
        $matchingJobs[0].conclusion -cne $Conclusion) {
        throw "Staging recovery job $Name did not complete with the required result."
    }
    return $matchingJobs[0]
}

function Assert-RequiredStep {
    param(
        [object]$Job,
        [string]$Name,
        [string]$Conclusion
    )

    $matchingSteps = @($Job.steps | Where-Object { $_.name -ceq $Name })
    if ($matchingSteps.Count -ne 1 -or $matchingSteps[0].conclusion -cne $Conclusion) {
        throw 'The recorded partial Staging deployment steps do not match the recovery checkpoint.'
    }
}

function Get-UniqueEvidenceFile {
    param(
        [string]$Root,
        [string]$Name,
        [switch]$Optional
    )

    $matches = @(Get-ChildItem -LiteralPath $Root -File -Recurse | Where-Object { $_.Name -ceq $Name })
    if ($Optional -and $matches.Count -eq 0) { return $null }
    if ($matches.Count -ne 1) { throw "Staging recovery evidence $Name is missing or ambiguous." }
    return $matches[0].FullName
}

function Download-RunArtifact {
    param(
        [string]$RunId,
        [string]$ArtifactName,
        [string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    & gh run download $RunId --repo $repository --name $ArtifactName --dir $Destination *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Staging recovery artifact $ArtifactName could not be downloaded."
    }
}

try {
    $deploymentRun = Get-ActionsMetadata "actions/runs/$DeploymentRunId"
    if ([string]$deploymentRun.id -cne $DeploymentRunId -or
        $deploymentRun.repository.full_name -cne $repository -or
        $deploymentRun.head_repository.full_name -cne $repository -or
        $deploymentRun.path -cne $deploymentWorkflowPath -or
        $deploymentRun.event -cne 'workflow_dispatch' -or
        $deploymentRun.head_branch -cne 'main' -or
        $deploymentRun.status -cne 'completed' -or
        $deploymentRun.conclusion -cne 'failure' -or
        $deploymentRun.head_sha -notmatch '^[a-f0-9]{40}$') {
        throw 'The selected run is not an authenticated failed Staging deployment from main.'
    }

    $deploymentJobs = Get-ActionsMetadata "actions/runs/$DeploymentRunId/jobs?per_page=100"
    if ($deploymentJobs.total_count -ne @($deploymentJobs.jobs).Count) {
        throw 'Staging recovery deployment job history is incomplete.'
    }
    [void](Assert-RequiredJob $deploymentJobs.jobs 'resolve-release' 'success')
    $deploymentJob = Assert-RequiredJob $deploymentJobs.jobs 'deploy-reviewed-release' 'failure'
    Assert-RequiredStep $deploymentJob 'Publish the reviewed Staging database plan' 'success'
    Assert-RequiredStep $deploymentJob 'Revoke and independently verify the Staging publisher role' 'success'
    Assert-RequiredStep $deploymentJob 'Deploy the same API package to Staging' 'success'
    Assert-RequiredStep $deploymentJob 'Verify Staging health, synthetic login, and tenant roles' 'failure'

    $deploymentArtifacts = Get-ActionsMetadata "actions/runs/$DeploymentRunId/artifacts?per_page=100"
    if ($deploymentArtifacts.total_count -ne @($deploymentArtifacts.artifacts).Count) {
        throw 'Staging recovery deployment artifact inventory is incomplete.'
    }
    Assert-Artifact $deploymentArtifacts 'staging-deployment-attempt' $DeploymentRunId $deploymentRun.head_sha

    $recoveryHistory = Get-ActionsMetadata 'actions/workflows/deploy_authcraft-api-staging.yml/runs?branch=main&per_page=100'
    if ($recoveryHistory.total_count -ne @($recoveryHistory.workflow_runs).Count) {
        throw 'Staging deployment history is incomplete; the recovery checkpoint may be stale.'
    }
    $deploymentCreatedAt = [DateTimeOffset]::Parse($deploymentRun.created_at)
    foreach ($historyRun in $recoveryHistory.workflow_runs) {
        if ($historyRun.repository.full_name -cne $repository -or
            $historyRun.head_repository.full_name -cne $repository -or
            $historyRun.path -cne $deploymentWorkflowPath -or
            $historyRun.head_branch -cne 'main' -or
            [string]$historyRun.id -notmatch '^[1-9][0-9]*$') {
            throw "Staging deployment history contains unverifiable metadata for run $($historyRun.id)."
        }
        if ([string]$historyRun.id -cne $DeploymentRunId -and
            [DateTimeOffset]::Parse($historyRun.created_at) -gt $deploymentCreatedAt) {
            throw 'A newer Staging deployment attempt exists; the recovery checkpoint is stale.'
        }
    }
    if (@($recoveryHistory.workflow_runs | Where-Object { [string]$_.id -ceq $DeploymentRunId }).Count -ne 1) {
        throw 'The selected deployment run is not present in the authenticated Staging history.'
    }

    $releaseRunId = $null
    New-Item -ItemType Directory -Path $temporaryDirectory | Out-Null
    & gh run download $DeploymentRunId --repo $repository --name 'staging-deployment-attempt' --dir $temporaryDirectory *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Staging recovery deployment evidence could not be downloaded.' }

    $attemptPath = Get-UniqueEvidenceFile $temporaryDirectory 'deployment-attempt.json'
    $candidatePath = Get-UniqueEvidenceFile $temporaryDirectory 'candidate-release.json'
    $sqlAttemptPath = Get-UniqueEvidenceFile $temporaryDirectory 'sql-attempt.json'
    $attempt = Get-Content -LiteralPath $attemptPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    $candidate = Get-Content -LiteralPath $candidatePath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    $sqlAttempt = Get-Content -LiteralPath $sqlAttemptPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop

    $releaseRunId = [string]$attempt.release_run_id
    if ($releaseRunId -notmatch '^[1-9][0-9]*$') {
        throw 'The failed deployment does not identify a valid original Staging release.'
    }
    $releaseRun = Get-ActionsMetadata "actions/runs/$releaseRunId"
    if ([string]$releaseRun.id -cne $releaseRunId -or
        $releaseRun.repository.full_name -cne $repository -or
        $releaseRun.head_repository.full_name -cne $repository -or
        $releaseRun.path -cne $releaseWorkflowPath -or
        $releaseRun.event -cne 'workflow_dispatch' -or
        $releaseRun.head_branch -cne 'main' -or
        $releaseRun.status -cne 'completed' -or
        $releaseRun.conclusion -cne 'success' -or
        $releaseRun.head_sha -cne $attempt.commit_sha) {
        throw 'The original Staging release provenance is not a successful matching main run.'
    }

    $releaseJobs = Get-ActionsMetadata "actions/runs/$releaseRunId/jobs?per_page=100"
    if ($releaseJobs.total_count -ne @($releaseJobs.jobs).Count) {
        throw 'The original Staging release job history is incomplete.'
    }
    foreach ($requiredJobName in @('validate', 'verify-staging-publisher', 'preflight-staging')) {
        [void](Assert-RequiredJob $releaseJobs.jobs $requiredJobName 'success')
    }
    $releaseArtifacts = Get-ActionsMetadata "actions/runs/$releaseRunId/artifacts?per_page=100"
    if ($releaseArtifacts.total_count -ne @($releaseArtifacts.artifacts).Count) {
        throw 'The original Staging release artifact inventory is incomplete.'
    }
    foreach ($requiredArtifactName in @('api', 'dacpac', 'release-manifest', 'candidate-release', 'staging-schema-review')) {
        Assert-Artifact $releaseArtifacts $requiredArtifactName $releaseRunId $releaseRun.head_sha
    }
    $releaseEvidenceDirectory = Join-Path $temporaryDirectory 'original-release'
    $releaseManifestDirectory = Join-Path $releaseEvidenceDirectory 'manifest'
    $releaseCandidateDirectory = Join-Path $releaseEvidenceDirectory 'candidate'
    Download-RunArtifact $releaseRunId 'release-manifest' $releaseManifestDirectory
    Download-RunArtifact $releaseRunId 'candidate-release' $releaseCandidateDirectory
    $manifestPath = Get-UniqueEvidenceFile $releaseManifestDirectory 'release-manifest.json'
    $builtCandidatePath = Get-UniqueEvidenceFile $releaseCandidateDirectory 'candidate-release.json'
    $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    $builtCandidate = Get-Content -LiteralPath $builtCandidatePath -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
    $releaseArtifactsMatch = $manifest.commit_sha -ceq $releaseRun.head_sha -and
        [string]$manifest.run_id -ceq $releaseRunId -and
        $manifest.api.artifact -ceq 'api' -and
        $manifest.database.artifact -ceq 'dacpac' -and
        $manifest.api.sha256 -cmatch '^[a-f0-9]{64}$' -and
        $manifest.database.sha256 -cmatch '^[a-f0-9]{64}$' -and
        $builtCandidate.commit_sha -ceq $releaseRun.head_sha -and
        [string]$builtCandidate.run_id -ceq $releaseRunId -and
        $builtCandidate.api_artifact_name -ceq 'api' -and
        $builtCandidate.api_sha256 -ceq $manifest.api.sha256 -and
        $builtCandidate.dacpac_artifact_name -ceq 'dacpac' -and
        $builtCandidate.dacpac_sha256 -ceq $manifest.database.sha256 -and
        $builtCandidate.state -ceq 'built' -and
        $builtCandidate.staging_validation_state -ceq 'not_validated' -and
        $null -eq $builtCandidate.staging_validation_reference -and
        $builtCandidate.promotion_authorized -eq $false
    if (-not $releaseArtifactsMatch) {
        throw 'The original Staging release manifest and candidate record do not match.'
    }

    $failedStageEvidenceValid = [string]$attempt.deployment_run_id -ceq $DeploymentRunId -and
        $attempt.commit_sha -ceq $releaseRun.head_sha -and
        $attempt.target_environment -ceq 'staging' -and
        $attempt.database_server -ceq 'authcraftdbbrasil.database.windows.net' -and
        $attempt.database_name -ceq 'authcraft-db-staging' -and
        $attempt.result -ceq 'failed' -and
        $attempt.failure_stage -ceq 'smoke' -and
        $attempt.schema_result -ceq 'succeeded' -and
        $attempt.application_result -ceq 'succeeded' -and
        $attempt.permission_grant_result -ceq 'succeeded' -and
        $attempt.permission_revoke_result -ceq 'succeeded' -and
        $attempt.smoke_result -ceq 'failed' -and
        $attempt.include_transactional_scripts -eq $true -and
        $attempt.postdeploy_transaction -eq $true -and
        $attempt.transaction_scope -ceq 'separate_schema_and_postdeploy' -and
        $attempt.postdeploy_result -ceq 'unknown' -and
        $attempt.failure_state_verification -in @('pending', 'unknown') -and
        $attempt.dacpac_sha256 -cmatch '^[a-f0-9]{64}$' -and
        $attempt.deploy_report_sha256 -cmatch '^[a-f0-9]{64}$' -and
        $attempt.deploy_script_sha256 -cmatch '^[a-f0-9]{64}$'
    if (-not $failedStageEvidenceValid) {
        throw 'The deployment attempt does not prove the required partial Staging failure state.'
    }

    $candidateMatchesAttempt = [string]$candidate.run_id -ceq $releaseRunId -and
        $candidate.commit_sha -ceq $releaseRun.head_sha -and
        $candidate.api_artifact_name -ceq 'api' -and
        $candidate.dacpac_artifact_name -ceq 'dacpac' -and
        $candidate.api_sha256 -ceq $manifest.api.sha256 -and
        $candidate.dacpac_sha256 -ceq $manifest.database.sha256 -and
        $candidate.dacpac_sha256 -ceq $attempt.dacpac_sha256 -and
        $candidate.state -ceq 'staging_failed' -and
        $candidate.staging_validation_state -ceq 'not_validated' -and
        $null -eq $candidate.staging_validation_reference -and
        $candidate.promotion_authorized -eq $false
    $sqlAttemptMatches = [string]$sqlAttempt.release_run_id -ceq $releaseRunId -and
        $sqlAttempt.commit_sha -ceq $releaseRun.head_sha -and
        $sqlAttempt.target_environment -ceq 'staging' -and
        $sqlAttempt.database_server -ceq 'authcraftdbbrasil.database.windows.net' -and
        $sqlAttempt.database_name -ceq 'authcraft-db-staging' -and
        $sqlAttempt.result -ceq 'succeeded' -and
        $sqlAttempt.schema_result -ceq 'succeeded' -and
        $sqlAttempt.dacpac_sha256 -ceq $attempt.dacpac_sha256 -and
        $sqlAttempt.deploy_report_sha256 -ceq $attempt.deploy_report_sha256 -and
        $sqlAttempt.deploy_script_sha256 -ceq $attempt.deploy_script_sha256
    if (-not $candidateMatchesAttempt -or -not $sqlAttemptMatches) {
        throw 'The retained CandidateRelease or SQL attempt does not match the deployment evidence.'
    }

    $checkpoint = [ordered]@{
        schema_version = 1
        checkpoint_type = 'partial_staging_deployment'
        created_at = [DateTimeOffset]::UtcNow.ToString('o')
        baseline_state = 'partial_recovery'
        baseline_commit_sha = $releaseRun.head_sha
        deployment_run_id = $DeploymentRunId
        release_run_id = $releaseRunId
        failure_stage = 'smoke'
        live_state = 'not_reconciled'
        attempt_sha256 = (Get-FileHash -LiteralPath $attemptPath -Algorithm SHA256).Hash.ToLowerInvariant()
        candidate_sha256 = (Get-FileHash -LiteralPath $candidatePath -Algorithm SHA256).Hash.ToLowerInvariant()
        sql_attempt_sha256 = (Get-FileHash -LiteralPath $sqlAttemptPath -Algorithm SHA256).Hash.ToLowerInvariant()
        baseline_eligible = $false
        deployment_authorized = $false
        promotion_authorized = $false
    }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $checkpointPath = Join-Path $OutputDirectory 'staging-recovery-checkpoint.json'
    $checkpointJson = $checkpoint | ConvertTo-Json -Depth 5 -Compress
    Set-Content -LiteralPath $checkpointPath -Value $checkpointJson -Encoding UTF8
    Write-Output $checkpointJson
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
finally {
    if (Test-Path -LiteralPath $temporaryDirectory -PathType Container) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}
