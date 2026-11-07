---
description: "Tareas ejecutables para separar y verificar los ambientes AuthCraft"
---

# Tasks: Separación y verificación de ambientes

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/environment-deployment.md](contracts/environment-deployment.md) y [quickstart.md](quickstart.md).

**Prerequisites**: Diseño aprobado; rama actual `main`; integrar el worktree existente sin sobrescribir cambios ajenos. No crear otra feature ni migraciones EF.

**Tests**: Se incluyen pruebas de los escenarios de aceptación y bloqueo exigidos por el spec, y de los cambios de comportamiento exigidas por la constitución. Escribirlas antes de la implementación relacionada y comprobar que detectan el comportamiento incorrecto.

**Organization**: Tareas agrupadas por historia. Generarlas no autoriza modificaciones Azure/GitHub, publicaciones, conexión a bases pausadas, gastos ni cambios de visibilidad.

## Format: `[ID] [P?] [Story] Description`

- Todas las tareas empiezan con checkbox e ID correlativo.
- `[US1]` a `[US4]` identifican historias del spec.
- `[P]` permite trabajar en archivos independientes después de satisfacer los prerrequisitos de la tabla de dependencias; no permite ejecutar simultáneamente cambios sobre el mismo recurso.
- Una tarea bloqueada permanece sin marcar. Registrar motivo y autorización necesaria en la documentación de ambientes; no convertir un bloqueo en una validación satisfactoria.

## Path Conventions

Rutas relativas a la raíz del repositorio, con separadores Windows. Los archivos nuevos indicados son entregables de implementación, no archivos existentes asumidos.
Documentación operativa en `docs\environments.md`; evidencias confidenciales de ejecución en almacenamiento protegido, enlazadas desde la guía sin copiar datos sensibles.
No agregar tablas para el modelo operativo de publicaciones.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Preparar el trabajo sin modificar recursos ni resolver cambios ajenos.

- [X] T001 Revisar diferencias locales/remotas y cambios pendientes en `.github\workflows\main_authcraft-api.yml`, `.github\workflows\main_authcraft-api-staging.yml` (remoto), `authcraft-api.slnx` y `authcraft-api.Tests\authcraft-api.Tests.csproj`; registrar en `docs\environments.md` qué se reutiliza y qué no pertenece a esta feature, sin checkout destructivo, commit ni push.
- [X] T002 Comprobar disponibilidad de .NET 10, Git Bash, Azure CLI, gh, MSBuild/SSDT y SqlPackage para los comandos de `specs\001-environment-separation\quickstart.md`; ejecutar builds focalizados API/SQL, identificar nombre/ruta real del DACPAC y registrar en `docs\environments.md` fallos de base y herramientas faltantes, sin instalar dependencias salvo fallo de dependencia ni conectarse a Azure SQL.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Fijar controles compartidos antes de implementar historias; no exige solucionar la protección production para poder trabajar localmente.

- [X] T003 Definir en `docs\environments.md` matriz de autorizaciones por operación, manejo de credenciales protegido, gates de conexión a SQL pausado y publicación; registrar production bloqueado, recursos CEKIDE fuera de alcance y período de retención de releases/evidencias permitido por el repositorio. Obtener confirmación del período antes de habilitar el workflow; no contratar ni ampliar límites automáticamente.
- [X] T004 Preparar reutilización del hosting y fixtures de `authcraft-api.Tests\InvitationsTests.cs` y `authcraft-api.Tests\Infrastructure\InvitationSignupSqlServerFixture.cs` para las nuevas pruebas: mantener `Testing` exclusivamente en host de prueba con sustituciones explícitas, proteger bootstrap/red/correo y garantizar que ningún escenario simulado abre conexiones Azure ni usa credenciales reales; no reescribir fixtures ajenos si basta un fixture específico.

**Checkpoint**: Herramientas conocidas, gates documentados y pruebas aisladas. Los impedimentos operativos remotos se registran y no bloquean escribir/verificar código local.

---

## Phase 3: User Story 1 - Identificar y verificar los tres ambientes (Priority: P1) — MVP de inventario

**Goal**: Inventariar y verificar asignaciones, identidades y límites de acceso con evidencia no destructiva, distinguiendo existencia de funcionamiento.

**Independent Test**: Ejecutar el verificador de inventario contra metadatos controlados y, con acceso autorizado, recursos reales; obtener exactamente tres ambientes, detectar recurso faltante/inaccesible y asignación cruzada sin escrituras. Solo confirmar aislamiento real tras revisar permisos efectivos.

### Tests for User Story 1

- [X] T005 [US1] Añadir pruebas en `authcraft-api.Tests\EnvironmentInventoryTests.cs` para exactamente tres ambientes, asignación cruzada, recurso ausente, ARM/GitHub inaccesible, identidad sin federación y evidencia de existencia que no prueba salud; invocar el verificador PowerShell con respuestas controladas y comprobar salida no cero ante fallo crítico, sin consultas remotas.

### Implementation for User Story 1

