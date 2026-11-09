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
        [string]$ScriptSha256,
        [string]$StagingBaselineSha
    )

    if ($Author -cne $AllowedReviewer) {
        return $false
    }
    if ($StagingBaselineSha -cne 'unknown' -and $StagingBaselineSha -notmatch '^[A-Fa-f0-9]{40}$') {
        return $false
    }

    $expectedFields = [ordered]@{
        deployment_review = 'approved'
        run_id = $RunId
        commit_sha = $CommitSha.ToLowerInvariant()
        staging_baseline_sha = $StagingBaselineSha.ToLowerInvariant()
        target_environment = $TargetEnvironment
        database_server = $DatabaseServer
        database_name = $DatabaseName
        dacpac_sha256 = $DacpacSha256.ToLowerInvariant()
        api_compatibility = 'accepted'
        data_impact = 'accepted'
        recovery_plan = 'accepted'
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
