# Recuperacion de publicaciones

Antes de publicar staging, conservar el paquete API y DACPAC de la misma ejecucion,
su commit y hashes, asi como el reporte/script SQL revisado. No usar una recompilacion
para sustituir un paquete validado.

## Diagnostico por etapa

### Fases SQL separadas: proteccion local verificada

Decision autorizada tras el ensayo LocalDB del 2026-11-09: conservar
IncludeTransactionalScripts=True para DacFx y dar al postdeploy una transaccion
propia que incluya semillas, validaciones y guard. No se promete rollback global.
El postdeploy versionado implementa esa proteccion: rechaza @@TRANCOUNT distinto
de cero con 51001 antes de escribir, usa XACT_ABORT/TRY/CATCH y revierte su propia
transaccion antes de relanzar el error. El inicializador y el publicador pasan
IncludeTransactionalScripts=True en todas las acciones.

El ensayo LocalDB con SqlPackage 170.4.53.0 y 170.5.96.0 comprobo exito y fallos
547/51000 desde otra conexion: en los fallos protegidos no persiste la semilla ni
la confianza adquirida por el postdeploy; la columna y el check de la fase DacFx
si permanecen. El control sin proteccion conserva esos efectos, por lo que no
demuestra atomicidad global. El ensayo no verifica el estado remoto de Staging.

Si falla con 547/51000, detener API/smoke/promocion. Inspeccionar desde otra
conexion columnas/constraints aplicadas por DacFx, estado de confianza, semillas y
datos previos. Confirmar por evidencia si la unidad postdeploy revirtio; el esquema
puede permanecer aunque esa fase haya revertido todo. Mantener el estado global
partial/unknown hasta inspeccion y comprobar compatibilidad de la API previa.
No escribir postdeploy_result=rolled_back ni failure_state_verification=unchanged
solo por el codigo de salida. Registrar transaction_scope y la referencia al
SQL/hash/herramienta/ensayo. DeploymentAttempt (T068) conserva automaticamente
esos campos en `sql-attempt.json` antes de Publish y al finalizar, y el workflow
guarda `deployment-attempt.json` mas la prueba LocalDB/TRX/SQL en el artefacto
`staging-deployment-attempt` durante 30 dias. La referencia vincula version/hash
del SqlPackage, fuentes, escenarios, limites y hash del plan real.

Ante fallo de Publish, postdeploy_result y failure_state_verification quedan
unknown, no rolled_back/unchanged. Incluso el exito SQL conserva
postdeploy_result=unknown hasta inspeccion independiente. API/smoke omitidos se
registran not_executed; si falla el smoke combinado, salud/autenticacion quedan
unknown hasta saber que comprobaciones se ejecutaron. El SQL completo nunca se
incluye en el resumen publico del paso, ni diagnosticos nativos o tokens.
Si se pierde el runner o no puede subirse el artefacto, tratar la evidencia como
no disponible; no reintentar sin inspeccionar el destino.

Resolver la causa y cualquier dato invalido solo con autorizacion. Reevaluar el
destino y exigir revision de los hashes nuevos cuando cambie el plan; no reintentar
automaticamente. No borrar columnas ni deshabilitar constraints para forzar exito.
El rollback del motor sobre la transaccion propia del postdeploy no es una
restauracion ni una reversion destructiva del esquema. PITR sigue siendo manual,
autorizado y hacia una base separada.

| Etapa fallida | Accion inmediata | Condicion para continuar |
|---|---|---|
| Preflight o verificacion de artefactos | Detener el flujo; conservar el run ID y el error sanitizado. No solicitar credenciales SQL ni iniciar publicaciones. | Corregir la causa y volver a validar el mismo release; cualquier cambio de paquete requiere un release nuevo. |
| Evaluacion/revision del esquema | No publicar la API. Conservar DeployReport, script, opciones y sus hashes con acceso restringido. | Un responsable autorizado confirma compatibilidad con la API anterior y candidata, datos y postdeploy. Si cambia el plan, generar evidencia y revision nuevas. |
| Publicacion DacFx | Detener las etapas siguientes. Tratar el estado de la base como parcial o desconocido hasta inspeccionarlo con acceso autorizado. | Revisar el estado efectivo del esquema y datos; no asumir atomicidad ni ejecutar un rollback SQL automatico. |
| Publicacion de la API | Conservar el esquema aplicado y detener smoke/promociones. Consultar salud, version, runtime, identidad, vault y conectividad sin imprimir secretos. | Reintentar el mismo paquete solo despues de diagnosticar la causa y verificar que sigue siendo compatible con el esquema aplicado. |
| Salud, login o roles | Marcar la version como fallida y bloquear promociones. No crear usuarios ni enviar notificaciones como parte del diagnostico. | Resolver el fallo y repetir las comprobaciones con la misma version y la cuenta sintetica autorizada del ambiente. |
| Aprobacion o preflight production | No obtener credenciales de modificacion ni ejecutar acciones sobre production. | Mantener production bloqueado hasta que la proteccion, aprobacion y evidencia correspondan al mismo release, destino y plan revisado. |