- [X] T006 [US1] Crear `scripts\environments.json` con asignaciones públicas aprobadas de development, staging y production, aplicaciones/bases/vaults AuthCraft y plan/servidor compartidos; implementar las restricciones del modelo: "`name`: development | staging | production; único.", "`runtime_environment`: Development | Staging | Production." y "`identity_purpose`: runtime | application_publish | schema_publish | metadata_review."; no incluir credenciales.
- [X] T007 [US1] Implementar `scripts\Test-EnvironmentInventory.ps1` leyendo `scripts\environments.json`, consultando únicamente metadatos ARM/GitHub y contrastando destinos, runtime, identidades, RBAC/federaciones, disparadores y controles; representar "`verification_state`: pending | confirmed | blocked.", checked_at/evidence_reference/blocking_reason, errores sanitizados y códigos de salida explícitos; no leer valores de secretos ni conectar SQL por defecto.
- [X] T008 [US1] Completar el inventario inicial en `docs\environments.md` mediante T007: hosts reales desde ARM, recursos existentes, dependencias compartidas, bases pausadas y distinción entre existencia/configuración/funcionamiento; enlazar evidencia protegida y mantener pendientes las comprobaciones que requieren datos o permisos no consultados.
- [X] T009 [US1] Auditar consumidores, adjuntos y federaciones de `oidc-msi-a9fa`, `oidc-msi-aede`, `authcraft-api-st-id-97cc` y la identidad por sistema production; registrar propósito confirmado o desconocido y scopes en `docs\environments.md`, contrastando configuración de workflows sin imprimir secretos; no borrar ni desacoplar identidades por inferencia.
- [ ] T010 [US1] Con autorización explícita para conexiones que reactiven bases, comprobar destinos efectivos y permisos SQL de runtime/publicadores mediante sesiones protegidas de solo lectura; registrar en `docs\environments.md` usuarios, privilegios relevantes y ausencia de modificación production por identidades local/staging. Cadenas distintas y RBAC ARM no bastan; si falta autorización/permisos, registrar bloqueo sin confirmar aislamiento.
- [ ] T011 [US1] Preparar y aplicar únicamente correcciones de permisos SQL identificadas en T010 que reciban autorización explícita, con scripts revisados guardados de forma protegida y referencias en `docs\environments.md`; separar usuarios por base/runtime/publicación, conservar autenticación runtime vigente cuando sea válida y no conceder administración de servidor/db_owner como atajo.

  Avance T010/T011 del 2026-11-06: consulta real staging confirmo ausencia del usuario
  publicador. Con autorizacion se creo el usuario externo por client ID con solo
  CONNECT/VIEW DEFINITION, sin roles ni DDL/datos; permisos efectivos verificados
  por impersonacion y catalogo independiente. Runtime sin cambios. Scripts/hashes
  en evidencia privada referenciada en la guia. No completa la auditoria de todos
  los principals ni acredita login OIDC/aislamiento o publicacion DacFx.

- [X] T012 [US1] Con autorización explícita, asignar identidad por sistema a `authcraft-api-staging` y Secrets User solo en `authcraft-vault-staging`; verificar acceso a su vault sin exponer valores y denegación al vault production mediante revisión de permisos. Registrar evidencia en `docs\environments.md`; no adjuntar publicadores al runtime ni cambiar la identidad production.
- [ ] T013 [US1] Reejecutar `scripts\Test-EnvironmentInventory.ps1` y pruebas T005; actualizar `docs\environments.md` con evidencias por control y discrepancias pendientes, comprobando el 100% de asignaciones y límites SQL/vault antes de declarar US1 completa. Recursos inaccesibles, gates no autorizados o discrepancias críticas mantienen la historia pendiente.

**Checkpoint**: Inventario útil aun con bloqueos explícitos; US1 completa solo si las verificaciones requeridas realmente pasan. No necesita los nuevos endpoints de salud ni publicar otra versión.

---

## Phase 4: User Story 2 - Desarrollar en localhost con configuración aislada (Priority: P1)

**Goal**: Iniciar development sin credenciales remotas, preparar su esquema SQL real y validar la aplicación contra datos locales.

**Independent Test**: Desde prerrequisitos disponibles, seguir la guía y completar inicio, login y lectura local en ≤30 minutos; secreto faltante/destino production falla antes de conexiones o bootstrap.

### Tests for User Story 2

- [X] T014 [P] [US2] Añadir `authcraft-api.Tests\EnvironmentConfigurationTests.cs` con ausencia de secreto, aliases iguales/distintos, destinos cruzados, SQL remoto desde Development, vault incorrecto, ambiente desconocido y OAuth opcional/parcial; comprobar precedencia de proveedores para una misma clave y cero accesos SQL/bootstrap/vault/correo antes de validar.
- [X] T015 [P] [US2] Añadir `authcraft-api.Tests\EnvironmentAuthenticationTests.cs` con claves/issuer/audience sintéticos distintos, emisión y rechazo JWT cruzado, consumo consistente de `JwtKey`/`Jwt:Key`, tenant correcto y transición de tokens existentes; mantener contrato HTTP actual sin cuentas ni credenciales production.

### Implementation for User Story 2

