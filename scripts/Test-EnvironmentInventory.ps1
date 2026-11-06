param(
    [string]$InventoryPath = (Join-Path $PSScriptRoot 'environments.json'),
    [string]$SnapshotPath
)

$ErrorActionPreference = 'Stop'
$inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
$expectedNames = @('development', 'staging', 'production')
if (@($inventory.environments).Count -ne 3 -or
    @($inventory.environments.name | Sort-Object -Unique).Count -ne 3 -or
    @($inventory.environments.name | Where-Object { $_ -notin $expectedNames }).Count -gt 0) {
    Write-Error 'Inventory must contain exactly development, staging and production.'
    exit 2
}
foreach ($resourceKind in @('application', 'database', 'vault_name')) {
    $assignments = @($inventory.environments | Where-Object { $_.name -ne 'development' } |
        ForEach-Object { $_.$resourceKind })
    if (@($assignments | Sort-Object -Unique).Count -ne 2 -or
        @($assignments | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
        Write-Error "Inventory contains missing or shared $resourceKind assignments."
        exit 2
    }
}

function Invoke-MetadataJson {
    param([string]$Command, [string[]]$Arguments)
    $output = & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Metadata command failed: $Command. No resource has been verified."
    }
    if ([string]::IsNullOrWhiteSpace(($output -join "`n"))) {
        throw "Metadata command returned no document: $Command."
    }
    return ($output -join "`n" | ConvertFrom-Json)
}

if ($SnapshotPath) {
    $metadata = Get-Content -LiteralPath $SnapshotPath -Raw | ConvertFrom-Json
    $source = 'snapshot'
} else {
    $source = 'live_metadata'
    $apps = @(Invoke-MetadataJson az @('webapp', 'list', '--resource-group', $inventory.resource_group, '--output', 'json'))
    $identities = @(Invoke-MetadataJson az @('identity', 'list', '--resource-group', $inventory.resource_group, '--output', 'json'))
    $vaults = @(Invoke-MetadataJson az @('keyvault', 'list', '--resource-group', $inventory.resource_group, '--output', 'json'))
    $roles = @(Invoke-MetadataJson az @('role', 'assignment', 'list', '--all', '--output', 'json'))
    $workflows = Invoke-MetadataJson gh @('api', "repos/$($inventory.repository)/actions/workflows")
    $observed = [ordered]@{}
    foreach ($expected in $inventory.environments | Where-Object { $_.name -ne 'development' }) {
        $app = $apps | Where-Object { $_.name -eq $expected.application } | Select-Object -First 1
        $databases = @(Invoke-MetadataJson az @('sql', 'db', 'list', '--resource-group', $inventory.resource_group,
            '--server', $expected.database_server, '--output', 'json'))
        $settings = @()
        if ($app) {
            $settings = @(Invoke-MetadataJson az @('webapp', 'config', 'appsettings', 'list', '--resource-group',
                $inventory.resource_group, '--name', $expected.application,
                '--query', "[?name=='ASPNETCORE_ENVIRONMENT' || name=='DOTNET_ENVIRONMENT' || name=='KeyVault__Name'].{name:name,value:value}", '--output', 'json'))
        }
        $runtimePrincipal = $app.identity.principalId
        $publisher = $identities | Where-Object { $_.name -eq $expected.publisher } | Select-Object -First 1
        $runtimeVaults = @($roles | Where-Object {
            $_.principalId -eq $runtimePrincipal -and $_.roleDefinitionName -eq 'Key Vault Secrets User'
        } | ForEach-Object { ($_.scope -split '/')[-1] })
        $publisherScopes = @($roles | Where-Object {
            $_.principalId -eq $publisher.principalId -and
            $_.roleDefinitionName -in @('Website Contributor', 'Contributor', 'Owner')
        } | ForEach-Object { ($_.scope -split '/')[-1] })
        $runtimeSetting = $settings | Where-Object { $_.name -eq 'ASPNETCORE_ENVIRONMENT' } | Select-Object -First 1
        $dotnetSetting = $settings | Where-Object { $_.name -eq 'DOTNET_ENVIRONMENT' } | Select-Object -First 1
        $vaultSetting = $settings | Where-Object { $_.name -eq 'KeyVault__Name' } | Select-Object -First 1
        $database = $databases | Where-Object { $_.name -eq $expected.database } | Select-Object -First 1
        $observed[$expected.name] = @{
            application = $app.name
            database = $database.name
            database_server = $expected.database_server
            database_state = $database.status
            vault_name = $vaultSetting.value
            vault_exists = [bool]($vaults | Where-Object { $_.name -eq $expected.vault_name })
            runtime_environment = $runtimeSetting.value
            dotnet_environment = $dotnetSetting.value
            runtime_principal = $runtimePrincipal
            runtime_vaults = $runtimeVaults
            publisher_scopes = $publisherScopes
            host = $app.defaultHostName
            shared_plan = $app.serverFarmId
        }
    }
    $identityAudit = @()
    foreach ($identity in $identities) {
        $federations = @(Invoke-MetadataJson az @('identity', 'federated-credential', 'list',
            '--identity-name', $identity.name, '--resource-group', $inventory.resource_group, '--output', 'json'))
        $identityAudit += @{
            name = $identity.name
            federated_subjects = @($federations.subject)
        }
    }
    $metadata = [pscustomobject]@{
        environments = [pscustomobject]$observed
        identities = $identityAudit
        metadata_errors = @()
        production_workflow_active = [bool]($workflows.workflows | Where-Object {
            $_.path -eq $inventory.production_workflow_path -and $_.state -eq 'active'
        })
    }
}