En cada caso registrar ambiente, run ID, commit, version, etapa, hora UTC y
resultado. El registro no debe incluir contrasenas, tokens, cadenas de conexion,
valores de Key Vault ni cuerpos de autenticacion.

## Estado actual de Staging

El 2026-11-06 se documento una publicacion manual del run `41882690687`,
commit `091fe2c9856fa8ba46387bc3e751fd21ca424662`, con deployment API
`2d9d8af3f892419b9a109a9207e9f580` y comprobaciones de login/roles. Es evidencia
historica, no una confirmacion de que esa version siga activa ni un release de
recuperacion aprobado.

La comprobacion posterior del 2026-11-09 encontro `/health/live` devolviendo 404
y el SHA corto de OneDeploy no pudo vincularse a un commit de este repositorio.
No existe un despliegue satisfactorio verificable del workflow nuevo que sirva
como baseline; por eso el preflight del run
[42637441215](https://github.com/leoperetti/authcraft-api/actions/runs/42637441215)
acepto `unknown` unicamente bajo el gate de bootstrap inicial. Ese run solo valido
paquetes, OIDC, procedencia y metadatos: no publico SQL ni API.

Por lo tanto, no hay una version Staging actualmente confirmada como recuperable.
La publicacion sigue bloqueada hasta revisar autenticadamente el plan exacto,
completar y verificar los permisos SQL requeridos y autorizar expresamente ese
release. Ver [evidencia de ambientes](environments.md). No borrar/recrear bases
o recursos ni tratar la revision del run antiguo como autorizacion.

## Publicaciones posteriores

1. Identificar version activa, release candidato, destino y etapa que fallo; cotejar
   los hashes con la evidencia retenida de la ejecucion original.
2. Detener etapas posteriores y promociones. Conservar evidencia sanitizada y
   confirmar que la base no esta siendo modificada por otra ejecucion.
3. Evaluar primero salud y escrituras recientes. Una restauracion exige evaluar el
   impacto de esas escrituras y recibir autorizacion especifica del responsable.
4. Volver al paquete API anterior solo despues de verificar su compatibilidad con el
   esquema ya aplicado. Nunca revertir automaticamente el esquema.
5. Si la recuperacion exige recuperar datos, solicitar una restauracion PITR a una
   base separada para inspeccion y validacion. El responsable de datos decide y
   autoriza cualquier accion posterior sobre la base activa.
6. No restaurar encima de production, borrar/recrear una base ni descartar escrituras
   validas automaticamente.

La identificacion de la version, la etapa fallida y la evidencia necesaria para
escalar deben completarse en 30 minutos o menos. Ese limite no autoriza una
restauracion ni una modificacion; una recuperacion de datos requiere aprobacion
separada y puede tomar mas tiempo. Registrar el tiempo observado durante un ensayo
antes de afirmar que se cumplio el objetivo.

La aprobacion protegida de production sigue pendiente; esta preparacion no autoriza
publicar alli. No usar cuentas reales, invitaciones ni emails para pruebas de recuperacion.

## Checkpoint de una publicacion parcial

Cuando una publicacion Staging termina con fallo despues de aplicar SQL o desplegar
la API, `scripts/Get-StagingRecoveryCheckpoint.ps1` puede validar la procedencia
autenticada del run, sus etapas completadas y fallidas, la vigencia de los artefactos,
el manifiesto y CandidateRelease del run original, y que no exista un intento de
despliegue posterior. El preflight de empaquetado puede recibir ese run mediante
`recovery_deployment_run_id` para generar `recovery_review_required` y conservar
el commit como `partial_recovery`.

Este checkpoint es evidencia historica, no una confirmacion del estado vivo de
Staging ni un baseline exitoso. No habilita el despliegue, no autoriza SQL/API ni
promocion, y no reemplaza la conciliacion del esquema, la version activa, la revision
autenticada exacta o la autorizacion expresa de publicacion. El workflow de
despliegue no acepta el identificador de recuperacion; cualquier reanudacion sigue
sujeta a sus gates normales.