- [X] T016 [US2] Implementar resolución única y validación de aliases JWT/Google en `authcraft-api\Configuration\EnvironmentConfiguration.cs`; mantener precedencia ASP.NET Core, pares Google opcionales completos, error por aliases contradictorios/secretos requeridos ausentes y nombres de claves en errores sin valores, según `specs\001-environment-separation\contracts\environment-deployment.md`.
- [X] T017 [US2] Implementar resolución/validación SQL en `Infrastructure\Configuration\DatabaseConfiguration.cs` y conectar `Infrastructure\DependencyInjection.cs`: SqlConnectionStringBuilder, aliases de conexión compatibles, conflicto explícito, localhost/LocalDB y base dedicada local, servidor/base Azure exactos y expectativas verificadas; no ubicar acceso ni abstracciones de datos en Application.
- [X] T018 [US2] Limpiar secretos/conexiones y defaults production de `authcraft-api\appsettings.json` y `authcraft-api\appsettings.Development.json`, agregar expectativas locales públicas y mantener `authcraft-api\Properties\launchSettings.json` en Development; documentar aprovisionamiento protegido User Secrets y no introducir valores secretos de ejemplo que funcionen.
- [X] T019 [US2] Revisar `authcraft-api\appsettings.Staging.json` y `authcraft-api\appsettings.Production.json` para vault/servidor/base propios sin secretos; definir contexto JWT y estrategia compatible de activación en `docs\environments.md`, incluyendo clientes y expiración de tokens previos. No activar issuer/audience estrictos en production ni alterar bootstrap existente sin transición ensayada y aprobada.
- [X] T020 [US2] Integrar validación en `authcraft-api\Program.cs` antes de DI, conexión y bootstrap, validar nombre de vault antes de cargarlo y mantener configuración efectiva compartida con `authcraft-api\Controllers\AuthenticationController.cs`; preservar rutas, respuestas y opción Google, y permitir `Testing` solo con hosting de prueba explícito.
- [X] T021 [US2] Revisar `authcraft-api\Bootstrap\SuperadminBootstrapper.cs` y `authcraft-api\Bootstrap\SuperadminBootstrapOptions.cs` para respetar validación previa y configuración explícita; preparar únicamente cuenta/tenant sintéticos locales con mecanismos existentes, sin cambiar defaults de production ni disparar correo durante validación. La preparación de datos staging pertenece a T040 y requiere sus gates remotos.
- [X] T022 [US2] Implementar `scripts\Initialize-LocalEnvironment.ps1` para seleccionar exactamente un DACPAC generado por MSBuild, validar destino localhost/LocalDB y base dedicada, generar/revisar DeployReport/Script y publicar con BlockOnPossibleDataLoss=True/DropObjectsNotInSource=False; fallar en errores y no usar EnsureCreated/migraciones EF para el esquema de development.
- [X] T023 [US2] Añadir `authcraft-api.Tests\LocalEnvironmentSqlTests.cs` y ejecutar validación real del DACPAC con base local descartable, postdeploy y operaciones locales mediante servicios/repositorios existentes; ejecutar también pruebas SQL existentes con AUTHCRAFT_TEST_SQL_CONNECTION y prefijo seguro, sin confundir fixture EnsureCreated con publicación del proyecto SQL.
- [X] T024 [US2] Actualizar `authcraft-api\README.md`, `docs\environments.md` y `specs\001-environment-separation\quickstart.md` con comandos verificados de desarrollo, fuentes de claves, SQL/SSDT/DACPAC y prueba login/roles sin imprimir password/token; eliminar instrucciones contradictorias directamente relacionadas sin reescribir documentación ajena.
- [X] T025 [US2] Ejecutar T014/T015/T023 y builds focalizados de `authcraft-api\authcraft-api.csproj` y `authcraft-db\authcraft-db.sqlproj`; seguir `docs\environments.md` desde prerrequisitos, medir preparación ≤30 minutos y registrar resultado de inicio/login/lectura local y bloqueos, limpiando solo bases/archivos descartables específicos creados para estas pruebas.

**Checkpoint**: Development funcional e independiente de autorización para modificar Azure. US2 puede ejecutarse en paralelo con la auditoría remota US1 después de Foundations.

---

## Phase 5: User Story 3 - Validar una versión en staging (Priority: P1)

**Goal**: Un único flujo main prepara aplicación y esquema, publica primero staging y prueba funcionamiento, sin ruta directa a production.

**Independent Test**: Publicar una versión autorizada y verificar hash/versionado, salud, login/roles sintéticos; simular fallo de construcción/esquema/smoke y comprobar que no se modifica production.

### Tests for User Story 3

- [X] T026 [P] [US3] Añadir `authcraft-api.Tests\EnvironmentHealthTests.cs` para GET /health/live y /health/ready: status/version exactos, live sin SQL, ready con consulta no destructiva, 503/timeout sanitizado sin cache, sin bootstrap/correo y versión de paquete no modificable mediante configuración remota.
- [X] T027 [P] [US3] Añadir `authcraft-api.Tests\DeploymentPipelineTests.cs` para manifiesto faltante/duplicado/hash distinto, referencia no main, versión superada, ejecución concurrente y fallos de build/esquema/smoke; cubrir revisión de esquema ausente, autor no autorizado y evidencia de otro destino/reporte/release, además de plan cambiado tras revisión; comprobar comandos de modificación no invocados mediante scripts con dependencias controladas y sin llamadas Azure.

### Implementation for User Story 3

