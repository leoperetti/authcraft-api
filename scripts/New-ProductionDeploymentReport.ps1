[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$stageOrder = @(
    'preflight',
    'schema_review',
    'approval',
    'schema_publish',
    'application_publish',
    'health_check',
    'authentication_check'
)

$stageLabels = @{
    preflight = 'Validación previa'
    schema_review = 'Revisión del esquema'
    approval = 'Aprobación protegida'
    schema_publish = 'Publicación del esquema'
    application_publish = 'Publicación de la API'
    health_check = 'Comprobación de salud'
    authentication_check = 'Comprobación de autenticación'
}

$failureStages = @{
    preflight_blocked = @('preflight')
    previous_package_incompatible = @('preflight')
    evidence_unavailable = @('preflight', 'schema_review')
    permission_denied = $stageOrder
    timeout = $stageOrder
    schema_review_failed = @('schema_review')
    approval_pending = @('approval')
    approval_rejected = @('approval')
    schema_publish_failed = @('schema_publish')
    application_publish_failed = @('application_publish')
    health_check_failed = @('health_check')
    authentication_check_failed = @('authentication_check')
}

$failureMessages = @{
    preflight_blocked = @{
        cause = 'La validación previa bloqueó la publicación.'
        next = 'Revisa la referencia, el destino y el release antes de reintentar.'
    }
    previous_package_incompatible = @{
        cause = 'El paquete de API anterior no es compatible con el esquema resultante.'
        next = 'No publiques una versión incompatible; define una transición compatible y revisa la recuperación.'
    }
    evidence_unavailable = @{
        cause = 'Falta evidencia necesaria para continuar.'
        next = 'Recupera o vuelve a generar la evidencia del mismo release y destino.'
    }
    permission_denied = @{
        cause = 'La identidad no tiene el permiso requerido.'
        next = 'Solicita una revisión de permisos antes de reintentar.'
    }
    timeout = @{
        cause = 'Una etapa superó el tiempo de espera.'
        next = 'Verifica el estado del destino y la etapa antes de iniciar otro intento.'
    }
    schema_review_failed = @{
        cause = 'La revisión del esquema no pasó.'
        next = 'Revisa el DeployReport y el SQL generado antes de solicitar una nueva revisión.'
    }
    approval_pending = @{
        cause = 'La aprobación protegida está pendiente.'
        next = 'Completa o rechaza la revisión protegida del release exacto.'
    }
    approval_rejected = @{
        cause = 'La aprobación protegida fue rechazada.'
        next = 'Resuelve los motivos de rechazo y solicita una revisión nueva del release.'
    }
    schema_publish_failed = @{
        cause = 'Falló la publicación del esquema.'
        next = 'Detén las etapas siguientes y sigue el procedimiento de recuperación.'
    }
    application_publish_failed = @{
        cause = 'Falló la publicación de la aplicación; el esquema pudo quedar aplicado.'
        next = 'Detén las etapas siguientes y verifica la compatibilidad del paquete anterior antes de recuperar.'
    }
    health_check_failed = @{
        cause = 'Falló la comprobación de salud.'
        next = 'Detén la promoción y revisa el estado de la aplicación y del esquema.'
    }
    authentication_check_failed = @{
        cause = 'Falló la comprobación de autenticación.'
        next = 'Detén la promoción y revisa la configuración efectiva sin exponer secretos.'
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string[]]$Expected
    )

    if ($null -eq $Value -or $Value -isnot [pscustomobject]) {
        throw 'El informe de entrada no tiene la estructura requerida.'
    }

    $actual = @($Value.PSObject.Properties.Name)
    if ($actual.Count -ne $Expected.Count) {
        throw 'El informe de entrada contiene campos faltantes o no permitidos.'
    }

    foreach ($name in $Expected) {
        if ($name -notin $actual) {
            throw 'El informe de entrada contiene campos faltantes o no permitidos.'
        }
    }
}

function Assert-SafeString {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Value,

        [Parameter(Mandatory = $true)]
        [string]$Pattern
    )

    if ($Value -isnot [string] -or $Value -notmatch $Pattern) {
        throw 'El informe contiene un valor fuera del formato permitido.'
    }
}

function Get-StatusLabel {
    param([string]$Status)

    switch ($Status) {
        'succeeded' { return 'exitoso' }
        'failed' { return 'fallido' }
        'blocked' { return 'bloqueado' }
        'not_executed' { return 'no ejecutado' }
        default { throw 'El informe contiene un estado de etapa no permitido.' }
    }
}

function Get-ApprovalLabel {
    param([string]$Status)

    switch ($Status) {
        'approved' { return 'aprobada' }
        'pending' { return 'pendiente' }
        'rejected' { return 'rechazada' }
        'not_required' { return 'no requerida' }
        default { throw 'El estado de aprobación no está permitido.' }
    }
}

