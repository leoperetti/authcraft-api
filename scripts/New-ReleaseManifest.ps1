[CmdletBinding()]
param(
    [string]$ApiArtifactPath,
    [string]$DatabaseArtifactPath,
    [string]$ApiArtifactName = 'api',
    [string]$DatabaseArtifactName = 'dacpac',
    [string]$CommitSha = $env:GITHUB_SHA,
    [string]$RunId = $env:GITHUB_RUN_ID,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
if (-not $ApiArtifactPath -or -not (Test-Path -LiteralPath $ApiArtifactPath -PathType Leaf)) {
    throw 'ApiArtifactPath must identify an existing API package file.'
}
if (-not $DatabaseArtifactPath -or -not (Test-Path -LiteralPath $DatabaseArtifactPath -PathType Leaf)) {
    throw 'DatabaseArtifactPath must identify an existing DACPAC file.'
}
if ([string]::Equals(
        (Resolve-Path -LiteralPath $ApiArtifactPath).Path,
        (Resolve-Path -LiteralPath $DatabaseArtifactPath).Path,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'API and database artifacts must be distinct files.'
}
if ([IO.Path]::GetExtension($ApiArtifactPath) -ne '.zip' -or
    [IO.Path]::GetExtension($DatabaseArtifactPath) -ne '.dacpac') {
    throw 'API artifact must be a zip package and database artifact must be a DACPAC.'
}
if ([string]::IsNullOrWhiteSpace($ApiArtifactName) -or
    [string]::IsNullOrWhiteSpace($DatabaseArtifactName) -or
    $ApiArtifactName -eq $DatabaseArtifactName) {
    throw 'Artifact names must be present and unique.'
}
if ($CommitSha -notmatch '^[A-Fa-f0-9]{40}$') {
    throw 'CommitSha must be a 40-character Git commit SHA.'
}
if ($RunId -notmatch '^[1-9][0-9]*$') {
    throw 'RunId must be a positive GitHub Actions run ID.'
}

$apiHash = (Get-FileHash -LiteralPath $ApiArtifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
$databaseHash = (Get-FileHash -LiteralPath $DatabaseArtifactPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($apiHash -notmatch '^[a-f0-9]{64}$' -or $databaseHash -notmatch '^[a-f0-9]{64}$') {
    throw 'SHA-256 generation failed.'
}

$manifest = [ordered]@{
    commit_sha = $CommitSha.ToLowerInvariant()
    run_id = $RunId
    api = [ordered]@{
        artifact = $ApiArtifactName
        sha256 = $apiHash
    }
    database = [ordered]@{
        artifact = $DatabaseArtifactName
        sha256 = $databaseHash
    }
}

$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
if ($outputFullPath -in @(
        [IO.Path]::GetFullPath($ApiArtifactPath),
        [IO.Path]::GetFullPath($DatabaseArtifactPath))) {
    throw 'Manifest output must be separate from both release artifacts.'
}
$outputDirectory = Split-Path -Parent $outputFullPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}
$temporaryPath = "$outputFullPath.$([Guid]::NewGuid().ToString('N')).tmp"
try {
    $json = $manifest | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($temporaryPath, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $outputFullPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

Write-Output "Release manifest created: $outputFullPath"
