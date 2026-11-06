[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Fa-f0-9]{40}$')]
    [string]$ExpectedVersion,
    [ValidateRange(1, 5)]
    [int]$MaxAttempts = 3,
    [ValidateRange(0, 15)]
    [int]$RetryDelaySeconds = 5
)

$ErrorActionPreference = 'Stop'
$identifier = [Environment]::GetEnvironmentVariable('AUTHCRAFT_STAGING_SMOKE_IDENTIFIER')
$password = [Environment]::GetEnvironmentVariable('AUTHCRAFT_STAGING_SMOKE_PASSWORD')
$clientId = [Environment]::GetEnvironmentVariable('AUTHCRAFT_STAGING_SMOKE_CLIENT_ID')
$expectedTenant = [Environment]::GetEnvironmentVariable('AUTHCRAFT_STAGING_SMOKE_TENANT_CLIENT_ID')

if ([string]::IsNullOrWhiteSpace($identifier) -or
    [string]::IsNullOrWhiteSpace($password) -or
    [string]::IsNullOrWhiteSpace($clientId) -or
    [string]::IsNullOrWhiteSpace($expectedTenant) -or
    $clientId -cne $expectedTenant) {
    Write-Error 'Staging smoke credentials and the matching staging tenant must be provided in process environment variables.'
    exit 1
}

$stagingHost = (& az webapp show `
        --resource-group AuthCraft `
        --name authcraft-api-staging `
        --query defaultHostName `
        --output tsv 2>$null)
if ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) {
    Write-Error 'Unable to resolve the staging hostname from Azure metadata.'
    exit 1
}
$stagingHost = ($stagingHost -join '').Trim()
if ($stagingHost -notmatch '^authcraft-api-staging(?:-[a-z0-9]+)?(?:\.[a-z0-9-]+)?\.azurewebsites\.net$') {
    Write-Error 'Azure returned a hostname outside the expected staging application.'
    exit 1
}

$baseUri = "https://$stagingHost"
$healthReady = $null
for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    try {
        $live = Invoke-RestMethod -Uri "$baseUri/health/live" -Method Get -TimeoutSec 30
        $ready = Invoke-RestMethod -Uri "$baseUri/health/ready" -Method Get -TimeoutSec 30
        if ($live.status -eq 'healthy' -and
            $ready.status -eq 'healthy' -and
            $live.version -ceq $ExpectedVersion -and
            $ready.version -ceq $ExpectedVersion) {
            $healthReady = $ready
            break
        }
    }
    catch {
        if ($attempt -eq $MaxAttempts) {
            Write-Error 'Staging health checks did not succeed within the bounded retry limit.'
            exit 1
        }
    }

    if ($attempt -lt $MaxAttempts -and $RetryDelaySeconds -gt 0) {
        Start-Sleep -Seconds $RetryDelaySeconds
    }
}

if ($null -eq $healthReady) {
    Write-Error 'Staging health is unhealthy or does not match the candidate release version.'
    exit 1
}

$loginBody = @{
    identifier = $identifier
    password = $password
    clientId = $clientId
} | ConvertTo-Json -Compress
try {
    $authentication = Invoke-RestMethod `
        -Uri "$baseUri/api/authentication/login" `
        -Method Post `
        -ContentType 'application/json' `
        -Body $loginBody `
        -TimeoutSec 30
}
catch {
    Write-Error 'Staging synthetic-account login failed.'
    exit 1
}
finally {
    $loginBody = $null
}

if ([string]::IsNullOrWhiteSpace($authentication.AccessToken) -or
    $authentication.TokenType -cne 'Bearer') {
    Write-Error 'Staging login response did not contain a bearer token.'
    exit 1
}

try {
    $tokenSegments = $authentication.AccessToken.Split('.')
    if ($tokenSegments.Length -ne 3) {
        throw 'Invalid token format.'
    }
    $payloadSegment = $tokenSegments[1].Replace('-', '+').Replace('_', '/')
    switch ($payloadSegment.Length % 4) {
        2 { $payloadSegment += '==' }
        3 { $payloadSegment += '=' }
        1 { throw 'Invalid token payload.' }
    }
    $payloadJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payloadSegment))
    $tokenPayload = $payloadJson | ConvertFrom-Json
    if ($tokenPayload.tenant -cne $expectedTenant) {
        throw 'Token tenant mismatch.'
    }
}
catch {
    $authentication = $null
    Write-Error 'Staging login token is malformed or belongs to another tenant.'
    exit 1
}

$authorizationHeaders = @{
    Authorization = "$($authentication.TokenType) $($authentication.AccessToken)"
}
$authentication = $null
try {
    $roles = @(Invoke-RestMethod `
        -Uri "$baseUri/api/roles" `
        -Method Get `
        -Headers $authorizationHeaders `
        -TimeoutSec 30)
}
catch {
    $authorizationHeaders = $null
    Write-Error 'Staging authenticated role lookup failed.'
    exit 1
}

$authorizationHeaders = $null
if ($roles.Count -eq 0) {
    Write-Error 'Staging authenticated role lookup returned no roles.'
    exit 1
}

[pscustomobject]@{
    result = 'staging smoke passed'
    host = $stagingHost
    version = $ExpectedVersion.ToLowerInvariant()
    health = 'healthy'
    authentication = 'passed'
    roles_count = $roles.Count
} | ConvertTo-Json -Compress
