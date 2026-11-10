# Quickstart: Validar los ambientes

Guía operativa para Development y para los gates pendientes de Staging/Production.
Development fue validado en LocalDB; no se conectó Azure SQL durante esa validación.

## Prerrequisitos y límites

Estado posterior al despliegue autorizado: run 42737737446 publico SQL y API del
candidato 42721985281, revoco permisos, pero fallo smoke por version=local.
Salud live/ready healthy no acredita el SHA esperado; login no se ejecuto.
No promover ni repetir bootstrap unknown: SQL/API ya cambiaron. La correccion
local de compilacion requiere candidato y revision nuevos, con baseline actual
inspeccionado. Ver evidencia en docs/environments.md.

La reanudacion ahora incluye localmente gestion temporal mediante
`assigner_revoker` (client ID c376b9f5-2b0b-406f-8579-ef8787001115).
Antes de ejecutarla verificar federacion OIDC y usuario/permisos SQL de la gestora:
ver [ventana de permisos](../../docs/environments.md#ventana-de-permisos-automatizada-preparacion-local).
Grant despues de preflight; revoke/inspeccion con always antes de API, tambien
ante fallo. Si el runner desaparece o no verifica cleanup, revocacion administrativa
obligatoria antes de reintentar. No es garantia de cleanup ante perdida del runner.

Actualizacion: la gestion ya fue subida y ensayada sin despliegue en
[run 42736112124](https://github.com/leoperetti/authcraft-api/actions/runs/42736112124).
OIDC, grant/revoke y segunda verificacion idempotente aprobados;
revocation_verified=true, published=false. Publicador sin db_owner y gestora
con su permiso persistente autorizado. Falta autorizacion de Publish/API y
ejecucion integral del candidato revisado 42721985281; no reconstruirlo.

Revision actual verificada: el responsable publico la
[decision 6755129089](https://github.com/leoperetti/authcraft-api/issues/11#issuecomment-6755129089)
para el candidato 42721985281. El parser exacto aprobo autor, ultima decision,
run/commit/baseline/destino, hashes, opciones y aceptaciones.
La revision propia ya no esta pendiente; sigue requiriendo reconsulta antes de
Publish y no autoriza operaciones remotas. Permisos/revocacion, SQL Publish,
deploy API y preparacion sintetica necesitan alcance operacional explicito.

Actualizacion posterior: el nuevo
[run 42721985281](https://github.com/leoperetti/authcraft-api/actions/runs/42721985281)
completo packaging, registro inicial built, OIDC SQL review-only y preflight,
sin Publish/API/permisos. Registro/manifiesto/paquetes descargados y hashes
verificados. Esta completada la comprobacion remota del registro inicial; falta
la reanudacion real. El candidato nuevo requiere revision autenticada propia,
no hereda la del run 42705333969 (la revision propia ya consta arriba). Ver
[evidencia y hashes](../../docs/environments.md#t039-verificacion-remota-review-only-del-registro-inicial).
Las secciones siguientes conservan la cronologia de las evaluaciones anteriores.

Actualizacion 2026-11-09: `IncludeTransactionalScripts=True` esta implementado
en evaluacion, reevaluacion y Publish del inicializador/publicador. El postdeploy
tiene transaccion propia verificada en LocalDB, no rollback global del esquema.
Antes de publicar seguir los gates de [transacciones](#validacion-transaccional-por-fases).

- PowerShell, Git Bash, .NET 10, MSBuild con SSDT y SqlPackage disponibles.
- SQL Server local/LocalDB y base local dedicada; credenciales sintéticas protegidas.
- Acceso autorizado a GitHub/Azure. No listar valores de secretos.
- Autorización explícita antes de modificar recursos o conectar Azure SQL pausado;
  una conexión puede reactivarlo y generar consumo.
- Production sigue bloqueado; no intentar publicaciones manuales alternativas.
- Consultar [contrato](contracts/environment-deployment.md) y
  [estados](data-model.md) para salidas, identidad y condiciones de bloqueo.

## Validacion transaccional por fases

El diseno esta implementado y verificado localmente con SqlPackage 170.4.53.0 y
170.5.96.0. La seleccion final paso 101 pruebas sin omisiones; incluye el DACPAC
real, gate sobre SQL generado y rechazo de transaccion ajena con 51001 antes de
escribir. T068 agrega evidencia de DeploymentAttempt y pruebas del resumen de
Actions; la nueva seleccion local suma 121 pruebas aprobadas sin omisiones.
T069 completada con el run review-only
[42705333969](https://github.com/leoperetti/authcraft-api/actions/runs/42705333969),
commit `ceaa5258111e266cbebaacf733869f4e9ad3cd47`: los tres jobs pasan,
paquetes/manifiesto verificados y SQL original aceptado por el gate local.
SqlPackage 170.4.53.0 y hash del ejecutable coinciden con los ensayos locales.
No se publico SQL/API ni se cambiaron permisos. T070 completada: el responsable
publico la [revision autenticada](https://github.com/leoperetti/authcraft-api/issues/11#issuecomment-6752655094),
verificada por el parser para este candidato. Ver
[hashes originales](../../docs/environments.md#evaluacion-review-only-nueva-42705333969)
y [estado de revision](../../docs/environments.md#revision-autenticada-del-candidato-42705333969).
Permisos/revocacion y autorizacion operacional siguen pendientes.
Este procedimiento no ejecuta ni autoriza Publish en Azure.

La reanudacion incorpora localmente el registro CandidateRelease: conserva
`candidate-release.json` en `staging-deployment-attempt` (30 dias). Un exito
integral acredita staging_validated, vincula el hash del intento y deja
state=production_blocked/promotion_authorized=false; un smoke aislado no basta.
Este cambio aun no fue ejecutado en GitHub ni altera el candidato revisado.

El packaging manual main tambien incorpora localmente el registro inicial built,
con timestamp UTC e identidad/hashes del manifiesto. Se sube en el artefacto
separado `candidate-release` por 30 dias; PR no lo genera. No autoriza despliegue,
no cambia el manifiesto contractual y no es requisito retroactivo para el
candidato revisado 42705333969. Falta comprobar este cambio en Actions.

1. Construir el DACPAC con el comando MSBuild de esta guia. Verificar que ambos
   scripts usen `IncludeTransactionalScripts=True` para DeployReport, Script,
   reevaluacion y Publish, sin diferencias de opciones.
2. Ejecutar los ensayos del fixture LocalDB:

   ```powershell
   $env:AUTHCRAFT_LOCAL_ENVIRONMENT_SQL_CONNECTION = 'Server=(localdb)\MSSQLLocalDB;Database=authcraft_development;Integrated Security=True;TrustServerCertificate=True'
   dotnet test .\authcraft-api.Tests\authcraft-api.Tests.csproj --configuration Release --filter "FullyQualifiedName~LocalEnvironmentSqlTests|FullyQualifiedName~DatabaseDeploymentReviewTests|FullyQualifiedName~DeploymentRecoveryTests"
   ```

   No contar tests omitidos ni confundir exito de la suite con atomicidad global.
   Los escenarios publican baseline/delta en bases descartables y provocan
   separadamente 547 y 51000, con control sin proteccion y postdeploy protegido.
3. Conservar evidencia de limites transaccionales y herramienta exacta; desde
   otra conexion comprobar columnas, constraints/trust, semillas y datos previos.
   Conservar el ensayo original como evidencia de falta de atomicidad global.
   En nuevos escenarios con postdeploy protegido exigir rollback de sus semillas
   y cambios de trust en ambos fallos y constraints confiables en exito.
   El esquema confirmado por DacFx puede permanecer; registrar estado parcial.
   Bloquear si el COMMIT dentro del postdeploy precede al guard o quedan escrituras
   de esa fase. Un exit code de error no demuestra rollback.
4. Generar DeployReport/Script de Staging con la opcion, solo con autorizacion
   para conectar. Verificar la cobertura y que el gate acepte el SQL real sin
   permitir SQL arbitrario posterior. No editar artefactos generados.
5. Obtener revision autenticada con `include_transactional_scripts=true`,
   `postdeploy_transaction=true` y hashes
   nuevos. El run `42692449745` y sus revisiones previas no acreditan esta opcion.
   Hasta completar estos gates, no ejecutar el workflow de reanudacion.

Para el candidato actual ya existe decision autenticada en #11; el publicador
debe reconsultarla y reevaluar el destino antes de Publish.
Los campos pending de esa plantilla bloquean deliberadamente: no convertirlos
en approved/accepted sin completar la revision. No reutilizar comentarios de
runs anteriores. Aun despues de revisar faltan autorizacion operativa y permisos
de publicacion/revocacion verificados; este quickstart no concede ninguno.

La reanudacion ahora ejecuta la prueba exacta de fases en LocalDB antes de OIDC,
con el mismo SqlPackage que usara para publicar. Conserva seis SQL sinteticos,
JSON de prueba y TRX; evidencia ausente, omitida o que no coincida con la
herramienta/fuentes actuales bloquea Publish. No reconstruye los paquetes candidatos.
Para reproducir solamente esa prueba con evidencia local en un directorio nuevo:

```powershell
$env:AUTHCRAFT_TRANSACTION_EVIDENCE_DIRECTORY = Join-Path $env:TEMP ('authcraft-phase-evidence-' + [Guid]::NewGuid().ToString('N'))
# Mantener SQLPACKAGE_PATH apuntando al mismo ejecutable que usara el publicador.
if ([string]::IsNullOrWhiteSpace($env:SQLPACKAGE_PATH) -or -not (Test-Path -LiteralPath $env:SQLPACKAGE_PATH -PathType Leaf)) {
  throw 'Configurar SQLPACKAGE_PATH con la ruta del SqlPackage instalado.'
}
dotnet test .\authcraft-api.Tests\authcraft-api.Tests.csproj --configuration Release `
  --filter 'FullyQualifiedName=authcraft_api.Tests.LocalEnvironmentSqlTests.TransactionalPublishDocumentsSeparateSchemaAndPostdeploymentCoverage' `
  --logger 'trx;LogFileName=transaction-tests.trx' --results-directory $env:AUTHCRAFT_TRANSACTION_EVIDENCE_DIRECTORY
if ($LASTEXITCODE -ne 0) { throw 'Fallo la prueba de fases.' }
Import-Module .\scripts\DeploymentReview.psm1 -Force
Get-DeploymentTransactionVerification `
  -Path (Join-Path $env:AUTHCRAFT_TRANSACTION_EVIDENCE_DIRECTORY 'transaction-verification.json') `
  -SqlPackagePath $env:SQLPACKAGE_PATH
```

El directorio debe estar fuera del repositorio; no reutilizar prueba de otras
fuentes ni editar sus SQL. La ruta de Actions guarda `sql-attempt.json` antes y
despues de Publish, y un `deployment-attempt.json` agregado incluso tras fallos.
Solo registro/upload usan always(); API/smoke no continuan tras fallo SQL.
El artefacto `staging-deployment-attempt` retiene esos archivos y los report/script
de evaluacion/reevaluacion durante 30 dias con el acceso del repositorio.
postdeploy_result=unknown exige inspeccion independiente, no implica fallo ni
rollback; un exit code cero tampoco lo convierte automaticamente en committed.
Una caida total del runner puede dejar evidencia no disponible.

Ver [contrato transaccional](contracts/environment-deployment.md#cobertura-transaccional-requerida)
y [diseno](plan.md#diseno-transaccional-y-fallos-de-constraints). No se promete una
transaccion que abarque conjuntamente SQL y el despliegue de la API.

Ante fallo del postdeploy: detener API/smoke/promocion, inspeccionar el esquema
desde otra conexion y confirmar que la API previa sigue compatible. No eliminar
columnas, deshabilitar constraints ni corregir datos automaticamente. Resolver la
causa con autorizacion, reevaluar y volver a revisar hashes/impacto antes de reintentar.

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
ni acreditaba por si solo la ausencia de otras copias. La auditoria posterior cerro
T029 para las rutas conocidas; T010 conserva la verificacion SQL pendiente. No
eliminar identidades por inferencia ni reactivar el workflow anterior. Ver
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
Esta ampliacion ya se subio/ejecuto con autorizacion: el run
[42269089780](https://github.com/leoperetti/authcraft-api/actions/runs/42269089780)
paso login, pero DeployReport fallo con `diagnostic_category=permission_denied`.
Se habilito diagnostico cifrado opt-in para investigar el bloqueo sin exponer
salida nativa en logs publicos. El diagnostico privado identifico SELECT denegado
en `dbo.__RefactorLog`. Con autorizacion explicita se concedio SELECT solo sobre
`sys.sql_expression_dependencies` y `dbo.__RefactorLog`, validado con impersonacion:
ambas consultas pasan y DDL/DML/lectura de negocio siguen denegados.
Run [42386493432](https://github.com/leoperetti/authcraft-api/actions/runs/42386493432):
`validate` y `verify-staging-publisher` en success; DeployReport/Script de
solo lectura completados con OIDC real y evidencia `staging-schema-review`.
No se ejecuto Publish ni postdeploy.

El workflow Staging de validacion verifica paquetes, procedencia y metadatos, y
prepara una revision; no publica. Se preparo localmente
`.github/workflows/deploy_authcraft-api-staging.yml` para reanudar, sin reconstruir,
un run exitoso y revisado. Esta ruta no se ha subido ni ejecutado. Antes de
habilitarla deben completarse T033/T035/T037/T038 y los gates concretos listados
abajo. El preflight exitoso no autoriza por si solo una publicacion. Preparar cuentas
o tenants sinteticos solo despues de aplicar un esquema compatible, antes del smoke,
con autorizacion de esa escritura y sin enviar notificaciones. Registrar run ID,
manifiesto y hashes; comprobar destino y base antes de cualquier escritura.

El workflow vigente agrega `preflight-staging` despues de `validate` y
`verify-staging-publisher`. Consulta Actions autenticado, vuelve a descargar y
compara API/DACPAC/manifiesto, valida el baseline contra un despliegue Staging
exitoso verificable (o acepta `unknown` solo con bootstrap explicito y sin
despliegues exitosos), y comprueba por OIDC metadatos ARM y el bloqueo de Production.
El resultado es `state=ready_for_review` con
`deployment_authorized=false`; la revision SQL autenticada, el permiso temporal y
Publish siguen siendo gates separados. La verificacion OIDC real se completo en el
[run 42637441215](https://github.com/leoperetti/authcraft-api/actions/runs/42637441215):
los tres jobs (`validate`, `verify-staging-publisher`, `preflight-staging`) pasaron,
y los artefactos se retienen 30 dias. No hubo Publish, postdeploy, despliegue API ni
smoke. No omitir el gate para probar una publicacion.

La comprobacion funcional historica del 2026-11-06 no confirma el estado actual:
el 2026-11-09 `/health/live` devolvio 404 y no se encontro un despliegue exitoso
verificable del workflow nuevo. El run 42637441215 produjo un preflight con baseline
`unknown`, no un despliegue. Consultar el [runbook de recuperacion](../../docs/deployment-recovery.md)
y no asumir que hay una version actual confirmada para restaurar.

La inspeccion del SQL exacto de `42669270551` encontro siete
`WAITFOR DELAY '00:00.010'`, cada uno antes de agregar una constraint con
`WITH NOCHECK`. No aparecen en el proyecto SQL, el PostDeployment fuente ni el
`postdeploy.sql`/`model.xml` del DACPAC. Su procedencia dentro del generador no
esta explicada; el SQL descargado coincide con el hash publicado. No aprobar ni
publicar este plan hasta reproducir y explicar esos `WAITFOR`. Si se regenera el
SQL, revisar y aprobar los hashes nuevos exactos.

Actualizacion: la reproduccion directa con SqlPackage 170.5.96.0 y el DACPAC
exacto si genera los siete WAITFOR, sin wrapper ni edicion manual. No es evidencia
de manipulacion. Sigue pendiente determinar su finalidad; el riesgo confirmado
es que no hay validacion final, incluso con `ScriptNewConstraintValidation=True`.
El gate inicial rechazo cualquier token `NOCHECK`. La correccion versionada ahora
permite solo las siete constraints revisadas y exige su bloque exacto de
`WITH CHECK CHECK CONSTRAINT`, seguido unicamente de mensajes/separadores y
validaciones DacFx reconocidos. Constraints ajenas, deshabilitaciones, cambios al
bloque y SQL posterior arbitrario bloquean Publish; Evaluate permanece disponible.
No editar el SQL descargado ni omitir el gate: reevaluar y revisar hashes nuevos.

`WITH NOCHECK` al agregar constraints omite la comprobacion de las filas
existentes y deja la constraint habilitada pero no confiable. Los cambios futuros
siguen validados salvo que se deshabilite la constraint por separado; las filas
antiguas no quedan certificadas y el optimizador no puede usar la constraint como
evidencia de integridad para ciertas optimizaciones. Una consulta posterior a
Staging encontro los checks `CK_memberships_status` y `CK_tenants_status`
habilitados y confiables (`is_disabled=0`, `is_not_trusted=0`), con definiciones
equivalentes a las fuentes. La fuente/DACPAC expresa sus listas como `IN (...)`;
el modelo extraido de la base las expresa como ORs equivalentes. Comparar ambos
DACPACs con `SqlPackage /Action:DeployReport` en modo offline reprodujo los
mismos `DROP`/`CREATE`, por lo que la normalizacion de la expresion es una causa
probable del diff, aunque no hay diagnostico DacFx que pruebe el motivo interno.
El script del release anterior degradaria esos checks confiables. La correccion
local normaliza las expresiones a OR y la nueva evaluacion no recrea esos checks;
valida explicitamente las siete constraints revisadas al cerrar el postdeploy.
El ensayo LocalDB publico el DACPAC, rechazo datos invalidos al validar y comprobo
confianza final. La evaluacion remota fue solo DeployReport/Script y el SQL paso
el gate local; no es una aprobacion de publicacion.
El script del ensayo local no contenia WAITFOR; la evaluacion actual del runner
conserva cinco instrucciones atribuibles al comportamiento DacFx reproducido.
No confundir ambos archivos ni editar el SQL original. Los hashes estan en el
[registro de ambientes](../../docs/environments.md#correccion-versionada-y-ensayo-local-verificado).
Usar el candidato actual `42705333969`: `42669270551` no contiene la correccion.

La revision SQL debe estar en una issue abierta de `leoperetti/authcraft-api`,
como comentario de `leoperetti`, con exactamente una linea por campo del
[contrato de revision](contracts/environment-deployment.md#evidencia-de-revisión-de-compatibilidad).
Debe incluir `staging_baseline_sha` (SHA verificado o `unknown` solo para el
bootstrap inicial), `api_compatibility=accepted`, `data_impact=accepted` y
`recovery_plan=accepted`, ademas del run, commit, destino, opciones y hashes
exactos. El bootstrap `unknown` no sustituye la revision ni autoriza Publish.
El publicador vuelve a consultar el comentario y a generar el plan antes de Publish.
Rechazo, campos repetidos, cambio de hashes, error de API o revocacion bloquean.
Conservar report/script originales y los archivos `staging-recheck-*` con acceso
restringido. Las pruebas locales sustituyen GitHub y SqlPackage; no demuestran
que el publicador tenga permisos SQL en Azure.

La ruta de reanudacion descarga API, DACPAC, manifiesto y evidencia del run original,
revalida procedencia y estado actual, exige el comentario de revision exacto y solo
entonces intenta SQL Publish; despues despliega el ZIP de ese mismo run y ejecuta el
smoke. No reconstruye, no concede permisos automaticamente y no hace rollback
destructivo. Antes de poder ejecutarla:

- para el candidato `42705333969`, debe existir en la issue
  #11 una revision autenticada que identifique ese run, commit, destino,
  baseline verificado o `unknown` inicial, opciones y sus cuatro hashes exactos,
  con aceptaciones explicitas de compatibilidad API, impacto de datos y recuperacion.
  No reutilizar la revision ni el DACPAC de `42669270551`;
- deben existir como GitHub Actions secrets los cuatro datos de una cuenta
  sintetica exclusiva de Staging (los nombres ya estan configurados):
  `AUTHCRAFT_STAGING_SMOKE_IDENTIFIER`,
  `AUTHCRAFT_STAGING_SMOKE_PASSWORD`, `AUTHCRAFT_STAGING_SMOKE_CLIENT_ID` y
  `AUTHCRAFT_STAGING_SMOKE_TENANT_CLIENT_ID`. No enviar estos valores por chat;
- debe verificarse que el principal SQL tenga permisos DacFx de publicacion y un
  procedimiento comprobable para retirarlos inmediatamente. Hoy el principal solo
  tiene permisos de lectura/revision; la autorizacion previa para un `db_owner`
  temporal no significa que se haya concedido.

Con los gates anteriores completados y despues de subir/revisar el workflow, la
reanudacion usa estos campos, sin reconstruir el candidato:

```powershell
gh workflow run deploy_authcraft-api-staging.yml --ref main `
  --field release_run_id=$reviewedReleaseRunId `
  --field review_issue_number=11 `
  --field staging_baseline_sha=unknown `
  --field allow_initial_staging_bootstrap=true
```

No ejecutar el comando mientras falte cualquiera de los gates. El workflow falla
antes de Azure si faltan secretos de smoke; sin permiso SQL la evaluacion/publicacion
DacFx tambien falla sin intentar rutas alternativas. Cualquier fallo despues de
SQL Publish exige inspeccion manual: el esquema puede estar aplicado aunque API o
smoke no hayan terminado.

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

### Informe del intento de publicación

Cuando exista un flujo Production autorizado, abrir el run correspondiente en
GitHub Actions y descargar el artefacto `production-deployment-report` (Markdown),
incluso si el job falló. Consultar primero la
[guía fija de revisión](../../docs/production-release-checklist.md); después usar el
informe para localizar la etapa, causa sanitizada y siguiente paso sugerido.
Los reportes/scripts SQL se descargan separadamente si están disponibles. El artefacto
se retiene 30 días y no se escribe de vuelta al archivo fijo del repositorio.
El informe no cambia los gates ni sustituye la aprobación protegida.
