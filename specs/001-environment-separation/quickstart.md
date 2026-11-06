# Quickstart: Validar los ambientes

Guía operativa para Development y para los gates pendientes de Staging/Production.
Development fue validado en LocalDB; no se conectó Azure SQL durante esa validación.

## Prerrequisitos y límites

- PowerShell, Git Bash, .NET 10, MSBuild con SSDT y SqlPackage disponibles.
- SQL Server local/LocalDB y base local dedicada; credenciales sintéticas protegidas.
- Acceso autorizado a GitHub/Azure. No listar valores de secretos.
- Autorización explícita antes de modificar recursos o conectar Azure SQL pausado;
  una conexión puede reactivarlo y generar consumo.
- Production sigue bloqueado; no intentar publicaciones manuales alternativas.
- Consultar [contrato](contracts/environment-deployment.md) y
  [estados](data-model.md) para salidas, identidad y condiciones de bloqueo.

## 1. Revisar inventario, sin cambios

Desde la raíz del repositorio:

```powershell
az webapp list --resource-group AuthCraft --query "[].{name:name,state:state,host:defaultHostName}" --output json
az identity list --resource-group AuthCraft --query "[].{name:name,id:id}" --output json
az webapp identity show --resource-group AuthCraft --name authcraft-api --output json
az webapp identity show --resource-group AuthCraft --name authcraft-api-staging --output json
az sql db list --resource-group AuthCraft --server authcraftdbbrasil --query "[].{name:name,status:status}" --output json
gh api repos/leoperetti/authcraft-api/actions/workflows --jq '.workflows[] | {name,path,state}'
gh api repos/leoperetti/authcraft-api/environments --jq '{total_count,names:[.environments[].name]}'
```

Esperado: recursos confirmados, aplicaciones con identidades runtime distintas tras
implementación y sin workflows directos activos a production. Registrar discrepancias,
no resolverlas con cambios automáticos. Running no demuestra salud.

