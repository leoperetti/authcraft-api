# Ambientes AuthCraft

## Estado de implementacion

Se preservan los cambios locales de invitaciones, signup, bootstrap y el proyecto de
pruebas. No pertenecen a esta feature y no se revierten. La solucion ya incluye
`authcraft-api.Tests`; se reutiliza su hosting de pruebas.

El workflow Production remoto (ID `374894897`) esta `disabled_manually`; el de
Staging (ID `407226559`) permanece `active`. En la revalidacion de solo lectura
del 2026-11-09, el HEAD de `main` remoto y local coincide en
`63ed699446bd3b8d8b1da934055208ecbaf3eb90`. La documentacion de esta evaluacion
queda local, sin commit, para no avanzar el SHA del release. No habia ejecuciones
Production encoladas ni en curso entre las 15 consultadas. No se modifico el
servicio Production.

El workflow Staging valida paquetes y, cuando se solicita, ejecuta el preflight
de procedencia, baseline y metadatos ARM. El preflight solo deja el release
`ready_for_review` con `deployment_authorized=false`: no publica SQL ni API.
El workflow `deploy_authcraft-api-staging.yml` se preparo localmente para reanudar
el run exacto sin reconstruir paquetes; aun no se subio ni se ejecuto. Exige
revision SQL autenticada, preflight nuevo, credenciales smoke sinteticas y permisos
SQL efectivos antes de intentar cambios.
El run [42664956766](https://github.com/leoperetti/authcraft-api/actions/runs/42664956766)
del commit `6a2a123149073b84bd61b0fc76a781294fdc7ffa` paso `validate`,
`verify-staging-publisher` y `preflight-staging`; DeployReport/Script se
retuvieron correctamente. No hubo Publish, postdeploy, despliegue API ni smoke.
El run previo [42660168304](https://github.com/leoperetti/authcraft-api/actions/runs/42660168304)
paso build/evaluacion, pero fallo al cargar el artefacto porque el workflow
buscaba nombres estaticos; el patron se corrigio y verifico en el run siguiente.
La publicación sigue bloqueada hasta completar los gates y obtener autorizacion
especifica para el release.

Revision de Actions 2026-11-06: el workflow staging remoto sigue activo y su ultima
ejecucion (`41882690684`) fallo en `Build with dotnet` con `MSB4278` al intentar
cargar el `.sqlproj` tradicional desde el build de la solucion; el job de despliegue
quedo omitido. El workflow staging local ahora es una propuesta de validacion de
paquetes: compila API, pruebas y DACPAC por separado y solo genera artefactos en un
`workflow_dispatch` de `main`. Esta version ya reemplazo el workflow staging remoto
con autorizacion; no tiene pasos de publicacion. Incluye un job OIDC opcional de
diagnostico SQL de solo lectura y, al solicitar evaluacion de esquema, un job
`preflight-staging` para verificar procedencia, baseline, metadatos ARM y que
Production siga deshabilitado. Ese preflight termina en `ready_for_review` con
`deployment_authorized=false`; no despliega a Azure.

El repositorio GitHub es publico por decision manual del usuario. Production sigue
bloqueado: no hay environments ni proteccion efectiva de `main`, y el workflow de
publicacion permanece `disabled_manually`. Secret Scanning esta deshabilitado; por
lo tanto, no hay evidencia de alertas ni de ausencia de secretos. No se cambio la
configuracion remota al verificar estos estados.

## Prerrequisitos comprobados

Verificacion 2026-11-07:

- .NET SDK 10.0.403; build Release de API satisfactorio. Las pruebas conservan una
  advertencia previa de `TestAuthHandler.Scheme` en el proyecto de pruebas.
- MSBuild/SSDT de Visual Studio Community 18: build SQL satisfactorio.
- Salida real: `authcraft-db\bin\Release\authcraft-db.dacpac`.
- SqlPackage instalado en
  `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\Microsoft\SQLDB\DAC\SqlPackage.exe`.
- Git Bash, Azure CLI y gh disponibles. MSBuild y SqlPackage no estan en PATH;
  ejecutar sus rutas completas o configurar PATH en la misma sesion.
- `.gitignore` ya protege bin, obj, packages, archivos de usuario, logs y `.env*`.

Revalidacion local 2026-11-09: `dotnet build` Release de la API paso. El comando
`msbuild` no esta en PATH; la ruta MSBuild localizada bajo SSMS 22 no encuentra
`Microsoft.Data.Tools.Schema.SqlTasks.targets` (MSB4226), por lo que este host no
pudo reconstruir el DACPAC. El build SQL satisfactorio indicado arriba corresponde
a la verificacion anterior del 2026-11-07; no instalar herramientas durante esta
revision ni confundir esta limitacion local con un resultado de Actions.

## Autorizaciones

El usuario autorizo conexiones SQL Azure (incluida reactivacion/consumo), identidad
runtime staging/vault exclusivo, cierre de futuras publicaciones directas production,
configuracion staging/OIDC/permisos minimos y publicacion staging sin correo real.
Cada correccion concreta de privilegios debe revisarse antes de aplicarse.
No se autorizaron publicacion production, gastos de nuevos recursos ni cambios de
visibilidad. El 2026-11-06 se autorizo retirar la federacion production identificada
abajo y eliminar identidades solo si se comprueba que es seguro. Posteriormente se
autorizaron los dos retiros de roles ARM y la eliminacion del secreto GitHub
indicados en la siguiente evidencia. No eliminar ni desacoplar una identidad con
consumidores sin resolver.

Retencion confirmada: **30 dias** para paquetes y evidencias, sujeta a limites
actuales de GitHub antes de activar el workflow. Expiracion bloquea promocion o
recuperacion; no reconstruir silenciosamente la version validada.

Nunca imprimir cadenas SQL, secretos, passwords, tokens o valores de vault.
Usar variables protegidas en memoria; documentar referencias y nombres, no valores.
Una lectura de metadatos ARM no abre SQL ni acredita permisos SQL efectivos.

## Hosting de pruebas

`InvitationsApiFactory` usa `Testing`, sustituye SQL por InMemory y correo por
`FakeEmailService`, y deshabilita bootstrap. Reutilizarla para pruebas HTTP sin
credenciales Azure. Las nuevas pruebas de configuracion se ejecutan sin iniciar
hosting remoto. `Testing` no es un ambiente de despliegue.

El fixture SQL existente admite localhost/LocalDB y bases
`authcraft_invitation_test_*`; crea un catalogo descartable. No reemplaza publicar
el DACPAC real contra una base local dedicada.

## Inventario

| Ambiente | Aplicacion | Base | Almacen de secretos |
|----------|------------|------|---------------------|
| development | localhost:5171 / localhost:7202 | local dedicada | User Secrets |
| staging | authcraft-api-staging | authcraft-db-staging | authcraft-vault-staging |
| production | authcraft-api | authcraft-db | authcraft-vault |

Staging/production comparten servidor `authcraftdbbrasil` y plan
`ASP-AuthCraft-bc2b` (F1). Hay impacto de capacidad compartida; no se asume factura
cero. Los estados observados no demuestran funcionamiento.

### Identidades observadas durante planificacion

- `oidc-msi-aede`: Website Contributor production; federacion GitHub main.
- `authcraft-api-st-id-97cc`: Website Contributor staging; federacion GitHub main.
- `oidc-msi-a9fa`: Website Contributor production, sin federacion; consumidor
  historico desconocido. No eliminar por nombre.
- Production tiene identidad por sistema con Secrets User en su vault.
- Staging no tenia identidad runtime asignada durante la planificacion; ahora tiene
  identidad por sistema exclusiva, verificada en la preparacion descrita abajo.

### Cierre parcial de confianza Production: 2026-11-06

Con autorizacion especifica, a las 16:03 UTC se retiro exclusivamente la credencial
federada `oidc-credential-b77f` de `oidc-msi-aede`. Antes de eliminarla se comprobaron
issuer `https://token.actions.githubusercontent.com`, audiencia
`api://AzureADTokenExchange` y subject
`repo:leoperetti@12513421/authcraft-api@1404745493:ref:refs/heads/main`.
Las consultas posteriores devolvieron listas vacias de federaciones para
`oidc-msi-aede` y `oidc-msi-a9fa`. Esto retira esa confianza para obtener nuevos
tokens; no revoca tokens ya emitidos ni acredita ausencia de otras credenciales.

Se preservaron la federacion staging `sjlugzywfnxde`, su subject main y su rol
Website Contributor limitado a `authcraft-api-staging`. La configuracion publica
staging sigue siendo Staging para ASPNETCORE y DOTNET, vault
`authcraft-vault-staging` y bootstrap desactivado.

Production mantiene su identidad por sistema y ambas identidades por usuario
adjuntas, sin cambios. No se elimino ninguna identidad: siguen siendo consumidores
observables de la aplicacion y su uso completo no esta resuelto. La consulta global
de consumidores mediante Azure Resource Graph no pudo completarse por un error de
acceso a una extension local de Azure CLI; no se considero evidencia de ausencia.
En esa lectura ambas identidades conservaban Website Contributor sobre
`authcraft-api`; retirar la federacion no bastaba para cerrar sus permisos.
Esos roles se retiraron posteriormente con autorizacion, como se registra abajo.

Las politicas ARM `basicPublishingCredentialsPolicies/scm` y `/ftp` devolvieron
`allow=false`; el workflow production sigue `disabled_manually` y staging `active`.
No se leyeron valores de secretos, no se conecto SQL, ni se reinicio o publico la
aplicacion Production. Ambas bases estaban Paused en la lectura ARM de esta revision.
Resta revisar y autorizar individualmente los cambios de permisos/credenciales
alternativos; no reactivar el workflow antiguo.

### Retiro autorizado de permisos publicadores: 2026-11-06

A las 16:18 UTC, tras confirmar los cambios concretos con el usuario:

- Se eliminaron solo las asignaciones Website Contributor de `oidc-msi-aede` y
  `oidc-msi-a9fa` sobre `authcraft-api`. Los IDs de asignacion eran
  `bdb564d1-989a-4fae-9065-6f2247513529` y
  `c6bdfe80-eb3c-42c3-98ca-44077df9e5e1`, respectivamente.
- Se elimino el secreto de repositorio `AZURE_SQL_CONNECTION_STRING`, sin leer su
  valor. La inspeccion de ambos workflows en main identifico su uso solamente en
  el workflow antiguo de Production, no en Staging.
- Antes de modificar permisos se verificaron el workflow production deshabilitado
  y cero ejecuciones in_progress/queued/waiting/pending/requested de ese workflow.
- Despues se comprobaron cero roles ARM para ambas identidades, incluidas las
  asignaciones heredadas visibles, y ausencia del secreto en GitHub. Las consultas
  de federaciones siguieron devolviendo listas vacias; SCM/FTP siguen allow=false.
- Se comparo la identidad ARM de la Web App antes/despues: identidad por sistema
  y ambos adjuntos identicos. El runtime conserva Key Vault Reader y Key Vault
  Secrets User en `authcraft-vault`; Key Vault references usa SystemAssigned.
  Staging conserva Website Contributor exclusivamente sobre su aplicacion.

No se eliminaron identidades, se modifico configuracion de la API, se reinicio,
se publico ni se conecto SQL. Eliminar el secreto GitHub no revoca la credencial
original SQL ni copias/tokens ya emitidos; no se puede afirmar cero rutas efectivas
de modificacion SQL. Ese era el estado registrado en esta verificacion del
2026-11-06; la auditoria posterior cerro T029 para las rutas conocidas, segun la
evidencia mas reciente de esta guia. T010 continua pendiente. No rotar credenciales
de runtime por inferencia.

`Test-EnvironmentInventory.ps1` ahora incluye identidades adjuntas, roles ARM del
runtime y de todos los publicadores observados, federaciones, politicas SCM/FTP y
presencia del secreto GitHub antiguo (solo nombres). Bloquea auditoria incompleta,
tipos de control invalidos y permisos/federaciones residuales. Para identidades
publicadoras Production, cualquier rol ARM requiere revision; para runtime solo se
reconocen Reader, Key Vault Reader y Key Vault Secrets User como no publicadores.
Roles personalizados no revisados no se suponen inocuos.

Ejecucion real del verificador, 16:22 UTC: `source=live_metadata`, controles
SCM/FTP/secreto antiguo en false, ambas identidades sin roles ni federaciones.
El inventario sigue `blocked` y devuelve codigo 1 porque Production aun carece de
los ajustes publicos explicitos runtime_environment/vault_name. No se corrigieron:
la configuracion Production y su posible reinicio tienen gates propios.
SQL/funcionalidad siguen pending; esos controles no se infieren de ARM.

Validacion: **76/76 pruebas focalizadas** satisfactorias (21 de inventario y 55
de bloqueo Production/procedencia/revision SQL/recuperacion parcial). Los ocho
casos iniciales nuevos de inventario fallaron antes de implementar los controles.
No se modificaron paquetes, esquema ni datos para esta validacion.

## Gates pendientes

La configuracion local, el DACPAC y la salud Development fueron verificados sin Azure.
El smoke local de login/roles sinteticos tambien paso. El workflow consolidado,
publicador SQL/OIDC y controles Production siguen bloqueados; staging tiene evidencia
real de funcionamiento e identidad runtime aislada descrita abajo. No declarar la
feature completa ni modificar checks de calidad para simular implementacion.

### Disponibilidad de aprobación protegida Production: 2026-11-09

Consulta de solo lectura con `gh api` en el repositorio `leoperetti/authcraft-api`:

- GitHub confirma que el repositorio es privado y que la rama predeterminada es `main`.
- `GET /repos/leoperetti/authcraft-api/environments` devolvió cero environments;
  no hay required reviewers de un environment Production configurados.
- Las consultas de protección efectiva de `main` y rulesets devolvieron HTTP 403
  con el mensaje de GitHub: “Upgrade to GitHub Pro or make this repository public
  to enable this feature.” No se pudo verificar una protección de rama/ruleset
  equivalente mediante la API disponible.

Resultado en esa fecha: no había evidencia de un mecanismo de aprobación protegida
efectiva para Production. T047 y las tareas posteriores quedaron pendientes de una
decisión compatible, autorización y verificación efectiva; este resultado no era
aprobación de publicación.

### Revalidación tras hacer público el repositorio: 2026-11-09

Consulta posterior de solo lectura con `gh api`:

- El repositorio es público y la rama predeterminada sigue siendo `main`.
- El workflow de publicación Production sigue `disabled_manually`; Staging sigue
  `active`.
- GitHub devuelve cero environments, HTTP 404 para la protección de `main`
  (`Branch not protected`) y una lista vacía de rulesets. No hay aprobación
  protegida efectiva configurada.
- GitHub lista seis Actions secrets, y ninguno se llama
  `AZURE_SQL_CONNECTION_STRING`. El workflow Production en `main` sí referencia ese
  nombre en `azure/sql-action`; no se leyó ningún valor. La solicitud `DELETE`
  autorizada devolvió HTTP 404, consistente con que la copia ya estuviera ausente;
  no se puede afirmar que la solicitud la eliminara. El workflow sigue
  `disabled_manually`.
- La lista de colaboradores contiene solo la cuenta propietaria; no se configura
  un reviewer independiente por inferencia. El usuario eligió esa cuenta
  propietaria como reviewer futuro, aceptando que no representa una aprobación
  independiente; esto no configura el environment ni habilita Production.
- La metadata de ambos Key Vaults lista `DatabaseConnectionString`. No se leyeron
  valores al inventariar; la comprobación autorizada posterior usó la conexión solo
  en memoria, sin imprimirla. Se hicieron dos intentos acotados de
  consulta de permisos en Production. No se conservó el diagnóstico sanitizado del
  primer intento; el segundo devolvió el error SQL de cliente `0`. Ninguno produjo una
  instantánea de permisos y no se ejecutó DDL/DML. No se pudo confirmar si la
  conexión se estableció. La consulta de estado ARM posterior muestra la base
  Production `Online`; no se tomó un estado inmediatamente anterior, así que no se
  atribuye el estado a estos intentos. El usuario confirmó dejarla `Online`; no se
  pausó ni modificó la base.
- No se puede determinar si los secretos de Key Vault son solo de runtime, si
  incluyen permisos de publicación o si alguno coincide con la credencial histórica
  de GitHub.
- Secret Scanning responde que está deshabilitado. No se pudo comprobar mediante
  alertas la ausencia de secretos.

Resultado de esa consulta: Production permanecio bloqueado. La enumeracion no
encontro `AZURE_SQL_CONNECTION_STRING`; el `DELETE` 404 no demostro que la solicitud
lo eliminara. Esa lectura no obtuvo evidencia de permisos SQL y no revoco
credenciales. La verificacion posterior de cierre de T029 se registra en la seccion
mas reciente de esta guia: el workflow Production sigue deshabilitado y se retiraron
sus credenciales conocidas de Actions; T029 esta completo, pero T010 y T047 siguen
pendientes. La publicacion del repositorio no equivale a configurar aprobacion ni
autoriza un despliegue.

### Checklist fija e informe por intento Production

La [guia fija de revision Production](production-release-checklist.md) cubre el DeployReport,
el SQL generado, el post-deploy, el impacto sobre datos y la recuperacion. GitHub Actions
no debe sobrescribirla ni crear commits automaticos.

El diseño prevé un artefacto `production-deployment-report` por run, en español y retenido
30 dias, con resultados por etapa, causas sanitizadas y siguientes pasos; las etapas no
alcanzadas deben figurar como no ejecutadas. El generador local y sus pruebas existen, pero
todavia no estan conectados al workflow Production. No asumir que el artefacto ya aparece
en los runs. El workflow production remoto continua deshabilitado y Production sigue
bloqueado por los gates de aprobacion y proteccion. Una interrupcion de GitHub o del runner
antes de generar/subir el informe no es un resultado satisfactorio ni garantiza un artefacto.
La guia y el informe no autorizan publicaciones.

### Preflight y revision SQL: verificacion en Actions del 2026-11-09

`Test-ReleasePreflight.ps1` consulta Actions mediante `gh` autenticado: verifica
repositorio, referencia, commit, evento y job de build; rechaza artefactos duplicados,
expirados o de otra ejecucion y compara los tres archivos locales contra descargas
del run original (API, DACPAC y manifiesto). Un baseline conocido debe coincidir
con la ultima ejecucion satisfactoria de `deploy-staging` en Actions; un build sin
ese job no acredita un despliegue. El bootstrap `unknown` solo se acepta con el input
explicito y cuando no existe despliegue satisfactorio registrado.

El workflow integra `preflight-staging` despues de `validate` y
`verify-staging-publisher`. Ese job comprueba la sesion OIDC y los metadatos ARM
limitados a Staging, la procedencia de los artefactos, el baseline y que Production
siga deshabilitado. Su unico resultado exitoso es
`state=ready_for_review`, `artifact_validation=verified` y
`deployment_authorized=false`; aun requiere revision SQL autenticada, permisos
temporales validados y un flujo separado antes de Publish.

Verificacion real 2026-11-09: el run
[42637441215](https://github.com/leoperetti/authcraft-api/actions/runs/42637441215)
completo `validate`, `verify-staging-publisher` y `preflight-staging` en success
con OIDC real. El baseline `unknown` fue aceptado solo despues de comprobar que no
habia despliegues Staging satisfactorios previos. Los artefactos API, DACPAC,
manifiesto y `staging-schema-review` quedaron disponibles; no se ejecuto Publish,
postdeploy, despliegue API ni smoke.

Revision local de solo lectura del artefacto `staging-schema-review` del run
42637441215:

- DeployReport SHA-256:
  `e06fff4b57ff74c22cce5d7241a30a031378f7d2ba084e74929ca5d4bbd8c466`.
- Script SQL SHA-256:
  `3dfab7b37839bbc09b4c00e7d1768df6cd27f569a34c7cd7fe131fb9631c9947`.
- El reporte propone eliminar y volver a crear dos CHECK constraints de
  `memberships` y `tenants`, alterar `invitations` para agregar columnas nullable
  y una columna `rowversion`, y crear siete constraints. Las siete creaciones usan
  `WITH NOCHECK`, por lo que la evidencia no valida las filas preexistentes.
- El postdeploy hace tres INSERT condicionales en `providers` (`google`, `apple`,
  `github`). No aparecen `DELETE`, `TRUNCATE TABLE` ni `DROP TABLE` en el script.

Esta inspeccion del archivo descargado no comprueba los datos existentes ni la
compatibilidad de la API anterior/candidata, y no constituye una revision
autenticada. El plan exacto debe revisarse antes de cualquier Publish; no se
ejecuto SQL ni se modifico Staging.

El publicador SQL de T033 conserva el responsable permitido `leoperetti` y consulta
por API el comentario original de una issue abierta de este repositorio. Rechaza
campos repetidos o contradictorios, autor no permitido y revisiones de otro release,
destino o plan. Despues de leer la revision, vuelve a generar DeployReport/Script
con las mismas opciones en archivos de evaluacion unicos, conserva los artefactos
revisados y las reevaluaciones anteriores, compara sus hashes y reconsulta la
aprobacion antes de Publish. Cualquier cambio exige una nueva revision.
La salida nativa de SqlPackage no se publica: puede contener credenciales, y su
texto no debe confundirse con el codigo de salida. Los errores de etapa son
explicitos y sanitizados. Azure SQL requiere validacion del certificado TLS.

T033 tambien permanece pendiente: las pruebas no sustituyen la revision autenticada
del release exacto ni verifican un perfil con permisos DacFx de publicacion
efectivamente probado. T032 ya completo su verificacion OIDC; T037 sigue pendiente
de resolver los grants minimos/temporales y probar su revocacion. La reevaluacion
no elimina la posible deriva entre comprobacion y Publish ni garantiza atomicidad
DacFx. El workflow debe serializar todo despliegue y tratar cualquier fallo como
bloqueo, sin rollback SQL automatico.

Validacion local: **81/81 pruebas focalizadas** satisfactorias, incluida publicacion
real del DACPAC en una base LocalDB descartable. Builds Release de API y SQL
satisfactorios; los scripts modificados pasan el parser PowerShell. Las pruebas
de Actions y revision GitHub usan respuestas simuladas, sin llamadas ni cambios
remotos. Las bases y directorios descartables se eliminan al terminar.

### Pruebas del bloqueo Production (T042)

`ProductionApprovalTests` ejecuta los entry points reales de preflight y Publish SQL
para diez escenarios: proteccion ausente/incompatible, rechazo, aprobacion de otro
release, ref no-main, reporte cambiado, artefacto expirado, dispatch, booleano y PR
de main. Usa paquetes, aprobaciones y estado Production exclusivamente sinteticos;
los comandos GitHub/Azure/Git/HTTP/SqlPackage se sustituyen por centinelas que fallan
y registran cualquier intento.

En cada caso se exige el bloqueo explicito por falta de aprobacion protegida antes
de consultar servicios o evaluar SQL, ningun comando externo y los mismos archivos
y hashes, incluidos version y escritura reciente simuladas. El archivo aportado como
aprobacion no se trata como acreditacion. Son pruebas del bloqueo incondicional
vigente, no de un mecanismo protegido disponible ni de una promocion satisfactoria.
T046-T052 y las pruebas de recuperacion completas T043 siguen pendientes.

Las diez pruebas nuevas y las regresiones de procedencia, revision SQL y recuperacion
parcial pasaron: **55/55**. El workflow staging local las incluye en su filtro, sigue
sin publicacion Azure y no se subio.

## Development local verificado

2026-11-07: se configuro `DatabaseConnectionString` en User Secrets con LocalDB y
autenticacion integrada; no se documentan valores de secretos. Se construyo el
DACPAC con MSBuild, se revisaron reporte y script create-only, y se publico en la
base local dedicada `authcraft_development` con
`BlockOnPossibleDataLoss=True` y `DropObjectsNotInSource=False`. El resultado tiene
13 tablas (incluida la tabla interna de refactorizacion) y los tres proveedores del
postdeploy. No se accedio a Azure SQL ni a Key Vault.

La API Development devolvio `/health/live` y `/health/ready` con estado `healthy`
y version `local`. La prueba de integracion SQL crea y elimina una base
`authcraft_environment_test_<guid>` y verifica DACPAC, postdeploy y
`ProviderRepository`; 1 prueba local SQL y 10 pruebas del pipeline pasaron. El
smoke de negocio creo tenant/cuenta aleatorios `example.test` solo en LocalDB:
login y GET roles respondieron 200 (4 roles), sin envio de correo. Las credenciales
se mantuvieron en memoria; no se leyeron ni sobrescribieron valores de User Secrets.
La API termino y el bootstrap quedo desactivado.
Con herramientas instaladas y User Secrets configurados, los builds, 51 pruebas
focalizadas (incluida publicacion DACPAC descartable y pruebas SQL con prefijo
seguro) y el smoke local tardaron **0.87 minutos**. No incluye instalacion inicial
de herramientas.

La suite completa registro 89/91 pruebas satisfactorias. Las dos fallas existentes
son `SuperadminBootstrapperTests` que esperan el tenant `cekide-app`, mientras el
cambio preexistente en `SuperadminBootstrapper.cs` busca `cekide-gym-web`. No se
modifico ese cambio local ajeno a la separacion de ambientes.

## Verificacion de inventario ejecutada

2026-11-06 02:53 UTC: `scripts\Test-EnvironmentInventory.ps1`, fuente
`live_metadata`, resultado **blocked**, salida 1. Esto es deteccion de discrepancias,
no fallo de las pruebas del verificador ni prueba de funcionamiento de la API.

- Staging: host
  `authcraft-api-staging-agcwhagyend9gkcj.westus3-01.azurewebsites.net`,
  base Paused en la lectura inicial; esa lectura fue anterior a la preparacion
  descrita en "Staging operativo verificado".
- Production: host
  `authcraft-api-c9hgcsaaaqbpfagf.westus3-01.azurewebsites.net`,
  base Paused; configuracion explicita de ambiente/vault pendiente.
- En esa lectura inicial el workflow production estaba activo. Posteriormente se
  deshabilito manualmente el workflow `374894897`; la aplicacion production no cambio.
- Las identidades observadas coinciden con la investigacion; las federaciones no
  acreditan por si solas que los secretos del workflow apunten a esas identidades.
- Pruebas `EnvironmentInventoryTests`: 9/9 satisfactorias, con fixtures JSON locales,
  sin llamadas Azure; antes de implementar el script fallaban por ausencia del mismo.

Ejecutar desde la raiz:

```powershell
.\scripts\Test-EnvironmentInventory.ps1
```

El resultado diferencia metadatos, permisos SQL pendientes y funcionamiento pendiente.
El modo `-SnapshotPath` es exclusivamente evidencia simulada para pruebas; sus
resultados indican `source=snapshot` y nunca sirven como verificacion Azure real.

### Bloqueo inicial T010 (resuelto para runtime staging)

Se intento la lectura autorizada de la configuracion SQL de staging mediante vault,
sin imprimir su valor ni cambiar secretos. No se logro abrir una conexion:

1. System.Data.SqlClient de Windows PowerShell rechazo el modo Authentication.
2. Microsoft.Data.SqlClient instalado con SqlPackage no pudo cargar sus dependencias
   en el host Windows PowerShell.

Un helper temporal .NET 10 con las dependencias existentes del proyecto permitio
abrir SQL posteriormente. El bloqueo de cliente queda resuelto para staging;
T010 sigue pendiente para los demas runtime/publicadores. En la verificacion inicial
no hubo cambios Azure; las operaciones autorizadas posteriores se registran abajo.

## Verificacion historica de Staging: 2026-11-06

La siguiente evidencia describe la preparacion y comprobaciones realizadas el
2026-11-06 UTC; no confirma que la version siga activa. En la lectura del
2026-11-09, `/health/live` devolvio 404 y el SHA corto de OneDeploy no se pudo
resolver a un commit de este repositorio. El preflight reciente no encontro un
despliegue exitoso verificable para usar como baseline. Ver el estado actual al
inicio de esta guia y el [runbook de recuperacion](deployment-recovery.md).

- URL: https://authcraft-api-staging-agcwhagyend9gkcj.westus3-01.azurewebsites.net
- API y DACPAC del run estable `41882690687`, commit
  `091fe2c9856fa8ba46387bc3e751fd21ca424662`. No se compilaron ni desplegaron
  los cambios locales pendientes de invitaciones/signup.
- Publicacion API: deployment ID `2d9d8af3f892419b9a109a9207e9f580`,
  estado Azure satisfactorio; posteriormente se verifico el comportamiento HTTP.
- La conexion previa de staging apuntaba al catalogo production y a un servidor
  incorrecto. Se corrigio exclusivamente su secreto para usar
  `authcraftdbbrasil.database.windows.net` / `authcraft-db-staging` con identidad
  administrada por sistema, sin password SQL.
- Revision y aprobacion del esquema:
  [issue #10](https://github.com/leoperetti/authcraft-api/issues/10).
  DACPAC y reporte/script se vincularon por SHA256 al run, commit y destino.
  La base estaba vacia; reporte sin alertas destructivas. Publicacion satisfactoria:
  13 tablas. Opciones: BlockOnPossibleDataLoss=True, DropObjectsNotInSource=False.
- Runtime principal: `d31e0e87-9d5c-4d09-b2e6-902ed3544bf4`; application/client ID:
  `7f7d852f-ba7c-4726-9ec7-ea2c0bc957eb`. RBAC: solamente Key Vault Secrets User
  en `authcraft-vault-staging`, sin adjuntar identidades publicadoras.
- Usuario SQL `authcraft-api-staging`: CONNECT, db_datareader y db_datawriter.
  Para `CREATE USER ... WITH SID ..., TYPE = E`, el SID del service principal se
  vinculo al **application/client ID**, no al object ID. La vinculacion inicial al
  object ID produjo error 18456 y se corrigio transaccionalmente, comprobando que
  el usuario no poseia schemas y sin tocar tablas ni ampliar privilegios.
- Una sesion con el token real del runtime verifico db_owner=0 y ALTER=0 en staging.
  La conexion a production fue denegada (18456); no se ejecuto ninguna consulta
  production. El primer intento recibio 40613 por base pausada y no se conto como
  prueba de aislamiento; el reintento autorizado obtuvo la denegacion efectiva.
- Acceso al vault production con identidad staging denegado (HTTP 403), ademas
  de la revision RBAC. No se leyeron valores de secretos production.
- Ambiente explicito Staging para ASPNETCORE y DOTNET, vault staging, issuer y
  audience `authcraft-staging`; clave JWT independiente en su vault.
- Bootstrap inicial con tenant y cuenta sinteticos staging, sin correo real.
  Despues se persistio `SuperadminBootstrap__Enabled=false` y se reinicio la API.
- Tras el reinicio: POST `/api/authentication/login` **200** y GET `/api/roles`
  con el accessToken devuelto **200**. El contrato estable usa `accessToken`,
  `tokenType`, `expiresIn`, `user`. Credentials y tokens no se registraron.

La preparacion fue manual y autorizada, no una ejecucion del workflow actual.
Los bloqueos de build, reconciliacion y credenciales descritos en el registro
original corresponden al estado observado entonces; posteriormente se reconcilio
el workflow de Staging y se completaron los gates de T028/T029/T032. El workflow
El workflow remoto Staging solo valida y prepara una revision; aun no incluye la
ruta local de reanudacion ni una publicacion ejecutada.
Production sigue `disabled_manually`.
No se modificaron configuracion, secretos, esquema ni publicacion production.
La comprobacion de conexion autorizada pudo reactivar su base pausada.

### Usuario SQL del publicador: evaluacion minima verificada

2026-11-06, 16:29-16:38 UTC: se inspeccionaron usuarios, SID, roles y permisos en
`authcraft-db-staging` con una sesion Entra protegida de auditoria. La base estaba
Paused: dos conexiones devolvieron 40613; despues ARM informo Online y la consulta
finalizo. La reactivacion estaba autorizada y puede generar consumo. Los primeros
errores no se interpretaron como denegacion de acceso.

La consulta confirmo que el publicador staging no tenia usuario contenido para su
application/client ID. Con autorizacion especifica se creo
`authcraft-staging-schema-publisher`, EXTERNAL_USER/EXTERNAL, SID vinculado a
`a2382f63-d653-47f3-82ca-9f2696f63f0a`, con solo CONNECT y VIEW DEFINITION.
La transaccion rechazo un principal previo por nombre/SID y verifico ausencia de
ALTER/CONTROL/CREATE TABLE/db_owner antes de confirmar. Una segunda sesion
reconsulto el catalogo: exactamente un usuario para ese SID, sin roles adicionales.
El runtime `authcraft-api-staging` conservo CONNECT, db_datareader y db_datawriter.
No se modificaron tablas, datos de negocio, bootstrap, correo ni Production.

Comprobacion efectiva mediante EXECUTE AS USER en la base staging:

| Control | Resultado |
|---------|-----------|
| CONNECT / VIEW DEFINITION | 1 / 1 |
| ALTER / CONTROL de base | 0 / 0 |
| CREATE TABLE / ALTER schema dbo | 0 / 0 |
| SELECT / INSERT en dbo.providers | 0 / 0 |
| db_owner | 0 |
| Tablas visibles por metadatos | 13 |

Esto demuestra un perfil de revision de metadatos, **no un publicador habilitado**:
no puede ejecutar el postdeploy ni aplicar DDL. La impersonacion SQL no verifica
obtencion/intercambio OIDC ni login con el token real de la identidad administrada.
T010/T011/T037 siguen parciales: faltan los otros principals/aislamiento requeridos,
la revision de privilegios de publicacion y una prueba real DacFx con ese token.
No usar el token de auditoria como sustituto del publicador en Actions.

Scripts revisados conservados en la evidencia local privada de esta sesion,
directorio `files` (no contienen tokens ni passwords):

- `staging-sql-review-user.sql`, SHA-256
  `e4239c98178905298779277450e3c4ca58df64b23293a9dc926476dbf1943653`.
- `staging-sql-review-permissions.sql`, SHA-256
  `1b4c2aba849855aa1c32fba0b6aa04a9bca0c42671623bb9cbae47fc4fe8cac6`.

El primer script ya se aplico y no se debe reejecutar como grant automatico: bloquea
si el usuario existe. El segundo solo consulta permisos bajo impersonacion.
El helper temporal .NET utilizado para conectar no forma parte del workflow.
La salida nativa de excepciones/token no se registro; se conservaron solo codigos
SQL sanitizados y resultados de catalogo acotados.

Validacion local relacionada: el test `LocalEnvironmentSqlTests` publica el DACPAC
real en una base descartable, crea un usuario SIN LOGIN con los mismos grants y
comprueba visibilidad de las 13 tablas y negacion de DDL/datos/postdeploy. No simula
un login Entra. **77/77 pruebas focalizadas** satisfactorias; builds API/SQL
correctos y cero bases `authcraft_environment_test_*` restantes.

### Diagnostico OIDC preparado, sin ejecutar remotamente

2026-11-06: el usuario eligio preparar/verificar localmente, sin commit ni push.
Se agrego a la propuesta local staging el job `verify-staging-publisher`, dependiente
de `validate`, solo para workflow_dispatch main con
`verify_staging_publisher=true`. El input tiene default false. PR no recibe permisos
Azure y los uploads API/DACPAC/manifiesto permanecen en el job validate.
Solo el diagnostico obtiene id-token:write e inicia sesion como el publicador staging
con los IDs publicos aprobados. No modifica firewall, grants, datos ni aplicaciones.

`Test-StagingPublisher.ps1` exige contexto Actions/main/repositorio/dispatch,
verifica por Azure CLI tenant/suscripcion/service principal y obtiene un token SQL
transitorio. Lo enmascara para Actions, lo pasa solo por entorno al proceso de
pruebas y restaura el entorno al terminar. No registra JWT ni salida nativa.
La salida sanitizada `state=verified` identifica exclusivamente
`staging_publisher_sql_review_only` con `deployment_authorized=false`.

El test remoto usa servidor/base fijos y TLS validado; comprueba el usuario y SID
autenticados, CONNECT/VIEW DEFINITION, ausencia de DDL/control/roles/grants directos
adicionales y permisos de escritura sobre tablas visibles. Solo consulta catalogos
y funciones de permisos; no lee filas de negocio ni ejecuta DDL/DML.
Hay tres intentos como maximo por 40613 y timeout global de tres minutos, mas
timeout de job de diez minutos. Una base pausada puede reactivarse al conectar.
Un fallo no modifica red ni intenta cambiar permisos.

El wrapper requiere un TRX con exactamente el test esperado ejecutado y aprobado:
skip, filtro sin coincidencias, reporte ausente o fallo no acreditan login.
El TRX temporal se elimina sin subirlo como artefacto. Los errores nativos se
retienen fuera del log para proteger credenciales; se informa fallo explicitamente.

Las pruebas aisladas verifican contexto, identidad incorrecta, errores de token/SQL,
reporte ausente, skip/test equivocado, restauracion del entorno y limpieza temporal.
La consulta y assertions SQL se comparten con el fixture LocalDB del DACPAC;
tambien se comprueba que un INSERT concedido solo en la base descartable invalida
el perfil de revision, sin insertar datos. Esto no sustituye un token OIDC real.
T037 sigue pendiente; ningun job de despliegue fue habilitado ni ejecutado.

Resultado final de esta preparacion: **92 pruebas aprobadas y 1 omitida**
(la conexion OIDC remota, deliberadamente desactivada). DACPAC real y perfil de
permisos comprobados en LocalDB; cero bases descartables restantes. Build SQL y
parser PowerShell satisfactorios. Las pruebas del workflow comprueban su estructura
y gates especificos; no sustituyen el parser/runner de GitHub. No hubo conexiones
Azure, cambios remotos, commit, push ni dispatch en esta preparacion.

### Diagnostico OIDC real satisfactorio y siguiente evaluacion DacFx

Posteriormente el usuario autorizo commit/push y diagnostico. La primera ejecucion
`42264664298` fallo en pruebas, antes de Azure: los procesos Windows PowerShell del
runner no resolvian Get-FileHash. Se agrego el directorio de modulos Windows
PowerShell a PSModulePath solo durante ese paso y se comprueba su disponibilidad
antes de ejecutar las pruebas; no se omiten casos ni se cambian permisos.

La ejecucion [42265429985](https://github.com/leoperetti/authcraft-api/actions/runs/42265429985)
del commit `5f3ea0d248fa002e2b2dcfa0be337e269614bb0e` termino success en los jobs
`validate` y `verify-staging-publisher`. API, DACPAC y release-manifest quedaron
retenidos/no expirados al consultar Actions. El segundo job verifico login OIDC,
usuario/SID y permisos SQL reales con el token del publicador, no por impersonacion.
No ejecuto Publish, no desplego API ni concedio permisos. Production permanece
disabled_manually. Este run no acredita un deploy-staging satisfactorio ni
capacidad DacFx de publicacion: T037 sigue pendiente.

Siguiente diagnostico preparado localmente: input `evaluate_staging_schema`,
default false, tambien exige workflow_dispatch main y ejecuta primero el check
de identidad/permisos. Descarga DACPAC/manifiesto del mismo run con download-artifact
sin run-id externo. Resuelve SqlPackage de Visual Studio mediante vswhere y falla
explicitamente si no existe; no instala herramientas ni modifica firewall.
El wrapper reutiliza `Invoke-DatabaseDeployment.ps1` exclusivamente con
Mode=Evaluate/TargetEnvironment=staging y el token transitorio ya comprobado.
El helper valida commit/run/hash del DACPAC antes de invocar DeployReport/Script.
Nunca se llama Publish. Conserva las opciones SQL/TLS existentes y rechaza planes
destructivos. Reporte/script se retienen en el artefacto privado
`staging-schema-review` durante 30 dias solo si la evaluacion tuvo exito.

La evaluacion puede fallar por permisos DacFx insuficientes aun con login correcto;
ese fallo es evidencia de bloqueo, no autorizacion para ampliar grants. El postdeploy
se genera para revision, no se ejecuta. La salida mantiene deployment_authorized=false.
Las pruebas controladas cubren evaluacion correcta, fallo SqlPackage, DACPAC alterado
y archivos faltantes, comprobando que solo se invocan DeployReport/Script y que se
restaura el entorno. Este diagnostico no sustituye preflight de publicacion,
revision SQL acreditada ni aprobacion Production.

### Resultado real de evaluacion DacFx: bloqueado por permisos

El usuario autorizo subir y ejecutar este diagnostico de solo lectura.
Run [42267628280](https://github.com/leoperetti/authcraft-api/actions/runs/42267628280),
commit `c5924986b775a81231db8625fdafd0d16686459e`: validate satisfactorio,
login/chequeo de permisos satisfactorio y fallo en SqlPackage DeployReport.
No se ejecutaron Script ni Publish; el artefacto de revision SQL no se genero.

Se agrego diagnostico sanitizado de SqlPackage: solo codigo de salida,
categoria fija y codigos SQL numericos prefijados, sin salida nativa libre.
La salida se captura transitoriamente en memoria y se descarta; las pruebas
verifican que un token sintetico y texto nativo no aparecen en el error.
Las categorias son orientativas: no identifican por si mismas el grant faltante.

Run [42269089780](https://github.com/leoperetti/authcraft-api/actions/runs/42269089780),
commit `5c4f31707145bcbba393ff67b233fcf91cb8710b`: validate satisfactorio;
el diagnostico volvio a pasar login/usuario/permisos y fallo en DeployReport con
`exit_code=1`, `diagnostic_category=permission_denied`, sin codigo SQL especifico.
SqlPackage si estaba disponible en el runner; no se trata de falta de herramienta.
No atribuir el fallo a VIEW DATABASE STATE ni concederlo por inferencia: la evidencia
actual solo identifica la categoria, no el permiso concreto denegado.

**T033/T037 siguen bloqueadas para DacFx**. CONNECT/VIEW DEFINITION permitieron
login y consulta de catalogos, pero no alcanzaron para esta evaluacion con DacFx.
Siguiente paso: identificar el permiso concreto requerido y ensayar el minimo
en un destino descartable antes de solicitar/aplicar otra correccion staging.
No conceder db_owner/administracion de servidor como atajo, ni usar el token
administrativo de auditoria para hacer pasar el workflow del publicador.

Validacion de los cambios: **74/74 pruebas focalizadas** locales satisfactorias
antes de cada push; ambos runs confirmaron el job validate. No se ampliaron grants,
modifico firewall, publico API, ejecuto postdeploy ni conecto Production durante
estos diagnosticos. Los paquetes retenidos de estos runs no acreditan un release
staging desplegado/validado ni sustituyen la revision SQL previa a Publish.

### Correccion minima autorizada: lectura de dependencias del esquema

La impersonacion SQL del principal esperado en `authcraft-db-staging` comprobo
`can_read_dependencies=0` y SQL **229** al consultar
`sys.sql_expression_dependencies`. No es una prueba de login OIDC; ese login ya
se verifico independientemente en Actions.
[Microsoft documenta](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-sql-expression-dependencies-transact-sql#permissions)
VIEW DEFINITION de base y SELECT sobre esa vista como requisitos de lectura.

Tras la aprobacion explicita **Aplicar y verificar**, se aplico:

```sql
GRANT SELECT ON OBJECT::sys.sql_expression_dependencies
TO [authcraft-staging-schema-publisher];
```

La transaccion valido base, tipo externo y SID exactos del principal, ausencia
de roles/grants inesperados y privilegios resultantes antes de COMMIT.
Conexion independiente: `can_read_dependencies=1`, consulta sin error.
ALTER/CONTROL/CREATE TABLE/SELECT e INSERT de providers/db_owner y numero de
tablas de usuario modificables permanecen en cero. No se modificaron permisos
runtime, firewall, tablas/datos ni Production. SQL serverless requirio reintentos
por 40613 durante Resuming; no se interpreto como fallo de autorizacion.

El perfil remoto y la prueba LocalDB solo permiten ese SELECT de objeto
adicional, sin grant option ni columnas individuales. **37/37 pruebas locales**
correctas, incluida publicacion del DACPAC en base LocalDB descartable,
rechazo del perfil sin SELECT de dependencias y rechazo de INSERT inesperado.

Run [42299486643](https://github.com/leoperetti/authcraft-api/actions/runs/42299486643),
commit `ef0c8e7`: validate correcto y login/perfil SQL actualizado correcto, pero
DeployReport continuo fallando con `permission_denied` y mencion de SELECT sin
objeto identificable en diagnostico publico.

Con autorizacion posterior se activo diagnostico cifrado opt-in (RSA-OAEP; llave
privada local, artefacto retencion 1 dia) y se identifico el objeto exacto:
`dbo.__RefactorLog`. Se concedio el segundo permiso minimo:

```sql
GRANT SELECT ON OBJECT::dbo.__RefactorLog
TO [authcraft-staging-schema-publisher];
```

Validacion independiente: `can_read_dependencies=1`, `can_read_refactorlog=1`,
ambas consultas en cero error y DDL/DML/lectura negocio siguen en cero.
Run [42386493432](https://github.com/leoperetti/authcraft-api/actions/runs/42386493432),
commit `a7a0fcb`: jobs `validate` y `verify-staging-publisher` en success; se
generaron DeployReport/Script de solo lectura (`staging-schema-review`) sin
Publish, sin postdeploy y sin cambios en Production.

El diagnostico publico conserva unicamente categorias, codigos, nombres fijos
de permisos y nombres de objetos `sys` extraidos de una sentencia de denegacion
estrictamente reconocida. No imprime salida nativa libre ni tokens.
Con el desbloqueo de Evaluate, T037 avanza en verificacion OIDC + evaluacion
DacFx. Siguen pendientes los permisos minimos para **Publish** y su evidencia
de revision (T033/T037), sin ampliar roles por anticipacion.

### Evaluacion actual para preparar Staging: 2026-11-09

Se ejecuto el workflow remoto de validacion con `evaluate_staging_schema=true`:
[run 42557944943](https://github.com/leoperetti/authcraft-api/actions/runs/42557944943),
commit `b60306b9a2f53246a9259d1a567ec5d0ad40a5f3`. Los jobs `validate` y
`verify-staging-publisher` terminaron `success`; el run valido el login OIDC,
el principal esperado y el DeployReport/Script DacFx. Los artefactos `api`,
`dacpac`, `release-manifest` y `staging-schema-review` se retienen hasta el
2026-12-09. El workflow actual no contiene `Publish` ni despliegue de Web App.
No se modificaron SQL ni la API de Staging.

Hashes SHA-256 de la evaluacion exacta:

- `staging-deploy-report.xml`:
  `e06fff4b57ff74c22cce5d7241a30a031378f7d2ba084e74929ca5d4bbd8c466`
- `staging-deploy.sql`:
  `b58e1ff68e0b91805380e137ec6cba33b34b101c520f720396aea1139bf86746`

Impacto que debe revisar el responsable antes de autorizar una publicacion:

- El reporte no presenta Alerts; propone eliminar dos constraints existentes,
  alterar `dbo.invitations` y crear siete constraints.
- El script agrega columnas nullable y una `rowversion` a `dbo.invitations`;
  agrega la FK y checks con `WITH NOCHECK`, por lo que las filas existentes no se
  validan retroactivamente al crear esas restricciones.
- El post-deploy contiene tres `INSERT` condicionales a `dbo.providers` (`google`,
  `apple`, `github`), solo si falta cada proveedor.
- El script de DacFx cambia Query Store y usa `WITH ROLLBACK IMMEDIATE`; puede
  terminar transacciones activas y causar interrupcion breve de conexiones durante
  la publicacion. No se ejecuto.
- La API desplegada actualmente en el hostname Staging de ARM devuelve HTTP 404 en
  `/health/live`; los app settings visibles no incluyen una version. El smoke de
  candidato no esta acreditado y no se debe confundir este estado con un release
  publicado por este workflow.

Al cerrar la evaluacion inicial, el [issue publico #11](https://github.com/leoperetti/authcraft-api/issues/11)
todavia no tenia una revision estructurada. El 2026-11-09 el usuario publico el
comentario autenticado `6731993007`, aprobado por el contrato para ese run, commit,
destino, DACPAC y hashes report/script. Esa aprobacion corresponde solo al plan
original de `42557944943`; no autoriza un plan regenerado con opciones distintas.

### Revalidacion de permisos y bloqueo de Production: 2026-11-09

- Se verifico el comentario `6731993007` del autor permitido `leoperetti` y los
  campos exactos de `42557944943`. La API Staging sigue devolviendo 404 en
  `/health/live`; el paquete de ese run no se desplego.
- Tras autorizacion del usuario, se eliminaron de GitHub Actions los tres secretos
  `AZUREAPPSERVICE_CLIENTID_*`, `AZUREAPPSERVICE_TENANTID_B25DCFFFC916496E99D5889500CCF662`
  y `AZUREAPPSERVICE_SUBSCRIPTIONID_67CD7042E7E9489EAF1EFD52BDC126E5` referenciados
  por el workflow antiguo de Production. Solo permanecen los tres secretos
  `AZUREAPPSERVICE_*` de Staging; no se leyeron valores.
- GitHub confirma `.github/workflows/main_authcraft-api.yml` en
  `disabled_manually` y el workflow Staging activo, de validacion solamente.
  En Azure, `oidc-msi-aede` y `oidc-msi-a9fa` no tienen federaciones; el unico
  subject GitHub observado es el de `authcraft-api-st-id-97cc` para `main`.
  `oidc-msi-aede` no tiene asignaciones RBAC sobre la Web App Production.
  Las politicas SCM y FTP de Production siguen `allow=false`.
- No se modifico la identidad runtime ni el vault Production. ARM reporto
  `authcraft-db` y `authcraft-db-staging` como `Online` en la lectura mas reciente.
  El intento local de consulta SQL de solo lectura contra Staging con `sqlcmd -G`
  agoto el tiempo de inicio de sesion; no obtuvo permisos verificables ni ejecuto
  cambios.
- La documentacion de SqlPackage indica `db_owner` para publicar en una base
  existente ([SqlPackage Publish](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-publish?view=sql-server-ver17)).
  El usuario autorizo conceder `db_owner` temporalmente al publicador Staging para
  una sola publicacion, con revocacion inmediata al finalizar. La concesion aun no
  se aplico; no se ejecuto Publish y la conexion local no permitio verificar una
  ruta administrativa para conceder/revocar el rol.
- Para evitar el cambio de Query Store y `WITH ROLLBACK IMMEDIATE` del plan
  original, `Invoke-DatabaseDeployment.ps1` agrega `ScriptDatabaseOptions=False`.
  Un ensayo DacFx en LocalDB desechable genero un script sin `ALTER DATABASE`, pero
  `/Action:Publish` fallo con SQL72014/SQL72045 por permisos. Los grants se limitaron
  al LocalDB, que fue eliminado.

### Lectura ARM limitada para preflight Staging: 2026-11-09

Tras autorización explícita, se asignó el rol integrado `Reader` al principal
`authcraft-api-st-id-97cc` (`2a446033-801f-444c-985e-6519c4661632`) únicamente en
los tres recursos Staging:

| Recurso | Scope | Asignación Reader |
|---------|-------|-------------------|
| App Service `authcraft-api-staging` | Recurso App Service | `7064d0c9-456a-49a3-91dd-5fc75a3beba4` |
| SQL `authcraft-db-staging` | Base individual | `d04c0928-44e2-472b-9837-4e4f7e71cb99` |
| Key Vault `authcraft-vault-staging` | Vault individual | `fc59503b-e58c-42fd-a8f6-3cbb9f6e80d4` |

La auditoría efectiva del principal devuelve exactamente esos tres `Reader` y el
`Website Contributor` ya existente, limitado a `authcraft-api-staging`; no se
observaron asignaciones en Production ni fuera de los scopes Staging. `Reader` es
solo lectura ARM: no concede permisos de datos SQL ni lectura de valores de secretos
del Key Vault. No se asignó `db_owner` ni se ejecutó Publish.

El inventario ejecutado localmente después del cambio sigue mostrando Staging
`confirmed`; Production queda `blocked` por la falta de `vault_name` y
`runtime_environment` explícitos, como antes. Esto fue lectura desde la sesión
local autorizada y aún no demuestra que la identidad OIDC pueda completar el
preflight desde Actions. La integración y verificación de ese gate permanecen
pendientes.

### Evaluacion Staging con opciones finales: 2026-11-09

El commit `7bd8f1ce0c5275f12dfb6b180270d7ba3edf4c1a` genero el run
[42564903495](https://github.com/leoperetti/authcraft-api/actions/runs/42564903495).
`validate` y `verify-staging-publisher` terminaron `success`; el OIDC y la
evaluacion DacFx de solo lectura pasaron. Los artefactos del run se retienen
30 dias, hasta 2026-12-09. No se ejecuto Publish ni se desplego API.

Hashes SHA-256 del release y plan exactos:

- DACPAC: `6528710646496b2026778e36c00cfb773de055708775d09fc620bdc15008e8c2`
- API ZIP: `06dfe538f39cdbbbbbe63ba879af50bd6288368068c42f428b6721feb6f306c6`
- DeployReport: `e06fff4b57ff74c22cce5d7241a30a031378f7d2ba084e74929ca5d4bbd8c466`
- SQL: `3dfab7b37839bbc09b4c00e7d1768df6cd27f569a34c7cd7fe131fb9631c9947`

El DeployReport no contiene Alerts. El plan:

- Elimina y recrea `CK_memberships_status` y `CK_tenants_status`.
- Altera `dbo.invitations` para agregar 13 columnas nullable y `row_version`
  (`rowversion`); el cambio puede requerir bloqueo de tabla y asigna una version
  a las filas existentes.
- Crea una FK y seis checks con `WITH NOCHECK`; las filas existentes no se
  validan retroactivamente, aunque las restricciones aplican a escrituras futuras.
- Ejecuta tres `INSERT` condicionales en `dbo.providers` (`google`, `apple`,
  `github`) cuando falte cada proveedor.
- No contiene `DELETE`, `TRUNCATE`, `ALTER DATABASE`, Query Store ni
  `ROLLBACK IMMEDIATE`.

La revision autenticada debe vincular `run_id`, `commit_sha`,
`staging_baseline_sha`, destino, hash DACPAC, las aceptaciones explicitas
`api_compatibility=accepted`, `data_impact=accepted` y `recovery_plan=accepted`,
`block_on_possible_data_loss=true`, `drop_objects_not_in_source=false`,
`script_database_options=false` y hashes report/script. Cada campo debe aparecer
una sola vez; ausencia, duplicacion o deriva bloquean Publish. El run preliminar
`42563790492` no se debe aprobar: su SHA SQL coincide, pero el codigo de revision
aun no exigia `script_database_options`. El run `42564903495` no se debe aprobar tras cambiar el contrato/codigo. Una
nueva evaluacion del release final se registro abajo; aun requiere revision
autenticada con todos los campos requeridos.

La API Staging continua devolviendo 404 en `/health/live`; la version de la API
actual no esta acreditada. El ultimo log de OneDeploy de App Service registra el
identificador corto `2d9d8af3f8`, que GitHub no resuelve como commit del repositorio.
No existe aun un `deploy-staging` exitoso del workflow nuevo que sirva como baseline
verificable. El usuario acepta un bootstrap inicial Staging con baseline desconocido,
pero solo si Actions confirma que no existe un despliegue satisfactorio previo y la
revision autenticada del release exacto acepta expresamente el baseline, compatibilidad
API, impacto de datos y recuperacion antes de Publish. Esa decision no aprueba el SQL
ni autoriza una publicacion por si sola. Production sigue sin publicador efectivo y
no se modifico.

### Evaluacion posterior de Staging: run 42664956766

El commit `6a2a123149073b84bd61b0fc76a781294fdc7ffa` completo con exito los
jobs `validate`, `verify-staging-publisher` y `preflight-staging`. La evaluacion
DacFx fue de solo lectura y el preflight devolvio el estado de revision
`ready_for_review`, `deployment_authorized=false` y baseline inicial `unknown`.
Los artefactos `api`, `dacpac`, `release-manifest` y `staging-schema-review` se
retuvieron por 30 dias. La evaluacion no modifico SQL ni API.

Hashes SHA-256 del release y plan:

- DACPAC: `9fe13dc1de83251e255b5de934ce2866e88b7b6a2f0202b6ce7e16f09d9b6458`
- API ZIP: `793da3a0bf7f30d1c9eb5dd9b0ab0815716ba3c8949c7cda9b8fa1cd5bd0a097`
- DeployReport: `e06fff4b57ff74c22cce5d7241a30a031378f7d2ba084e74929ca5d4bbd8c466`
- SQL: `3dfab7b37839bbc09b4c00e7d1768df6cd27f569a34c7cd7fe131fb9631c9947`

El DeployReport no contiene Alerts. El plan coincide con la evaluacion anterior:
elimina/recrea dos CHECK constraints, altera `dbo.invitations` para agregar
columnas nullable y `row_version`, crea una FK y seis checks `WITH NOCHECK`, e
inserta condicionalmente `google`, `apple` y `github` en `dbo.providers`. El script
no contiene `DELETE`, `TRUNCATE`, `DROP TABLE` ni `ALTER DATABASE`. Los checks
`WITH NOCHECK` no validan retroactivamente filas existentes. Esta inspeccion no
demuestra compatibilidad con los datos ni con la API anterior/candidata.

El issue #11 no contiene una revision autenticada que identifique el run
`42664956766` ni el campo obligatorio `script_database_options=false`; la
revision debe vincular los hashes, el baseline, el destino y las aceptaciones
explicitas requeridas antes de que T033 pueda avanzar. La comprobacion OIDC de
`verify-staging-publisher` confirma solo permisos de lectura/revision; no verifica
ni concede privilegios DacFx de Publish. No se obtuvo `db_owner` ni se intento
Publish. Por tanto T033, T035, T037 y la publicacion Staging siguen pendientes.

### Evaluacion actual de Staging: run 42669270551

Con autorizacion para evaluacion de solo lectura, el commit
`63ed699446bd3b8d8b1da934055208ecbaf3eb90` completo `validate`,
`verify-staging-publisher` y `preflight-staging`. La verificacion de publicador
confirmo OIDC y permisos de lectura/revision; no acredita permisos DacFx para
Publish. El preflight valido el estado `ready_for_review`, destino `staging`,
`deployment_authorized=false` y baseline inicial `unknown`. No ejecuto Publish,
postdeploy, despliegue API ni smoke. Los artefactos se retienen por 30 dias.

Hashes SHA-256 del release y plan:

- DACPAC: `69761df641a90d61858579059417a6d63006fa14c5672d3d92e7b17b434bc1aa`
- API ZIP: `a73ae929eaf72a940ab1477eb1703a2e4d243cd08f011d9beef320ca3eba498a`
- DeployReport: `e06fff4b57ff74c22cce5d7241a30a031378f7d2ba084e74929ca5d4bbd8c466`
- SQL: `3dfab7b37839bbc09b4c00e7d1768df6cd27f569a34c7cd7fe131fb9631c9947`

Los hashes DeployReport/SQL coinciden con la evaluacion anterior; los hashes
API/DACPAC corresponden a este commit y run, y deben citarse en cualquier revision.

**Bloqueo de revision adicional:** al inspeccionar directamente el artefacto
`staging-schema-review` de este run se confirmaron siete instrucciones
`WAITFOR DELAY '00:00.010'`, cada una inmediatamente antes de un
`ALTER TABLE ... WITH NOCHECK ADD CONSTRAINT` (lineas 81, 92, 103, 114, 125,
136 y 147 del SQL). El SHA-256 del archivo descargado coincide con el manifiesto:
`3dfab7b37839bbc09b4c00e7d1768df6cd27f569a34c7cd7fe131fb9631c9947`.
No se encontro `WAITFOR` en el codigo versionado, el post-deploy fuente ni el
`postdeploy.sql` interno del DACPAC; tampoco en `model.xml`. El DACPAC exacto
contiene `ProductVersion 170.4.53.0`, pero el log del job no registra la version
del ejecutable SqlPackage usado para generar el script. El workflow invoca
`Invoke-DatabaseDeployment.ps1` en modo de evaluacion, que ejecuta
`SqlPackage /Action:Script`; aun no se ha establecido si SqlPackage/DacFx lo
emitio o si intervino otra parte del runner/generacion. No se debe aceptar ni
publicar este plan hasta reproducir y explicar esas siete instrucciones; si el
SQL se vuelve a generar, hay que reevaluar y aprobar los hashes nuevos exactos.

La comparacion especifica de los dos checks de status encontro una explicacion
probable para el `DROP`/`CREATE`, no una deriva funcional: las fuentes y el modelo
del DACPAC usan expresiones `IN (...)`; el modelo extraido de la base Staging las
expresa como disyunciones equivalentes (`[status]='...' OR ...`). Se extrajo el
esquema de Staging con SqlPackage 170.5.96.0 en modo `ExtractAllTableData=False`;
la operacion fue de solo lectura y no incluyo datos. Una comparacion offline
`SqlPackage /Action:DeployReport` entre ese DACPAC de destino y el DACPAC exacto
del release reprodujo los mismos dos `Drop` y dos `Create` para
`CK_memberships_status` y `CK_tenants_status`. Esto confirma que DacFx detecta una
diferencia entre los modelos serializados, aunque las expresiones son
semanticamente equivalentes; no prueba si la causa interna es exactamente el
normalizador de SQL Server o la comparacion de DacFx. El DeployReport/XML tampoco
presenta una explicacion por operacion. No se hallaron `IgnoreWhitespace` ni
`IgnoreKeywordCasing` en el proyecto/opciones.

Consulta SQL de solo lectura a `sys.check_constraints` en Staging, al 2026-11-09:
ambos checks existen sobre sus tablas esperadas, con definiciones equivalentes a
la fuente, `is_disabled=0`, `is_not_trusted=0`,
`is_not_for_replication=0` e `is_system_named=0`. Por ello, las filas actuales
estan cubiertas por constraints habilitadas y confiables; no hay evidencia de
deriva en estos dos checks. Sin embargo, el plan candidato los quita y los vuelve
a agregar con `WITH NOCHECK`, lo cual si perderia la confianza sin necesidad
funcional aparente. En `ADD CONSTRAINT`, `WITH NOCHECK` no valida filas existentes
y deja la constraint habilitada pero no confiable; nuevas escrituras siguen
validadas salvo que se deshabilite por separado. El optimizador no esta "roto"
globalmente, pero no puede usar una constraint no confiable para ciertas
simplificaciones o mejoras de planes.

Del total de siete, la FK y cuatro checks de `dbo.invitations` dependen de columnas
que el mismo plan agrega como nullable sin default (`accepted_identity_id`,
`purpose`, `dni`, `registered_dni`, y las columnas usadas por `student_profile`).
En el delta previsto, las filas existentes obtendrian NULL en esas columnas, lo
cual satisface sus condiciones actuales; aun asi, el script no valida/trustea
formalmente esas filas. Los dos checks de status actualmente estan confiables,
pero la recreacion planeada con `NOCHECK` los degradaria. Antes de reconsiderar
el plan, corregir/revisar la generacion para que no reemplace innecesariamente
checks equivalentes y para que las constraints nuevas se validen (`WITH CHECK
ADD`); investigar por separado la procedencia de los `WAITFOR`. Cualquier nuevo
script exige revisar sus hashes exactos. No aprobar ni publicar el plan actual.

Verificacion adicional 2026-11-09: `dbo.providers.name` tiene en el proyecto la
constraint `UQ_providers_name UNIQUE`. Una consulta de solo lectura al catalogo
de Staging confirmo el indice `UQ_providers_name`, unico, habilitado, sin filtro
y con `name` como unica columna clave. No se necesitan cambios para agregar
unicidad a este destino; esto no hace atomico por si solo un `IF NOT EXISTS`
seguido de `INSERT` bajo concurrencia.

La ayuda de SqlPackage local 170.5.96.0 indica
`ScriptNewConstraintValidation=True` por defecto: puede crear constraints con
`NOCHECK` y validarlas al final. Por tanto, la revision debe comprobar el script
completo y el estado final, no sustituir texto `WITH NOCHECK` a ciegas. El script
historico inspeccionado no contenia esa validacion final. La reproduccion contra
Staging con el DACPAC exacto sigue pendiente: la descarga de GitHub fallo con
HTTP 503 `Egress is over the account limit`. El DACPAC de cache local tiene otro
SHA-256 y no se uso como sustituto. No se ejecuto Script contra Staging en este
intento ni se intento Publish. Sigue pendiente explicar los `WAITFOR` con una
reproduccion de la herramienta y registrar su version y opciones.

#### Reproduccion completada y bloqueo automatico

Se recupero despues el DACPAC exacto del run `42669270551`, verificando SHA-256
`69761df641a90d61858579059417a6d63006fa14c5672d3d92e7b17b434bc1aa`.
SqlPackage local 170.5.96.0, invocado directamente con `/Action:Script` contra
Staging y las tres opciones del publicador, reprodujo los siete
`WAITFOR DELAY '00:00.010'` y los siete `WITH NOCHECK`. Por tanto, se corrige la
sospecha anterior: los WAITFOR si son reproducibles en la salida de Microsoft
SqlPackage/DacFx, sin edicion manual ni wrapper del pipeline. No se ha demostrado
su finalidad interna; no se deben presentar como prueba de manipulacion. El
literal `00:00.010` tambien existe en la DLL local
`Microsoft.Data.Tools.Schema.Sql.dll`, junto a mensajes de integracion del motor.

El SQL nuevo tiene SHA-256
`3e5b2e6bb5f8a185472096fb23933fec9756715161ffe40f81ee3fd27018023f`;
no sustituye la evidencia del runner ni autoriza ese hash. Una segunda generacion
con `/p:ScriptNewConstraintValidation=True` explicito produjo el mismo hash y
tampoco incluyo `WITH CHECK CHECK CONSTRAINT` al final. No hubo Publish, DDL, DML
ni concesion de permisos remotos.

Como primera proteccion local adicional, `Invoke-DatabaseDeployment.ps1` bloqueo
Publish si el SQL generado contiene el token `NOCHECK`, antes de consultar la
aprobacion o invocar Publish. Es un gate lexical conservador: tambien rechaza
scripts con validacion diferida y menciones en comentarios; no pretende analizar
SQL arbitrario ni acreditar su estado final. Evaluate sigue generando/retornando
evidencia para diagnostico. No existe flag de bypass ni se reemplazan clausulas
del script a mano. Habilitar un plan con validacion diferida requiere implementar
y probar una comprobacion de confianza final, no quitar este gate.

Las tres regresiones nuevas fallaron antes del cambio (CHECK sin validar, FK sin
validar y constraint deshabilitada); despues, 51 pruebas de review, recuperacion y
workflow pasaron, incluida la preservacion de Evaluate. Falta corregir/revisar
la generacion del plan, construir/evaluar el nuevo candidato y obtener revision
autenticada de sus hashes. Este bloqueo no completa T033 ni habilita Staging.

El issue #11 no contiene una revision identificada para `42669270551` ni los
campos obligatorios de revision exacta. Se agrego localmente
`.github/workflows/deploy_authcraft-api-staging.yml` para reanudar el run sin
reconstruirlo; aun no se subio ni se ejecuto. El publicador SQL sigue con permisos
de lectura/revision, no se verifico Publish, y no se autoriza Publish con el
preflight. Los cuatro nombres de secretos de smoke sintetico ya estan configurados
en GitHub Actions; sus valores no se consultaron. T033/T035/T037 y la publicacion
Staging siguen pendientes.

Implementacion local del workflow de reanudacion: valida que el run original y sus
cuatro artefactos pertenezcan al mismo `workflow_dispatch` exitoso de `main`;
vuelve a ejecutar el preflight actual y exige que el resultado siga siendo
`ready_for_review` con `deployment_authorized=false`; `Invoke-DatabaseDeployment.ps1`
consulta y revalida el comentario autenticado exacto antes de SQL Publish; luego
despliega el ZIP de ese mismo run y ejecuta el smoke con credenciales sinteticas.
El gate de secretos se ejecuta antes del login OIDC. No hay grants automaticos,
rollback SQL ni ruta Production. Pruebas focalizadas de
`StagingPublisherVerificationTests` pasan 20/20; la verificacion integral del
workflow remoto sigue pendiente.

#### Correccion versionada y ensayo local verificado

Se implemento la opcion autorizada de validacion explicita en post-deployment:

- Los checks de status de memberships y tenants usan ORs equivalentes al modelo
  extraido. El nuevo DeployReport contra Staging ya no elimina ni recrea esos checks.
- `postdeploys/ValidateConstraints.sql` valida las siete constraints revisadas con
  `WITH CHECK CHECK CONSTRAINT` y comprueba su estado habilitado/confiable.
  El guard se limita a ese conjunto: durante una instalacion inicial, DacFx puede
  validar otras constraints despues del post-deployment. No se altera ni repara
  silenciosamente ninguna constraint ajena.
- El publicador solo admite `WITH NOCHECK ADD CONSTRAINT` para esas tablas/nombres
  y exige el bloque versionado exacto. Despues solo permite separadores GO,
  mensajes PRINT literales, USE del DatabaseName y validaciones adicionales del
  mismo conjunto generadas por DacFx. SQL posterior arbitrario, constraints fuera
  del conjunto, deshabilitaciones o un bloque modificado bloquean Publish.
  Sigue siendo un gate conservador de forma conocida, no un parser SQL general
  ni un reemplazo de la revision autenticada del plan.

Validacion real: build SQL sin errores ni advertencias; 58 pruebas focalizadas con
LocalDB, review, recuperacion y workflow aprobadas, sin omisiones. La base LocalDB descartable
se publico correctamente, termino sin checks/FKs deshabilitadas o no confiables,
rechazo datos existentes invalidos con SQL 547 al validar y recupero la confianza
tras limpiar exclusivamente el fixture. La base de desarrollo existente no se
modifico y la base de prueba se elimino.

La nueva evaluacion contra Staging uso exclusivamente DeployReport/Script:
ALTER de invitations y creacion de una FK y cuatro checks, sin DROP de constraints.
Incluye las siete validaciones explicitas y DacFx tambien agrega su propia fase
final de validacion; esta generacion no contiene WAITFOR. El SQL real paso el
gate exacto del publicador, sin invocar Publish.

Hashes SHA-256 de esta evaluacion local (no son un release de Actions aprobado):

- DACPAC: `b3cde09b2436ed3cfe52ca5f86949b12bc672974d3024f016a065979e900a8a3`.
- Report: `a01e0e92a079f14ddab1a4a9c39d16b2db235374852cb6cb42314f8f5da31b64`.
- SQL: `38c0445cd7682bca09cbdb98cedefd793b7864cca8058805160d8ccef52014aa`.

La evidencia se conserva fuera del repositorio en la carpeta de sesion
`files/staging-constraint-validation`. El release `42669270551` contiene el DACPAC
anterior y no incorpora esta correccion: no reanudarlo para desplegarla. Falta
versionar/subir los cambios, generar un nuevo release de Actions, evaluar y revisar
sus hashes exactos y verificar concesion/revocacion de permisos antes de publicar.
No se ejecutaron DDL/DML remotos, no se concedieron permisos y no se toco Production.

#### Preparacion del nuevo release autorizada

El usuario autorizo commit/push a main y ejecutar Actions para generar/evaluar un
nuevo release, exclusivamente sin Publish. Antes del commit se verifico que
HEAD y origin/main no divergian y pasaron las 147 pruebas de la seleccion exacta
del workflow, incluida la integracion LocalDB habilitada, sin omisiones.
El dispatch autorizado corresponde solo a `main_authcraft-api-staging.yml` con
evaluacion SQL y bootstrap inicial de baseline desconocido; no al workflow
`deploy_authcraft-api-staging.yml`. Esta autorizacion no concede permisos SQL,
no aprueba hashes nuevos ni permite publicar API/SQL.

#### Nuevo candidato Actions 42692449745

Commit/push autorizado: `38da8568c2c3420df4a896aac84763ae15ade37d`.
El [run 42692449745](https://github.com/leoperetti/authcraft-api/actions/runs/42692449745)
termino con `validate`, `verify-staging-publisher` y `preflight-staging` en success.
Solo se construyo/empaqueto, verifico OIDC/permisos de revision, genero
DeployReport/Script y ejecuto preflight review-only; no hubo Publish ni deploy API.

Se descargaron los cuatro artefactos, verificando commit/run del manifiesto y
SHA-256 de los dos paquetes:

| Evidencia | SHA-256 |
|-----------|---------|
| API ZIP | `cffd92cb0c8d4a1db40505976dfc25e21dbc3dacd7f9bfb4569015bc4d01f73c` |
| DACPAC | `939c66ebe421f8b6b08690871e2387728f348e5e5da84c189966bd34d488a92e` |
| DeployReport | `a01e0e92a079f14ddab1a4a9c39d16b2db235374852cb6cb42314f8f5da31b64` |
| SQL | `d79e26bea0e507b472f8be4e7b09826dd8aa10b999bd6d72087b8680e0106fd8` |

El report no tiene alertas ni operaciones Drop: altera invitations y crea una FK
y cuatro checks. El SQL exacto de Actions incluye cinco WAITFOR de DacFx y cinco
creaciones WITH NOCHECK, seguidas de las siete validaciones explicitas y el guard
versionado. A diferencia del ensayo local con otra instalacion SqlPackage, el
runner conserva WAITFOR y no agrega otra fase de validacion despues del postdeploy.
La procedencia DacFx de esos WAITFOR fue reproducida previamente; su finalidad
interna sigue sin confirmarse y debe constar en la revision, no ocultarse ni
confundirse con una modificacion manual.

El SQL original del runner paso el gate de constraints exacto del publicador
ejecutado aisladamente, sin autenticar una aprobacion ni invocar Publish.
Este gate no acredita por si solo todos los requisitos de revision constitucional.
Los artefactos originales se conservan en Actions y en la carpeta privada de
sesion `files/staging-release-42692449745`, sin editar el SQL.
El workflow de reanudacion ya esta versionado en main, pero no se ejecuto.
Production sigue `disabled_manually`; no se concedieron permisos SQL.
Pendientes: revision autenticada del nuevo candidato y sus hashes, resolucion
explicita de los puntos de revision, permisos/revocacion comprobados y restantes
gates operacionales antes de cualquier publicacion.

T028/T029/T032 estan completos. T033/T035/T037/T038/T040/T041 siguen pendientes.
No habilitar Production, no conceder aun `db_owner` y no publicar Staging hasta que
el plan exacto sea reevaluado/revisado, el workflow de publicacion este listo y
exista un procedimiento verificable para conceder y revocar el permiso temporal.

### Ensayo inicial historico: atomicidad global no demostrada

El 2026-11-09 se implemento T062 en `LocalEnvironmentSqlTests` y se ejecuto con
SqlPackage 170.4.53.0 (Visual Studio) y 170.5.96.0 (dotnet tool). Se construyeron
dos DACPACs sinteticos mediante MSBuild/SSDT: baseline y candidato con columna
nullable/check nuevos y postdeploy que inserta una semilla, valida el check y
puede emitir THROW 51000. No se modifico el DACPAC ni el guard de AuthCraft.
Cada escenario publico una baseline en una base LocalDB distinta y descartable,
cargo una fila sintetica y ejecuto Script/Publish con
`IncludeTransactionalScripts=True`, `BlockOnPossibleDataLoss=True`,
`DropObjectsNotInSource=False` y `ScriptDatabaseOptions=False`.

En ambas herramientas el script incluye SET XACT_ABORT ON (linea 48),
BEGIN TRANSACTION (52) y COMMIT TRANSACTION (117), pero el INSERT postdeploy
empieza en 124, WITH CHECK CHECK en 125 y THROW en 127: quedan despues del COMMIT.
La propia validacion diferida de DacFx aparece mas tarde, en 139.
True no cubre la unidad esquema/postdeploy en este ensayo.

Estado comprobado desde una conexion nueva despues de finalizar Publish:

| Escenario | Exit code | Columna nueva | Check nuevo | Check confiable | Semilla postdeploy | Fila baseline |
|-----------|-----------|---------------|-------------|-----------------|--------------------|---------------|
| Exito | 0 | si | si | si | si | conservada |
| Datos invalidos, SQL 547 | 1 | si | si | no | si | conservada |
| Guard sintetico SQL 51000 | 1 | si | si | si | si | conservada |

En el ensayo inicial, el test exigia baseline intacta ante ambos errores y **fallo** con
`Transactional coverage gate failed`; es un bloqueo reproducible, no una suite
aprobada. T062 registra el test implementado; T064 y dependientes no estan
completadas. Se detuvo el incremento antes de modificar el inicializador local
o el publicador remoto: IncludeTransactionalScripts=True se utilizo solamente
en los fixtures, no se presenta como implementado en T063/T066.

Logs sanitizados de ambas ejecuciones conservados en la carpeta privada de
sesion `files/transaction-coverage`. Los scripts sinteticos, proyectos y bases
temporales se eliminaron en finally. No se modifico la base development existente,
no hubo conexion Azure, cambios de permisos, commit/push/dispatch ni Publish remoto.
No se envolvio el SQL generado en otra transaccion ni se relajo el gate.

Se requiere revisar el diseno antes de continuar: activar la opcion por si sola
no satisface la atomicidad requerida. Mantener el estado parcial/desconocido tras
fallos hasta inspeccion y no continuar a API/smoke/promocion. Este resultado local
no acredita un comportamiento Azure diferente ni aprueba ningun release anterior.

### Postdeploy transaccional por fases: implementado y verificado localmente

Tras la decision autorizada de aceptar fases separadas, se implementaron T063–T067
y T071. No se convirtio el ensayo inicial en una afirmacion de atomicidad global:
el test conserva un control sin proteccion y agrega casos protegidos que usan
el prefijo/sufijo del postdeploy versionado real.

El inicializador y el publicador pasan IncludeTransactionalScripts=True y
ScriptDatabaseOptions=False en todas las acciones. Script.PostDeployment.sql
rechaza una transaccion ajena con 51001 antes de escribir; abre su propia unidad
con XACT_ABORT ON/TRY/BEGIN TRANSACTION y solo hace COMMIT despues de semillas,
siete validaciones y guard. CATCH revierte la unidad activa y relanza el error.
El COMMIT de esquema DacFx anterior a esta fase se admite expresamente.

Estado comprobado desde una conexion nueva, con ambas herramientas:

| Fase postdeploy | Escenario | Columna/check DacFx | Check confiable | Semilla | Fila previa |
|----------------|-----------|--------------------|-----------------|---------|-------------|
| Sin proteccion (control) | Exito | presentes | si | presente | conservada |
| Sin proteccion (control) | 547 | presentes | no | presente | conservada |
| Sin proteccion (control) | 51000 | presentes | si | presente | conservada |
| Protegida | Exito | presentes | si | presente | conservada |
| Protegida | 547 | presentes | no | ausente | conservada |
| Protegida | 51000 | presentes | no | ausente | conservada |

El segundo fallo protegido demuestra tambien rollback del cambio de confianza
que precede al THROW. Esto acredita separate_schema_and_postdeploy en el fixture,
no una reversion del esquema. No inferir el estado remoto desde estos resultados.

Validaciones locales completadas:

- SqlPackage 170.5.96.0: 99 pruebas de la seleccion focalizada, sin fallos ni
  omisiones, incluidos ambos ensayos por fases y Publish del DACPAC AuthCraft.
- Seleccion final con SqlPackage 170.4.53.0: 101 pruebas, sin fallos ni omisiones;
  agrega cobertura explicita de fallos 547/51000 del publicador y secuencia de
  recuperacion. Compilo API/tests Release; persiste el warning CS0108 preexistente.
- MSBuild/SSDT Release del DACPAC: cero errores y advertencias.
- El inicializador publico el DACPAC en una base descartable, comprobo las 13
  tablas, proveedor y todas las constraints confiables. Verifico siete llamadas
  reales con las mismas opciones, sin modificar los hashes revisados.
- El gate remoto se ensayo sin Azure sobre el SQL real de una reevaluacion local.
  Exige el postdeploy versionado completo, incluido el salto de linea de :r y
  lectura UTF8. Rechaza COMMIT prematuro, transaccion ausente, bloque alterado,
  NOCHECK no cubierto y SQL posterior arbitrario. Solo permite la cola DacFx conocida.
- El filtro local permite exclusivamente la limpieza conocida de #tmpErrors de
  DacFx, incluidos finales CRLF, sin admitir tablas permanentes u otros temporales.
- El parser exige exactamente `include_transactional_scripts=true` y
  `postdeploy_transaction=true`; ausencia, False y duplicados impiden Publish.
  Las pruebas de recuperacion conservan esquema parcial y no ejecutan restauracion
  automatica, API o smoke despues de un fallo SQL.

Logs sanitizados persistidos en la carpeta privada de sesion
`files/transaction-coverage`: `protected-vs-tests.log` y
`protected-global-99-tests.log`. El primero conserva version, hashes SQL sinteticos,
limites y vectores observados por escenario. Las bases y archivos descartables
se eliminaron en finally; la base development existente no se modifico.

Pendientes al cerrar esta etapa: T068 (integrar campos/evidencia de transaccion en DeploymentAttempt),
T069 (nuevo candidato y evaluacion remota autorizada) y T070 (revision autenticada
de sus nuevos hashes). El run 42692449745 no contiene esta proteccion y no debe
reutilizarse. No hubo commit/push/dispatch, conexiones Azure, permisos nuevos ni
Publish remoto; Production permanece bloqueado.

### Ventana de permisos automatizada: preparacion local

Publicacion explicitamente autorizada del candidato 42721985281 con revision
6755129089: primer intento run 42736407449 bloqueado antes de invocar Publish
por lector de comentarios JSON anidados. Report/SQL/DACPAC coinciden con revision.
La prueba LocalDB real paso; grant y cleanup/revocacion terminaron success.
Intento result=blocked, schema_result=blocked, API/smoke not_executed.
No se ejecutaron escrituras de esquema/postdeploy en este intento.
Correccion del lector conserva todos los campos y gates: asignar primero la
respuesta Invoke-RestMethod y normalizar despues. Regresion reproduce arreglo
JSON no enumerado con decision vieja rechazada y nueva aprobada.
134 pruebas locales aprobadas, ninguna omitida; candidato/paquetes sin cambios.
El reintento conserva el alcance autorizado, sin Production ni datos nuevos.

Ensayo remoto completado:
[run 42736112124](https://github.com/leoperetti/authcraft-api/actions/runs/42736112124),
commit b0ff3bfd9e562649e03ba2297a8c3ef08101d66e, login OIDC de
assigner_revoker, Grant y Revoke inmediato aprobados. El segundo Revoke
idempotente verifico independientemente baseline desde conexion nueva.
Artefacto staging-permission-manager descargado y validado para run/commit:
login_result/rehearsal_result/cleanup_result=success,
revocation_verified=true, published=false, 2026-11-10T01:29:55Z.
El publicador queda sin db_owner; la gestora conserva su db_owner autorizado.
Production disabled_manually confirmado. No hubo Publish, API ni login de negocio.
El paso de gestion de permisos ya tiene evidencia OIDC real, no solo
impersonacion administrativa. No acredita aun una publicacion DacFx real ni
la ruta de fallo/cancelacion remota; perdida total del runner requiere operador.
El candidato revisado sigue siendo 42721985281, sin reconstruccion.

Autorizacion posterior: preparar, commit/push y ejecutar exclusivamente el
ensayo OIDC grant/revoke sin despliegue. Workflow manual
verify_staging_permission_manager.yml, solo main, serializado con la publicacion
Staging, ejecuta Grant y Revoke inmediato en finally, seguido de Revoke/verificacion
independiente always. Conserva evidencia sanitizada 30 dias, sin secretos.
79 pruebas focalizadas aprobadas, ninguna omitida, antes de subir. La autorizacion
no incluye Publish, API, login de negocio ni Production. No reconstruye candidato.

Concesion posterior explicitamente autorizada 2026-11-10T01:26:57Z: se agrego
assigner_revoker a db_owner exclusivamente en authcraft-db-staging, validando
previamente base y SID de ambas identidades. Una conexion nueva, con
impersonacion de sesion, confirmo db_owner/CONTROL/ALTER ANY ROLE/IMPERSONATE
del publicador=1 para la gestora; el publicador conserva db_owner=0.
El permiso de la gestora es persistente y elevado, no minimo: el workflow
retira solo el rol temporal del publicador. No se publico SQL/API ni se
modifico Production. Falta prueba real de login OIDC/gestion desde Actions.

Creacion posterior autorizada: se ejecuto CREATE USER [assigner_revoker] FROM
EXTERNAL PROVIDER mediante sesion Entra administrativa, exclusivamente en
authcraft-db-staging. Verificado EXTERNAL_USER con SID igual al client ID
c376b9f5-2b0b-406f-8579-ef8787001115, coincidente con el guard del helper.
No se concedieron roles: gestora db_owner=0 y publicador db_owner=0.
Falta autorizacion/configuracion de los privilegios de gestion y prueba OIDC real.

Diagnostico posterior autorizado, solo lectura: Reader de assigner_revoker
verificado directamente en authcraft-db-staging, sin asignaciones heredadas.
En SQL no existe usuario con nombre assigner_revoker ni SID correspondiente
al client ID o principal ID de esa identidad. No se impersono una identidad
no verificada ni se crearon usuarios/grants. El publicador permanece db_owner=0.
La gestora no esta preparada para ejecutar el helper: falta provisionar su
acceso SQL con privilegios revisados y verificar OIDC real desde Actions.

El usuario indico como gestora Staging existente `assigner_revoker`, client ID
`c376b9f5-2b0b-406f-8579-ef8787001115`. La reanudacion incorpora login OIDC
separado con esa identidad despues del preflight y de las pruebas LocalDB.
Set-StagingPublisherRole.ps1 valida cuenta Azure/suscripcion/tenant, base,
SID de la gestora y SID del publicador; exige baseline de solo revision,
concede db_owner y comprueba permisos efectivos desde conexion nueva.
Si el grant o su verificacion falla, intenta retirar el rol y verificar baseline.
No se da al publicador capacidad de elevarse.

Antes de SQL se restaura la sesion publicadora. Despues, los pasos always
restauran la gestora, revocan el rol de forma idempotente y verifican baseline
desde conexion nueva, incluso si fallo SQL o el grant. Fallo de cleanup/login
bloquea API y queda explicito en resumen/evidencia; el intento no puede acreditar
Staging validado sin grant/revocacion exitosos. La API usa otra vez el publicador.
Una caida total del runner, perdida de red o cancelacion forzada puede impedir
always: no hay garantia absoluta de cleanup. El operador debe vigilar el run y,
si no consta revocacion verificada, usar el procedimiento administrativo ya
ensayado para DROP MEMBER y comprobar los permisos antes de cualquier reintento.
No hay watchdog externo provisionado.

Prerrequisitos NO verificados todavia: federacion de assigner_revoker para el
subject GitHub main inmutable vigente, audiencia api://AzureADTokenExchange,
issuer https://token.actions.githubusercontent.com, visibilidad de suscripcion
para azure/login y usuario Entra contenido exclusivo en authcraft-db-staging.
La gestora necesita poder alterar membresia de db_owner y ejecutar impersonacion
para la verificacion; ALTER ANY ROLE por si solo no acredita poder modificar un
rol fijo. El mecanismo directo exige privilegios elevados (normalmente db_owner
en esta base), no se presenta como minimo privilegio. Revisar/aprobar esos
privilegios de la gestora separadamente; no asignarlos automaticamente ni darle
acceso a Production/runtime/vault. Si el SID contenido difiere, detener y
diagnosticar, no recrearlo por suposicion. No se crearon federaciones ni grants.

Verificacion posterior tras configuracion del usuario: assigner_revoker coincide
con el client ID esperado y tenant correcto. Se encontro exactamente la credencial
github-authcraft-staging-main con issuer https://token.actions.githubusercontent.com,
audiencia api://AzureADTokenExchange y subject
repo:leoperetti@12513421/authcraft-api@1404745493:ref:refs/heads/main.
Configuracion federada verificada por ARM en solo lectura; falta intercambio OIDC
real desde Actions. La consulta de asignaciones ARM devolvio cero roles para esta
identidad: no se acredita visibilidad de suscripcion requerida por azure/login.
Permisos/usuario SQL de la gestora siguen pendientes de verificacion. No se
asignaron roles ni se ejecuto el workflow.

Validacion local: 78 pruebas pipeline/publicador aprobadas sin omisiones,
incluido rechazo ejecutado de cuatro contextos Azure incorrectos, orden de
cleanup y simulacion de revocacion fallida. No equivalen a login OIDC de la
gestora ni a grant/revoke SQL real desde Actions. Sin commit/push/dispatch.

### Verificacion de cuenta y tenant sinteticos en Staging

Actualizacion posterior 2026-11-10T00:34:07Z: con autorizacion explicita del
usuario se retiro db_owner del publicador, tras validar base y SID exactos.
Una conexion nueva verifico db_owner/ALTER/CONTROL/CREATE TABLE/ALTER dbo/
SELECT e INSERT providers=0; CONNECT/VIEW DEFINITION=1.
revocation_verified=true, published=false. El permiso ya no permanece activo.
La proxima concesion debe coordinarse exclusivamente con la ventana de Publish
y revocacion ante exito/fallo; el workflow actual no automatiza ese control.

Consulta autorizada de solo lectura contra authcraft-db-staging: el client_id
authcraft-staging identifica un tenant unico y activo (no coincide con su slug).
La cuenta sintetica staging-admin@example.test existe una vez, tiene cuenta
local con material de credenciales no vacio, membership activa y un rol
asignado en ese tenant; existen cuatro roles del tenant. No se extrajeron hashes,
salt, passwords, tokens ni datos de otras cuentas. No se ejecuto login ni
se modificaron filas, permisos o configuracion.

Los cuatro nombres de secretos smoke existen en GitHub. No se pueden consultar
sus valores por API: no se acredita correspondencia de esos valores ni validez
de la password por esta lectura. Login/roles HTTP siguen pendientes del smoke.

El usuario informo que concedio db_owner; la consulta confirmo
publisher_db_owner=1 para authcraft-staging-schema-publisher. El rol permanece
activo y no fue concedido/revocado por este diagnostico. Debe coordinarse su
revocacion incluso ante fallo, sin interpretar la comprobacion como autorizacion
de Publish o deploy API. Production no fue consultado.

### Revision autenticada del candidato 42721985281

El responsable publico personalmente la
[revision 6755129089](https://github.com/leoperetti/authcraft-api/issues/11#issuecomment-6755129089),
actualizada 2026-11-10T00:17:48Z. La consulta autenticada a GitHub confirmo
autor leoperetti, issue #11 abierta y ultima decision del responsable.
Test-DeploymentReviewRecord devolvio true para run 42721985281, commit
6f139baa4834681f9d6b93293948425331b0868e, baseline unknown, destino Staging,
hashes DACPAC/report/SQL exactos, opciones y tres aceptaciones.
No se transfirio la revision previa ni se publico/edito la decision por el asistente.

Esta revision satisface el gate tecnico del nuevo candidato, no autoriza
permisos temporales, Publish, deploy API, preparacion de datos ni Production.
Antes de una publicacion autorizada se deben reconsultar decision/retencion,
procedencia y plan live; revocacion, edicion incompatible o deriva bloquean.
T037/T039/T040 siguen parciales por permisos/publicacion real y verificaciones
operacionales pendientes. No se conecto Azure SQL ni se concedieron roles.

### T039: verificacion remota review-only del registro inicial

Autorizacion ejecutada en
[run 42721985281](https://github.com/leoperetti/authcraft-api/actions/runs/42721985281),
commit `6f139baa4834681f9d6b93293948425331b0868e`.
validate, verify-staging-publisher y preflight-staging finalizaron success.
El registro candidate-release real fue descargado y comparado con manifiesto y
ambos paquetes: commit/run exactos, created_at UTC, state=built,
staging_validation_state=not_validated, referencia null y
promotion_authorized=false. Los cinco artefactos conservan procedencia del mismo
run y no estan expirados; retencion hasta 2026-12-09.

Hashes SHA-256 verificados tras descargar, sin editar ni ejecutar SQL:

| Evidencia | SHA-256 |
|-----------|---------|
| API | `542f09fdadbde3bed667ac8095b0316cd08c5c079411e37aa2cdf8cbb34ff898` |
| DACPAC | `4efe47760b536db09ea14db11056c07081f654cfd4332862750464e56e8ba869` |
| DeployReport | `a01e0e92a079f14ddab1a4a9c39d16b2db235374852cb6cb42314f8f5da31b64` |
| SQL | `778fb7f1c40b85bc824356c486c0ae1577b83a5aed355386ce8ef8ae4bc7b68d` |
| Toolchain JSON | `1ae1f6d3fd30b7b54172400d4191f2114b7416e2ab8b1a8aa10e6498c4db32e2` |

El reporte/SQL coincide byte a byte por SHA-256 con la evaluacion anterior:
Alter invitations y cinco Create constraints, sin Drop ni recreacion de status.
SqlPackage 170.4.53.0 conserva hash
`907454a8848e9617666ac03db8c18e82a15e0435aaa223d8d132f830a7841d26`;
IncludeTransactionalScripts=true y ScriptDatabaseOptions=false.
La identidad OIDC paso su comprobacion SQL real de review-only y devolvio
deployment_authorized=false. No se concedieron permisos ni se ejecuto Publish,
deploy API, postdeploy ni smoke. Production sigue disabled_manually.

Actions aprobo 188 pruebas y omitio dos LocalEnvironmentSqlTests por falta de
conexion local configurada en ese job; esas omisiones no acreditan ensayos de
motor. Las 72 regresiones locales previas no tuvieron omisiones. Se mantienen
las advertencias preexistentes de nullabilidad/CS0108 y de runtime de actions.

Estado al cerrar la evaluacion, antes de la revision 6755129089:
la revision del run 42705333969 NO aprueba este candidato, aunque el SQL coincida:
commit/run/DACPAC son distintos. Antes de Publish se requiere nueva revision
autenticada con estos valores y revalidacion live del plan/baseline. No se creo
ni edito una aprobacion en nombre del responsable. Baseline inicial sigue unknown;
el bootstrap review-only no constituye despliegue. T039 sigue parcial: la
reanudacion real y sus estados posteriores aun no se ejecutaron y Production
permanece bloqueado. La verificacion del registro inicial en Actions si esta
completada; no repetirla como pendiente.

### T039: registro inicial del candidato integrado localmente

Autorizacion posterior 2026-11-09: el usuario autoriza commit/push a main y
evaluacion review-only de Staging de estos nueve archivos, incluyendo OIDC,
DeployReport/Script y preflight de solo lectura (con posible consumo SQL).
Se comprobo Production disabled_manually antes del push. No autoriza Publish,
deploy API, permisos, datos sinteticos ni operaciones sobre Production.
El nuevo candidato requiere su propia revision exacta; no hereda la aprobacion
del run 42705333969.

El packaging manual main ahora genera `candidate-release.json` y lo sube como
artefacto independiente `candidate-release`, retenido 30 dias. Registra built,
created_at UTC, identidad/hashes exactos del manifiesto, staging not_validated,
referencia de validacion null y promotion_authorized=false. PR no genera ni
sube ese registro. No cambia paquetes ni manifiesto ni exige artefactos nuevos
al candidato historico revisado 42705333969.

Ocho simulaciones ejecutan el bloque PowerShell real: exito y rechazos por
run/commit cruzados, hashes API/SQL invalidos, artefacto inesperado, JSON invalido
y manifiesto ausente. Verifican tambien timestamp, hashes, manifiesto intacto
y condiciones/retencion del upload. Pendiente validacion remota autorizada;
T039 permanece parcial. No se realizo commit/push/dispatch ni acceso Azure.
Validacion conjunta final: 72 pruebas aprobadas, ninguna omitida, y diff --check
sin errores. Se conserva la advertencia CS0108 preexistente en las pruebas.

### T039: estado del candidato integrado localmente en reanudacion

El resumen de la reanudacion conserva `candidate-release.json` en el mismo
artefacto `staging-deployment-attempt` (30 dias), sin cambiar paquetes/manifiesto.
Vincula commit/run/hashes del manifiesto coincidente. Un intento bloqueado queda
built y uno fallido staging_failed. Solo todas las etapas aprobadas junto a SQL
exitoso, opciones/transaccion verificadas y la identidad exacta permiten registrar
staging_validation_state=staging_validated y state=production_blocked.
La referencia incluye run, artefacto y SHA-256 de deployment-attempt.json.
promotion_authorized siempre permanece false.

Doce simulaciones verifican resultados normales y rechazan smoke exitoso con SQL
fallido, manifiesto ausente/cruzado e intento perteneciente a otro candidato.
La clase focalizada pasa 32 pruebas sin omisiones. Evidencia inconsistente falla
explicitamente el registro y no conserva una referencia de validacion.
T039 permanece parcial: faltan packaging inicial, ciclo protegido Production y
ejecucion remota autorizada. Cambios locales, sin commit/push/dispatch ni Azure.

### T068: evidencia transaccional del intento integrada localmente

La ruta de reanudacion ahora ejecuta la prueba exacta de fases en LocalDB antes
de OIDC, con el SqlPackage que recibira el publicador. No reconstruye los paquetes
candidatos. El harness descubre MSBuild/SSDT por vswhere en lugar de depender de
rutas locales fijas; sus bases y proyectos sinteticos siguen siendo descartables.

El ensayo genera seis SQL originales y un JSON que vincula herramienta/version,
fuentes postdeploy/validaciones/fixture, hashes SQL, limites y estados observados.
El publicador exige ademas el TRX con exactamente una prueba ejecutada/aprobada.
Verifica version actual y hash del ejecutable, fuentes vigentes, seis casos unicos
con sus estados esperados, limites y hashes SQL antes de permitir Publish.
Rechaza evidencia ausente, JSON invalido, prueba omitida, caso ausente/duplicado,
estado incorrecto, SQL alterado, limites insuficientes y herramienta/version/fuente
distintas. La referencia incluye hash del plan real y linea inicial del postdeploy.

`sql-attempt.json` se guarda pending antes de llamar Publish y se actualiza en
finally incluso con fallo. Solo acredita separate_schema_and_postdeploy tras
validar evidencia y plan; antes permanece unverified. Ante fallo SQL deja
postdeploy_result/failure_state_verification=unknown y etapas posteriores
not_executed. Incluso tras exito SQL conserva postdeploy_result=unknown hasta
inspeccion independiente: no infiere committed/rolled_back del exit code.

Actions agrega un `deployment-attempt.json` y resumen sanitizado. Solo registro y
upload usan always(); API/smoke mantienen su condicion de exito. Se simularon ocho
estados: bloqueo previo, revision bloqueada, fallo SQL, cancelacion sin registro,
interrupcion con instantanea pending, fallo API, fallo smoke y exito. Smoke
combinado fallido deja salud/autenticacion unknown, no inventa que ambas pruebas
llegaron a ejecutarse. El artefacto `staging-deployment-attempt` retiene JSON, TRX,
SQL sintetico y planes de evaluacion/reevaluacion durante 30 dias con el acceso
del repositorio. Una perdida total del runner puede impedir captura/upload:
evidencia no disponible sigue bloqueando y requiere inspeccion.

Validacion local: 121 pruebas aprobadas, ninguna omitida:

- 1 prueba exacta con seis escenarios Script/Publish reales, SqlPackage 170.4.53.0,
  MSBuild/SSDT descubierto y TRX; Get-DeploymentTransactionVerification valido
  realmente el conjunto, no solo un mock.
- 120 regresiones de LocalEnvironmentSqlTests (DACPAC AuthCraft real),
  DatabaseDeploymentReviewTests, DeploymentPipelineTests, DeploymentRecoveryTests
  y StagingPublisherVerificationTests, incluidas las simulaciones del resumen.
- Build API Release final: cero errores/advertencias. Los scripts del publicador
  y nueve bloques PowerShell del workflow pasan parseo sintactico; diff --check
  sin errores. Esto no sustituye ejecutar el workflow en GitHub.

Evidencia privada de sesion en `files/transaction-attempt-proof-final`:
`transaction-tests.trx`, `transaction-verification.json`, seis SQL sinteticos y
`attempt-regressions.trx`. Hash SHA256 del JSON de prueba:
`365661969338cecb9c5aaf6445385c93591ff72bf220f516bed2eeb50f0d9c5b`.
Estos hashes son del ensayo local, no de un release Azure ni una aprobacion remota.

T068 queda completada localmente. T039 sigue parcial y T069/T070 requieren nueva
autorizacion de commit/push/dispatch/conexion y revision autenticada del nuevo
candidato. No hubo publicaciones ni operaciones Azure; Production sigue bloqueado.

### Autorizacion acotada para T069

El usuario autorizo commit/push y evaluacion review-only de Staging, incluida
conexion SQL y posible reactivacion/consumo, sin Publish, despliegue API, cambios
de permisos ni operaciones sobre Production. Antes del push se confirmo mediante
GitHub API que `main_authcraft-api.yml` sigue disabled_manually y el workflow
Staging de evaluacion esta active. Esta autorizacion no es revision del SQL nuevo.

La evaluacion conservara ademas `evaluation-toolchain.json` con version/hash del
SqlPackage, commit/run y opciones, para comprobar el alcance de la herramienta
exacta del runner sin inferir su version del encabezado del script.

### Evaluacion review-only nueva: 42705333969

Se hizo commit/push autorizado de
`ceaa5258111e266cbebaacf733869f4e9ad3cd47` y dispatch exclusivo de
`main_authcraft-api-staging.yml`, con verify_staging_publisher=true,
evaluate_staging_schema=true, baseline unknown y bootstrap inicial explicito.
El [run 42705333969](https://github.com/leoperetti/authcraft-api/actions/runs/42705333969)
finalizo success en validate, verify-staging-publisher y preflight-staging.
El workflow legacy Production seguia disabled_manually despues de la evaluacion.

Validate aprobo 176 pruebas y omitio las dos de LocalDB porque ese job no configura
conexion local. Ambas ya fueron ejecutadas sin omision en la seleccion local de
121 pruebas; no se presenta la omision como ensayo del motor ni como rollback
acreditado por Actions. El wrapper OIDC exige una prueba SQL de permisos ejecutada
y aprobada, y devolvio deployment_authorized=false. El preflight verifico procedencia
y gates live bajo bootstrap inicial; no acredita una baseline desplegada.
Se observaron advertencias preexistentes de nullability/CS0108 y de Node 20 en
actions, sin fallos. No se modificaron asuntos ajenos a esta feature.

Los cuatro artefactos originales (api, dacpac, release-manifest,
staging-schema-review) pertenecen al run y commit anteriores y estaban no
expirados al descargarlos. Se comprobo el manifiesto y hashes contra los paquetes:

| Evidencia | SHA256 |
|-----------|--------|
| API ZIP | `89e38b4efddfda267c438679a7a543b6123bb9ab66e86a9ab2e0ab1924ff6117` |
| DACPAC | `68e93438772747b42379324d8f0fa33f54a11d8627c131f8a78300ee9c60bfe8` |
| DeployReport | `a01e0e92a079f14ddab1a4a9c39d16b2db235374852cb6cb42314f8f5da31b64` |
| SQL original | `778fb7f1c40b85bc824356c486c0ae1577b83a5aed355386ce8ef8ae4bc7b68d` |
| evaluation-toolchain.json | `10bcad872b88c97a8bb8b0079458d75787fc825e9ff7f5fb76a8912aa7971477` |

Toolchain original: SqlPackage 170.4.53.0, ejecutable SHA256
`907454a8848e9617666ac03db8c18e82a15e0435aaa223d8d132f830a7841d26`,
include_transactional_scripts=true y script_database_options=false.
Version y hash del ejecutable coinciden con el ensayo real LocalDB de fases.

El SQL original descargado paso el gate del publicador local sin editarlo ni
ejecutarlo contra SQL. Limites observados (lineas del archivo original):

- Esquema DacFx: XACT_ABORT 44, BEGIN 48 y COMMIT 252. Los BEGIN internos de
  recuperacion condicional DacFx no son la transaccion propia del postdeploy.
- Postdeploy: guard de entrada 51001 en 271, XACT_ABORT 273, BEGIN 275,
  semillas 279/281/283, siete WITH CHECK CHECK 285–291, THROW 51000 en 307,
  COMMIT 311, rollback condicional en 315 y rethrow 316.
- El COMMIT del esquema precede a la fase protegida tal como admite el diseno.
  No hay COMMIT prematuro dentro del postdeploy. El resultado demuestra forma
  por fases compatible con el ensayo, no estado de rollback en Azure.
- El reporte no contiene Drop; conserva los dos status checks equivalentes.
  Persisten cinco WAITFOR atribuibles al comportamiento DacFx ya reproducido;
  no se insertaron ni eliminaron manualmente ni se atribuye una finalidad interna
  no demostrada. Siguen sujetos a revision del responsable.

La evidencia original y logs sanitizados quedan en la carpeta privada de sesion
`files/staging-release-42705333969`, ademas de los artefactos del run.
El DeployReport conserva el mismo hash de evaluaciones anteriores porque la
extension cambia el postdeploy y opciones, no esas operaciones de modelo;
eso no hace intercambiables SQL, DACPAC ni revisiones de runs anteriores.

T069 completada. T070 pendiente: #11 no tiene comentario de leoperetti para el
nuevo run al verificarlo. La plantilla siguiente no es una aprobacion: el revisor
debe descargar/inspeccionar el SQL, reporte, paquetes y recuperacion, decidir los
campos pending y publicar personalmente su comentario autenticado. No se escribio
aprobacion en su nombre. No hubo SQL Publish, despliegue API, cambios de permisos
ni operaciones sobre Production.

```text
deployment_review=pending
run_id=42705333969
commit_sha=ceaa5258111e266cbebaacf733869f4e9ad3cd47
staging_baseline_sha=unknown
target_environment=staging
database_server=authcraftdbbrasil.database.windows.net
database_name=authcraft-db-staging
dacpac_sha256=68e93438772747b42379324d8f0fa33f54a11d8627c131f8a78300ee9c60bfe8
api_compatibility=pending
data_impact=pending
recovery_plan=pending
block_on_possible_data_loss=true
drop_objects_not_in_source=false
script_database_options=false
include_transactional_scripts=true
postdeploy_transaction=true
deploy_report_sha256=a01e0e92a079f14ddab1a4a9c39d16b2db235374852cb6cb42314f8f5da31b64
deploy_script_sha256=778fb7f1c40b85bc824356c486c0ae1577b83a5aed355386ce8ef8ae4bc7b68d
```

pending bloquea el parser deliberadamente. Si el responsable aprueba, debe
decidir deployment_review=approved y las tres aceptaciones accepted; una decision
negativa, datos distintos o campos duplicados bloquean. Incluso una revision
valida no autoriza automaticamente permisos temporales, Publish ni despliegue API.

### Revision autenticada del candidato 42705333969

El responsable `leoperetti` publico personalmente la
[revision 6752655094](https://github.com/leoperetti/authcraft-api/issues/11#issuecomment-6752655094)
el 2026-11-09T22:03:09Z. Se verifico mediante GitHub API que #11 sigue abierta,
que el comentario pertenece a esa issue y es la ultima decision del autor
autorizado. Test-DeploymentReviewRecord devuelve true para el run 42705333969,
commit ceaa5258111e266cbebaacf733869f4e9ad3cd47, baseline unknown y destino Staging.
Coinciden hashes, opciones explicitas y aceptaciones de compatibilidad API,
impacto de datos y recuperacion, sin duplicados. T070 queda completada.

No se creo ni edito el comentario en nombre del usuario. La revision queda
sujeta a reconsulta y reevaluacion exactas antes de cualquier Publish; si se
revoca, cambia el plan o expiran los artefactos, vuelve a bloquearse.

El siguiente gate es T037: comprobar ruta administrativa y permisos efectivos
del publicador, asi como concesion/revocacion verificable del permiso temporal.
La autorizacion reciente fue solo commit/push y evaluacion review-only; no se
extiende a grants, Publish SQL, despliegue API ni escrituras sinteticas. No se
realizaron esas operaciones al registrar la revision. T033/T040 siguen abiertos
hasta satisfacer sus demas criterios; Production permanece bloqueado.

### T037: diagnostico SQL autorizado de solo lectura

Tras autorizacion expresa nueva, se conecto exclusivamente a authcraft-db-staging
con token Entra de corta duracion adquirido por Azure CLI. El token se mantuvo en
memoria, no se imprimio ni persistio. La conexion explicita con AccessToken funciono,
a diferencia del timeout historico de sqlcmd -G.

La sesion administrativa devuelve db_owner=1, CONTROL=1, ALTER ANY ROLE=1 e
IMPERSONATE del publicador=1. Existe exactamente un principal externo
authcraft-staging-schema-publisher; no es miembro de db_owner.
Con EXECUTE AS USER y REVERT de sesion (sin grants), los permisos efectivos del
publicador fueron CONNECT=1 y VIEW DEFINITION=1; ALTER, CONTROL, CREATE TABLE,
ALTER del esquema dbo, SELECT e INSERT de providers y db_owner devolvieron cero.
El SID observado del principal contenido se representa como
a2382f63-d653-47f3-82ca-9f2696f63f0a; esta observacion no cambia su identidad ni
sustituye la verificacion OIDC del run.

Esto confirma una ruta administrativa disponible y falta de permisos Publish.
No demuestra todavia concesion/revocacion ejecutada ni permisos DacFx efectivos
despues de elevar. T037 sigue pendiente. No se concedieron ni revocaron roles,
no se ejecuto SQL Publish, no se desplego API ni se consulto Production.

### Ensayo autorizado de concesion/revocacion temporal en Staging

El usuario autorizo expresamente conceder db_owner temporalmente al publicador
solo en authcraft-db-staging, verificar permisos y revocarlo inmediatamente,
sin Publish. Se verifico por ARM que authcraft-api-st-id-97cc tiene clientId
a2382f63-d653-47f3-82ca-9f2696f63f0a y principalId
2a446033-801f-444c-985e-6519c4661632. El guard SQL uso el SID contenido observado
en el diagnostico, sin reemplazar usuarios ni cambiar su identidad.

Ensayo 2026-11-09T22:10:22.0084935Z a 22:10:23.3773349Z, exclusivamente Staging:

| Permiso efectivo | Antes | Elevado | Tras revocacion |
|------------------|-------|---------|-----------------|
| db_owner | 0 | 1 | 0 |
| CONNECT / VIEW DEFINITION | 1 | 1 | 1 |
| ALTER / CONTROL / CREATE TABLE | 0 | 1 | 0 |
| ALTER del esquema dbo | 0 | 1 | 0 |
| SELECT / INSERT de providers | 0 | 1 | 0 |

Cada inspeccion uso una conexion nueva e impersonacion de sesion del principal
contenido seguida de REVERT. ADD MEMBER y DROP MEMBER se ejecutaron con guard
de base/identidad y transaccion propia; la revocacion estuvo en finally. Se
comparo cada permiso posterior con la baseline: revocation_verified=true.
No se ejecutaron inserts, ALTER de tablas ni SqlPackage Publish. published=false.
El publicador termino sin privilegios elevados; no se modifico Production.

Evidencia sanitizada privada de sesion:
`files/staging-temporary-role-rehearsal.json` y procedimiento exacto
`files/Test-StagingTemporaryPublisherRole.ps1`. No contienen tokens ni claves.
La inspeccion acredita permisos efectivos del usuario contenido, no un Publish
real DacFx con OIDC elevado. T037 conserva ese limite y sigue pendiente de su
validacion integral; la concesion no se dejo activa para un futuro dispatch.

Antes de publicar debe coordinarse otra autorizacion y una ventana acotada con
revocacion/verificacion incluso ante fallo, ademas de los restantes gates de
workflow, smoke y datos sinteticos. Este ensayo no autoriza esa publicacion.