- [ ] T028 [US3] Con autorización explícita, deshabilitar primero la publicación directa production existente en GitHub y reconciliar `.github\workflows\main_authcraft-api.yml` y `.github\workflows\main_authcraft-api-staging.yml` contra main remoto sin sobrescribir cambios locales; registrar estado remoto en `docs\environments.md`. No fusionar/activar el nuevo flujo mientras quede un publicador paralelo o ejecución antigua capaz de modificar production.
- [ ] T029 [US3] Con autorización explícita y auditoría T009, inhabilitar confianza GitHub main y credenciales alternativas de publicación production, revisar publicadores adjuntos y desacoplarlos solo si no afecta consumidores/runtime; conservar `oidc-msi-a9fa` si su uso sigue sin resolver y quitar su capacidad publicadora utilizable si corresponde. Verificar cero rutas de publicación production efectivas y registrar evidencia en `docs\environments.md`, manteniendo identidad por sistema/vault y servicio actual.

  Avance 2026-11-06: con autorizacion especifica se retiraron la federacion main
  production, ambos roles Website Contributor y el secreto GitHub SQL antiguo.
  Se verificaron cero roles/federaciones de esos publicadores y se preservaron
  identidades/adjuntos/runtime/vault/Staging. El inventario ahora detecta rutas
  alternativas ARM/federacion/SCM/FTP/secreto antiguo con 21 pruebas aisladas.
  T029 sigue pendiente: no se revoco ni verifico la credencial SQL original; borrar
  su copia GitHub no demuestra cero rutas SQL. Evidencia y limites en la guia.

- [X] T030 [US3] Implementar SQL readiness no destructivo con timeout/cancelación en `Infrastructure\Health\DatabaseReadinessCheck.cs`, sin tablas de negocio, y respuestas contractuales en `authcraft-api\Controllers\HealthController.cs`; cablear DI/rutas en `authcraft-api\Program.cs` preservando autenticación/CORS de negocio y errores sanitizados.
- [X] T031 [US3] Incorporar versión commit inmutable en `authcraft-api\authcraft-api.csproj` durante build/publish y probar su uso por salud; implementar `scripts\New-ReleaseManifest.ps1` para paquete API y DACPAC reales con SHA-256, commit/run ID y rechazo de faltantes/duplicados; usar exactamente los campos de `specs\001-environment-separation\contracts\environment-deployment.md`, sin secretos en paquetes.
- [ ] T032 [US3] Implementar `scripts\Test-ReleasePreflight.ps1` reutilizando inventario T007, con comprobación de referencia main, identidad/scope, destino, configuración requerida, permisos, procedencia de la misma ejecución y "`Hash distinto, versión superada o evidencia staging de otro release invalida promoción.`"; comparar último release satisfactorio por evidencia Actions, fallar ante ausencia de evidencia necesaria y no crear tabla de versiones.
- [ ] T033 [US3] Implementar `scripts\Invoke-DatabaseDeployment.ps1` con modos explícitos evaluar/publicar, DeployReport/Script, token SQL Entra protegido y usuario contenido propio; mismas opciones BlockOnPossibleDataLoss=True/DropObjectsNotInSource=False. Para cambios de esquema o postdeploy, exigir revisión de compatibilidad API anterior/candidata y datos por responsable autorizado en `docs\environments.md`, acreditada mediante registro GitHub autenticado cuya autoría/resultado se consulta por API, enlazado a run/commit, hash DACPAC, destino, opciones y hashes report/script; aplicar el contrato de revisión, bloquear ausencia/autor no permitido/deriva y volver a revisar si cambia el plan. No aceptar archivo aportado libremente ni booleano como acreditación; sin rollback destructivo automático.
- [X] T034 [US3] Implementar `scripts\Test-StagingRelease.ps1` para host resuelto desde ARM, HTTPS, status/version exactos en salud, login/roles de tenant sintético, timeout finito/reintentos acotados y resultado fallido explícito; credenciales/token en memoria protegida, sin signup/invitaciones/reset ni notificaciones reales.
- [ ] T035 [US3] Construir el único workflow vigente en `.github\workflows\main_authcraft-api.yml`: PR solo build/pruebas sin Azure; push main/workflow_dispatch main construyen API y SQL una vez, ejecutan pruebas SQL locales en runner preparado, publican manifiesto/paquetes con retención acordada, preflight/evaluación/revisión acreditada de cambios SQL/esquema antes de API/smoke staging, contents:read global e id-token:write solo jobs Azure; permitir reanudar el mismo release tras revisión sin reconstrucción, serializar publicación completa con cancel-in-progress:false y production sin job publicador efectivo. La revisión SQL staging no sustituye aprobación protegida production.
- [X] T036 [US3] Con autorización explícita, configurar ASPNETCORE_ENVIRONMENT=Staging, vault esperado y secretos exclusivos requeridos en `authcraft-api-staging`/`authcraft-vault-staging`; revisar configuración pública production sin publicarla ni activar transición JWT/alterar bootstrap. Documentar claves/capacidades opcionales y fuentes en `docs\environments.md`, sin copiar secretos production.
- [ ] T037 [US3] Con autorización explícita, establecer/vincular `authcraft-api-st-id-97cc` como publicador OIDC main limitado a staging, verificar issuer/audiencia/prefijo inmutable y usuario SQL contenido con permisos DacFx mínimos probados en su propia base; no conceder permisos production ni exigir environments privados no disponibles. Registrar verificación de token/scope sin JWT completo en `docs\environments.md`.

  Avance 2026-11-06: `authcraft-staging-schema-publisher` ya existe con SID del
  client ID correcto y solo CONNECT/VIEW DEFINITION. No puede publicar ni ejecutar
  el postdeploy; faltan privilegios revisados y DacFx/login con token OIDC real.
  La impersonacion y el test LocalDB de grants no sustituyen esa evidencia.

  Avance posterior: preparado job manual opt-in `verify-staging-publisher` y
  wrapper/test SQL de solo lectura para login OIDC real con usuario/SID esperados.
  PR no obtiene OIDC, skip no cuenta como prueba ejecutada y no hay pasos de
  despliegue. Consulta/assertions probadas en LocalDB real, incluidos grants
  inesperados; controles del wrapper probados con dependencias simuladas.
  Usuario autorizo solo preparacion local: no commit/push/dispatch. T037 pendiente
  de ejecucion autentica y permisos DacFx de publicacion revisados.

  Evidencia posterior: run Actions 42265429985, commit
  5f3ea0d248fa002e2b2dcfa0be337e269614bb0e, validate y verify-staging-publisher
  success. Login OIDC real/usuario/SID/grants de revision ya comprobados; no Publish.
  Preparado diagnostico opt-in evaluate_staging_schema para DeployReport/Script
  del DACPAC/manifiesto del mismo run, con evidencia retenida y sin ampliar grants.
  T037 sigue pendiente de DacFx y permisos de publicacion revisados/probados.

  Evaluacion real autorizada: runs 42267628280 y 42269089780 pasaron validate y
  login/usuario SQL; DeployReport fallo. Segundo run identifica
  diagnostic_category=permission_denied, sin permiso/codigo SQL concreto.
  No se invocaron Script/Publish ni ampliaron grants. T033/T037 siguen pendientes:
  identificar el grant faltante y validar un minimo antes de otra correccion.
  Evidencia, commits y limites registrados en docs/environments.md.

  Correccion autorizada posterior: impersonacion del publicador reprodujo SQL 229
  al leer sys.sql_expression_dependencies. Grant SELECT limitado a esa vista,
  validado en LocalDB descartable y Staging (229 antes, consulta exitosa despues).
  Diagnostico cifrado opt-in identifico bloqueo restante en dbo.__RefactorLog;
  se concedio SELECT solo sobre ese objeto, con verificacion independiente de
  ambas lecturas y sin DDL/DML/lectura de negocio ni roles adicionales.
  Run 42386493432, commit a7a0fcb: validate y verify-staging-publisher success,
  DeployReport/Script de solo lectura completados. T037 avanza; Publish sigue
  pendiente de evidencia de revision y permisos minimos especificos.

