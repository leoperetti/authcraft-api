# Data Model: Ambientes y publicaciones

Modelo operativo; no agrega tablas, migraciones EF ni entidades Domain.
Las evidencias se guardan como documentación y artefactos protegidos de publicación,
no en la base de negocio.

## Environment

- `name`: development | staging | production; único.
- `runtime_environment`: Development | Staging | Production.
- `application`, `database_server`, `database_name`, `vault`: asignaciones esperadas.
- `runtime_identity`, `publisher_identity`: referencias independientes, nunca credenciales.
- `shared_dependencies`: servidor SQL/plan compartidos.
- `verification_state`: pending | confirmed | blocked.
- `checked_at`, `evidence_reference`, `blocking_reason`: trazabilidad.

Relaciones: cada ambiente tiene varias asignaciones y muchos intentos de publicación.
Local no tiene vault remoto. Una aplicación/base/vault no puede asignarse a otro
ambiente; el servidor/plan sí pueden compartirse con impacto documentado.

## ResourceAssignment

- `environment`, `resource_kind`, `resource_name`, `resource_id`.
- `identity_purpose`: runtime | application_publish | schema_publish | metadata_review.
- `permission_scope`, `federation_subject`, `verification_state`, `evidence_reference`.

Validar pertenencia al grupo/ambiente y permisos SQL efectivos además de RBAC.
No registrar passwords, cadenas de conexión completas, JWT, access tokens ni valores
de vault. confirmed requiere evidencia del control concreto, no solo existencia ARM.

## CandidateRelease

- `commit_sha`, `run_id`, `created_at`.
- `api_artifact_name`, `api_sha256`, `dacpac_artifact_name`, `dacpac_sha256`.
- `previous_release`, `staging_validation_reference`.
- `state`: built | staging_failed | staging_validated | awaiting_approval |
  production_blocked | approved | production_failed | published.

La identidad del release no cambia entre ambientes. Reports/scripts son específicos
del destino y no forman parte del par inmutable de paquetes.
Hash distinto, versión superada o evidencia staging de otro release invalida promoción.
`production_blocked` es el estado previsto mientras no haya protección compatible.

## DeploymentAttempt

Extension implementada localmente 2026-11-09, sin tablas nuevas:

- `include_transactional_scripts`: debe ser true en las opciones revisadas.
- `postdeploy_transaction`: true solo tras verificar el bloque versionado exacto.
  Ambos campos pueden ser null si el intento se detuvo antes del publicador;
  no significa que se ejecutaron opciones falsas ni que haya cobertura acreditada.
- `transaction_scope`: unverified | schema_only | separate_schema_and_postdeploy;
  describe evidencia de cobertura, no el valor de la opcion.
- `transaction_verification_reference`: SQL/hash, version de SqlPackage, limites
  observados y referencia a ensayos de exito/fallos 547 y 51000.
- `failure_state_verification`: pending | unchanged | partial | unknown;
  se determina por inspeccion del estado, nunca por el exit code solamente.

Publicacion bloqueada si no se demuestra la transaccion propia completa del
postdeploy ademas del alcance DacFx documentado. separate_schema_and_postdeploy
no representa una transaccion global: tras un fallo del postdeploy, el estado
global puede ser partial aunque esa fase haya revertido sus propios cambios.
Agregar `postdeploy_result`: pending | committed | rolled_back | unknown,
determinado por evidencia independiente; no inferir rolled_back del exit code.
Un fallo marca failed y detiene API/promocion; no implica que unchanged
sea cierto ni autoriza rollback/restauracion. Ningun campo adicional acredita
aprobacion por si solo.

En la ruta de reanudacion Staging, el runner ejecuta la prueba LocalDB exacta
antes de OIDC y conserva seis SQL sinteticos, JSON de prueba y TRX. El publicador
valida la unica prueba ejecutada/aprobada, los seis estados esperados, hashes de
SQL/herramienta/fuentes y version actual de SqlPackage antes de Publish. La
referencia conserva esos hashes, limites de cada fixture, hash del SQL de destino
y linea inicial del postdeploy. El booleano de la opcion no sustituye esa prueba.

El publicador guarda `sql-attempt.json` antes de invocar Publish y al finalizar,
tambien ante error. Tras iniciar Publish, postdeploy_result queda unknown hasta
inspeccion independiente, incluso con exit code cero; un error deja
failure_state_verification=unknown y nunca rolled_back/unchanged automaticamente.
El resumen agregado de Actions preserva `deployment-attempt.json`; API/smoke
omitidos quedan not_executed. Si el smoke combinado falla, salud/autenticacion
individuales quedan unknown porque no se acredita cual llego a ejecutarse.
El artefacto `staging-deployment-attempt` retiene JSON, TRX y SQL durante 30 dias,
con los permisos de acceso del repositorio. La retencion no autoriza el despliegue.
Si el runner se pierde antes de guardar/subir evidencia, el estado sigue
desconocido y requiere inspeccion; no hay garantia de captura tras una caida total.

- `release`, `target_environment`, `started_at`, `finished_at`.
- `preflight_result`, `schema_report_reference`, `schema_result`, `application_result`,
  `health_result`, `authentication_result`.
- `report_artifact_reference`, `report_retention_days`: `production-deployment-report`
  artifact in the same GitHub Actions run, downloadable and retained for 30 days.
- `failure_stage`, `sanitized_failure_summary`, `recommended_next_step`: plain-language
  Spanish summary; empty when no failure; never contain secrets or personal data.
- `approval_reference`, `approved_by`, `approved_at`: solo para production y vinculados
  al release/destino/report revisado; referencias del mecanismo protegido, no un booleano.
- `result`: pending | blocked | failed | succeeded.
- `recovery_reference`: procedimiento y evidencia de intervención cuando corresponde.

El informe es un artefacto por run, no una fila de base de negocio ni una modificación
automática de `docs/production-release-checklist.md`. Un fallo marca las etapas
posteriores como no ejecutadas; el informe no convierte una omisión en éxito.

Transiciones:

1. Validar identidad, destino, referencia, versión y hashes; fallo → blocked, sin escrituras.
2. Staging: evaluar esquema → publicar esquema compatible → publicar aplicación →
   verificar salud/autenticación/lectura → staging_validated o staging_failed.
3. Production sin protección → production_blocked, sin credenciales de modificación.
4. Production con protección: evaluar destino → awaiting_approval → approved →
   revalidar → esquema compatible → aplicación → salud → published.
5. Rechazo, deriva, versión superada o aprobación inválida → blocked.
6. Fallo posterior a una modificación → failed; no implica que se haya revertido.

Una sola modificación activa por destino; el workflow serializa las publicaciones.
Antes de publicar, comparar contra último release satisfactorio del destino.
No permitir downgrade accidental; una recuperación es una intervención explícita
separada, con prueba de compatibilidad y autorización.
