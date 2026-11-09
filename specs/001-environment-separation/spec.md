# Feature Specification: Separación y verificación de ambientes

**Feature Branch**: `main` (rama actual; no se creó una rama para esta especificación)

**Created**: 2026-11-05

**Status**: Draft

**Input**: User description: "Necesito que hagamos un spec al igual que con cekide-api, 001-environment-separation, crear y verificar los ambientes existentes, necesito development para mi localhost, staging y production. Los recursos de Azure ya están creados. Pero necesitamos revisar las instrucciones para Github Actions."

## Clarifications

### Session 2026-11-07

- Q: ¿Qué esperás que ocurra con `docs/production-release-checklist.md` en cada despliegue de Production: que sea una guía fija para completar, que Actions genere un informe descargable por ejecución, o que Actions actualice y suba ese mismo archivo al repositorio? → A: Mantener una checklist fija en `docs/` y generar por cada ejecución un informe descargable como artefacto de GitHub Actions; no sobrescribir ni hacer commits automáticos del archivo.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Identificar y verificar los tres ambientes (Priority: P1)

Como responsable de AuthCraft, necesito conocer el propósito, los recursos y el estado de development, staging y production para trabajar sin confundir los destinos ni afectar datos reales.

**Why this priority**: Los recursos existentes no garantizan que los ambientes estén correctamente configurados o aislados.

**Independent Test**: Revisar el inventario y ejecutar una comprobación no destructiva por ambiente, identificando el destino real de la aplicación y de sus datos.

**Acceptance Scenarios**:

1. **Given** los recursos existentes, **When** se revisa el inventario, **Then** aparecen exactamente development local, staging de validación y production de uso real, con sus recursos, dependencias compartidas y estado de verificación.
2. **Given** un recurso registrado, **When** se compara el inventario con su configuración efectiva, **Then** se confirma su asignación o se registra una discrepancia con evidencia y acción pendiente.
3. **Given** las aplicaciones y bases de los tres ambientes, **When** se verifican sus destinos y permisos, **Then** cada aplicación opera sobre su propia base y las identidades de development y staging no pueden modificar datos de production.
4. **Given** un recurso faltante o inaccesible, **When** se intenta verificarlo, **Then** se informa el bloqueo y no se lo presenta como verificado ni se crea un sustituto automáticamente.

---

### User Story 2 - Desarrollar en localhost con configuración aislada (Priority: P1)

Como desarrollador, necesito ejecutar AuthCraft en development desde localhost con configuración y datos locales, sin depender de credenciales de los ambientes remotos.

**Why this priority**: El desarrollo y las pruebas deben poder realizarse sin acceso accidental a datos o servicios de producción.

**Independent Test**: Seguir las instrucciones de desarrollo en una instalación con los prerrequisitos disponibles, iniciar la aplicación y completar una operación de autenticación contra datos locales de prueba.

**Acceptance Scenarios**:

1. **Given** los prerrequisitos y secretos locales configurados, **When** se inicia la aplicación siguiendo la guía, **Then** funciona como development en localhost y utiliza exclusivamente su base local.
2. **Given** que existe una actualización pendiente del esquema local, **When** se prepara development, **Then** se aplica mediante el mecanismo de esquema del proyecto y se valida contra la base local.
3. **Given** una configuración requerida ausente o un destino de production configurado por error, **When** se inicia development, **Then** se informa un error accionable sin revelar secretos y no se realizan operaciones contra production.

---

### User Story 3 - Validar una versión en staging (Priority: P1)

Como responsable de publicaciones, necesito que cada cambio integrado en main llegue primero a staging y demuestre que la aplicación y su base funcionan antes de habilitar production.

**Why this priority**: La validación previa evita que una versión defectuosa se publique directamente para usuarios reales.

**Independent Test**: Publicar una versión candidata en staging, verificar su versión y sus operaciones principales y simular un fallo para comprobar que production no cambia.

**Acceptance Scenarios**:

1. **Given** un cambio integrado en main, **When** se ejecuta GitHub Actions, **Then** se verifica y prepara una versión que incluye la aplicación y el cambio de esquema correspondiente, y se publica primero en staging.
2. **Given** una publicación en staging, **When** finaliza el proceso, **Then** se comprueban disponibilidad, autenticación y acceso a datos de prueba, y se registra la versión evaluada.
3. **Given** un fallo de construcción, pruebas, actualización de esquema o comprobación posterior, **When** se evalúa la publicación, **Then** se informa la etapa fallida, se bloquea la promoción y production conserva su versión.
4. **Given** workflows locales y remotos con reglas distintas o publicaciones duplicadas, **When** se revisa la automatización, **Then** queda un flujo vigente coherente con las instrucciones y sin rutas paralelas que publiquen directamente a production.

---

### User Story 4 - Promover y recuperar production de forma controlada (Priority: P2)

Como responsable de production, necesito aprobar una versión validada en staging, conocer lo que se publica y disponer de instrucciones de recuperación que protejan los datos reales.

**Why this priority**: La aprobación y la trazabilidad reducen errores operativos y permiten responder ante una publicación fallida.

**Independent Test**: Recorrer una promoción autorizada y ensayar los bloqueos y el procedimiento de recuperación sin modificar destructivamente datos reales.

**Acceptance Scenarios**:

1. **Given** una versión validada en staging, **When** se solicita publicar en production, **Then** el proceso espera una aprobación manual de una persona autorizada y muestra la versión y el destino.
2. **Given** una aprobación válida, **When** se publica en production, **Then** se utilizan los mismos paquetes de aplicación y esquema evaluados en staging, sin reconstruirlos, con la configuración propia de production.
3. **Given** una aprobación rechazada o ausente, **When** se consulta production, **Then** continúa disponible la versión previa y no se modifica su esquema.
4. **Given** una publicación o actualización de datos fallida, **When** se aplica la guía de recuperación, **Then** se detienen las etapas restantes y se restablece una versión compatible sin restauraciones ni reversiones destructivas automáticas.
5. **Given** dos solicitudes de publicación simultáneas o una versión candidata superada, **When** se intenta promoverlas, **Then** no se intercalan cambios sobre el mismo destino y se impide reemplazar involuntariamente una versión más reciente.
6. **Given** cualquier intento de publicación de Production, **When** una validación falla o el proceso termina, **Then** el run ofrece un informe en español descargable con la etapa, causa sanitizada y siguiente paso; las etapas posteriores se identifican como no ejecutadas.

---

### Edge Cases

