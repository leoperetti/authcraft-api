# Implementation Plan: Separación y verificación de ambientes

**Branch**: `main` | **Date**: 2026-11-07 | **Spec**: [spec.md](spec.md)

**Input**: `specs/001-environment-separation/spec.md` y nombres Azure confirmados por el usuario.

El setup resolvió el identificador lógico `001-environment-separation` desde el
directorio activo; la rama Git comprobada es `main`. No se cambió de rama.

## Summary

Habilitar development en localhost y staging sobre recursos existentes; diseñar la
promoción de la misma aplicación y DACPAC a production con aprobación protegida.
Por decisión del usuario, el repositorio es público y la publicación de production
permanece bloqueada hasta disponer de protección verificable. No se reemplaza la aprobación
por `workflow_dispatch`, un booleano del workflow ni la revisión de main.

Separar identidades de ejecución y publicación, validar destinos antes de abrir
conexiones o ejecutar el bootstrap y reconciliar los dos workflows remotos.
Mantener el proyecto SQL existente: construir con MSBuild, evaluar el DACPAC y
publicar únicamente cambios compatibles con la aplicación anterior y candidata.
No introducir migraciones EF ni modificar contratos de negocio.

Esta fase solamente consulta metadatos y genera documentación. No asigna permisos,
configura secretos, despierta bases pausadas, publica código ni crea recursos.
La actualización de diseño de 2026-11-07 agrega a Production una guía fija y un
informe Markdown por intento, sin cambiar el bloqueo ni la autorización vigente.

## Technical Context

**Language/Version**: C# / .NET 10; PowerShell para verificación y automatización en Windows.

**Primary Dependencies**: ASP.NET Core, Azure.Identity, configuración Azure Key Vault,
EF Core SQL Server, Microsoft.Data.SqlClient; GitHub Actions, Azure CLI, MSBuild/SSDT
y SqlPackage para la publicación del proyecto SQL tradicional.

**Storage**: SQL Server local para development; Azure SQL `authcraftdbbrasil` con
`authcraft-db-staging` y `authcraft-db` separadas. Vaults distintos por ambiente.
No se incorpora Blob Storage.

**Testing**: xUnit/VSTest en `authcraft-api.Tests`. Añadir pruebas de configuración,
aislamiento, salud y controles de publicación. Ejecutar pruebas SQL locales con
`AUTHCRAFT_TEST_SQL_CONNECTION` y base descartable aceptada por el fixture.
Además validar el esquema del DACPAC localmente: el fixture existente usa
`EnsureCreated` y SQL adicional y no demuestra por sí solo que el proyecto SQL publica.

**Target Platform**: Windows local y dos Azure Web Apps Windows con runtime v10.0,
en el plan compartido F1 `ASP-AuthCraft-bc2b`, grupo AuthCraft.

**Project Type**: API existente y automatización de publicación.

**Performance Goals**: Preparación local y ensayo de recuperación en 30 minutos
cada uno, según SC-002/SC-005; sin objetivo nuevo de carga. Comprobaciones remotas
con timeout finito y reintentos acotados por cold start, sin declarar éxito por timeout.

**Constraints**:
- Sin secretos en repositorio, paquetes, comandos impresos, reportes públicos o logs.
- Development no accede a vaults remotos; runtime y publicador no comparten privilegios.
- Ninguna escritura remota durante esta fase. Ambas bases están pausadas; conectar
  puede reactivarlas y generar consumo. Pedir autorización antes de validarlas.
- No crear, eliminar, recrear, contratar ni cambiar capacidad/visibilidad sin autorización.
- Fallos bloquean etapas siguientes; no hay reversión destructiva automática.
- El informe Production se genera sin salida nativa sin sanitizar y no modifica
  documentación versionada; un fallo del runner antes de la fase final se registra
  como interrupción no concluyente, no como una ejecución con informe exitoso.
- Mantener production bloqueado también en Azure: un job omitido no basta si persisten
  credenciales utilizables por workflows alternativos.

**Scale/Scope**: Tres ambientes; dos aplicaciones, dos bases, dos vaults y tres
identidades administradas asignadas por usuario en AuthCraft. Production también
tiene una identidad asignada por sistema. No se modifican recursos de CEKIDE.

## Constitution Check

*GATE: revisado antes de investigación y después del diseño.*

