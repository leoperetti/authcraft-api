# Checklist de revisión de publicaciones Production

## Alcance y reglas

Esta guía fija los controles que la persona revisora debe completar para una
publicación de Production. No es el informe de una ejecución ni una autorización
de despliegue. GitHub Actions no debe sobrescribirla ni crear commits para
actualizarla. La validación y publicación de Staging tienen su procedimiento
separado en la documentación de ambientes.

Production permanece bloqueado mientras no exista una aprobación protegida y
se verifiquen todos los controles de identidad, procedencia del release,
revisión de base de datos y recuperación. Un run exitoso, una revisión del SQL,
una ejecución manual o completar esta guía no habilita ni aprueba una publicación.

## 1. Identificar el intento y el destino

- [ ] Abrir el run de GitHub Actions correspondiente y confirmar el repositorio,
  número de ejecución, commit y release que se pretende publicar.
- [ ] Confirmar que el destino es Production y que las verificaciones requeridas
  de Staging corresponden al mismo commit y a los mismos paquetes inmutables.
- [ ] Descargar el artefacto `production-deployment-report` del run, cuando esté
  disponible, y confirmar que su ejecución, commit, release y destino coinciden.
- [ ] Revisar el resultado de cada etapa, la aprobación y la disponibilidad de
  los artefactos SQL. Una etapa `no ejecutado` no equivale a una validación exitosa.
- [ ] Si el artefacto no está disponible, determinar si el run terminó antes de
  poder generarlo. Tratarlo como evidencia no concluyente; no inferir éxito ni
  continuar la publicación por otra ruta.

## 2. Revisar DeployReport y SQL generado

- [ ] Descargar el DeployReport y el script SQL generados para este mismo release
  y destino, si están disponibles.
- [ ] Comparar los hashes con los registrados en el informe y confirmar que los
  archivos no cambiaron desde su generación o revisión.
- [ ] Revisar las operaciones en contexto, no solo buscar `DROP`: considerar
  cambios de tablas/columnas, nulabilidad, restricciones, índices, datos,
  bloqueos, recreación de objetos, `WITH NOCHECK`, operaciones de Query Store y
  acciones que puedan afectar consultas o disponibilidad.
- [ ] Identificar cada operación que pueda eliminar, invalidar, volver inaccesible
  o transformar datos. Confirmar qué datos afecta, su volumen esperado y si la
  operación puede bloquear o extender el tiempo de despliegue.
- [ ] Revisar el post-deploy completo, incluidas inserciones, actualizaciones,
  borrados y condiciones de ejecución. No asumir que un reporte de esquema sin
  diferencias elimina los efectos del post-deploy.
- [ ] Detener la revisión si falta el SQL, el hash no coincide, hay operaciones
  inesperadas o el impacto no puede explicarse con evidencia suficiente.

## 3. Confirmar compatibilidad de aplicación y datos

- [ ] Verificar que la aplicación candidata funciona con el esquema actual y que
  la aplicación anterior sigue siendo compatible con el esquema resultante.
- [ ] Revisar cambios contractivos, conversiones, valores por defecto y
  restricciones contra los datos existentes; exigir una transición compatible
  cuando aplicación y esquema se desplieguen en etapas distintas.
- [ ] Confirmar que el paquete de API y el DACPAC son los mismos artefactos
  validados en Staging; no aceptar reconstrucciones ni hashes distintos.
- [ ] Confirmar que las pruebas posteriores son no destructivas y no crean
  usuarios, envían correos ni alteran datos reales como prueba.

## 4. Evaluar impacto operativo

- [ ] Estimar bloqueos, reinicios, indisponibilidad y efecto en consultas y
  procesos concurrentes.
- [ ] Confirmar que el destino y su configuración efectiva son Production, que
  los secretos no aparecen en logs/artefactos y que el publicador tiene solo los
  permisos necesarios para la operación aprobada.
- [ ] Revisar fallos previos, deriva del destino y cualquier release posterior
  que haga obsoleta la evidencia revisada.
- [ ] Detenerse ante errores no explicados, permisos excesivos, destino ambiguo,
  evidencia vencida o una diferencia entre lo revisado y lo que se ejecutará.

## 5. Confirmar aprobación y recuperación antes de autorizar

- [ ] Confirmar una aprobación protegida de una persona autorizada, asociada al
  commit/release, destino y hashes exactos revisados. Una aprobación de otro
  release o una aprobación implícita no es válida.
- [ ] Confirmar quién detendrá el proceso y cómo se verificará el resultado por
  etapa. Ante un fallo, las etapas siguientes deben permanecer detenidas.
- [ ] Revisar el procedimiento de [recuperación](./deployment-recovery.md):
  identificar la versión de API anterior compatible, preservar escrituras
  recientes y escalar cualquier recuperación de datos para revisión y autorización
  explícitas. No asumir rollback SQL ni restauración automática.
- [ ] Confirmar que el informe del run y los artefactos fuente se conservarán con
  acceso adecuado y que otra persona puede localizar la evidencia.

## Señales de parada

No autorizar ni continuar si falta la protección de Production; el informe está
ausente o no corresponde al release; los hashes no coinciden; una etapa necesaria
no se ejecutó o falló; el SQL/post-deploy tiene efectos no explicados; la
compatibilidad o recuperación no está demostrada; o el destino, la aprobación o
los permisos no son verificables. Registrar la causa y solicitar una revisión
nueva después de resolverla; no cambiar el archivo del informe ni crear una ruta
manual alternativa.
