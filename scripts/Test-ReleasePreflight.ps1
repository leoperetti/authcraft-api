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
    [Parameter(Mandatory = $true)]
    [string]$LastSuccessfulCommitSha,
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
    if ($ExpectedCommitSha -notmatch '^[A-Fa-f0-9]{40}$' -or
        $LastSuccessfulCommitSha -notmatch '^[A-Fa-f0-9]{40}$' -or
        $ExpectedRunId -notmatch '^[1-9][0-9]*$') {
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

    if ([string]::Equals(
            $ExpectedCommitSha,
            $LastSuccessfulCommitSha,
            [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Preflight 'Candidate release is not newer than the last successful release.'
    }

    $repositoryRoot = (Resolve-Path -LiteralPath $RepositoryPath).Path
    $checkedOutCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or
        -not [string]::Equals($checkedOutCommit, $ExpectedCommitSha, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-Preflight 'Checked-out source does not match the release commit.'
    }
    & git -C $repositoryRoot merge-base --is-ancestor $LastSuccessfulCommitSha $ExpectedCommitSha *> $null
    if ($LASTEXITCODE -ne 0) {
        Stop-Preflight 'Candidate release is older than or diverged from the last successful release.'
    }

    $run = Get-ActionsMetadata "actions/runs/$ExpectedRunId"
    if ([string]$run.id -cne $ExpectedRunId -or
        $run.repository.full_name -cne 'leoperetti/authcraft-api' -or
        $run.head_repository.full_name -cne 'leoperetti/authcraft-api' -or
        $run.head_sha -cne $ExpectedCommitSha.ToLowerInvariant() -or
        $run.head_branch -cne 'main' -or
        $run.event -notin @('push', 'workflow_dispatch') -or
        $run.path -notin @('.github/workflows/main_authcraft-api.yml', '.github/workflows/main_authcraft-api-staging.yml')) {
        Stop-Preflight 'Actions candidate does not match an authorized main build from this repository.'
    }
    $jobs = Get-ActionsMetadata "actions/runs/$ExpectedRunId/jobs?per_page=100"
    $buildJobName = 'build'
    if ($run.path -ceq '.github/workflows/main_authcraft-api-staging.yml') {
        $buildJobName = 'validate'
    }
    $buildJobs = @($jobs.jobs | Where-Object { $_.name -ceq $buildJobName })
    if ($jobs.total_count -ne @($jobs.jobs).Count -or
        $buildJobs.Count -ne 1 -or $buildJobs[0].status -cne 'completed' -or $buildJobs[0].conclusion -cne 'success') {
        Stop-Preflight 'Actions build completion could not be verified.'
    }

    $artifactMetadata = Get-ActionsMetadata "actions/runs/$ExpectedRunId/artifacts?per_page=100"
    if ($artifactMetadata.total_count -ne @($artifactMetadata.artifacts).Count) {
        Stop-Preflight 'Actions artifact metadata is truncated; provenance cannot be verified.'
    }
    $evidenceDirectory = Join-Path ([IO.Path]::GetTempPath()) "authcraft-preflight-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $evidenceDirectory | Out-Null
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

    $history = Get-ActionsMetadata 'actions/workflows/main_authcraft-api.yml/runs?branch=main&status=success&per_page=100'
    $lastStagingRun = $null
    foreach ($previousRun in $history.workflow_runs) {
        if ($previousRun.repository.full_name -cne 'leoperetti/authcraft-api' -or
            $previousRun.head_repository.full_name -cne 'leoperetti/authcraft-api' -or
            $previousRun.path -cne '.github/workflows/main_authcraft-api.yml' -or
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
            $lastStagingRun = $previousRun
            break
        }
    }
    if ($null -eq $lastStagingRun -or
        $lastStagingRun.head_sha -cne $LastSuccessfulCommitSha.ToLowerInvariant()) {
        Stop-Preflight 'Last successful staging release is missing or does not match authenticated Actions history.'
    }

    [pscustomobject]@{
        state = 'blocked'
        artifact_validation = 'verified'
        deployment_authorized = $false
        blocking_reason = 'Live inventory, publisher identity, effective SQL permissions and configuration gates are not yet integrated.'
        commit_sha = $ExpectedCommitSha.ToLowerInvariant()
        run_id = $ExpectedRunId
        previous_run_id = [string]$lastStagingRun.id
        target = 'staging'
        database_server = 'authcraftdbbrasil.database.windows.net'
        database_name = 'authcraft-db-staging'
    } | ConvertTo-Json -Compress
    Stop-Preflight 'Deployment remains blocked until live inventory, identity, SQL permissions and configuration gates are integrated.'
}
catch {
    if ($_.Exception.Message -notmatch '^(Production publication|Release publication|Commit or run|Manifest|A release|Candidate release)') {
        Write-Error 'Release preflight failed. No deployment command was run.'
        exit 1
    }
    throw
}
finally {
    if ($evidenceDirectory -and (Test-Path -LiteralPath $evidenceDirectory)) {
        Remove-Item -LiteralPath $evidenceDirectory -Recurse -Force
    }
}
