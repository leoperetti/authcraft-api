[CmdletBinding()]
param(
    [string]$InventoryPath = (Join-Path $PSScriptRoot 'environments.json')
)

$ErrorActionPreference = 'Stop'
$inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
$staging = @($inventory.environments | Where-Object { $_.name -ceq 'staging' })
if ($staging.Count -ne 1 -or
    [string]::IsNullOrWhiteSpace($staging[0].publisher_client_id) -or
    [string]::IsNullOrWhiteSpace($staging[0].publisher_principal_id)) {
    throw 'Staging publisher metadata is missing or ambiguous.'
}
$expected = $staging[0]

function Get-MetadataJson {
    param(
        [string]$Command,
        [string[]]$Arguments
    )

    $output = & $Command @Arguments 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace(($output -join "`n"))) {
        throw "Required Staging preflight metadata is unavailable from $Command."
    }
    try {
        return ($output -join "`n" | ConvertFrom-Json -ErrorAction Stop)
    } catch {
        throw "Required Staging preflight metadata from $Command is malformed."
    }
}

$resourceGroup = $inventory.resource_group
$app = Get-MetadataJson az @(
    'webapp', 'show', '--resource-group', $resourceGroup, '--name', $expected.application,
    '--query', '{id:id,name:name,state:state,host:defaultHostName,runtime_principal:identity.principalId}',
    '--output', 'json'
)
$settings = @(Get-MetadataJson az @(
    'webapp', 'config', 'appsettings', 'list', '--resource-group', $resourceGroup,
    '--name', $expected.application,
    '--query', "[?name=='ASPNETCORE_ENVIRONMENT' || name=='DOTNET_ENVIRONMENT' || name=='KeyVault__Name'].{name:name,value:value}",
    '--output', 'json'
))
$database = Get-MetadataJson az @(
    'sql', 'db', 'show', '--resource-group', $resourceGroup, '--server', $expected.database_server,
    '--name', $expected.database, '--query', '{id:id,name:name,status:status}', '--output', 'json'
)
$vault = Get-MetadataJson az @(
    'keyvault', 'show', '--resource-group', $resourceGroup, '--name', $expected.vault_name,
    '--query', '{id:id,name:name,rbac_enabled:properties.enableRbacAuthorization}', '--output', 'json'
)
$account = Get-MetadataJson az @(
    'account', 'show', '--query', '{subscription_id:id,user_type:user.type,user_name:user.name}', '--output', 'json'
)

$appScope = [string]$app.id
$databaseScope = [string]$database.id
$vaultScope = [string]$vault.id
$roleAssignmentsByScope = @{}
foreach ($resourceScope in @($appScope, $databaseScope, $vaultScope)) {
    if ([string]::IsNullOrWhiteSpace($resourceScope)) {
        throw 'A required Staging resource scope is missing.'
    }
    $roleAssignmentsByScope[$resourceScope] = @(Get-MetadataJson az @(
        'role', 'assignment', 'list', '--scope', $resourceScope, '--output', 'json'
    ))
}
$workflows = Get-MetadataJson gh @('api', "repos/$($inventory.repository)/actions/workflows")

