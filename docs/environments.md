# Ambientes AuthCraft

## Estado de implementacion

Se preservan los cambios locales de invitaciones, signup, bootstrap y el proyecto de
pruebas. No pertenecen a esta feature y no se revierten. La solucion ya incluye
`authcraft-api.Tests`; se reutiliza su hosting de pruebas.

El workflow production remoto (ID `374894897`) esta `disabled_manually`; el de
staging (ID `407226559`) permanece `active`. El commit remoto main verificado es
`091fe2c9856fa8ba46387bc3e751fd21ca424662`. No se modifico el servicio production.
El workflow staging remoto sigue sin cumplir el recorrido completo de
esquema/aprobacion/publicacion. Ahora solo valida paquetes y permite diagnostico
manual OIDC/SQL, como acredita la ejecucion descrita al final de esta guia.
No habilitar publicacion hasta satisfacer los gates pendientes.

Revision de Actions 2026-11-06: el workflow staging remoto sigue activo y su ultima
ejecucion (`41882690684`) fallo en `Build with dotnet` con `MSB4278` al intentar
cargar el `.sqlproj` tradicional desde el build de la solucion; el job de despliegue
quedo omitido. El workflow staging local ahora es una propuesta de validacion de
paquetes: compila API, pruebas y DACPAC por separado y solo genera artefactos en un
`workflow_dispatch` de `main`. Esta version ya reemplazo el workflow staging remoto
con autorizacion; no tiene pasos de publicacion. Incluye un job OIDC opcional de
diagnostico SQL de solo lectura, descrito abajo. No despliega a Azure.

Production permanece pendiente de aprobacion protegida compatible para este
repositorio privado. No se cambia visibilidad, contrata ni modifica CEKIDE.

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
de modificacion SQL. **T029 permanece pendiente** hasta resolver esa evidencia y
los restantes gates de T028/T010, sin rotar credenciales de runtime por inferencia.

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

### Preflight y revision SQL: avance local del 2026-11-06

`Test-ReleasePreflight.ps1` consulta Actions mediante `gh` autenticado: verifica
repositorio, referencia, commit, evento y job de build; rechaza artefactos duplicados,
expirados o de otra ejecucion y compara los tres archivos locales contra descargas
del run original (API, DACPAC y manifiesto). El SHA anterior aportado debe coincidir
con la ultima ejecucion satisfactoria de `deploy-staging` encontrada en el workflow
consolidado. Un build satisfactorio sin ese job no acredita un despliegue staging.
Si no existe esa evidencia, el preflight bloquea: la publicacion manual inicial no
se convierte automaticamente en una ejecucion Actions validada.

Esta parte no completa T032: falta integrar inventario vivo, identidad/scope,
configuracion efectiva y permisos SQL. Incluso cuando los artefactos pasan, la salida
es `state=blocked`, `artifact_validation=verified`, `deployment_authorized=false`
y codigo de salida no cero. Ningun workflow debe interpretar esa salida como
autorizacion para publicar. El workflow staging local incluye las nuevas pruebas,
pero continua sin pasos Azure y aun no se subio.

El publicador SQL de T033 conserva el responsable permitido `leoperetti` y consulta
por API el comentario original de una issue abierta de este repositorio. Rechaza
campos repetidos o contradictorios, autor no permitido y revisiones de otro release,
destino o plan. Despues de leer la revision, vuelve a generar DeployReport/Script
con las mismas opciones, conserva la evaluacion original, compara sus hashes y
reconsulta la aprobacion antes de Publish. Cualquier cambio exige una nueva revision.
La salida nativa de SqlPackage no se publica: puede contener credenciales, y su
texto no debe confundirse con el codigo de salida. Los errores de etapa son
explicitos y sanitizados. Azure SQL requiere validacion del certificado TLS.

T033 tambien permanece pendiente: estas pruebas aisladas no verifican un usuario
contenido Entra ni sus permisos efectivos; faltan T032 y T037. La reevaluacion no
elimina la posible deriva entre comprobacion y Publish ni garantiza atomicidad
DacFx. El workflow consolidado debe serializar todo el despliegue y tratar
cualquier fallo como bloqueo, sin rollback SQL automatico.

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

## Staging operativo verificado

Preparacion y comprobaciones: 2026-11-06 UTC.

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

La preparacion fue manual y autorizada, no una ejecucion del futuro workflow.
GitHub Actions staging aun requiere corregir el build de la solucion SQL tradicional
(MSB4278) y completar los gates/OIDC/promocion. El workflow staging remoto sigue
`active`; production esta `disabled_manually`. **No hacer push a main** hasta
reconciliar el workflow local/remoto y retirar publicadores paralelos.
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
