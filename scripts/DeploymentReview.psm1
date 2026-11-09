function Test-DeploymentReviewRecord {
    param(
        [string]$Body,
        [string]$Author,
        [string]$AllowedReviewer,
        [string]$RunId,
        [string]$CommitSha,
        [string]$TargetEnvironment,
        [string]$DatabaseServer,
        [string]$DatabaseName,
        [string]$DacpacSha256,
        [string]$ReportSha256,
        [string]$ScriptSha256
    )

    if ($Author -cne $AllowedReviewer) {
        return $false
    }

    $expectedFields = [ordered]@{
        deployment_review = 'approved'
        run_id = $RunId
        commit_sha = $CommitSha.ToLowerInvariant()
        target_environment = $TargetEnvironment
        database_server = $DatabaseServer
        database_name = $DatabaseName
        dacpac_sha256 = $DacpacSha256.ToLowerInvariant()
        block_on_possible_data_loss = 'true'
        drop_objects_not_in_source = 'false'
        script_database_options = 'false'
        deploy_report_sha256 = $ReportSha256.ToLowerInvariant()
        deploy_script_sha256 = $ScriptSha256.ToLowerInvariant()
    }

    foreach ($field in $expectedFields.GetEnumerator()) {
        $pattern = '(?im)^[ \t]*' + [Regex]::Escape($field.Key) + '[ \t]*=[ \t]*([^\r\n]*)\r?$'
        $entries = [Regex]::Matches($Body, $pattern)
        if ($entries.Count -ne 1 -or
            $entries[0].Groups[1].Value.Trim() -cne [string]$field.Value) {
            return $false
        }
    }

    return $true
}

function Test-LocalDeploymentScriptSafety {
    param([string]$ScriptContent)

    $destructiveStatement = '(?i)\bDROP\s+(?:DATABASE|TABLE|SCHEMA|COLUMN)\b|\bDELETE\s+FROM\b|\bTRUNCATE\s+TABLE\b'
    return -not [Regex]::IsMatch($ScriptContent, $destructiveStatement)
}

Export-ModuleMember -Function Test-DeploymentReviewRecord, Test-LocalDeploymentScriptSafety
