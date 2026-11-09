# Contracts: Configuración, salud y publicación

Contrato para implementación; los endpoints de salud y manifiesto descritos aquí
todavía no existen. Las rutas y respuestas de negocio actuales se conservan.

## 1. Configuración

| Clave efectiva | Development | Staging | Production |
|----------------|-------------|---------|------------|
| ASPNETCORE_ENVIRONMENT | Development, launch profile | Staging, Web App | Production, Web App |
| DatabaseConnectionString | User Secrets, SQL local | vault staging | vault production |
| Database:ExpectedServer | localhost o instancia LocalDB | authcraftdbbrasil.database.windows.net | authcraftdbbrasil.database.windows.net |
| Database:ExpectedName | base local dedicada | authcraft-db-staging | authcraft-db |
| KeyVault:Name | ausente | authcraft-vault-staging | authcraft-vault |
| Jwt:Key | User Secrets local | secreto exclusivo staging | secreto exclusivo production |
| Jwt:Issuer / Jwt:Audience | valores locales explícitos | valores staging explícitos | valores production explícitos |
| Authentication:Google:ClientId / ClientSecret | par opcional local | par de prueba opcional | par production si se habilita |
| Brevo:ApiKey / FromEmail | protegido si se prueba correo | protegido y destinatarios sintéticos | vault production |
| SuperadminBootstrap:* | configuración/secretos locales explícitos | configuración/secretos de prueba explícitos | preservar y revisar configuración existente |

- Key Vault convierte `--` en `:`. Mantener aliases existentes `JwtKey`, `ClientId`,
  `ClientSecret`/`ClientSecretId` y `ConnectionStrings:DefaultConnection`/
  `ConnectionStrings:AZURE_SQL_CONNECTIONSTRING` como transición documentada.
- Resolver cada valor una vez; si aliases presentes difieren, fallar indicando
  nombres de claves, nunca valores. La precedencia de proveedores para una misma
  clave conserva el comportamiento ASP.NET Core.
- Infrastructure recibe la conexión normalizada; validarla con
  SqlConnectionStringBuilder, no búsqueda parcial de texto.
- Rechazar destino cruzado, servidor remoto local, base inesperada, JWT requerido
  ausente y OAuth parcial antes del bootstrap. No cambiar reglas de negocio OAuth.
- Los publicadores comparan expectativas de configuración con el inventario aprobado.
- Ninguna credencial production se copia a staging/development.
- Issuer/audience nuevos requieren transición explícita: comprobar clientes actuales
  y expiración de tokens previos antes de activar la validación estricta. No invalidar
  sesiones actuales de production durante preparación; permanecer bloqueado hasta
  aprobar y ensayar la transición de contexto por ambiente.

## 2. Salud HTTP adicional

`GET /health/live`, sin credenciales, sin Origin:

```json
{"status":"healthy","version":"<commit_sha>"}
```

200 solo si el proceso está operativo y la versión del paquete está identificada.
No consulta SQL. El commit no se obtiene de un valor modificable por ambiente.

`GET /health/ready`, sin credenciales, sin Origin:

```json
{"status":"healthy","version":"<commit_sha>"}
```

200 si configuración válida y conexión SQL del ambiente operativa mediante una
consulta no destructiva, sin tablas de negocio. Fallo/timeout:

```json
{"status":"unhealthy","version":"<commit_sha>"}
```

503, sin excepción, servidor, catálogo, identidad o secretos en la respuesta.
Registrar el fallo con identificador de correlación y mensaje sanitizado.
Sin cache; timeout finito. No aceptar 200 con payload inválido como validación.
Las pruebas de publicación verifican versión exacta y formato. Probes de producción
no habilitan bootstrap, envío de correo ni escrituras.

## 3. Smoke de negocio existente

- `POST /api/authentication/login`: request actual con `identifier`, `password`,
  `clientId`, cuenta sintética exclusiva de local/staging; esperar 200 y token válido.
