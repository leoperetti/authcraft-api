[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -cne 'true' -or
    $env:GITHUB_REPOSITORY -cne 'leoperetti/authcraft-api' -or
    $env:GITHUB_REF -cne 'refs/heads/main' -or
    $env:GITHUB_EVENT_NAME -cne 'workflow_dispatch' -or
    $env:GITHUB_RUN_ID -notmatch '^[1-9][0-9]*$' -or
    $env:GITHUB_SHA -notmatch '^[a-f0-9]{40}$') {
    throw 'Publisher verification requires an explicitly dispatched main run in this repository.'
}

$originalToken = [Environment]::GetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN')
$originalOptIn = [Environment]::GetEnvironmentVariable('AUTHCRAFT_VERIFY_STAGING_PUBLISHER')
$token = $null
$resultsDirectory = $null
try {
    $accountOutput = & az account show --output json 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Azure account metadata is unavailable.'
    }
    try {
        $account = ($accountOutput -join "`n") | ConvertFrom-Json
    } catch {
        throw 'Azure account metadata is invalid.'
    }
    if ($account.id -cne '948a8f49-fd90-40d9-8fa9-12f722768a7a' -or
        $account.tenantId -cne 'b7bd0c07-88dc-4c47-81ba-608fd7607a82' -or
        $account.user.type -cne 'servicePrincipal' -or
        $account.user.name -cne 'a2382f63-d653-47f3-82ca-9f2696f63f0a') {
        throw 'Azure login is not the expected staging publisher, tenant, and subscription.'
    }
    $tokenOutput = & az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw 'Staging SQL token acquisition failed.'
    }
    $token = ($tokenOutput -join '').Trim()
    $tokenOutput = $null
    if ([string]::IsNullOrWhiteSpace($token) -or $token -notmatch '^[A-Za-z0-9._~-]+$') {
        throw 'Staging SQL token is missing or malformed.'
    }
    Write-Output "::add-mask::$token"
    [Environment]::SetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN', $token)
    [Environment]::SetEnvironmentVariable('AUTHCRAFT_VERIFY_STAGING_PUBLISHER', 'true')

    $testProject = Join-Path $PSScriptRoot '..\authcraft-api.Tests\authcraft-api.Tests.csproj'
    $resultsDirectory = Join-Path ([IO.Path]::GetTempPath()) "authcraft-publisher-check-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $resultsDirectory | Out-Null
    & dotnet test $testProject --configuration Release --no-build `
        --filter 'FullyQualifiedName=authcraft_api.Tests.StagingPublisherSqlTests.OidcPublisherHasOnlyApprovedReviewPermissions' `
        --logger 'trx;LogFileName=publisher.trx' --results-directory $resultsDirectory *> $null
    if ($LASTEXITCODE -ne 0) {
        throw 'Staging SQL login or review-permission verification failed. No deployment was attempted. Native test output is withheld to protect credentials.'
    }
    try {
        [xml]$report = Get-Content -LiteralPath (Join-Path $resultsDirectory 'publisher.trx') -Raw
    } catch {
        throw 'Staging SQL verification did not produce a readable test report.'
    }
    $counters = $report.TestRun.ResultSummary.Counters
    $testResults = @($report.TestRun.Results.UnitTestResult)
    if ($counters.total -cne '1' -or $counters.executed -cne '1' -or
        $counters.passed -cne '1' -or $counters.failed -cne '0' -or
        $testResults.Count -ne 1 -or $testResults[0].outcome -cne 'Passed' -or
        $testResults[0].testName -cne 'authcraft_api.Tests.StagingPublisherSqlTests.OidcPublisherHasOnlyApprovedReviewPermissions') {
        throw 'Staging SQL verification was skipped, missing, or did not pass the exact required test.'
    }
    [ordered]@{
        state = 'verified'
        verification = 'staging_publisher_sql_review_only'
        database_name = 'authcraft-db-staging'
        run_id = $env:GITHUB_RUN_ID
        commit_sha = $env:GITHUB_SHA
        deployment_authorized = $false
    } | ConvertTo-Json -Compress
}
finally {
    [Environment]::SetEnvironmentVariable('AUTHCRAFT_SQL_ACCESS_TOKEN', $originalToken)
    [Environment]::SetEnvironmentVariable('AUTHCRAFT_VERIFY_STAGING_PUBLISHER', $originalOptIn)
    $token = $null
    $tokenOutput = $null
    $originalToken = $null
    if ($resultsDirectory -and (Test-Path -LiteralPath $resultsDirectory)) {
        Remove-Item -LiteralPath $resultsDirectory -Recurse -Force
    }
}
