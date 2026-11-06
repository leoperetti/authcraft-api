# Research: Separación y verificación de ambientes

**Fecha**: 2026-11-05
**Alcance**: lecturas locales, ARM y metadatos GitHub; sin valores de secretos,
conexiones SQL ni cambios de recursos. Consulta de documentación oficial separada
para aprobación, OIDC y SqlPackage.

## 1. Recursos y dependencias

**Decision**: Reutilizar las aplicaciones, bases y vaults confirmados por el usuario.
Ambas Web Apps están Running y comparten `ASP-AuthCraft-bc2b`, F1, Windows v10.0.
Las bases están Paused, SKU GP_S_Gen5; no conectarlas durante planificación.

**Rationale**: Aplicaciones independientes no significan capacidad independiente.
Compartir servidor SQL y plan implica impacto operativo común; bases distintas
requieren también permisos distintos. F1 no garantiza costo total cero: SQL y
vaults tienen consumo separado.

**Alternatives considered**: Nuevos planes, slots, bases o recursos: descartados
por alcance y necesidad de autorización. Trasladar Blob/migraciones de CEKIDE:
no corresponde al proyecto.

**Evidence**: `az webapp list`, `az appservice plan show`, `az sql db list`,
`az keyvault show`. Los dos vaults usan RBAC y acceso de red público habilitado.
No se validaron contenidos de cadenas de conexión ni usuarios SQL.

## 2. Las tres identidades administradas de AuthCraft

| Identidad | Asignación observada | Federación | Interpretación y decisión |
|-----------|----------------------|------------|--------------------------|
| oidc-msi-a9fa | Adjunta a API production; Website Contributor solo sobre esa Web App | ninguna registrada | Puede ser configuración anterior/incompleta; propósito histórico no comprobado. Conservar hasta auditar consumidores. |
| oidc-msi-aede | Adjunta a API production; Website Contributor solo sobre esa Web App | issuer GitHub, audiencia AzureADTokenExchange, subject main | Publicador production candidato. Su configuración permite publicar; no prueba que el secreto del workflow apunte a ella. |
| authcraft-api-st-id-97cc | No adjunta al runtime staging; Website Contributor solo sobre staging | issuer GitHub, audiencia AzureADTokenExchange, subject main | Publicador staging candidato; no es actualmente identidad runtime de staging. |

**Decision**: Separar publicadores de identidades runtime. Production tiene además
una identidad por sistema con `Key Vault Reader` y `Key Vault Secrets User` solo
sobre `authcraft-vault`; staging no tiene identidad asignada por ARM.
Usar identidades por sistema para los vaults, cada una en su ambiente.

**Rationale**: Website Contributor autoriza administración/publicación de la Web App,
no lectura de secretos ni permisos SQL. Una identidad publicadora adjunta al runtime
mezcla capacidades y debe revisarse antes de desacoplarla.

**Alternatives considered**: Reutilizar publicadores para leer vault o SQL de runtime:
rechazado. Eliminar `oidc-msi-a9fa` automáticamente: rechazado por falta de evidencia
de consumidores. Modificar identidades CEKIDE: fuera de alcance.

**Evidence**: `az identity list --resource-group AuthCraft`,
`az identity federated-credential list`, `az role assignment list`,
`az webapp identity show`. No se consultaron valores de secretos GitHub.
La finalidad original de creación no puede inferirse solo del nombre.

## 3. Aprobación protegida en repositorio privado

**Decision**: Mantener privado y bloquear production hasta tener protección
compatible, confirmado por el usuario. No elegir ni contratar otra modalidad.

**Rationale**: La documentación limita required reviewers para repositorios privados
a modalidades Enterprise compatibles. El repositorio es privado personal; la API
de protección de main respondió 403 solicitando Pro o visibilidad pública. No hay
GitHub Environments registrados. Pro habilita algunos controles privados, pero
no garantiza required reviewers privados. Un disparador manual o aprobación de PR
anterior a staging no satisface FR-012.

**Alternatives considered**: Hacer público, migrar/contratar Enterprise o integrar
un aprobador externo: no autorizados. `workflow_dispatch` como aprobación: rechazado.
Deshabilitar solo un job y dejar confianza Azure disponible: insuficiente.

**Sources**:
- https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments
- https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments
- https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/control-deployments

## 4. OIDC con subject inmutable

**Decision**: Conservar el formato inmutable confirmado por la API GitHub.
El prefijo efectivo es `repo:leoperetti@12513421/authcraft-api@1404745493`.
Actualmente las federaciones usan `:ref:refs/heads/main`. Cuando un job declare
environment, debe usar `:environment:staging` o `:environment:production`.
Validar coincidencia exacta, issuer y audiencia, sin registrar el JWT completo.