| Principio | Resultado del diseño |
|-----------|----------------------|
| I. Capas | PASS: validación de hosting en API; conexión y salud SQL en Infrastructure; Application conserva solamente lógica de negocio. |
| II. Seguridad | PASS: aprobación obligatoria, destinos verificados, permisos separados y secretos protegidos. Production no se habilita sin controles verificables. |
| III. Contratos/datos | PASS: rutas existentes intactas; esquema en el proyecto SQL; sin migraciones EF ni cambios de columnas por esta feature. |
| IV. Verificabilidad | PASS: build explícito de API y MSBuild SQL, pruebas focalizadas y validación real del DACPAC local. |
| V. Configuración | PASS: proveedores ASP.NET Core, User Secrets local y vault por ambiente; normalización compatible de claves existentes y validación temprana. |
| VI. Nombres | PASS: variables descriptivas en código y scripts futuros. |
| VII. Repositorios | PASS: las operaciones de datos de prueba usan servicios/repositorios existentes; la salud SQL no consulta tablas de negocio. No se refactorizan repositorios ajenos. |
| VIII. Production protegido | PASS: checklist fija y reporte por ejecución son documentación/evidencia solamente; no habilitan workflows ni sustituyen aprobación o gates. |

No hay excepciones constitucionales. Diseño listo para tareas; **habilitación de
production bloqueada**, no requisito satisfecho. La feature completa no puede
declararse terminada hasta resolver la protección y verificar FR-012/SC-003.

## Project Structure

### Documentation (this feature)

```text
specs\001-environment-separation\
  spec.md
  plan.md
  research.md
  data-model.md
  quickstart.md
  contracts\
    environment-deployment.md
  checklists\
    requirements.md
docs\
  production-release-checklist.md # guía fija; no se actualiza automáticamente
  # production-deployment-report.md generado por run como artefacto Actions
```

`tasks.md` será generado por `/speckit-tasks`, no por este comando.

### Source Code (repository root)

```text
.github\workflows\             # reconciliar publicación staging y production
authcraft-api\
  Program.cs                  # validar antes de DI/SQL/bootstrap
  appsettings*.json            # configuración pública específica del ambiente
  Properties\launchSettings.json
  Controllers\                # salud adicional; no alterar rutas de negocio
  Bootstrap\                  # preservar bootstrap; pruebas no lo disparan en production
Infrastructure\
  DependencyInjection.cs      # resolver conexión efectiva y validar destino
  Data\                       # comprobación SQL no destructiva
Application\                  # conservar servicios de negocio
Domain\                       # sin nuevas entidades persistidas
authcraft-db\                  # MSBuild/SSDT, DACPAC y postdeploy existente
authcraft-api.Tests\           # pruebas unitarias/integración y SQL local
scripts\                      # verificadores PowerShell pequeños, por crear
docs\                         # guía de ambientes y recuperación, por crear
```

**Structure Decision**: Reutilizar capas y herramientas existentes. Los scripts de
verificación deben compartir validación de destinos dentro de su propio runtime;
no crear un framework de despliegue ni una tabla de versiones en la base.

## Phase 0 — Investigación y decisiones

Ver [research.md](research.md) para evidencia, decisiones y alternativas.
Datos confirmados el 2026-11-05:

| Ambiente | Aplicación | Base | Vault | Runtime |
|----------|------------|------|-------|---------|
| development | localhost:5171 / localhost:7202 | base local dedicada | User Secrets | usuario local |
| staging | authcraft-api-staging | authcraft-db-staging | authcraft-vault-staging | no hay identidad asignada actualmente |
| production | authcraft-api | authcraft-db | authcraft-vault | identidad por sistema con Secrets User |

Los nombres y estados se confirmaron mediante ARM. No se obtuvieron valores de
secretos ni se estableció conexión SQL. Existencia y estado Running no prueban
salud, aislamiento SQL ni compatibilidad del esquema.

## Phase 1 — Diseño

### Configuración y runtime

- Conservar nombres de ambiente ASP.NET Core `Development`, `Staging`, `Production`;
  usar `development`, `staging`, `production` en documentación y registros.
- Configuración común sin conexiones, claves o nombres de vault de production como
  valores por defecto locales. User Secrets contiene `DatabaseConnectionString`
  y `Jwt:Key` para development; los archivos específicos contienen solo ajustes públicos.
- Normalizar una vez claves existentes de conexión, JWT y Google al contrato
  [environment-deployment.md](contracts/environment-deployment.md). Consumidores
  comparten valores efectivos; no permitir aliases distintos para el mismo secreto.
  No imprimir sus valores para diagnosticar conflictos.