- [ ] T038 [US3] Ejecutar T026/T027 con scripts nuevos y builds/tests focalizados; comprobar antes de activación `.github\workflows\main_authcraft-api.yml` y el estado remoto contra casos negativos, ausencia de rutas production y opciones de SQL, sin disparar workflows de publicación como prueba local.
- [ ] T039 [US3] Implementar registro en el resumen/artefacto protegido de `.github\workflows\main_authcraft-api.yml` de DeploymentAttempt con "`result`: pending | blocked | failed | succeeded.", timestamps, versión/destino y resultados preflight/esquema/API/salud/autenticación; CandidateRelease con "`state`: built | staging_failed | staging_validated | awaiting_approval | production_blocked | approved | production_failed | published.", marcando production_blocked tras staging sin aprobación disponible y sin sugerir que production fue publicado; verificar estados con ejecuciones simuladas antes de activar el workflow.
- [ ] T040 [US3] Con autorización explícita, recuperación T044 documentada y gates T028/T029/T036/T037 satisfechos, activar y ejecutar la publicación staging de `.github\workflows\main_authcraft-api.yml` solo tras acreditar revisión SQL T033; después de aplicar el esquema compatible y antes del smoke, preparar cuenta/tenant sintéticos exclusivamente en staging mediante servicios/repositorios existentes y configuración explícita, con autorización de esa escritura y sin envío de correo. Comprobar hashes/versiones, salud/login/roles y fallos con fixtures aislados, y registrar evidencia en `docs\environments.md` sin cambios ni conexiones de prueba a production.
- [ ] T041 [US3] Actualizar `docs\environments.md` y `specs\001-environment-separation\quickstart.md` con el workflow realmente activo, secuencia compatible de esquema/API, evidencia de release, prerrequisitos SQL/OIDC, cold start, prohibiciones de notificaciones y gates production; repetir verificación de disparadores remotos y declarar US3 completa solo con staging real validado.

**Avance local 2026-11-06 (T032/T033, aun pendientes)**: T032 ya contrasta procedencia
Actions, build, retencion y archivos descargados de la misma ejecucion, y consulta
el ultimo job `deploy-staging` satisfactorio para contrastar el SHA anterior.
Sigue devolviendo bloqueo/no cero hasta integrar inventario, identidad, configuracion
y permisos efectivos. T033 tiene pruebas aisladas de revision GitHub autenticada,
rechazo de campos duplicados, autor/destino/release incorrectos, revocacion,
reevaluacion de plan y bloqueo por deriva; no acredita usuario contenido ni grants
Entra reales. No completar estas tareas solo por pasar las pruebas simuladas.