- `GET /api/roles`: Bearer de esa cuenta; esperar 200 con colección y claim tenant
  del ambiente. Verificar aislamiento con fixtures separados, sin usuarios reales.
- No invocar signup, invitaciones o recuperación de contraseña durante smoke remoto.
- Usar HTTPS y no enviar Origin salvo la prueba CORS específica; preservar validación
  de tenant/origen existente. Nunca incluir password o token en logs/artefactos.

## 4. Manifiesto y workflow

Manifiesto JSON de release:

```json
{
  "commit_sha":"<commit_sha>",
  "run_id":"<run_id>",
  "api":{"artifact":"<name>","sha256":"<sha256>"},
  "database":{"artifact":"<actual_dacpac_name>","sha256":"<sha256>"}
}
```

Campos requeridos, un solo paquete API y DACPAC, hashes de 64 caracteres hexadecimales;
rechazar faltantes, duplicados o discrepancias. Los artefactos y el manifiesto deben
provenir de la misma ejecución de construcción autorizada, no de entradas libres.
Retener paquetes de versión estable y evidencia por el período de recuperación
acordado antes de habilitar publicación; si expiraron, bloquear promoción/recuperación.

- Disparadores remotos: push main y workflow_dispatch sobre main; PR solo valida.
- Permisos globales contents:read; id-token:write únicamente para jobs Azure autorizados.
- Serialización de publicación completa, sin cancelar trabajos que ya modifican bases.
- Staging puede usar subject main mientras no existan environments privados compatibles.
- Production permanece sin ruta publicadora efectiva hasta disponer de protección.
- Subject GitHub actual: `repo:leoperetti@12513421/authcraft-api@1404745493`.
  Con environments, agregar `:environment:staging` o `:environment:production`;
  audiencia `api://AzureADTokenExchange`, issuer `https://token.actions.githubusercontent.com`.
- No agregar confianza main de production como fallback del environment protegido.
- Identidades SQL por base y tokens Entra transitorios; permisos de evaluación
  separados de modificación. Azure Website Contributor no autoriza SQL.
- Reports/scripts del destino se revisan con acceso restringido y sin secretos;
  aprobación production identifica hash, destino y reporte. Reevaluación distinta
  exige nueva revisión y aprobación antes de Publish.

## 5. Esquema y fallos

MSBuild construye el `.sqlproj`; SqlPackage evalúa y publica el DACPAC.
DeployReport/Script y Publish usan mismas opciones:
`BlockOnPossibleDataLoss=True`, `DropObjectsNotInSource=False` y
`ScriptDatabaseOptions=False` para no alterar opciones de la base destino ni
requerir permisos `ALTER DATABASE` por diferencias ajenas al esquema del DACPAC.
Decision de diseno 2026-11-09: agregar explicitamente
`IncludeTransactionalScripts=True` en todas esas acciones y en el inicializador
local. Implementado y verificado localmente; el run `42692449745` no utiliza esta opcion.
La revision autenticada debe exigir ademas exactamente una linea
`include_transactional_scripts=true`; ausencia, duplicacion o valor distinto
bloquea. No aceptar revisiones anteriores como acreditacion de esta propiedad.
El registro autenticado de revisión debe incluir exactamente
`script_database_options=false` junto con las demás opciones y hashes; una opción
ausente o distinta invalida la aprobación.
Compatible significa que API anterior y candidata pueden operar con el esquema
resultante, incluidos postdeploy y cambios dependientes de datos.
Cambios destructivos/contractivos no pasan el flujo automático.

### Cobertura transaccional requerida

True significa transacciones donde DacFx las admite, no garantia global.
Antes de Publish, verificar en el SQL generado por la herramienta exacta los
limites BEGIN/COMMIT y el manejo de errores/XACT_ABORT, incluida la posicion del
postdeploy y sus validaciones. Fallo SQL 547 en WITH CHECK CHECK y THROW 51000 del
guard deben bloquear API/promocion y tener ensayos independientes de Publish en
LocalDB descartable con comparacion de esquema/datos desde otra conexion.