- Un ambiente declara un nombre correcto, pero su configuración efectiva apunta a la base o al almacén de secretos de otro ambiente: bloquear operaciones y registrar la discrepancia.
- Faltan permisos para consultar recursos o configurar la aprobación de production: informar el bloqueo; no sustituir la aprobación por una publicación sin protección.
- Falta un secreto requerido o su almacén está inaccesible: informar el origen y la clave afectados sin mostrar valores; no usar credenciales de otro ambiente como alternativa.
- La plataforma disponible no admite la protección manual requerida: no habilitar la publicación de production hasta disponer de un control equivalente verificable.
- Staging y production comparten infraestructura subyacente: documentar el posible impacto compartido y verificar que no compartan datos ni permisos de escritura.
- Una versión de la aplicación y un cambio de esquema no son compatibles durante una publicación parcial: detener el flujo y seguir la recuperación documentada.
- Las pruebas de staging o development podrían enviar notificaciones reales o crear cuentas privilegiadas: usar destinatarios y cuentas de prueba limitados al ambiente.
- Una ejecución manual intenta saltar staging o publicar desde una referencia no autorizada: aplicar las mismas validaciones y aprobación que al flujo normal.
- Verificar o corregir un recurso requiere recrearlo, eliminarlo o modificar capacidad con posible costo: solicitar autorización antes de la operación.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: La solución MUST definir exactamente tres ambientes denominados development, staging y production; development se ejecuta en localhost y los otros dos utilizan los recursos Azure existentes.
- **FR-002**: El equipo MUST disponer de un inventario verificable por ambiente que identifique aplicación, base de datos, almacén de secretos, identidad de ejecución, identidad de publicación, dirección de acceso y dependencias compartidas; cada elemento debe indicar si está confirmado, pendiente o bloqueado.
- **FR-003**: La verificación MUST comparar el inventario con la configuración efectiva y los permisos, registrar evidencia sin secretos y distinguir existencia de recursos, configuración correcta y funcionamiento comprobado.
- **FR-004**: Los tres ambientes MUST utilizar bases de datos distintas. Development y staging MUST usar datos de prueba, y sus identidades MUST carecer de permisos de modificación sobre los datos de production.
- **FR-005**: Cada ambiente MUST tener configuración, credenciales y contexto de autenticación propios. Los secretos MUST mantenerse fuera del repositorio, los paquetes publicados y las evidencias; development debe usar almacenamiento local protegido y los ambientes remotos sus almacenes protegidos correspondientes.
- **FR-006**: La aplicación y el proceso de publicación MUST informar y bloquear configuración requerida ausente o asignaciones cruzadas de destinos antes de efectuar operaciones contra el destino incorrecto, sin recurrir a valores de otro ambiente.
- **FR-007**: La guía de development MUST permitir preparar la base local, aplicar cambios pendientes de esquema y comprobar inicio, autenticación y acceso a datos locales sin credenciales de staging ni production.
- **FR-008**: GitHub Actions MUST gestionar la publicación remota. Todo cambio integrado en main MUST pasar primero por staging; las ejecuciones manuales MUST conservar los mismos controles y no permitir saltar esa validación.
- **FR-009**: Antes de publicar, el proceso MUST verificar la construcción de la aplicación y del proyecto de base de datos y ejecutar las pruebas aplicables. Los cambios de esquema MUST conservar el mecanismo del proyecto de base de datos existente.
- **FR-010**: Cada publicación MUST mantener compatibles la aplicación y el esquema durante todas sus etapas, identificar el destino de datos antes de modificarlo y bloquear etapas posteriores ante un fallo. La secuencia y la recuperación deben quedar documentadas antes de habilitar la publicación.
- **FR-011**: Staging MUST comprobar disponibilidad, autenticación y acceso a datos de prueba tras la publicación; cualquier fallo MUST impedir promover esa versión.
- **FR-012**: Production MUST requerir aprobación manual de una persona autorizada después de validar staging y antes de modificar la aplicación o su base. El control MUST estar protegido y asociado explícitamente al ambiente de destino.
- **FR-013**: Production MUST recibir exactamente los mismos paquetes de aplicación y esquema validados en staging, sin reconstrucción, conservando exclusivamente su configuración y secretos propios.
- **FR-014**: Cada intento de publicación MUST registrar versión, destino, resultado de verificaciones, aprobación cuando corresponda y resultado final. Las publicaciones sobre un mismo destino MUST evitar ejecución simultánea y promoción accidental de versiones superadas.
- **FR-015**: El equipo MUST disponer de instrucciones para fallos parciales y recuperación del servicio y sus datos. No se permiten restauraciones ni reversiones destructivas automáticas de production.
- **FR-016**: La revisión MUST reconciliar los workflows existentes localmente y en GitHub, sus disparadores, destinos, permisos, configuración protegida y despliegue de base de datos. Las instrucciones MUST reflejar el flujo vigente y eliminar o deshabilitar rutas alternativas que eviten sus controles.
- **FR-017**: El trabajo MUST reutilizar los recursos Azure existentes, preservar los contratos y comportamiento funcional de AuthCraft y no crear, eliminar, recrear ni cambiar capacidad de recursos sin autorización explícita. Las comprobaciones MUST ser no destructivas y no enviar notificaciones a usuarios reales.
- **FR-018**: Cada intento de publicación de Production MUST generar un informe descargable vinculado a su ejecución de GitHub Actions, tanto si termina correctamente como si falla. En lenguaje claro en español, el informe MUST resumir etapa, resultado, versión, destino, comprobaciones, aprobación y evidencia de revisión del esquema; ante fallos MUST indicar la etapa, una causa sanitizada y el siguiente paso recomendado. MUST indicar qué artefactos de reporte/script están disponibles, excluir secretos y datos personales, conservarse durante 30 días y no sobrescribir ni generar commits automáticos en `docs/production-release-checklist.md`. Ese archivo MUST ser una guía fija de revisión, no el informe de una ejecución.

### Key Entities *(include if feature involves data)*

