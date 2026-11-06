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

    [pscustomobject]@{
        state = 'ready'
        commit_sha = $ExpectedCommitSha.ToLowerInvariant()
        run_id = $ExpectedRunId
        target = 'staging'
        database_server = 'authcraftdbbrasil.database.windows.net'
        database_name = 'authcraft-db-staging'
    } | ConvertTo-Json -Compress
}
catch {
    if ($_.Exception.Message -notmatch '^(Production publication|Release publication|Commit or run|Manifest|A release|Candidate release)') {
        Write-Error 'Release preflight failed. No deployment command was run.'
        exit 1
    }
    throw
}