Decision revisada: se admiten fases separadas, no atomicidad global. El postdeploy
versionado debe tener transaccion propia con entrada @@TRANCOUNT=0, XACT_ABORT ON,
TRY/CATCH y rollback condicional a XACT_STATE(), relanzando el error original.
Debe incluir todas las semillas y validaciones; COMMIT solo despues del guard.
El COMMIT de esquema DacFx antes del postdeploy es admisible y debe documentarse.

Los ensayos 547/51000 deben demostrar rollback de semillas y cambios de trust
realizados por el postdeploy frente a su estado de entrada. Esquema confirmado
antes de esa fase puede permanecer; no exigir baseline global intacta ni
llamar al resultado unchanged por revertir solo postdeploy. El camino exitoso
debe conservar todas las constraints revisadas confiables.
Exigir ademas `postdeploy_transaction=true` en la revision autenticada, exactamente
una vez junto con `include_transactional_scripts=true`; revisiones anteriores
no acreditan el nuevo diseno. Un COMMIT prematuro dentro del postdeploy,
transaccion ajena activa o evidencia insuficiente bloquean.
El gate de SQL solo podra aceptar cierres transaccionales
identificados y probados, sin permitir SQL arbitrario despues de la validacion.
Cambiar opciones o limites invalida la revision previa y exige nuevos report/script,
hashes y revision autenticada. Ante cualquier fallo operativo seguir tratando el
estado como parcial/desconocido hasta inspeccion; no hay rollback SQL destructivo
automatico ni garantia sobre una transaccion que incluya tambien el despliegue API.

### Evidencia transaccional del intento Staging

Antes de OIDC, la ruta de reanudacion ejecuta en LocalDB la prueba exacta
`TransactionalPublishDocumentsSeparateSchemaAndPostdeploymentCoverage`.
No reconstruye ni reemplaza los paquetes candidatos: solo construye el harness
y sus DACPACs sinteticos descartables. Usa el SqlPackage que despues recibira el
publicador, descubierto en una instalacion Visual Studio con SSDT.

El test emite seis SQL originales y `transaction-verification.json` solo tras
comprobar todos los escenarios; `transaction-tests.trx` debe acreditar exactamente
una prueba ejecutada/aprobada y ninguna omitida/fallida. El publicador requiere
`TransactionVerificationPath`, verifica version actual y hash del ejecutable,
hashes de postdeploy/validaciones/fixture, seis casos unicos con sus vectores
esperados y limites de transaccion, y los hashes de sus SQL. Evidencia ausente,
omitida, alterada, de otra herramienta/fuente o insuficiente bloquea Publish.
No se acepta `schema_only` ni un parametro booleano como sustituto de cobertura.

`AttemptPath` conserva un JSON por intento (ruta nueva, sin sobrescribir evidencia
anterior); por defecto el publicador genera un nombre unico junto a su evidencia SQL.
Guarda una instantanea pending antes de Publish y la actualiza en finally:
blocked antes de escribir, failed ante fallo de publicacion, succeeded solo para
la fase SQL completada. Incluye tiempos UTC, release/destino, hashes del DACPAC y
plan, opciones, transaction_scope, postdeploy_result, failure_state_verification,
referencia de prueba, causa/accion sanitizadas y etapas posteriores not_executed.
No copia diagnosticos nativos, tokens ni cadenas de conexion.

La referencia vincula el hash del plan real y la linea inicial del postdeploy con
los hashes/TRX/herramienta/limites de los ensayos. Esta evidencia acredita cobertura
probada por fases, no el estado posterior de Azure. Tras intentar Publish,
postdeploy_result permanece unknown hasta inspeccion independiente, incluso si
SqlPackage devuelve cero; ante fallo, failure_state_verification es unknown,
nunca unchanged o rolled_back inferidos. Sin intento SQL, queda pending.