**Checkpoint**: Staging validado y production sin publicador efectivo. No declarar la feature completa.

---

## Phase 6: User Story 4 - Promover y recuperar production de forma controlada (Priority: P2)

**Goal**: Preparar recuperación y pruebas sin modificar production; habilitar promoción exclusivamente cuando exista aprobación protegida compatible y autorización.

**Independent Test**: Rechazo/ausencia de aprobación o deriva dejan production intacto; una promoción protegida aprobada usa hashes staging idénticos. Ensayo no destructivo de recuperación por otro responsable en ≤30 minutos.

### Tests for User Story 4

- [X] T042 [P] [US4] Añadir `authcraft-api.Tests\ProductionApprovalTests.cs` para protección ausente/no compatible, aprobación rechazada/de otro release, referencia no autorizada, reporte revisado cambiado y artefacto expirado; comprobar cero modificaciones con simulaciones aisladas y que workflow_dispatch/booleanos/PR de main no se aceptan como aprobación posterior a staging.
- [ ] T043 [P] [US4] Añadir `authcraft-api.Tests\DeploymentRecoveryTests.cs` para fallo de esquema antes de API, fallo API con esquema aplicado, paquete anterior incompatible y protección de escrituras recientes; comprobar que no se dispara reversión/restauración destructiva automática y que se bloquean siguientes etapas.

### Implementation for User Story 4

- [X] T044 [US4] Redactar `docs\deployment-recovery.md` con diagnóstico por etapa, identificación de versión/evidencia, recuperación de paquete anterior solo compatible, fallos parciales DacFx y escalamiento manual PITR a base separada; requerir revisión de escrituras/autorización y prohibir restauraciones encima de datos actuales y rollback SQL automático.
- [ ] T045 [US4] Ensayar T044 con otro responsable en local/staging, sin cambios production ni correo real; ejecutar T043 y medir identificación/evidencia/recuperación ≤30 minutos, registrar resultados en `docs\deployment-recovery.md` y conservar release anterior/evidencia conforme a retención acordada.
- [ ] T046 [US4] Verificar y registrar en `docs\environments.md` la disponibilidad efectiva de aprobación protegida para este repositorio privado; mantener bloqueo si no existe. Esta tarea requiere decisión futura explícita del usuario sobre un mecanismo compatible y no autoriza contratar, hacer público, migrar repositorio ni inventar aprobador externo.
- [ ] T047 [US4] SOLO tras T046 satisfecho y autorización explícita, configurar environments staging/production restringidos a main, required reviewers production y ausencia de bypass administrativo; contrastar controles reales con `scripts\Test-EnvironmentInventory.ps1` y registrar evidencia en `docs\environments.md`. No marcar completa por existir el YAML.
- [ ] T048 [US4] SOLO tras T047 y autorización explícita, migrar federaciones de publicadores a subjects inmutables por environment, retirar confianza main/credenciales alternativas production y verificar runtime separado y grants SQL de modificación mínimos; documentar acceso de revisión de metadatos production separado del publicador en `docs\environments.md`, sin alterar runtime innecesariamente.
- [ ] T049 [US4] Preparar evaluación production de solo lectura en `scripts\Invoke-DatabaseDeployment.ps1` y cablearla en `.github\workflows\main_authcraft-api.yml` antes de aprobación: mismo release validado staging, DeployReport/Script contra destino production, evidencia protegida para revisar, identidad sin modificación y bloqueo si no hay permiso de evaluación o paquete/evidencia retenidos.
- [ ] T050 [US4] SOLO después de T047/T048/T049, implementar job production protegido en `.github\workflows\main_authcraft-api.yml`: esperar aprobación vinculada a release/destino/reporte antes de obtener credenciales de modificación, revalidar deriva/hash/versión superada, publicar exactamente DACPAC/API staging sin reconstrucción y exigir nueva revisión/aprobación si cambia el plan de esquema; registrar aprobación/resultados sin permitir ruta manual alternativa. Incluir comprobación preflight de ASPNETCORE_ENVIRONMENT=Production y ausencia de DOTNET_ENVIRONMENT contradictorio antes de publicar.
- [ ] T051 [US4] Ejecutar T042/T043 y casos de promoción/rechazo/concurrencia contra dependencias simuladas de `.github\workflows\main_authcraft-api.yml`; comprobar token staging rechazado por configuración production aislada y que fallo/ausencia de aprobación conserva versión/esquema production, sin usar credenciales staging contra production real.
- [ ] T052 [US4] SOLO con protección probada y autorización específica de publicación y transición JWT compatible de T019, configurar y verificar ASPNETCORE_ENVIRONMENT=Production en `authcraft-api`, ausencia de DOTNET_ENVIRONMENT contradictorio y vault/destino efectivos production; tratar el posible reinicio como cambio remoto autorizado, preservar bootstrap y no activar cambios JWT fuera de la transición aprobada. Antes de publicar, comprobar el preflight T050 y ejecutar promoción desde `.github\workflows\main_authcraft-api.yml`; comparar hashes staging, revisión/aprobación y resultados no destructivos de salud/versionado, conservar versión previa compatible y registrar evidencias en `docs\environments.md`. No generar usuarios, pruebas de escritura ni notificaciones reales como validación.
- [ ] T053 [US4] Completar `docs\environments.md` y `docs\deployment-recovery.md` con aprobación activa, asignaciones/controles efectivos, permisos y evidencia de promoción/recuperación; confirmar SC-003 y FR-012/FR-013/FR-015 o mantener explícitamente US4 y la feature pendientes si T046–T052 están bloqueadas.