$blockingReasons = [System.Collections.Generic.List[string]]::new()
foreach ($metadataError in $metadata.metadata_errors) {
    $blockingReasons.Add([string]$metadataError)
}
$results = @()
foreach ($expected in $inventory.environments) {
    $reasons = [System.Collections.Generic.List[string]]::new()
    $actual = $metadata.environments.($expected.name)
    if ($expected.name -ne 'development') {
        foreach ($field in @('application', 'database', 'database_server', 'vault_name', 'runtime_environment')) {
            if ($actual.$field -ne $expected.$field) {
                $reasons.Add("$($expected.name): $field missing or assigned to another environment.")
            }
        }
        if ([string]::IsNullOrWhiteSpace($actual.runtime_principal)) {
            $reasons.Add("$($expected.name): runtime identity is missing.")
        }
        if ($actual.vault_exists -eq $false) {
            $reasons.Add("$($expected.name): expected vault resource is missing.")
        }
        if ([string]::IsNullOrWhiteSpace($actual.host)) {
            $reasons.Add("$($expected.name): application hostname is missing.")
        }
        if ($actual.dotnet_environment -and $actual.dotnet_environment -ne $expected.runtime_environment) {
            $reasons.Add("$($expected.name): DOTNET_ENVIRONMENT contradicts the expected environment.")
        }
        if ($expected.vault_name -notin @($actual.runtime_vaults)) {
            $reasons.Add("$($expected.name): exclusive runtime vault access is not confirmed.")
        }
        if (@($actual.runtime_vaults | Where-Object { $_ -ne $expected.vault_name }).Count -gt 0) {
            $reasons.Add("$($expected.name): runtime has access to another vault.")
        }
        if (@($actual.publisher_scopes | Where-Object { $_ -ne $expected.application }).Count -gt 0) {
            $reasons.Add("$($expected.name): publisher has a scope outside its application.")
        }
        if ($expected.name -eq 'staging' -and $expected.application -notin @($actual.publisher_scopes)) {
            $reasons.Add('staging: application publisher permission is missing.')
        }
        if ($expected.name -eq 'production' -and @($actual.publisher_scopes).Count -gt 0) {
            $reasons.Add('production: publisher permissions remain; publication protection is not confirmed.')
        }
    }
    foreach ($reason in $reasons) { $blockingReasons.Add($reason) }
    $state = 'confirmed'
    if ($expected.name -eq 'development') { $state = 'pending' }
    if ($reasons.Count -gt 0) { $state = 'blocked' }
    $results += [ordered]@{
        name = $expected.name
        verification_state = $state
        evidence_reference = $source
        observed = $actual
        blocking_reasons = @($reasons.ToArray())
        database_permissions = @{
            verification_state = 'pending'
            blocking_reason = 'SQL permissions and effective connection have not been queried by this metadata verifier.'
        }
        functionality = @{
            verification_state = 'pending'
            blocking_reason = 'Resource existence does not verify application health or authentication.'
        }
    }
}
if ($metadata.production_workflow_active) {
    $blockingReasons.Add('production: existing workflow is active; its publication controls require review before activation.')
}
$audit = @($metadata.identities | ForEach-Object {
    @{
        name = $_.name
        federated_subjects = @($_.federated_subjects)
        verification_state = 'pending'
        blocking_reason = 'Federation metadata does not identify actual workflow consumers.'
    }
})
$overall = 'pending'
if ($blockingReasons.Count -gt 0) { $overall = 'blocked' }
[ordered]@{
    source = $source
    checked_at = [DateTime]::UtcNow.ToString('o')
    verification_state = $overall
    environments = $results
    identity_audit = $audit
    blocking_reasons = @($blockingReasons.ToArray())
} | ConvertTo-Json -Depth 12
if ($blockingReasons.Count -gt 0) { exit 1 }
exit 0
