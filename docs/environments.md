# Ambientes AuthCraft

## Estado de implementacion

Se preservan los cambios locales de invitaciones, signup, bootstrap y el proyecto de
pruebas. No pertenecen a esta feature y no se revierten. La solucion ya incluye
`authcraft-api.Tests`; se reutiliza su hosting de pruebas.

El workflow production remoto (ID `374894897`) esta `disabled_manually`; el de
staging (ID `407226559`) permanece `active`. El commit remoto main verificado es
`091fe2c9856fa8ba46387bc3e751fd21ca424662`. No se modifico el servicio production.
El workflow staging remoto sigue sin cumplir el nuevo recorrido de esquema/aprobacion.
No hacer push a main hasta reconciliar el workflow local/remoto y retirar publicadores
paralelos.

Revision de Actions 2026-11-06: el workflow staging remoto sigue activo y su ultima
ejecucion (`41882690684`) fallo en `Build with dotnet` con `MSB4278` al intentar
cargar el `.sqlproj` tradicional desde el build de la solucion; el job de despliegue
quedo omitido. El workflow staging local ahora es una propuesta de validacion de
paquetes: compila API, pruebas y DACPAC por separado y solo genera artefactos en un
`workflow_dispatch` de `main`. No tiene permisos OIDC ni pasos de publicacion; aun no
se subio ni reemplazo el workflow remoto, y esta propuesta no despliega a Azure.

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
No se autorizaron publicacion production, gastos de nuevos recursos, cambios de
visibilidad ni eliminacion de identidades cuyo consumidor no este comprobado.

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

## Gates pendientes

La configuracion local, el DACPAC y la salud Development fueron verificados sin Azure.
El smoke local de login/roles sinteticos tambien paso. El workflow consolidado,
publicador SQL/OIDC y controles Production siguen bloqueados; staging tiene evidencia
real de funcionamiento e identidad runtime aislada descrita abajo. No declarar la
feature completa ni modificar checks de calidad para simular implementacion.

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