- Comparar servidor/base y vault contra los destinos esperados por ambiente antes
  de DI, apertura SQL y bootstrap. Local admite únicamente localhost o LocalDB y
  bases dedicadas; staging/production deben coincidir con su servidor/base conocidos.
  Validar ambientes desconocidos como error; `Testing` queda exclusivamente en
  hosting de pruebas con reemplazos explícitos, no como bypass remoto.
- Añadir expectativas públicas `Database:ExpectedServer` y `Database:ExpectedName`
  en archivos por ambiente; el verificador de publicación las compara con el
  inventario fijo aprobado antes de confiar en ellas.
- Configurar `ASPNETCORE_ENVIRONMENT` explícito en las dos Web Apps; no asumir
  que el nombre de la aplicación activa Staging.
  En production, configurar y verificar `Production` únicamente con autorización
  durante la habilitación futura, antes del preflight/publicación; resolver cualquier
  `DOTNET_ENVIRONMENT` contradictorio y considerar el posible reinicio.
- Production usa su identidad por sistema para su vault. Asignar identidad por
  sistema a staging y conceder Secrets User solo sobre su vault, previa autorización.
  No usar las identidades publicadoras como credenciales de runtime.
- Verificar usuarios y permisos SQL de cada credencial de runtime; cadenas distintas
  no demuestran aislamiento. Cambios de privilegios se revisan antes de aplicarlos.
- Mantener bootstrap explícito: no alterar su valor actual en production sin revisión.
  Pruebas de publicación nunca habilitan bootstrap ni usan usuarios reales.
  Preparar cuentas/tenant sintéticos únicamente en local/staging mediante mecanismos existentes.
  La preparación local no depende de Azure. En staging, preparar datos únicamente
  después de sus permisos/configuración y del esquema compatible, antes del smoke,
  con autorización específica y sin notificaciones.
- Añadir salud viva y lista sin tablas de negocio, secretos ni dependencia de Brevo.
  Ver contrato para respuestas y validación del release. OAuth Google es opcional
  si ambas claves están ausentes; una configuración parcial falla explícitamente.
  Documentar credenciales/destinatarios de prueba de correo sin cambiar reglas de negocio.

### Identidades y permisos

- `oidc-msi-aede`: candidata de publicación production; federación existente desde main.
- `authcraft-api-st-id-97cc`: candidata de publicación staging; federación existente desde main.
- `oidc-msi-a9fa`: Website Contributor production sin federación registrada; conservar
  hasta verificar consumidores. No eliminar por parecer redundante.
- Auditar las identidades publicadoras adjuntas a production antes de desacoplarlas;
  quitar esas asignaciones solo tras probar la identidad de runtime y aprobar el cambio.
- Mientras production esté bloqueado, retirar o inhabilitar, con autorización, la
  confianza de publicación desde main y cualquier credencial alternativa utilizable.
  Mantener la identidad runtime y su acceso al vault para no interrumpir el servicio.
- El publicador SQL usa token Entra obtenido por OIDC y usuario contenido de su
  propia base, con permisos revisados para DacFx. No conceder administración del
  servidor ni db_owner por defecto. Crear usuarios requiere ejecución inicial autorizada.
- Acceso de runtime a SQL puede conservar credenciales actuales del vault si se
  verifica su alcance mínimo; no se impone una migración de autenticación en esta feature.

### Construcción, staging y promoción

1. Reconciliar main remoto contra checkout sin sobrescribir cambios locales.
   Primero retirar las rutas directas a production; no activar el nuevo flujo mientras
   el anterior pueda publicar en paralelo.
2. PR: build explícito API, MSBuild SQL y pruebas sin permisos Azure. Main y ejecución
   manual autorizada: repetir verificaciones y construir una sola vez el release.
3. Publicar aplicación y exactamente un DACPAC con manifiesto de commit, run ID,
   nombres y hashes SHA-256. Obtener la ruta real de salida MSBuild; el `.sqlproj`
   declara `AssemblyName=authcraft_db`, mientras el workflow supone `authcraft-db.dacpac`.
   No asumir que ese nombre es válido sin comprobar el artefacto generado.
4. Staging: comprobar referencia main, identidades, destinos, hash, configuración y
   permisos. Serializar el workflow completo con `cancel-in-progress: false`;
   comprobar versión superada antes de cualquier modificación de un destino. Solo
   para el primer despliegue, permitir baseline desconocido si Actions no contiene
   un despliegue Staging satisfactorio verificable y se activa el bootstrap explícito.