**Rationale**: El job con environment cambia el contexto del subject; copiar el
formato antiguo sin IDs o mantener la referencia main causaría fallos de intercambio.
La confianza production desde main debe retirarse cuando se use la protegida.

**Alternatives considered**: Subjects basados solo en nombres, comodines o conservar
main como alternativa de production: rechazados. Staging puede mantener main mientras
environments privados no estén disponibles, con identidad limitada a staging.

**Sources**:
- https://docs.github.com/en/actions/reference/security/oidc
- https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-azure
- https://learn.microsoft.com/en-us/entra/workload-id/workload-identity-federation-create-trust

## 5. Configuración efectiva y secretos

**Decision**: Validar y normalizar configuración antes de conectar y del bootstrap.
Mantener aliases actuales de conexión, JWT y Google solo como transición compatible;
si dos fuentes nominales distintas proporcionan valores incompatibles, fallar.
Cada entorno requiere su destino y credenciales, sin fallback cruzado.

**Rationale**: `Program.cs` consulta vault solo para Staging/Production, lee
`DatabaseConnectionString` y admite aliases JWT/Google; Infrastructure exige el
override y no usa los nombres de conexión indicados en su error. Development y
configuración común contienen valores de conexión y nombre del vault production.
Issuer/audience están vacíos y bootstrap habilitado en los cuatro archivos.
Las Web Apps no tienen ajustes explícitos ASPNETCORE_ENVIRONMENT/KeyVault__Name.
No se interpreta ninguna de estas observaciones como prueba de salud.

Production tiene nombres de secretos para conexión, JWT, Google, Brevo y bootstrap.
Staging solo presenta `DatabaseConnectionString` habilitado. Se listaron nombres y
estado, nunca valores. Completar staging sin copiar secretos production y decidir
integraciones opcionales por capacidades documentadas; salud no depende de correo.

**Alternatives considered**: Fallback de staging a production, configuración común
con destinos sensibles o múltiples resoluciones inconsistentes: rechazados.
Rotación automática de todos los secretos y reescritura de negocio: fuera de alcance.

**Evidence**: [Program.cs](../../authcraft-api/Program.cs),
[DependencyInjection.cs](../../Infrastructure/DependencyInjection.cs),
[configuraciones](../../authcraft-api/), `az webapp config appsettings list` con
selección de nombres/ajustes públicos, `az keyvault secret list` sin valores.

## 6. DACPAC, autenticación y recuperación

**Decision**: Mantener SQL project tradicional y MSBuild/SSDT. Publicar DACPAC
compatible antes de la API porque el bootstrap corre al iniciar y accede a tablas.
Construir una vez, comprobar nombre real de salida y hash; evaluar incrementalmente
cada base. Obtener token SQL por Entra/OIDC para el publicador y verificar usuario
contenido y permisos mínimos antes de habilitar publicación.

**Rationale**: El workflow local publica aplicación antes de SQL; el remoto staging
construye la solución indiscriminadamente y no publica base. El proyecto tiene
AssemblyName `authcraft_db`; no se confirmó el archivo supuesto por el workflow.
Postdeploy inserta proveedores de forma idempotente. Hay administrador Entra
configurado, pero no se verificaron grants SQL ni capacidad DacFx del publicador.
La regla de red que permite recursos Azure no demuestra acceso limitado por identidad.

**Safety**: DeployReport y Script previos, opciones idénticas a Publish,
BlockOnPossibleDataLoss=True y DropObjectsNotInSource=False. Revisar renombres,
tipos, nulabilidad, postdeploy y compatibilidad con ambas versiones. Un reporte
estático no prueba datos reales ni atomicidad global; permisos y schema drift se
verifican de nuevo. No usar BlockWhenDriftDetected como única protección: depende
del registro previo de la base como aplicación de datos.

**Alternatives considered**: Migraciones EF, `EnsureCreated` remoto, publicación de
SQL después de una API incompatible o reversiones de esquema automáticas: rechazados.
SqlPackage con SQL password compartida y permisos globales: evitar; conservar
credenciales runtime existentes únicamente después de comprobar alcance mínimo.

**Sources**:
- https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-publish
- https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-script
- https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-deploy-report
- https://learn.microsoft.com/en-us/azure/azure-sql/database/authentication-aad-configure

## 7. Estado de la investigación

Decisiones de diseño resueltas. Pendientes de implementación/verificación:
protección production, consumers de identidades, grants SQL, destino efectivo de
secretos, configuración staging, salida real MSBuild y pruebas de funcionamiento.
Estos puntos tienen acciones y condiciones de bloqueo; no se presume su éxito.
Las conexiones a bases pausadas y cualquier cambio remoto requieren autorización.
