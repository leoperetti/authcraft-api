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
