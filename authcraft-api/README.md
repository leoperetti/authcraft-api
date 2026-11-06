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
