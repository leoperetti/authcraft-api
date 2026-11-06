[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Evaluate', 'Publish')]
    [string]$Mode,
    [Parameter(Mandatory = $true)]
    [ValidateSet('staging', 'production')]
    [string]$TargetEnvironment,
    [Parameter(Mandatory = $true)]
    [string]$DacpacPath,
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedCommitSha,
    [Parameter(Mandatory = $true)]
    [string]$ExpectedRunId,
    [string]$SqlPackagePath,
    [string]$ReviewIssueNumber,
    [string]$AllowedReviewer = 'leoperetti'
)

$ErrorActionPreference = 'Stop'
$originalSqlAccessToken = [Environment]::GetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN')
$originalGitHubToken = [Environment]::GetEnvironmentVariable('GITHUB_TOKEN')
if ($Mode -eq 'Publish' -and $TargetEnvironment -ne 'staging') {
    throw 'Production database publication is blocked until protected approval is available.'
}
if ($AllowedReviewer -cne 'leoperetti') {
    throw 'The SQL reviewer must match the repository-approved reviewer.'
}
if ($ExpectedCommitSha -notmatch '^[A-Fa-f0-9]{40}$' -or
    $ExpectedRunId -notmatch '^[1-9][0-9]*$') {
    throw 'Release commit and run metadata are missing or malformed.'
}
if (-not (Test-Path -LiteralPath $DacpacPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw 'DACPAC and release manifest files are required.'
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$dacpacHash = (Get-FileHash -LiteralPath $DacpacPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($manifest.commit_sha -cne $ExpectedCommitSha.ToLowerInvariant() -or
    [string]$manifest.run_id -cne $ExpectedRunId -or
    $manifest.database.artifact -ne 'dacpac' -or
    -not [string]::Equals($manifest.database.sha256, $dacpacHash, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'DACPAC does not match the candidate release manifest.'
}

if ($TargetEnvironment -eq 'staging') {
    $targetServer = 'authcraftdbbrasil.database.windows.net'
    $targetDatabase = 'authcraft-db-staging'
} else {
    $targetServer = 'authcraftdbbrasil.database.windows.net'
    $targetDatabase = 'authcraft-db'
}

$sqlAccessToken = [Environment]::GetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN')
if ([string]::IsNullOrWhiteSpace($sqlAccessToken)) {
    throw 'A short-lived SQL access token is required in AUTHCRAFT_SQL_ACCESS_TOKEN.'
}

if (-not $SqlPackagePath) {
    $SqlPackagePath = 'C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe'
}
if (-not (Test-Path -LiteralPath $SqlPackagePath -PathType Leaf)) {
    throw 'SqlPackage.exe was not found.'
}

$artifactDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $DacpacPath).Path
$reportPath = Join-Path $artifactDirectory "$TargetEnvironment-deploy-report.xml"
$scriptPath = Join-Path $artifactDirectory "$TargetEnvironment-deploy.sql"
$commonArguments = @(
    "/SourceFile:$((Resolve-Path -LiteralPath $DacpacPath).Path)",
    "/TargetServerName:tcp:$targetServer,1433",
    "/TargetDatabaseName:$targetDatabase",
    '/TargetTrustServerCertificate:False',
    '/p:BlockOnPossibleDataLoss=True',
    '/p:DropObjectsNotInSource=False',
    "/AccessToken:$sqlAccessToken"
)

function Invoke-SqlPackage {
    param(
        [string]$Path,
        [string[]]$Arguments
    )

    $script:sqlPackageFailure = 'diagnostic_category=unavailable'
    $nativeOutput = $null
    try {
        if ([IO.Path]::GetExtension($Path) -ieq '.ps1') {
            $nativeOutput = & powershell.exe -NoProfile -NonInteractive -File $Path @Arguments 2>&1
        }
        else {
            $nativeOutput = & $Path @Arguments 2>&1
        }
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $diagnosticText = $nativeOutput -join "`n"
            $codes = @([Regex]::Matches($diagnosticText, '\bSQL[0-9]{5,6}\b') |
                ForEach-Object { $_.Value } | Sort-Object -Unique)
            $permissionNames = @(
                'VIEW DATABASE STATE', 'VIEW DATABASE PERFORMANCE STATE', 'VIEW DATABASE SECURITY STATE',
                'VIEW DEFINITION', 'VIEW ANY DEFINITION', 'VIEW SERVER STATE',
                'ALTER ANY USER', 'ALTER ANY ROLE', 'ALTER ANY SCHEMA', 'CREATE TABLE',
                'ALTER', 'CONTROL', 'SELECT', 'SHOWPLAN', 'CONNECT'
            )
            $mentionedPermissions = @($permissionNames | Where-Object {
                [Regex]::IsMatch($diagnosticText, '(?i)\b' + [Regex]::Escape($_) + '\b')
            })
            $permissionChecks = [ordered]@{
                sql_expression_dependencies_select_denied = '(?i)\bSELECT\b[^\r\n]*\bpermission\b[^\r\n]*\bdenied\b[^\r\n]*\bsql_expression_dependencies\b'
                alter_database_denied = '(?i)(?:does not have permission|permission.*denied)[^\r\n]*\balter\b[^\r\n]*\bdatabase\b'
                view_definition_denied = '(?i)(?:do not have|does not have|denied)[^\r\n]*\bview definition\b|\bview definition\b[^\r\n]*\bdenied\b'
                master_access_denied = '(?i)(?:permission|access)[^\r\n]*\bmaster\b|\bmaster\b[^\r\n]*(?:permission|access)'
            }
            $matchedChecks = @($permissionChecks.GetEnumerator() | Where-Object {
                [Regex]::IsMatch($diagnosticText, $_.Value)
            } | ForEach-Object { $_.Key })
            $deniedSystemObjects = @([Regex]::Matches($diagnosticText,
                '(?i)\bSELECT permission (?:was )?denied on (?:the )?object [''"](?<object>[a-z_][a-z0-9_]{0,127})[''"][^\r\n]*\bschema [''"]sys[''"]') |
                ForEach-Object { 'sys.' + $_.Groups['object'].Value } | Sort-Object -Unique)
            $category = 'unclassified'
            if ($diagnosticText -match '(?i)VIEW DATABASE (?:PERFORMANCE |SECURITY )?STATE') {
                $category = 'database_state_permission'
            } elseif ($diagnosticText -match '(?i)permission.*denied|does not have.*permission|insufficient.*permission') {
                $category = 'permission_denied'
            } elseif ($diagnosticText -match '(?i)login failed|authentication failed|token.*expired') {
                $category = 'authentication'
            } elseif ($diagnosticText -match '(?i)connection.*timeout|server was not found|database.*unavailable|firewall') {
                $category = 'connectivity'
            }
            $script:sqlPackageFailure = "exit_code=$exitCode; diagnostic_category=$category; diagnostic_codes=$($codes -join ','); mentioned_permissions=$($mentionedPermissions -join ','); permission_checks=$($matchedChecks -join ','); denied_system_objects=$($deniedSystemObjects -join ',')"
        }
        return $exitCode
    }
    catch {
        throw 'SqlPackage invocation failed. Inspect the target; no rollback was attempted. Command output is withheld to protect credentials.'
    }
    finally {
        $nativeOutput = $null
        $diagnosticText = $null
    }
}

function Get-AuthenticatedReview {
    try {
        $issue = Invoke-RestMethod -Uri $issueUri -Headers $headers -Method Get -TimeoutSec 30
        $comments = @(Invoke-RestMethod -Uri "$issueUri/comments?per_page=100" -Headers $headers -Method Get -TimeoutSec 30)
    }
    catch {
        throw 'Authenticated deployment review could not be retrieved from GitHub.'
    }
    if ($issue.state -ne 'open' -or $issue.pull_request) {
        throw 'Deployment review must be recorded on an open GitHub issue.'
    }
    if ($comments.Count -ge 100) {
        throw 'Review issue exceeds the verifiable comment limit; use a new review issue.'
    }
    $reviewComments = @($comments | Where-Object {
        $_.user.login -ceq $AllowedReviewer -and $_.body -match '(?im)^\s*deployment_review\s*='
    } | Sort-Object created_at, id)
    if ($reviewComments.Count -eq 0) {
        throw 'No authenticated deployment review was found.'
    }
    $latestReview = $reviewComments[-1]
    if ([string]$latestReview.id -notmatch '^[1-9][0-9]*$' -or
        -not (Test-DeploymentReviewRecord `
                -Body $latestReview.body `
                -Author $latestReview.user.login `
                -AllowedReviewer $AllowedReviewer `
                -RunId $ExpectedRunId `
                -CommitSha $ExpectedCommitSha `
                -TargetEnvironment $TargetEnvironment `
                -DatabaseServer $targetServer `
                -DatabaseName $targetDatabase `
                -DacpacSha256 $dacpacHash `
                -ReportSha256 $reportHash `
                -ScriptSha256 $scriptHash)) {
        throw 'Latest authenticated review does not approve this exact release, destination, and SQL plan.'
    }
    return $latestReview
}

try {
    foreach ($outputPath in @($reportPath, $scriptPath)) {
        if (Test-Path -LiteralPath $outputPath) {
            Remove-Item -LiteralPath $outputPath -Force
        }
    }
    $reportExitCode = Invoke-SqlPackage -Path $SqlPackagePath -Arguments (@('/Action:DeployReport') + $commonArguments + @("/OutputPath:$reportPath"))
    if ($reportExitCode -ne 0 -or -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw "SqlPackage DeployReport failed. No database changes were made. $script:sqlPackageFailure"
    }

    $scriptExitCode = Invoke-SqlPackage -Path $SqlPackagePath -Arguments (@('/Action:Script') + $commonArguments + @("/DeployScriptPath:$scriptPath"))
    if ($scriptExitCode -ne 0 -or -not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "SqlPackage Script failed. No database changes were made. $script:sqlPackageFailure"
    }

    $reportHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $scriptHash = (Get-FileHash -LiteralPath $scriptPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Import-Module (Join-Path $PSScriptRoot 'DeploymentReview.psm1') -Force
    if (-not (Test-LocalDeploymentScriptSafety -ScriptContent (Get-Content -LiteralPath $scriptPath -Raw))) {
        throw 'Deployment script contains destructive statements; automatic publication is blocked.'
    }
    if ($Mode -eq 'Evaluate') {
        [pscustomobject]@{
            state = 'evaluated'
            target_environment = $TargetEnvironment
            database_server = $targetServer
            database_name = $targetDatabase
            commit_sha = $ExpectedCommitSha.ToLowerInvariant()
            run_id = $ExpectedRunId
            dacpac_sha256 = $dacpacHash
            deploy_report_sha256 = $reportHash
            deploy_script_sha256 = $scriptHash
            deploy_report_path = $reportPath
            deploy_script_path = $scriptPath
        } | ConvertTo-Json -Compress
        return
    }

    if ($ReviewIssueNumber -notmatch '^[1-9][0-9]*$') {
        throw 'Publish requires a GitHub issue with an authenticated deployment review.'
    }
    $githubToken = [Environment]::GetEnvironmentVariable('GITHUB_TOKEN')
    $repository = [Environment]::GetEnvironmentVariable('GITHUB_REPOSITORY')
    if ([string]::IsNullOrWhiteSpace($githubToken) -or $repository -cne 'leoperetti/authcraft-api') {
        throw 'Authenticated GitHub review access is unavailable.'
    }

    $headers = @{
        Authorization = "Bearer $githubToken"
        Accept = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    $issueUri = "https://api.github.com/repos/$repository/issues/$ReviewIssueNumber"
    $review = Get-AuthenticatedReview

    $recheckReportPath = Join-Path $artifactDirectory "$TargetEnvironment-recheck-report.xml"
    $recheckScriptPath = Join-Path $artifactDirectory "$TargetEnvironment-recheck.sql"
    foreach ($outputPath in @($recheckReportPath, $recheckScriptPath)) {
        if (Test-Path -LiteralPath $outputPath) {
            Remove-Item -LiteralPath $outputPath -Force
        }
    }
    $recheckReportExitCode = Invoke-SqlPackage -Path $SqlPackagePath -Arguments (@('/Action:DeployReport') + $commonArguments + @("/OutputPath:$recheckReportPath"))
    if ($recheckReportExitCode -ne 0 -or -not (Test-Path -LiteralPath $recheckReportPath -PathType Leaf)) {
        throw "SqlPackage DeployReport revalidation failed. Publication is blocked. $script:sqlPackageFailure"
    }
    $recheckScriptExitCode = Invoke-SqlPackage -Path $SqlPackagePath -Arguments (@('/Action:Script') + $commonArguments + @("/DeployScriptPath:$recheckScriptPath"))
    if ($recheckScriptExitCode -ne 0 -or -not (Test-Path -LiteralPath $recheckScriptPath -PathType Leaf)) {
        throw "SqlPackage Script revalidation failed. Publication is blocked. $script:sqlPackageFailure"
    }
    if ((Get-FileHash -LiteralPath $DacpacPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $dacpacHash -or
        (Get-FileHash -LiteralPath $recheckReportPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $reportHash -or
        (Get-FileHash -LiteralPath $recheckScriptPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $scriptHash) {
        throw 'DACPAC or SQL plan changed after review. A new review is required.'
    }
    $recheckedReview = Get-AuthenticatedReview
    if ($recheckedReview.id -ne $review.id -or $recheckedReview.body -cne $review.body) {
        throw 'Deployment review changed during revalidation. Publication is blocked.'
    }
    if ((Get-FileHash -LiteralPath $DacpacPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $dacpacHash) {
        throw 'DACPAC changed during approval revalidation. Publication is blocked.'
    }

    $publishExitCode = Invoke-SqlPackage -Path $SqlPackagePath -Arguments (@('/Action:Publish') + $commonArguments)
    if ($publishExitCode -ne 0) {
        throw "SqlPackage Publish failed. Inspect the target; no rollback was attempted. $script:sqlPackageFailure"
    }

    [pscustomobject]@{
        state = 'published'
        target_environment = $TargetEnvironment
        database_name = $targetDatabase
        commit_sha = $ExpectedCommitSha.ToLowerInvariant()
        run_id = $ExpectedRunId
        dacpac_sha256 = $dacpacHash
        review_comment_id = $review.id
    } | ConvertTo-Json -Compress
}
finally {
    $sqlAccessToken = $null
    $githubToken = $null
    [Environment]::SetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN', $originalSqlAccessToken)
    [Environment]::SetEnvironmentVariable('GITHUB_TOKEN', $originalGitHubToken)
    $originalSqlAccessToken = $null
    $originalGitHubToken = $null
}
