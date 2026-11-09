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
    [string]$StagingBaselineSha,
    [string]$TransactionVerificationPath,
    [string]$AttemptPath,
    [string]$AllowedReviewer = 'leoperetti'
)

$ErrorActionPreference = 'Stop'
$originalSqlAccessToken = [Environment]::GetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN')
$originalGitHubToken = [Environment]::GetEnvironmentVariable('GITHUB_TOKEN')
if ($Mode -eq 'Publish' -and $TargetEnvironment -ne 'staging') {
    throw 'Production database publication is blocked until protected approval is available.'
}
if ($Mode -eq 'Publish' -and
    $StagingBaselineSha -cne 'unknown' -and
    $StagingBaselineSha -notmatch '^[A-Fa-f0-9]{40}$') {
    throw 'Publish requires a verified Staging baseline or the explicitly accepted unknown initial baseline.'
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
$evaluationId = [Guid]::NewGuid().ToString('N')
$evaluationPrefix = if ($Mode -eq 'Publish') { 'publish' } else { 'evaluation' }
$reportPath = Join-Path $artifactDirectory "$TargetEnvironment-$evaluationPrefix-$ExpectedRunId-$evaluationId-report.xml"
$scriptPath = Join-Path $artifactDirectory "$TargetEnvironment-$evaluationPrefix-$ExpectedRunId-$evaluationId-script.sql"
$commonArguments = @(
    "/SourceFile:$((Resolve-Path -LiteralPath $DacpacPath).Path)",
    "/TargetServerName:tcp:$targetServer,1433",
    "/TargetDatabaseName:$targetDatabase",
    '/TargetTrustServerCertificate:False',
    '/p:BlockOnPossibleDataLoss=True',
    '/p:DropObjectsNotInSource=False',
    '/p:ScriptDatabaseOptions=False',
    '/p:IncludeTransactionalScripts=True',
    "/AccessToken:$sqlAccessToken"
)

function Export-EncryptedSqlPackageDiagnostic {
    param([string]$DiagnosticText)

    if ([string]::IsNullOrWhiteSpace($env:AUTHCRAFT_SQL_DIAGNOSTIC_PUBLIC_KEY)) { return }
    if ([string]::IsNullOrWhiteSpace($env:AUTHCRAFT_SQL_DIAGNOSTIC_PATH)) {
        throw 'An encrypted SQL diagnostic output path is required.'
    }
    $rsa = $null
    $plaintext = $null
    try {
        $publicKey = $env:AUTHCRAFT_SQL_DIAGNOSTIC_PUBLIC_KEY | ConvertFrom-Json
        $parameters = New-Object Security.Cryptography.RSAParameters
        $parameters.Modulus = [Convert]::FromBase64String($publicKey.modulus)
        $parameters.Exponent = [Convert]::FromBase64String($publicKey.exponent)
        $rsa = New-Object Security.Cryptography.RSACng
        $rsa.ImportParameters($parameters)
        if ($rsa.KeySize -lt 2048) { throw 'Diagnostic encryption requires RSA 2048 or stronger.' }
        $plaintext = [Text.Encoding]::UTF8.GetBytes($DiagnosticText)
        if ($plaintext.Length -gt 1048576) { throw 'SQL diagnostic exceeds the encrypted capture limit.' }
        $blockSize = $rsa.KeySize / 8 - 66
        $blocks = @(
            for ($offset = 0; $offset -lt $plaintext.Length; $offset += $blockSize) {
                $length = [Math]::Min($blockSize, $plaintext.Length - $offset)
                $block = New-Object byte[] $length
                [Array]::Copy($plaintext, $offset, $block, 0, $length)
                [Convert]::ToBase64String($rsa.Encrypt($block, [Security.Cryptography.RSAEncryptionPadding]::OaepSHA256))
            }
        )
        [ordered]@{
            version = 1
            algorithm = 'RSA-OAEP-SHA256'
            blocks = $blocks
        } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $env:AUTHCRAFT_SQL_DIAGNOSTIC_PATH -Encoding UTF8
    }
    finally {
        if ($null -ne $rsa) { $rsa.Dispose() }
        if ($null -ne $plaintext) { [Array]::Clear($plaintext, 0, $plaintext.Length) }
    }
}

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
            Export-EncryptedSqlPackageDiagnostic -DiagnosticText $diagnosticText
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
                sql_expression_dependencies_select_denied = '(?is)(?=.*\bSELECT\b)(?=.*\bsql_expression_dependencies\b)(?=.*(?:permission.*denied|does not have.*permission|insufficient.*permission))'
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
                -ScriptSha256 $scriptHash `
                -StagingBaselineSha $StagingBaselineSha)) {
        throw 'Latest authenticated review does not approve this exact release, destination, and SQL plan.'
    }
    return $latestReview
}

$attempt = $null
if ($Mode -eq 'Publish') {
    if (-not $AttemptPath) {
        $AttemptPath = Join-Path $artifactDirectory "$TargetEnvironment-attempt-$ExpectedRunId-$evaluationId.json"
    }
    if (Test-Path -LiteralPath $AttemptPath) {
        throw 'Deployment attempt evidence already exists; use a new attempt path.'
    }
    $attempt = [ordered]@{
        schema_version = 1
        release_run_id = $ExpectedRunId
        commit_sha = $ExpectedCommitSha.ToLowerInvariant()
        target_environment = $TargetEnvironment
        database_server = $targetServer
        database_name = $targetDatabase
        started_at = [DateTimeOffset]::UtcNow.ToString('o')
        finished_at = $null
        result = 'blocked'
        schema_result = 'blocked'
        application_result = 'not_executed'
        health_result = 'not_executed'
        authentication_result = 'not_executed'
        include_transactional_scripts = $true
        postdeploy_transaction = $false
        transaction_scope = 'unverified'
        postdeploy_result = 'pending'
        failure_state_verification = 'pending'
        transaction_verification_reference = $null
        dacpac_sha256 = $dacpacHash
        deploy_report_sha256 = $null
        deploy_script_sha256 = $null
        failure_stage = 'schema_review'
        sanitized_failure_summary = 'La revision del esquema bloqueo la publicacion.'
        recommended_next_step = 'Revisar la evidencia y obtener una revision exacta antes de reintentar.'
    }
}
try {
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
    if ($null -ne $attempt) {
        $attempt.deploy_report_sha256 = $reportHash
        $attempt.deploy_script_sha256 = $scriptHash
    }
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

    $deploymentSql = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
    if ($Mode -eq 'Publish') {
        $validationPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'authcraft-db\postdeploys\ValidateConstraints.sql'
        $postdeploymentPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'authcraft-db\postdeploys\Script.PostDeployment.sql'
        $validationSql = (Get-Content -LiteralPath $postdeploymentPath -Raw -Encoding UTF8).Replace("`r`n", "`n").Trim()
        $constraintSql = (Get-Content -LiteralPath $validationPath -Raw -Encoding UTF8).Replace("`r`n", "`n")
        $validationSql = $validationSql.Replace(':r .\ValidateConstraints.sql', $constraintSql)
        # After the exact block, allow only DacFx's validation and progress statements.
        $normalizedSql = $deploymentSql.Replace("`r`n", "`n")
        $validationIndex = $normalizedSql.LastIndexOf("`n$validationSql", [StringComparison]::Ordinal)
        $validationTail = if ($validationIndex -ge 0) {
            $normalizedSql.Substring($validationIndex + 1 + $validationSql.Length)
        } else { '' }
        $validationTail = [Regex]::Replace($validationTail,
            '(?im)^[ \t]*(?:GO[ \t]*$|PRINT[ \t]+N''(?:[^'']|'''')*''[ \t]*;|USE[ \t]+\[\$\(DatabaseName\)\][ \t]*;)', '')
        $validationTargets = @{
            invitations = @('FK_invitations_accepted_identity', 'CK_invitations_purpose',
                'CK_invitations_registered_dni', 'CK_invitations_student_dni', 'CK_invitations_student_profile')
            memberships = @('CK_memberships_status')
            tenants = @('CK_tenants_status')
        }
        $unrecognizedSql = $deploymentSql
        foreach ($tableName in $validationTargets.Keys) {
            $constraintNames = ($validationTargets[$tableName] | ForEach-Object { [Regex]::Escape($_) }) -join '|'
            $creationPattern = '(?im)^[ \t]*ALTER\s+TABLE\s+\[dbo\]\.\[' + $tableName +
                '\]\s+WITH\s+NOCHECK\s+ADD\s+CONSTRAINT\s+\[(?:' + $constraintNames +
                ')\]\s+(?:CHECK\s*\(|FOREIGN\s+KEY\s*\()'
            $unrecognizedSql = [Regex]::Replace($unrecognizedSql, $creationPattern, '')
            $checkingPattern = '(?im)^[ \t]*ALTER\s+TABLE\s+\[dbo\]\.\[' + $tableName +
                '\]\s+WITH\s+CHECK\s+CHECK\s+CONSTRAINT\s+\[(?:' + $constraintNames + ')\]\s*;'
            $validationTail = [Regex]::Replace($validationTail, $checkingPattern, '')
        }
        if ([string]::IsNullOrWhiteSpace($validationSql) -or
            $validationIndex -lt 0 -or -not [string]::IsNullOrWhiteSpace($validationTail) -or
            [Regex]::IsMatch($unrecognizedSql, '(?i)\bNOCHECK\b')) {
            throw 'SQL plan lacks the exact transactional postdeployment block, contains uncovered NOCHECK, or has an unrecognized tail. Publication is blocked; regenerate and review the plan.'
        }
        $attempt.postdeploy_transaction = $true
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

    $recheckReportPath = Join-Path $artifactDirectory "$TargetEnvironment-recheck-$ExpectedRunId-$evaluationId-report.xml"
    $recheckScriptPath = Join-Path $artifactDirectory "$TargetEnvironment-recheck-$ExpectedRunId-$evaluationId-script.sql"
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

    $attempt.transaction_verification_reference = Get-DeploymentTransactionVerification `
        -Path $TransactionVerificationPath -SqlPackagePath $SqlPackagePath
    $attempt.transaction_verification_reference | Add-Member -NotePropertyName deploy_script_sha256 -NotePropertyValue $scriptHash
    $attempt.transaction_verification_reference | Add-Member -NotePropertyName postdeployment_start_line `
        -NotePropertyValue (1 + ($normalizedSql.Substring(0, $validationIndex + 1).ToCharArray() | Where-Object { $_ -eq "`n" }).Count)
    $attempt.transaction_scope = 'separate_schema_and_postdeploy'
    $attempt.failure_stage = 'schema_publish'
    $attempt.result = 'pending'
    $attempt.schema_result = 'pending'
    $attempt.postdeploy_result = 'unknown'
    $attempt.failure_state_verification = 'unknown'
    $attempt.sanitized_failure_summary = 'La publicacion SQL no tiene un resultado confirmado; el esquema puede estar aplicado parcialmente.'
    $attempt.recommended_next_step = 'Detener API y smoke; inspeccionar esquema, datos y confianza desde otra conexion antes de reintentar.'
    $attempt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $AttemptPath -Encoding UTF8
    $publishExitCode = Invoke-SqlPackage -Path $SqlPackagePath -Arguments (@('/Action:Publish') + $commonArguments)
    if ($publishExitCode -ne 0) {
        throw "SqlPackage Publish failed. Inspect the target; no rollback was attempted. $script:sqlPackageFailure"
    }
    $attempt.result = 'succeeded'
    $attempt.schema_result = 'succeeded'
    $attempt.failure_state_verification = 'pending'
    $attempt.failure_stage = $null
    $attempt.sanitized_failure_summary = $null
    $attempt.recommended_next_step = $null

    [pscustomobject]@{
        state = 'published'
        target_environment = $TargetEnvironment
        database_name = $targetDatabase
        commit_sha = $ExpectedCommitSha.ToLowerInvariant()
        run_id = $ExpectedRunId
        dacpac_sha256 = $dacpacHash
        deploy_report_sha256 = $reportHash
        deploy_script_sha256 = $scriptHash
        deploy_report_path = $reportPath
        deploy_script_path = $scriptPath
        review_comment_id = $review.id
        transaction_scope = $attempt.transaction_scope
        deployment_attempt_path = $AttemptPath
    } | ConvertTo-Json -Compress
}
finally {
    try {
        if ($null -ne $attempt) {
            if ($attempt.result -eq 'pending') {
                $attempt.result = 'failed'
                $attempt.schema_result = 'failed'
            }
            $attempt.finished_at = [DateTimeOffset]::UtcNow.ToString('o')
            $attempt | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $AttemptPath -Encoding UTF8
        }
    } finally {
        $sqlAccessToken = $null
        $githubToken = $null
        [Environment]::SetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN', $originalSqlAccessToken)
        [Environment]::SetEnvironmentVariable('GITHUB_TOKEN', $originalGitHubToken)
        $originalSqlAccessToken = $null
        $originalGitHubToken = $null
    }
}
