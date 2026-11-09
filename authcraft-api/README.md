# authcraft-api — Ambientes y secretos

## Desarrollo guiado por especificaciones

Este repositorio usa [GitHub Spec Kit](https://github.com/github/spec-kit) con
GitHub Copilot. Instala su CLI una vez con `uv tool install specify-cli` y
ejecuta las skills desde Copilot Chat, una por una:

1. `/speckit-constitution` — revisa `.specify/memory/constitution.md`.
2. `/speckit-specify` — define una funcionalidad acotada.
3. `/speckit-clarify` (opcional) y `/speckit-plan`.
4. `/speckit-tasks` y `/speckit-analyze` (opcional).
5. `/speckit-implement` y `/speckit-converge` hasta completar.

Las plantillas y skills están en `.specify/` y `.github/skills/`.

## Development local

Usa `.NET User Secrets` para claves locales. `DatabaseConnectionString` debe usar
SQL Server LocalDB o localhost con autenticación integrada; no apuntes Development
a Azure SQL ni cargues Key Vault. No imprimas ni comitees valores de secretos.
El bootstrap local está desactivado por defecto; habilítalo solo para una cuenta
sintética exclusiva de Development y vuelve a desactivarlo después.

Construye el API y el DACPAC por separado:

```powershell
dotnet build .\authcraft-api\authcraft-api.csproj --configuration Release
msbuild .\authcraft-db\authcraft-db.sqlproj /p:Configuration=Release
```

Sigue [`specs/001-environment-separation/quickstart.md`](../specs/001-environment-separation/quickstart.md)
para configurar User Secrets, revisar hashes de DeployReport/Script, publicar el
DACPAC local y comprobar `/health/live` y `/health/ready`.

## Staging y Production

Staging y Production usan sus recursos y Key Vault propios. No copies secretos
entre ambientes. Production permanece bloqueado hasta disponer de aprobaciones y
publicación protegida. Consulta [`docs/environments.md`](../docs/environments.md)
para el estado operativo y los límites actuales.

### Revisión de publicaciones Production

El proceso previsto separa la guía fija del informe de cada ejecución:

- [`docs/production-release-checklist.md`](../docs/production-release-checklist.md)
  será la guía para revisar una publicación; GitHub Actions no la sobrescribirá.
- Cada intento de Production generará `production-deployment-report.md`, disponible
  como artefacto `production-deployment-report` en el run de GitHub Actions durante
  30 días. El informe estará en español y resumirá los resultados; ante un fallo,
  indicará la etapa, una causa sanitizada, el siguiente paso y las etapas no ejecutadas.
- El proceso no hará commits automáticos ni habilitará Production. Si GitHub o el
  runner se interrumpe antes de generar el artefacto, el run será evidencia de una
  interrupción no concluyente.

La guía y el informe documentan y facilitan la revisión: no sustituyen las aprobaciones
ni los controles de publicación protegida.