function Get-EvidenceLabel {
    param([string]$Status)

    switch ($Status) {
        'available' { return 'disponible' }
        'unavailable' { return 'no disponible' }
        'not_generated' { return 'no generado' }
        default { throw 'El estado de la evidencia SQL no está permitido.' }
    }
}

if (-not (Test-Path -LiteralPath $InputPath -PathType Leaf)) {
    throw 'No se encontró el archivo de entrada del informe.'
}

if ([System.IO.Path]::GetFileName($OutputPath) -cne 'production-deployment-report.md') {
    throw 'El archivo de salida debe llamarse production-deployment-report.md.'
}

if (Test-Path -LiteralPath $OutputPath) {
    throw 'El archivo de salida ya existe; no se sobrescribió.'
}

$inputItem = Get-Item -LiteralPath $InputPath
if ($inputItem.Length -gt 65536) {
    throw 'El archivo de entrada excede el tamaño permitido.'
}

$inputJson = Get-Content -LiteralPath $InputPath -Raw
try {
    $reportData = ConvertFrom-Json -InputObject $inputJson -ErrorAction Stop
}
catch {
    throw 'El archivo de entrada no contiene JSON válido.'
}

Assert-ExactProperties -Value $reportData -Expected @(
    'schema_version',
    'run_id',
    'commit_sha',
    'release',
    'target_environment',
    'started_at',
    'finished_at',
    'approval',
    'evidence',
    'stages'
)

if ($reportData.schema_version -ne 1 -or $reportData.schema_version -is [bool]) {
    throw 'La versión del esquema del informe no está permitida.'
}

Assert-SafeString -Value $reportData.run_id -Pattern '^[0-9]{1,20}$'
Assert-SafeString -Value $reportData.commit_sha -Pattern '^(?:[0-9a-f]{40}|[0-9a-f]{64})$'
Assert-SafeString -Value $reportData.release -Pattern '^[A-Za-z0-9][A-Za-z0-9._-]{0,79}$'

if ($reportData.target_environment -cne 'production') {
    throw 'El destino del informe debe ser production.'
}

$timestampPattern = '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})$'
Assert-SafeString -Value $reportData.started_at -Pattern $timestampPattern
Assert-SafeString -Value $reportData.finished_at -Pattern $timestampPattern

$startedAt = [DateTimeOffset]::Parse(
    $reportData.started_at,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind
)
$finishedAt = [DateTimeOffset]::Parse(
    $reportData.finished_at,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::RoundtripKind
)
if ($finishedAt -lt $startedAt) {
    throw 'La fecha de finalización es anterior al inicio.'
}

Assert-ExactProperties -Value $reportData.approval -Expected @('status', 'reference')
if ($reportData.approval.status -notin @('approved', 'pending', 'rejected', 'not_required')) {
    throw 'El estado de aprobación no está permitido.'
}
if ($null -ne $reportData.approval.reference) {
    Assert-SafeString -Value $reportData.approval.reference -Pattern '^[A-Za-z0-9_-]{1,64}$'
}
if ($reportData.approval.status -eq 'approved' -and $null -eq $reportData.approval.reference) {
    throw 'La aprobación requiere una referencia protegida.'
}

Assert-ExactProperties -Value $reportData.evidence -Expected @('deploy_report', 'deployment_script')
$evidenceStatuses = @{}
foreach ($evidenceName in @('deploy_report', 'deployment_script')) {
    $evidenceItem = $reportData.evidence.$evidenceName
    Assert-ExactProperties -Value $evidenceItem -Expected @('status', 'sha256')
    if ($evidenceItem.status -notin @('available', 'unavailable', 'not_generated')) {
        throw 'El estado de la evidencia SQL no está permitido.'
    }

    if ($evidenceItem.status -eq 'available') {
        Assert-SafeString -Value $evidenceItem.sha256 -Pattern '^[0-9a-f]{64}$'
    }
    elseif ($null -ne $evidenceItem.sha256) {
        throw 'La evidencia no disponible no puede incluir un hash.'
    }

    $evidenceStatuses[$evidenceName] = $evidenceItem
}

if ($reportData.stages -isnot [System.Array]) {
    throw 'Las etapas del informe deben ser una lista.'
}

