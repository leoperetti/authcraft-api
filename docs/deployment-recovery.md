# Recuperacion de publicaciones

Antes de publicar staging, conservar el paquete API y DACPAC de la misma ejecucion,
su commit y hashes, asi como el reporte/script SQL revisado. No usar una recompilacion
para sustituir un paquete validado.

## Diagnostico por etapa

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

## Preparacion inicial de staging

La base staging estaba vacia. Se usa el release estable de GitHub Actions
`41882690687`, commit `091fe2c9856fa8ba46387bc3e751fd21ca424662`, sin incluir
los cambios locales pendientes.

Si falla la publicacion SQL, no publicar la API y revisar el estado efectivo de
staging: DacFx no garantiza una transaccion global. No aplicar el esquema a production.
Si falla la API, conservar el esquema y diagnosticar identidad, vault, runtime y
conexion; staging aun no tiene una version estable anterior para restaurar.
No borrar/recrear bases o recursos como alternativa automatica.

La preparacion termino satisfactoriamente: este run/commit es ahora la primera
version estable de staging. Deployment API `2d9d8af3f892419b9a109a9207e9f580`;
login sintetico y lectura autenticada de roles HTTP 200, tambien despues de
deshabilitar bootstrap y reiniciar. Ver [evidencia de ambientes](environments.md).
Los artifacts originales y su revision estan vinculados en
[issue #10](https://github.com/leoperetti/authcraft-api/issues/10).
Esto identifica una version recuperable, pero no acredita un ensayo completo de
rollback ni los controles automaticos de retencion/promocion pendientes.

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