**Checkpoint**: T042–T045 se pueden ejecutar ahora; T046–T053 no habilitan production hasta resolver los gates. No marcar tareas bloqueadas como realizadas ni retirarlas para declarar completion.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verificar coherencia y resultados, preservando cambios y comportamiento existentes.

- [ ] T054 Revisar `authcraft-api\README.md`, `docs\environments.md`, `docs\deployment-recovery.md` y `specs\001-environment-separation\quickstart.md` contra contratos/flujo efectivo; ejecutar comandos documentados autorizados, comprobar enlaces y retirar instrucciones de publicación production sin protección.
- [X] T055 Ejecutar validación conjunta de cambios en `authcraft-api.Tests\authcraft-api.Tests.csproj`, build Release de `authcraft-api\authcraft-api.csproj` y MSBuild de `authcraft-db\authcraft-db.sqlproj`; empezar con selectores relacionados, ampliar solo si fallos requieren baseline, probar DACPAC local real y comprobar preservación de rutas/auth/tenant/CORS/bootstrap directamente afectados.
- [ ] T056 Revisar los paquetes generados por `.github\workflows\main_authcraft-api.yml`, configuración y evidencias para cero secretos expuestos/cero modificaciones cruzadas/cero notificaciones reales; verificar cinco bloqueos SC-004 y preparar métricas SC-001 a SC-006 en `docs\environments.md`, evitando escanear/imprimir almacenes personales de secretos.
- [ ] T057 Revisar cada aceptación de `specs\001-environment-separation\spec.md` contra evidencia, registrar pendientes en `specs\001-environment-separation\tasks.md` sin alterar el checklist de calidad para simular éxito, y limpiar únicamente archivos/bases descartables creados por esta feature; no declarar completion total si US4 u otros gates siguen bloqueados.

---

## Dependencies & Execution Order

### Phase Dependencies

Setup → Foundations → US1 y US2 independientes.
US3 necesita runtime/configuración US2, asignaciones/permisos relevantes US1 y cierre de rutas production.
Recuperación US4 puede prepararse en paralelo a US3; promoción US4 necesita staging validado y protección nueva.
Polish local/documental puede realizarse aunque US4 esté bloqueada, pero T057 no autoriza completion total.

### User Story Dependencies

```text
Setup + Foundations
  +-- US1: inventario, auditoría y aislamiento --+
  +-- US2: development y configuración --------+--> US3: staging validado
  +-- US4: pruebas/guía recuperación --------------------+
                                                        |
US3 + aprobación protegida futura + autorizaciones ----+--> US4: promoción
Todas las historias verificadas ----------------------------> cierre completo
```

La dependencia US3→US4 aplica a promoción, no a escribir pruebas/guía de recuperación.
La guía de recuperación T044 es un prerrequisito de la primera publicación T040;
puede completarse antes de US3 sin esperar protección ni promoción production.
US1 puede verificar estado actual sin desplegar US2/US3. US2 no necesita permiso de publicación remota.

### Within Each User Story

Escribir pruebas, implementar, ejecutar escenarios y registrar resultados.
La tabla es la fuente de prerrequisitos reales; orden numérico por sí solo no crea dependencia.