5. Generar DeployReport y Script contra staging. Revisar compatibilidad con la API
   anterior y candidata, postdeploy y cambios dependientes de datos. Rechazar
   cambios incompatibles/destructivos; no confiar solo en BlockOnPossibleDataLoss.
   Cambios de esquema o postdeploy requieren revisión por responsable autorizado,
   acreditada con autoría/resultado consultados por API GitHub y vinculada al release,
   destino, baseline, compatibilidad API, impacto en datos, recuperación, opciones y
   hashes de DACPAC/report/script según el contrato.
   Si falta evidencia válida, bloquear y permitir reanudar el mismo release sin
   reconstrucción después de revisión. Esta revisión SQL no reemplaza la protección
   de aprobación production. Si el baseline inicial es desconocido, la revisión
   autenticada también debe reconocerlo. Antes de la primera publicación, documentar
   el procedimiento de recuperación (T044), aunque sea una tarea de US4.
6. Para cambios compatibles, publicar esquema antes de aplicación; esto permite que
   el bootstrap de la candidata encuentre su esquema. Mantener
   `BlockOnPossibleDataLoss=True` y `DropObjectsNotInSource=False`. Verificar que
   opciones son idénticas en evaluación y publicación y reevaluar si cambia el destino.
7. Publicar API staging, verificar salud/versionado, login de prueba y lectura de roles
   de ese tenant; verificar rechazo del token staging bajo la configuración production
   mediante pruebas aisladas, sin probar credenciales staging contra producción real.
8. Production permanece deshabilitado mientras no haya protección compatible.
   No declarar `environment: staging` como requisito actual si la cuenta no admite
   environments privados: la publicación staging puede usar su confianza main limitada.
9. Cuando la protección esté disponible, configurar environments `staging` y
   `production`, restringidos a main; production con required reviewers y sin bypass
   administrativo. Reconfigurar subjects OIDC por ambiente y retirar confianza main
   de production. No basta añadir YAML ni un secreto al environment.
10. Evaluar production sin escrituras para obtener report/script antes de aprobación,
    usando permiso de lectura de metadatos separado del publicador; mostrar release
    y riesgos. El job con credenciales de modificación espera aprobación protegida,
    revalida el destino, publica el mismo DACPAC y API y comprueba salud. Si se detecta
    deriva respecto al reporte revisado, bloquear y solicitar nueva revisión/aprobación.
11. Para cada intento de publicación production, generar `production-deployment-report.md`
    y adjuntarlo en el artefacto estable `production-deployment-report` del mismo run,
    en éxito o fallo; enlazarlo desde el resumen del run. Incluir versión, destino,
    etapa/resultado, verificaciones, referencia de aprobación y disponibilidad/hash
    de evidencia SQL. En fallo, exponer únicamente causa sanitizada y siguiente paso.
    Generar/adjuntar el informe aun si una etapa previa falla y marcar etapas no
    alcanzadas como no ejecutadas; excluir credenciales, datos personales y salida
    nativa sin sanitizar. Retenerlo 30 días. Mantener
    `docs/production-release-checklist.md` como guía revisable fija, sin commits
    automáticos ni sobrescritura por cada intento. Un reporte solo documenta resultados:
    no autoriza publicación ni habilita Production.

### Validación y recuperación

Pruebas de configuración, JWT cruzado, secretos faltantes, salud, manifiesto,
referencia no autorizada y release superado. Prueba local con DACPAC real y las
pruebas SQL del fixture. Staging con datos sintéticos, sin signup/invitaciones/reset
que envíen correo. Production: verificaciones no destructivas y solo autorizadas.
Probar el informe con casos de éxito y fallos en distintas etapas; verificar español,
causa/acción sanitizadas, etapas posteriores como no ejecutadas, ausencia de secretos
y asociación/retención del artefacto.

Fallo de esquema bloquea API y promoción; no asumir transacción global de DacFx.
Fallo de API permite volver al paquete anterior solo si es compatible con el esquema
aplicado. Cambios contractivos requieren otra versión/plan aprobado. Restauración
PITR requiere intervención explícita, base separada, revisión de escrituras recientes
y autorización; nunca restaurar encima de datos actuales automáticamente.

## Readiness and completion gates

- Diseño completo y decisiones materiales resueltas; puede generarse `tasks.md`.
- Bloqueos operativos explícitos: aprobación protegida production, identidad runtime
  staging, secretos requeridos, permisos SQL efectivos y autorización de pruebas que
  reactiven bases pausadas. Son prerrequisitos verificables, no supuestos de éxito.
- La API de production sigue en su estado actual durante el plan. Bloquear futuras
  publicaciones exige acciones de implementación autorizadas; no se ejecutaron ahora.
- No completar la feature con solo development/staging: FR-012 y SC-003 siguen pendientes.
