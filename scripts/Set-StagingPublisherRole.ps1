[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Grant', 'Revoke')]
    [string]$Mode
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Data
$accountOutput = az account show --output json
if ($LASTEXITCODE -ne 0) { throw 'Unable to verify the permission manager Azure session.' }
$account = $accountOutput | ConvertFrom-Json
if ($account.id -cne '948a8f49-fd90-40d9-8fa9-12f722768a7a' -or
    $account.tenantId -cne 'b7bd0c07-88dc-4c47-81ba-608fd7607a82' -or
    $account.user.type -cne 'servicePrincipal' -or
    $account.user.name -cne 'c376b9f5-2b0b-406f-8579-ef8787001115') {
    throw 'Only the reviewed Staging permission manager may manage this role.'
}

$guard = @'
IF DB_NAME() <> N'authcraft-db-staging' THROW 51000, 'Unexpected database.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.database_principals
    WHERE principal_id = USER_ID() AND type = 'E'
      AND sid = CONVERT(binary(16), CONVERT(uniqueidentifier, 'c376b9f5-2b0b-406f-8579-ef8787001115')))
    THROW 51000, 'Unexpected contained permission manager identity.', 1;
IF NOT EXISTS (
    SELECT 1 FROM sys.database_principals
    WHERE name = N'authcraft-staging-schema-publisher' AND type = 'E'
      AND sid = CONVERT(binary(16), CONVERT(uniqueidentifier, 'a2382f63-d653-47f3-82ca-9f2696f63f0a')))
    THROW 51000, 'Unexpected contained publisher identity.', 1;
'@
$permissionQuery = @'
EXECUTE AS USER = N'authcraft-staging-schema-publisher';
SELECT IS_ROLEMEMBER(N'db_owner') AS is_db_owner,
       HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CONNECT') AS can_connect,
       HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'VIEW DEFINITION') AS can_view_definition,
       HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER') AS can_alter,
       HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CONTROL') AS can_control,
       HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CREATE TABLE') AS can_create_table,
       HAS_PERMS_BY_NAME(N'dbo', N'SCHEMA', N'ALTER') AS can_alter_schema,
       HAS_PERMS_BY_NAME(N'dbo.providers', N'OBJECT', N'SELECT') AS can_read_providers,
       HAS_PERMS_BY_NAME(N'dbo.providers', N'OBJECT', N'INSERT') AS can_insert_providers;
REVERT;
'@
function Invoke-PermissionSql([string]$Sql) {
    $connection = New-Object System.Data.SqlClient.SqlConnection 'Server=tcp:authcraftdbbrasil.database.windows.net,1433;Database=authcraft-db-staging;Encrypt=True;TrustServerCertificate=False;Connection Timeout=45;Application Name=AuthCraftStagingPermissionWindow'
    try {
        $connection.AccessToken = $script:permissionToken
        $connection.Open()
        $command = $connection.CreateCommand()
        try {
            $command.CommandTimeout = 30
            $command.CommandText = "SET NOCOUNT ON;`n$guard`n$Sql"
            $reader = $command.ExecuteReader()
            try {
                if ($reader.Read()) {
                    $permissions = [ordered]@{}
                    for ($columnIndex = 0; $columnIndex -lt $reader.FieldCount; $columnIndex++) {
                        $permissions[$reader.GetName($columnIndex)] = $reader.GetValue($columnIndex)
                    }
                    return $permissions
                }
            } finally { $reader.Dispose() }
        } finally { $command.Dispose() }
    } finally { $connection.Dispose() }
}
function Assert-PermissionState($Permissions, [bool]$Elevated) {
    if ($null -eq $Permissions) { throw 'No effective permission evidence returned.' }
    foreach ($permissionName in $Permissions.Keys) {
        $expected = 0
        if ($Elevated -or $permissionName -in @('can_connect', 'can_view_definition')) { $expected = 1 }
        if ($Permissions[$permissionName] -ne $expected) {
            throw "Unexpected effective permission: $permissionName. Stop deployment."
        }
    }
}
$script:permissionToken = $null
try {
    $script:permissionToken = az account get-access-token --resource https://database.windows.net/ --query accessToken --output tsv
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($script:permissionToken)) {
        throw 'Unable to obtain the permission manager SQL token.'
    }
    if ($Mode -eq 'Grant') {
        Assert-PermissionState (Invoke-PermissionSql $permissionQuery) $false
        try {
            $null = Invoke-PermissionSql @'
IF ISNULL(IS_ROLEMEMBER(N'db_owner', N'authcraft-staging-schema-publisher'), -1) <> 0
    THROW 51000, 'Publisher role changed before grant.', 1;
ALTER ROLE [db_owner] ADD MEMBER [authcraft-staging-schema-publisher];
'@
            Assert-PermissionState (Invoke-PermissionSql $permissionQuery) $true
        } catch {
            $null = Invoke-PermissionSql "IF IS_ROLEMEMBER(N'db_owner', N'authcraft-staging-schema-publisher') = 1 ALTER ROLE [db_owner] DROP MEMBER [authcraft-staging-schema-publisher];"
            Assert-PermissionState (Invoke-PermissionSql $permissionQuery) $false
            throw
        }
    } else {
        $null = Invoke-PermissionSql "IF IS_ROLEMEMBER(N'db_owner', N'authcraft-staging-schema-publisher') IS NULL THROW 51000, 'Unknown role membership.', 1; IF IS_ROLEMEMBER(N'db_owner', N'authcraft-staging-schema-publisher') = 1 ALTER ROLE [db_owner] DROP MEMBER [authcraft-staging-schema-publisher];"
        Assert-PermissionState (Invoke-PermissionSql $permissionQuery) $false
    }
    [pscustomobject]@{
        mode = $Mode
        target_database = 'authcraft-db-staging'
        verified = $true
        verified_at = [DateTimeOffset]::UtcNow.ToString('o')
    } | ConvertTo-Json -Compress
} finally { $script:permissionToken = $null }