| Tarea(s) | Prerrequisitos |
|----------|----------------|
| T001, T002 | ninguno; operaciones independientes, guardar secciones de documentación sin conflictos |
| T003 | T001, T002 |
| T004 | T001, T003 |
| T005, T006 | T003, T004 |
| T007 | T005, T006 |
| T008 | T007 |
| T009 | T008 |
| T010 | T009; autorización SQL pausado y acceso efectivo |
| T011 | T010; autorización por corrección |
| T012 | T009; autorización identidad/vault |
| T013 | T007–T012 completos o evidencia explícita de bloqueo; solo completar con gates satisfechos |
| T014, T015 | T004 |
| T016 | T014 |
| T017 | T014, T006 |
| T018 | T016, T017 |
| T019 | T016, T017; aprobación necesaria antes de activar transición remota |
| T020 | T016–T019, T004 |
| T021 | T020 |
| T022 | T002, T017, T018 |
| T023 | T021, T022, T004 |
| T024 | T023 |
| T025 | T014, T015, T019–T024 |
| T026, T027 | T004, T006; pueden escribirse sin esperar validaciones remotas |
| T028 | T001, T009; autorización GitHub y revisión de ejecuciones activas |
| T029 | T028, T009; autorización Azure/credenciales y comprobación de consumidores |
| T030 | T026, T020 |
| T031 | T002, T030 |
| T032 | T007, T027, T031 |
| T033 | T027, T032, T022 |
| T034 | T030, T032, T015 |
| T035 | T029, T025, T031–T034, T003; retención confirmada |
| T036 | T012, T019–T021; autorización app settings/secretos |
| T037 | T010, T011, T029; autorización OIDC/usuario SQL |
| T038 | T026, T027, T035 |
| T039 | T038 |
| T040 | T013, T025, T036–T039, T028, T029, T044; revisión SQL T033 acreditada y autorización activación/publicación/preparación sintética |
| T041 | T040 |
| T042, T043 | T004; pruebas nuevas independientes del hosting remoto |
| T044 | T003, T043 |
| T045 | T044, T025; autorización adicional si ensayo usa staging |
| T046 | T003; decisión futura explícita y protección compatible disponible |
| T047 | T046, T029; autorización de configuración |
| T048 | T047, T009, T010, T011; autorización OIDC/SQL |
| T049 | T033, T041, T048; acceso de evaluación production autorizado |
| T050 | T047–T049, T042 |
| T051 | T050, T042, T043, T015 |
| T052 | T051, T040, T019; aprobación protegida y autorización de publicación/transición |
| T053 | T045, T052 |
| T054 | T024, T041, T044; incorporar T053 solo cuando esté completa |
| T055 | T025, T038; incluir T051 si el diseño production ya fue habilitado |
| T056 | T013, T025, T041, T042, T043, T055; añadir evidencia T052/T053 cuando disponible |
| T057 | T054–T056; cierre completo requiere además T053 y todos los gates verificados |

### Parallel Opportunities

- T014/T015, T026/T027 y T042/T043 crean archivos distintos y solo requieren Foundations.
- T012 puede ejecutarse separada de T010/T011 si tiene autorización y no comparte cambios de recurso; sus resultados se integran sin editar simultáneamente la misma sección de guía.
- US1 remoto y US2 local pueden avanzar a la vez; US2 no espera permisos Azure.
- No paralelizar tareas que tocan `Program.cs`, el mismo workflow o la misma guía/recurso.
- `[P]` no autoriza subagentes ni cambios remotos: es una oportunidad de planificación.

---

## Parallel Example: User Story 1

Después de T009 y con autorizaciones separadas, auditar permisos SQL T010 mientras
se prepara la asignación de runtime staging T012. Consolidar evidencia secuencialmente
en `docs\environments.md`. Sin autorización, solo continuar trabajo local independiente.

## Parallel Example: User Story 2

Después de Foundations, escribir simultáneamente T014 (`EnvironmentConfigurationTests.cs`)
y T015 (`EnvironmentAuthenticationTests.cs`); luego integrar cambios T016–T020 en orden.

## Parallel Example: User Story 3

Escribir T026 (`EnvironmentHealthTests.cs`) y T027 (`DeploymentPipelineTests.cs`)
en paralelo. Implementar salud y scripts después de sus pruebas; no editar workflow
desde dos tareas simultáneas ni activarlo antes de T028/T029.

## Parallel Example: User Story 4

Escribir T042 (`ProductionApprovalTests.cs`) y T043 (`DeploymentRecoveryTests.cs`)
en paralelo; preparar T044/T045 sin esperar habilitación de protección production.
No anticipar modificaciones T047–T052.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

Completar Setup/Foundations y entregar inventario con evidencia y discrepancias
explícitas. Si SQL/identidades no están autorizados, entregar inventario parcial sin
declarar US1 completa. Este MVP no implica development operativo ni producción protegida.

### Incremental Delivery

1. Inventario/auditoría US1 y development US2; cambios locales validados primero.
2. Con autorización, corregir aislamiento y cerrar rutas production antes de activar staging.
3. Completar US3 con esquema compatible, release inmutable y pruebas reales staging.
4. Preparar recuperación/pruebas US4; mantener tareas de promoción bloqueadas hasta protección compatible.
5. Habilitar US4 solo con decisión y autorización futuras, verificar promoción y cerrar métricas.

### Parallel Team Strategy

Separar pruebas de configuración/JWT, salud/pipeline y aprobación/recuperación.
Asignar un único responsable de edición de workflow y de cada recurso remoto.
No resolver bloqueos delegando una operación sin autorización.

---

## Notes

- **57 tareas**: Setup 2, Foundations 2, US1 9, US2 12, US3 16, US4 12 y Polish 4.
- **6 tareas `[P]`** en tres parejas de pruebas independientes.
- Todos los checks comienzan pendientes; no se realizó implementación, despliegue ni cambio Azure/GitHub al generar esta lista.
- Cobertura: FR-001–FR-004 → US1; FR-005–FR-007 → US2 y configuración US3; FR-008–FR-011/FR-016 → US3; FR-012–FR-015 → US4 y manifiesto/preflight US3; FR-017 → autorizaciones compartidas y revisión final.
- SC-001 → T013/T056; SC-002 → T025; SC-003 → T052/T053; SC-004 → T027/T042/T051/T056; SC-005 → T045; SC-006 → T056.
- No considerar éxito una promoción bloqueada sin haber probado publicación protegida. SC-003 no se satisface por ausencia de publicaciones.
- No alterar los marcadores del checklist de calidad de requisitos durante implementación para superar gates.
