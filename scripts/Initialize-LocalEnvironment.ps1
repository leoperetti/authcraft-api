[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$TargetServerName = '(localdb)\MSSQLLocalDB',
    [string]$TargetDatabaseName = 'authcraft_development',
    [string]$DacpacPath,
    [string]$SqlPackagePath,
    [string]$EvidenceDirectory,
    [switch]$Publish,
    [string]$ReviewedReportSha256,
    [string]$ReviewedScriptSha256,
    [string]$ConfirmedTargetDatabase
)

$ErrorActionPreference = 'Stop'
$deploymentReviewModule = Join-Path $PSScriptRoot 'DeploymentReview.psm1'
Import-Module $deploymentReviewModule -Force

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dacpacDirectory = Join-Path $repositoryRoot 'authcraft-db\bin\Release'
$dacpacFiles = @(Get-ChildItem -LiteralPath $dacpacDirectory -Filter '*.dacpac' -File -ErrorAction SilentlyContinue)
if ($dacpacFiles.Count -ne 1) {
    throw "Expected exactly one MSBuild DACPAC under '$dacpacDirectory'; found $($dacpacFiles.Count)."
}

$dacpac = $dacpacFiles[0]
if ($DacpacPath) {
    $requestedDacpac = (Resolve-Path -LiteralPath $DacpacPath).Path
    if (-not [string]::Equals($requestedDacpac, $dacpac.FullName, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'DacpacPath must identify the single DACPAC produced in authcraft-db\bin\Release.'
    }
}

$normalizedServer = $TargetServerName.Trim()
if ($normalizedServer.StartsWith('tcp:', [StringComparison]::OrdinalIgnoreCase)) {
    $normalizedServer = $normalizedServer.Substring(4)
}
if ($normalizedServer.Contains(',')) {
    $normalizedServer = $normalizedServer.Split(',')[0]
}
$localServers = @('localhost', '.', '(local)', '(localdb)\MSSQLLocalDB', '127.0.0.1', '::1', '[::1]')
if ($normalizedServer -notin $localServers -or
    $TargetDatabaseName -notmatch '^authcraft_(development|environment_test|invitation_test)(?:_[a-z0-9_]+)?$') {
    throw 'Only an approved localhost/LocalDB server and dedicated AuthCraft development/test database are allowed.'
}

if (-not $SqlPackagePath) {
    $candidates = @(
        @(
        $env:SQLPACKAGE_PATH,
        'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe'
        ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) }
    )
    if ($candidates.Count -eq 0) {
        throw 'SqlPackage.exe was not found. Set SQLPACKAGE_PATH or pass SqlPackagePath.'
    }
    $SqlPackagePath = $candidates[0]
}
if (-not (Test-Path -LiteralPath $SqlPackagePath -PathType Leaf)) {
    throw 'SqlPackagePath must identify an existing SqlPackage.exe.'
}

if (-not $EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $env:TEMP ('authcraft-local-sql-' + [Guid]::NewGuid().ToString('N'))
}
$evidenceFullPath = [IO.Path]::GetFullPath($EvidenceDirectory)
if ($evidenceFullPath.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Deployment reports must be kept outside the repository.'
}
New-Item -ItemType Directory -Path $evidenceFullPath -Force | Out-Null
$reportPath = Join-Path $evidenceFullPath 'local-deploy-report.xml'
$scriptPath = Join-Path $evidenceFullPath 'local-deploy.sql'

$commonArguments = @(
    "/SourceFile:$($dacpac.FullName)",
    "/TargetServerName:$TargetServerName",
    "/TargetDatabaseName:$TargetDatabaseName",
    '/TargetTrustServerCertificate:True',
    '/p:BlockOnPossibleDataLoss=True',
    '/p:DropObjectsNotInSource=False',
    '/p:ScriptDatabaseOptions=False',
    '/p:IncludeTransactionalScripts=True'
)

& $SqlPackagePath /Action:DeployReport @commonArguments "/OutputPath:$reportPath"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
    throw 'SqlPackage DeployReport failed; no database changes were made.'
}

& $SqlPackagePath /Action:Script @commonArguments "/DeployScriptPath:$scriptPath"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'SqlPackage Script failed; no database changes were made.'
}

$deploymentScript = Get-Content -LiteralPath $scriptPath -Raw
if (-not (Test-LocalDeploymentScriptSafety -ScriptContent $deploymentScript)) {
    throw 'Generated SQL contains destructive database or data operations; review the target and schema before publishing.'
}

$reportHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash
$scriptHash = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash
Write-Output "DACPAC: $($dacpac.Name)"
Write-Output "Target: $TargetServerName / $TargetDatabaseName"
Write-Output "Report: $reportPath (SHA256 $reportHash)"
Write-Output "Script: $scriptPath (SHA256 $scriptHash)"

if (-not $Publish) {
    Write-Output 'Review both files before publishing. Rerun with -Publish, both reviewed hashes, and -ConfirmedTargetDatabase.'
    return
}

if ($ConfirmedTargetDatabase -cne $TargetDatabaseName) {
    throw 'Publishing requires -ConfirmedTargetDatabase to match the requested local database.'
}
if ($ReviewedReportSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
    $ReviewedScriptSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
    -not [string]::Equals($ReviewedReportSha256, $reportHash, [StringComparison]::OrdinalIgnoreCase) -or
    -not [string]::Equals($ReviewedScriptSha256, $scriptHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The reviewed report/script hashes are absent or no longer match the evaluated deployment.'
}
if (-not $PSCmdlet.ShouldProcess("$TargetServerName / $TargetDatabaseName", 'Publish reviewed local DACPAC')) {
    return
}

& $SqlPackagePath /Action:Publish @commonArguments
if ($LASTEXITCODE -ne 0) {
    throw 'SqlPackage Publish failed. Inspect the local target; no rollback was attempted.'
}

Write-Output 'Local DACPAC publish completed.'