- **Ambiente**: Contexto development, staging o production con propósito, ubicación, configuración y límites de acceso propios.
- **Asignación de recurso**: Relación entre un ambiente y sus recursos existentes, incluyendo dependencias compartidas, permisos, evidencia y estado de verificación.
- **Versión candidata**: Cambio identificable con paquetes de aplicación y esquema que se evalúa en staging y puede promoverse sin reconstrucción.
- **Registro de publicación**: Evidencia por ejecución descargable desde GitHub Actions del destino, versión, validaciones, aprobación, resultado y acciones de recuperación; no se versiona automáticamente en el repositorio.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Los tres ambientes tienen el 100% de sus asignaciones inventariadas y verificadas, sin discrepancias críticas abiertas de destino, secretos o permisos.
- **SC-002**: Un desarrollador con los prerrequisitos disponibles puede iniciar development y completar una autenticación y una operación local de datos siguiendo únicamente la guía, en un máximo de 30 minutos.
- **SC-003**: El 100% de las versiones publicadas en production cuenta con validación previa satisfactoria en staging, aprobación registrada y coincidencia verificable de los paquetes publicados.
- **SC-004**: En al menos cinco pruebas de bloqueo —fallo de validación, aprobación ausente, destino cruzado, secreto requerido ausente y referencia no autorizada— se impide la promoción y no se alteran aplicación ni datos de production.
- **SC-005**: Un responsable distinto del autor puede identificar la versión activa, consultar sus evidencias y completar un ensayo no destructivo del procedimiento de recuperación en un máximo de 30 minutos siguiendo la documentación.
- **SC-006**: Durante la verificación y las pruebas de aislamiento se producen cero modificaciones cruzadas de datos, cero secretos expuestos y cero notificaciones a usuarios reales.
- **SC-007**: El 100% de los intentos de publicación Production, incluidos los fallidos, produce un informe en español descargable asociado al run y retenido por 30 días; los fallos identifican etapa y siguiente paso sin exponer secretos ni datos personales, y no generan cambios automáticos en el repositorio.

## Assumptions

- Esta fase produce solamente la especificación y su revisión de calidad; la configuración de ambientes y workflows se realizará en fases posteriores.
- El usuario confirmó el flujo de referencia de cekide-api: main → staging → validación → aprobación manual → production, promoviendo la misma versión sin reconstruir.
- Durante la planificación, el usuario confirmó mantener privado el repositorio y bloquear la publicación de production hasta disponer de una aprobación protegida compatible. Esto no elimina FR-012 ni habilita una publicación manual sin protección; development y staging pueden prepararse mientras se resuelve ese prerrequisito.
- No se trasladan decisiones ajenas a esta necesidad desde cekide-api, como almacenamiento de archivos compartido, provisión de recursos o un mecanismo distinto de cambios de esquema.
- El inventario Azure de solo lectura del 2026-11-05 confirmó en el grupo AuthCraft las aplicaciones `authcraft-api-staging` y `authcraft-api`, las bases `authcraft-db-staging` y `authcraft-db` del servidor `authcraftdbbrasil`, y los almacenes `authcraft-vault-staging` y `authcraft-vault`. Su correspondencia staging/production se debe verificar contra la configuración efectiva; existencia no significa funcionamiento comprobado.
- La consulta de GitHub del 2026-11-05 mostró dos workflows activos, para staging y production, disparados desde main, y ningún GitHub Environment registrado. El checkout local contiene solamente el workflow de production; la diferencia debe reconciliarse antes de modificar la automatización.
- El repositorio ya contiene configuraciones Development, Staging y Production y perfiles de ejecución local. Se revisará su coherencia sin asumir que los archivos prueban el aislamiento efectivo.
- La elección de comandos, controles de aprobación compatibles con el repositorio, secuencia de actualización del esquema y verificaciones operativas corresponde al plan, respetando los requisitos y la constitución vigentes.
- Se requiere acceso autorizado para verificar Azure y GitHub y configurar los controles de publicación. Si ese acceso o una protección necesaria no están disponibles, se registrará un bloqueo sin debilitar los requisitos.
- No se cambia la lógica de negocio, las rutas públicas ni el modelo de datos salvo lo estrictamente necesario para configurar y verificar los ambientes. No se ejecutan publicaciones ni cambios de datos reales durante la elaboración de este spec.