Solo los pasos de registro y upload usan always(); SQL, API y smoke conservan
su condicion de exito. El registro agregado distingue fallos/bloqueos y etapas
omitidas, preserva la evidencia SQL y no convierte un fallo SQL en rollback.
`staging-deployment-attempt` se guarda por run durante 30 dias con el acceso del
repositorio; una caida total del runner puede impedir captura/upload y obliga a
tratar la evidencia como no disponible. El mecanismo no activa el workflow,
no concede permisos ni sustituye revision autenticada/autorizacion remota.

### Evidencia de revisión de compatibilidad

- Designar en la guía de ambientes los responsables autorizados para revisar SQL.
- Para cualquier cambio de esquema o ejecución de postdeploy, un responsable
  revisa compatibilidad con ambas versiones, datos, opciones y recuperación.
- La acreditación es un registro de revisión autenticado en GitHub, con identificador
  consultable por API, autor permitido y resultado explícito de aprobación. Debe
  identificar run ID, commit, destino, hash DACPAC, opciones y hashes report/script.
  También debe declarar `staging_baseline_sha` como el SHA del último despliegue
  Staging verificado o exactamente `unknown`, y aceptar explícitamente
  `api_compatibility=accepted`, `data_impact=accepted` y `recovery_plan=accepted`.
  Cada campo debe aparecer una sola vez; una ausencia, duplicación o valor distinto
  invalida la revisión.
  El publicador consulta el registro original por API; no confía en la autoría
  declarada en un archivo, comentario copiado o parámetro booleano.
- Revalidar registro, permiso del autor y correspondencia de hashes antes de Publish.
  Registro ausente, rechazado, inaccesible o modificado sin correspondencia bloquea.
  Regenerar la evaluación con las mismas opciones; cambios relevantes de destino,
  reporte/script o release invalidan la revisión y requieren otra.
- Una evaluación sin cambios de esquema y sin postdeploy ejecutable puede continuar
  automáticamente; no asumir que comparación de esquema vacía elimina el postdeploy.
- El workflow staging puede quedar bloqueado por revisión y reanudarse con el mismo
  release verificado, sin reconstruirlo. La revisión SQL no es sustituto del
  environment protegido requerido para aprobar production.
- La primera publicación Staging puede usar `staging_baseline_sha=unknown` solo
  cuando Actions no registra ningún despliegue Staging satisfactorio verificable.
  El preflight debe comprobar esa ausencia y una opción explícita de bootstrap;
  la revisión autenticada debe reconocer el baseline desconocido. Esto no aprueba
  el plan ni autoriza Publish por sí solo.
- La recuperación debe estar documentada antes de habilitar la primera publicación.

### Informe descargable de intentos Production

- `docs/production-release-checklist.md` es la guía fija que el responsable consulta
  para revisar una publicación. Actions MUST NOT sobrescribirla ni crear commits por run.
- Cada intento Production MUST producir `production-deployment-report.md` y publicarlo
  en el artefacto estable `production-deployment-report` del mismo run, descargable y
  retenido 30 días; el resumen del run enlaza al artefacto. Aplica a resultados
  exitosos y fallidos. Si una verificación falla, el informe indica la etapa, causa
  sanitizada, siguiente paso recomendado y las etapas posteriores como no ejecutadas.
- El informe incluye run/commit/release, destino, resultado por etapa/gate, referencia
  de aprobación y disponibilidad/hash de report/script SQL. No incluye secretos,
  datos personales, cadenas de conexión ni salida nativa sin sanitizar.
- La generación del informe es evidencia, nunca aprobación ni autorización para
  desplegar. Un fallo global de runner/plataforma anterior al paso final puede impedir
  adjuntarlo; el historial del run queda como evidencia de interrupción no concluyente.

Preflight fallido: ninguna escritura. Esquema fallido: no publicar candidata.
Aplicación o salud fallida: bloquear promoción; no revertir base automáticamente.
Paquete anterior solo con compatibilidad comprobada; restauración de datos siempre
con revisión y autorización explícitas.