El 2026-11-06 se retiró con autorización la federación main de `oidc-msi-aede`.
Con autorización posterior también se retiraron sus roles Website Contributor y el
secreto GitHub `AZURE_SQL_CONNECTION_STRING`; ambas identidades se conservaron
adjuntas, sin roles ARM ni federaciones. Esto no revoca la credencial SQL original
ni acredita ausencia de otras copias: T029 sigue pendiente. No eliminar identidades
por inferencia ni reactivar el workflow anterior. Ver
[evidencia y límites](../../docs/environments.md#retiro-autorizado-de-permisos-publicadores-2026-11-06).

El inventario ahora verifica también publicadores adjuntos, roles runtime,
SCM/FTP y nombres de secretos de repositorio; necesita permisos para consultar
estos metadatos. Ausencia de acceso/evidencia bloquea, no equivale a controles seguros.
La lectura real de las 16:22 UTC siguió bloqueada por falta de ajustes explícitos
de ambiente/vault Production, no por roles publicadores residuales.

Comprobar el bloqueo local de Production sin conexiones remotas:

```powershell
dotnet test .\authcraft-api.Tests\authcraft-api.Tests.csproj --configuration Release --filter "FullyQualifiedName~ProductionApprovalTests|FullyQualifiedName~DeploymentPipelineTests|FullyQualifiedName~DatabaseDeploymentReviewTests|FullyQualifiedName~DeploymentRecoveryTests"
```

Estas pruebas acreditan el rechazo vigente, incluso ante dispatch o aprobación
aportada libremente; no acreditan una promoción protegida disponible.

Desde el 2026-11-06 existe en Staging el usuario externo
`authcraft-staging-schema-publisher`, vinculado al client ID del publicador.
Tiene CONNECT/VIEW DEFINITION y SELECT solo sobre
`sys.sql_expression_dependencies`: **no esta habilitado para Publish ni postdeploy**.
No sustituirlo por el token administrativo de auditoría. Ver
[permisos comprobados y límites](../../docs/environments.md#usuario-sql-del-publicador-evaluacion-minima-verificada).
Login OIDC real comprobado; T037 requiere todavia permisos DacFx suficientes
revisados/probados.

## 2. Construir y validar development

```powershell
dotnet build .\authcraft-api\authcraft-api.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Falló build API' }
msbuild .\authcraft-db\authcraft-db.sqlproj /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'Falló build SQL' }
Get-ChildItem .\authcraft-db\bin\Release -Filter *.dacpac
```

Configurar `DatabaseConnectionString` en User Secrets con una conexión local y
autenticación integrada. No almacenar una conexión Azure en Development. Las claves
JWT locales son `Jwt:Key`, `Jwt:Issuer` y `Jwt:Audience`; Google y correo son
opcionales. El bootstrap está desactivado por defecto. Solo habilitarlo en Development
con una cuenta sintética exclusiva y volver a desactivarlo al terminar.

Antes de usar un catálogo nuevo, comprobarlo y crearlo sin eliminar uno existente:

```powershell
$sqlcmd = Get-Command sqlcmd.exe -ErrorAction Stop
& $sqlcmd.Source -S '(localdb)\MSSQLLocalDB' -d master -b -Q "IF DB_ID(N'authcraft_development') IS NULL CREATE DATABASE [authcraft_development];"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar la base local' }
```

Evaluar y revisar ambos archivos generados fuera del repositorio. El inicializador
rechaza SQL que pueda borrar la base o sus datos, además de exigir los hashes
revisados y confirmar explícitamente el destino:

```powershell
$evidence = Join-Path $env:TEMP 'authcraft-local-sql'
.\scripts\Initialize-LocalEnvironment.ps1 -EvidenceDirectory $evidence
# Revisar local-deploy-report.xml y local-deploy.sql; copiar los dos SHA-256
# impresos y confirmar authcraft_development antes de publicar.
.\scripts\Initialize-LocalEnvironment.ps1 -EvidenceDirectory $evidence -Publish `
  -ReviewedReportSha256 '<hash revisado>' -ReviewedScriptSha256 '<hash revisado>' `
  -ConfirmedTargetDatabase 'authcraft_development'
if ($LASTEXITCODE -ne 0) { throw 'Falló publicación SQL local' }
```

El script selecciona exactamente un DACPAC de MSBuild y usa
`BlockOnPossibleDataLoss=True` / `DropObjectsNotInSource=False`. No usar
`EnsureCreated` ni migraciones EF para publicar el esquema de Development.

Iniciar la API desde el perfil HTTPS y verificar salud:

```powershell
dotnet run --project .\authcraft-api\authcraft-api.csproj --configuration Release --launch-profile https
$live = Invoke-RestMethod 'https://localhost:7202/health/live'
$ready = Invoke-RestMethod 'https://localhost:7202/health/ready'
if ($live.status -ne 'healthy' -or $ready.status -ne 'healthy') { throw 'Salud no válida' }
```

Resultado verificado: LocalDB `authcraft_development`, 13 tablas, los tres
proveedores iniciales y `/health/live` + `/health/ready` saludables. No se usó
Key Vault ni Azure SQL. Un smoke posterior creó una cuenta/tenant aleatorios
`example.test` solo en la base local, obtuvo 200 en login y GET roles (4 roles), y
no envió correo. No se leyeron ni cambiaron credenciales persistidas; el proceso
terminó con bootstrap desactivado. No reutilizar credenciales de otro ambiente.
Con las herramientas instaladas y User Secrets configurados, builds, 51 pruebas
focalizadas y smoke tardaron 0.87 minutos. Eliminar únicamente el directorio de
evidencia específico tras revisarlo.

## 3. Pruebas locales

```powershell
$env:AUTHCRAFT_TEST_SQL_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=authcraft_invitation_test_validation;Integrated Security=True;TrustServerCertificate=True'
dotnet test .\authcraft-api.Tests\authcraft-api.Tests.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Fallaron pruebas' }
```

El fixture existente usa una base descartable con prefijo
`authcraft_invitation_test_`; no apuntarlo a la base de development ni Azure.
Ejecutar las pruebas focalizadas de configuración/salud/promoción primero,
escalando según fallos. Para la prueba real del DACPAC, compilar primero el
`.sqlproj` y proporcionar por proceso
`AUTHCRAFT_LOCAL_ENVIRONMENT_SQL_CONNECTION` apuntando a `authcraft_development`.
La prueba crea, publica, verifica y elimina una base única
`authcraft_environment_test_<guid>` en LocalDB. El conjunto existente no reemplaza
la validación independiente del DACPAC.

Con la aplicación local activa:

```powershell
$live = Invoke-RestMethod 'https://localhost:7202/health/live'
$ready = Invoke-RestMethod 'https://localhost:7202/health/ready'
if ($live.status -ne 'healthy' -or $ready.status -ne 'healthy') { throw 'Salud no válida' }
```

Usar certificado local confiable; no desactivar validación TLS remota.
Para completar el smoke de negocio, habilitar bootstrap solo con Email/Password
sintéticos locales, ejecutar la API una vez y desactivarlo de inmediato. Enviar
`POST /api/authentication/login` con `identifier`, `password` y `clientId`; guardar
el `accessToken` solo en memoria y usarlo en `GET /api/roles`. Ambos deben responder
200. Nunca imprimir credenciales ni token, y no reutilizar valores de otro ambiente.
Medir preparación ≤30 minutos con prerrequisitos disponibles.

## 4. Staging, únicamente con autorización

Al 2026-11-06 el workflow staging remoto ya fue reemplazado con autorización por
la versión sin despliegue. El run
[42265429985](https://github.com/leoperetti/authcraft-api/actions/runs/42265429985)
pasó build/pruebas/paquetes y diagnóstico OIDC/SQL real con
`verify_staging_publisher=true`. No acredita un despliegue ni permisos Publish.
Los inputs remotos son diagnósticos explícitos, no aprobación de despliegue.

Con autorización se puede despachar en main con `verify_staging_publisher=true`.
Primero valida/crea los
paquetes, luego autentica exclusivamente como publicador staging y consulta sus
permisos reales en `authcraft-db-staging`. No configura firewall ni concede permisos.
Cuenta equivocada, fallo SQL o prueba omitida bloquean. Solo un test ejecutado y
aprobado acredita el login; `deployment_authorized` permanece false.
Ver [limites del diagnostico](../../docs/environments.md#diagnostico-oidc-preparado-sin-ejecutar-remotamente).
Este diagnostico puede reactivar SQL pausado y no prueba aun permisos DacFx Publish.

La ampliación local `evaluate_staging_schema=true` incluye el diagnóstico anterior
y genera DeployReport/Script con el mismo token, DACPAC/manifiesto del run y opciones
seguras. No requiere activar ambos inputs. Guarda reporte/script por 30 días en
`staging-schema-review` solo tras éxito; nunca ejecuta Publish ni postdeploy.
Si falta SqlPackage o permisos, bloquea sin instalar, ampliar grants ni cambiar red.
Esta ampliación ya se subió/ejecutó con autorización: el run
[42269089780](https://github.com/leoperetti/authcraft-api/actions/runs/42269089780)
pasó login, pero DeployReport falló con `diagnostic_category=permission_denied`.
No hay plan aprobado ni permisos DacFx suficientes acreditados. El error no
identifica el grant faltante; no ampliar permisos por inferencia.
Posteriormente se verifico SQL 229 al consultar `sys.sql_expression_dependencies`
con impersonacion del publicador. Con autorizacion explicita se concedio SELECT
solo sobre esa vista: consulta exitosa y sin DDL/DML ni acceso a datos de negocio.
El run [42299486643](https://github.com/leoperetti/authcraft-api/actions/runs/42299486643)
paso login/perfil actualizado, pero DeployReport sigue fallando con una mencion
de SELECT sin objeto identificable. Este grant no resolvio por completo DacFx.
No conceder SELECT de base, db_datareader ni db_owner para sortear el bloqueo.

Después de configurar identidad runtime, vault y grants SQL, reconciliar workflows
y retirar rutas production, publicar un commit main desde el único flujo vigente.
Documentar primero la recuperación y acreditar la revisión de esquema/postdeploy
conforme al contrato; si está pendiente, no publicar. Preparar cuenta/tenant sintéticos
staging solo tras aplicar su esquema compatible, antes del smoke, con autorización
de la escritura y sin enviar notificaciones.
Registrar run ID, manifiesto, hashes y reporte SQL; comprobar el destino
`authcraft-api-staging` y base `authcraft-db-staging` antes de escribir.

El preflight actual solo tiene implementada la parte de procedencia de artefactos:
consulta Actions autenticado, vuelve a descargar y compara API/DACPAC/manifiesto,
y contrasta la version anterior con un job `deploy-staging` exitoso del workflow
consolidado. Sin historial verificable no acepta un SHA anterior arbitrario. Mientras
no se integren inventario, identidad, configuracion y permisos SQL, devuelve
`state=blocked` y codigo no cero incluso si `artifact_validation=verified`.
No omitir ese bloqueo para probar una publicacion.

La revision SQL debe estar en una issue abierta de `leoperetti/authcraft-api`,
como comentario de `leoperetti`, con exactamente una linea por campo del
[contrato de revision](contracts/environment-deployment.md#evidencia-de-revisión-de-compatibilidad).
El publicador vuelve a consultar el comentario y a generar el plan antes de Publish.
Rechazo, campos repetidos, cambio de hashes, error de API o revocacion bloquean.
Conservar report/script originales y los archivos `staging-recheck-*` con acceso
restringido. Las pruebas locales sustituyen GitHub y SqlPackage; no demuestran
que el publicador tenga permisos SQL en Azure.

Obtener hostname desde ARM; no construir `<app>.azurewebsites.net` por suposición.
Tras implementación/autorización:

```powershell
$stagingHost = az webapp show --resource-group AuthCraft --name authcraft-api-staging --query defaultHostName --output tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($stagingHost)) { throw 'No se pudo resolver staging' }
$stagingHealth = Invoke-RestMethod "https://$stagingHost/health/ready"
if ($stagingHealth.status -ne 'healthy') { throw 'Staging no está listo' }
```

Comparar versión exacta con manifiesto, comprobar login/roles de tenant sintético y
que no se envíe correo. Medir y limitar reintentos de cold start; 503 o timeout falla.
El smoke automatizado `scripts\Test-StagingRelease.ps1` vuelve a resolver el hostname
desde ARM, comprueba live/ready contra el commit esperado y prueba login/roles sin
mostrar la contraseña ni el token. Usarlo solo después de autorizar la cuenta
sintética y la publicación staging:

```powershell
$secretNames = @(
    'AUTHCRAFT_STAGING_SMOKE_IDENTIFIER',
    'AUTHCRAFT_STAGING_SMOKE_PASSWORD',
    'AUTHCRAFT_STAGING_SMOKE_CLIENT_ID',
    'AUTHCRAFT_STAGING_SMOKE_TENANT_CLIENT_ID'
)
$previousValues = @{}
foreach ($name in $secretNames) {
    $previousValues[$name] = [Environment]::GetEnvironmentVariable($name)
}
$smokeCredential = Get-Credential -Message 'Cuenta sintetica exclusiva de staging'
try {
    $env:AUTHCRAFT_STAGING_SMOKE_IDENTIFIER = $smokeCredential.UserName
    $env:AUTHCRAFT_STAGING_SMOKE_PASSWORD =
        [Net.NetworkCredential]::new('', $smokeCredential.Password).Password
    $env:AUTHCRAFT_STAGING_SMOKE_CLIENT_ID = '<clientId sintetico de staging>'
    $env:AUTHCRAFT_STAGING_SMOKE_TENANT_CLIENT_ID = '<clientId sintetico de staging>'
    .\scripts\Test-StagingRelease.ps1 -ExpectedVersion '<commit_sha de 40 caracteres>'
    if ($LASTEXITCODE -ne 0) { throw 'El smoke de staging fallo' }
}
finally {
    foreach ($name in $secretNames) {
        [Environment]::SetEnvironmentVariable($name, $previousValues[$name])
    }
    $smokeCredential = $null
    $previousValues = $null
}
```

## 5. Bloqueos y recuperación

En pruebas aisladas con credenciales sustituidas, ensayar al menos:

1. Fallo de validación staging.
2. Aprobación production ausente.
3. Destino de base cruzado.
4. Secreto requerido ausente.
5. Referencia distinta de main.

Además: hash alterado, JWT de otro ambiente, versión superada y ejecución concurrente.
Esperado: ninguna invocación de modificación production, estado blocked/failed y
error sanitizado; no probar los bloqueos manipulando recursos production reales.

Ensayar recuperación en local/staging con paquete anterior compatible. Identificar
versión activa, evidencia y acciones en ≤30 minutos. Nunca restaurar ni borrar datos
production como prueba. Mientras falte aprobación protegida, la validación completa
de promoción queda pendiente y la feature no se declara terminada.

## 6. Habilitación futura de production

No ejecutar hasta disponer de mecanismo compatible aprobado y revisado. Verificar
required reviewers real, restricciones main, ausencia de bypass, subject inmutable
por ambiente y retiro de confianza main/credenciales alternativas.
Con autorización de configuración y posible reinicio, establecer y comprobar
ASPNETCORE_ENVIRONMENT=Production, resolver DOTNET_ENVIRONMENT contradictorio y
verificar vault/destino efectivos antes del preflight, preservando bootstrap y
la transición JWT aprobada.
Evaluar esquema sin modificación, aprobar release/reporte y publicar los mismos
hashes validados en staging. Si falta un control, paquete o evidencia, bloquear.
No reconstruir el release ni permitir que otra ejecución complete la aprobación.