$blockingReasons = [System.Collections.Generic.List[string]]::new()
$expectedSubscriptionId = [Regex]::Match($appScope, '(?i)^/subscriptions/([^/]+)/').Groups[1].Value
if ($account.subscription_id -cne $expectedSubscriptionId -or
    $account.user_type -cne 'servicePrincipal' -or
    $account.user_name -cne $expected.publisher_client_id) {
    $blockingReasons.Add('Azure OIDC session does not match the authorized Staging publisher and subscription.')
}
if ($app.name -cne $expected.application -or $app.state -cne 'Running' -or
    [string]::IsNullOrWhiteSpace($app.host)) {
    $blockingReasons.Add('Staging application identity, state, or host does not match the inventory.')
}
if ([string]::IsNullOrWhiteSpace($app.runtime_principal)) {
    $blockingReasons.Add('Staging runtime identity is missing.')
}
if ($database.name -cne $expected.database -or
    $databaseScope -notmatch "(?i)/servers/$([Regex]::Escape($expected.database_server))/databases/$([Regex]::Escape($expected.database))$") {
    $blockingReasons.Add('Staging database resource does not match the inventory.')
}
if ($vault.name -cne $expected.vault_name -or $vault.rbac_enabled -ne $true) {
    $blockingReasons.Add('Staging Key Vault is missing, crossed, or not using the expected RBAC mode.')
}
$runtimeEnvironment = @($settings | Where-Object { $_.name -ceq 'ASPNETCORE_ENVIRONMENT' })
$dotnetEnvironment = @($settings | Where-Object { $_.name -ceq 'DOTNET_ENVIRONMENT' })
$vaultSetting = @($settings | Where-Object { $_.name -ceq 'KeyVault__Name' })
if ($runtimeEnvironment.Count -ne 1 -or $runtimeEnvironment[0].value -cne $expected.runtime_environment -or
    ($dotnetEnvironment.Count -gt 1) -or
    ($dotnetEnvironment.Count -eq 1 -and $dotnetEnvironment[0].value -cne $expected.runtime_environment) -or
    $vaultSetting.Count -ne 1 -or $vaultSetting[0].value -cne $expected.vault_name) {
    $blockingReasons.Add('Staging runtime environment or vault configuration is missing or crossed.')
}

$expectedPublisherRoles = @{}
$expectedPublisherRoles[$appScope] = @('Reader', 'Website Contributor')
$expectedPublisherRoles[$databaseScope] = @('Reader')
$expectedPublisherRoles[$vaultScope] = @('Reader')
foreach ($resourceScope in $expectedPublisherRoles.Keys) {
    $publisherRoles = @($roleAssignmentsByScope[$resourceScope] | Where-Object {
        $_.principalId -ceq $expected.publisher_principal_id -and
        $_.scope.TrimEnd('/') -ieq $resourceScope.TrimEnd('/')
    } | ForEach-Object { [string]$_.roleDefinitionName } | Sort-Object -Unique)
    $expectedRoles = @($expectedPublisherRoles[$resourceScope] | Sort-Object -Unique)
    if (Compare-Object -ReferenceObject $expectedRoles -DifferenceObject $publisherRoles) {
        $blockingReasons.Add('Staging publisher ARM roles are missing, unexpected, or outside the exact resource scopes.')
    }
}
$runtimeVaultRoles = @($roleAssignmentsByScope[$vaultScope] | Where-Object {
    $_.principalId -ceq $app.runtime_principal -and
    $_.scope.TrimEnd('/') -ieq $vaultScope.TrimEnd('/')
} | ForEach-Object { [string]$_.roleDefinitionName } | Sort-Object -Unique)
if ('Key Vault Secrets User' -notin $runtimeVaultRoles) {
    $blockingReasons.Add('Staging runtime does not have the expected direct Key Vault Secrets User assignment.')
}

$productionWorkflows = @($workflows.workflows | Where-Object {
    $_.path -ceq $inventory.production_workflow_path
})
if ($productionWorkflows.Count -ne 1 -or $productionWorkflows[0].state -cne 'disabled_manually') {
    $blockingReasons.Add('The known Production workflow is not verifiably disabled.')
}

$state = 'confirmed'
if ($blockingReasons.Count -gt 0) { $state = 'blocked' }
[ordered]@{
    source = 'live_metadata'
    checked_at = [DateTime]::UtcNow.ToString('o')
    verification_state = $state
    target_environment = 'staging'
    application = $expected.application
    database = $expected.database
    vault_name = $expected.vault_name
    publisher_identity = $expected.publisher
    publisher_roles_verified = ($blockingReasons -notcontains 'Staging publisher ARM roles are missing, unexpected, or outside the exact resource scopes.')
    production_workflow_disabled = ($productionWorkflows.Count -eq 1 -and $productionWorkflows[0].state -ceq 'disabled_manually')
    blocking_reasons = @($blockingReasons.ToArray())
} | ConvertTo-Json -Depth 5 -Compress
if ($blockingReasons.Count -gt 0) { exit 1 }
exit 0