$reportedStages = @{}
foreach ($stage in $reportData.stages) {
    Assert-ExactProperties -Value $stage -Expected @('name', 'status', 'failure_code')
    if ($stage.name -notin $stageOrder -or $reportedStages.ContainsKey($stage.name)) {
        throw 'El informe contiene una etapa desconocida o duplicada.'
    }
    if ($stage.status -notin @('succeeded', 'failed', 'blocked', 'not_executed')) {
        throw 'El informe contiene un estado de etapa no permitido.'
    }

    if ($stage.status -in @('failed', 'blocked')) {
        if ($stage.failure_code -isnot [string] -or -not $failureMessages.ContainsKey($stage.failure_code)) {
            throw 'La causa del fallo no está en la lista permitida.'
        }
        if ($stage.name -notin $failureStages[$stage.failure_code]) {
            throw 'La causa del fallo no corresponde a la etapa indicada.'
        }
    }
    elseif ($null -ne $stage.failure_code) {
        throw 'Una etapa sin fallo no puede incluir una causa.'
    }

    $reportedStages[$stage.name] = $stage
}

$orderedStages = [System.Collections.Generic.List[object]]::new()
$blockedByPriorStage = $false
foreach ($stageName in $stageOrder) {
    if ($reportedStages.ContainsKey($stageName)) {
        $stage = $reportedStages[$stageName]
    }
    else {
        $stage = [pscustomobject]@{
            name = $stageName
            status = 'not_executed'
            failure_code = $null
        }
    }

    if ($blockedByPriorStage -and $stage.status -cne 'not_executed') {
        throw 'Una etapa posterior a un bloqueo debe constar como no ejecutada.'
    }

    $orderedStages.Add($stage)
    if ($stage.status -in @('failed', 'blocked', 'not_executed')) {
        $blockedByPriorStage = $true
    }
}

$failedStage = $orderedStages | Where-Object { $_.status -in @('failed', 'blocked') } | Select-Object -First 1
$allSucceeded = @($orderedStages | Where-Object { $_.status -cne 'succeeded' }).Count -eq 0
if ($null -ne $failedStage) {
    if ($failedStage.status -eq 'failed') {
        $overallResult = 'Fallido'
    }
    else {
        $overallResult = 'Bloqueado'
    }
}
elseif ($allSucceeded) {
    $overallResult = 'Exitoso'
}
else {
    $overallResult = 'Incompleto'
}

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('# Informe de intento de publicación Production')
$lines.Add('')
$lines.Add("Resultado general: $overallResult")
$lines.Add('')
$lines.Add('## Identificación')
$lines.Add('')
$lines.Add("| Run | Commit | Release | Destino | Inicio (UTC) | Fin (UTC) |")
$lines.Add('|---|---|---|---|---|---|')
$lines.Add("| $($reportData.run_id) | $($reportData.commit_sha) | $($reportData.release) | production | $($startedAt.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ssZ')) | $($finishedAt.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ssZ')) |")
$lines.Add('')
$lines.Add('## Aprobación y evidencia SQL')
$lines.Add('')
$lines.Add("| Control | Resultado | SHA-256 |")
$lines.Add('|---|---|---|')
$approvalReference = if ($null -eq $reportData.approval.reference) { 'no disponible' } else { $reportData.approval.reference }
$approvalLabel = Get-ApprovalLabel -Status $reportData.approval.status
$lines.Add("| Aprobación protegida | $approvalLabel (referencia: $approvalReference) | - |")
foreach ($evidenceName in @('deploy_report', 'deployment_script')) {
    $evidenceItem = $evidenceStatuses[$evidenceName]
    $evidenceLabel = if ($evidenceName -eq 'deploy_report') { 'DeployReport' } else { 'Script SQL' }
    $evidenceStatusLabel = Get-EvidenceLabel -Status $evidenceItem.status
    $evidenceHash = if ($evidenceItem.status -eq 'available') { $evidenceItem.sha256 } else { '-' }
    $lines.Add("| $evidenceLabel | $evidenceStatusLabel | $evidenceHash |")
}
$lines.Add('')
$lines.Add('## Resultado por etapa')
$lines.Add('')
$lines.Add('| Etapa | Resultado |')
$lines.Add('|---|---|')
foreach ($stage in $orderedStages) {
    $stageLabel = Get-StatusLabel -Status $stage.status
    $lines.Add("| $($stageLabels[$stage.name]) ($($stage.name)) | $stageLabel |")
}

if ($null -ne $failedStage) {
    $failureMessage = $failureMessages[$failedStage.failure_code]
    $lines.Add('')
    $lines.Add('## Fallo detectado')
    $lines.Add('')
    $lines.Add("**Etapa**: $($stageLabels[$failedStage.name]) ($($failedStage.name))")
    $lines.Add('')
    $lines.Add("**Causa**: $($failureMessage.cause)")
    $lines.Add('')
    $lines.Add("**Siguiente paso**: $($failureMessage.next)")
}

$lines.Add('')
$lines.Add('Este informe registra resultados; no constituye aprobación ni autorización para publicar.')
$lines.Add('')

$outputDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($OutputPath))
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    throw 'No se encontró el directorio de salida del informe.'
}

$reportContent = [string]::Join([Environment]::NewLine, $lines)
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    $reportContent,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Output 'Se generó el informe de Production.'
