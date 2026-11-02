# authcraft-api — Gestión de secretos

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

Breve guía para ver y gestionar los secretos de desarrollo (user-secrets) y recomendaciones para producción.

Dónde están los secretos (user-secrets)
- En Windows: %APPDATA%\Microsoft\UserSecrets\<UserSecretsId>\secrets.json
- En macOS/Linux: ~/.microsoft/usersecrets/<UserSecretsId>/secrets.json

Cómo ver el UserSecretsId del proyecto
- Abre el fichero `authcraft-api/authcraft-api.csproj` y busca la propiedad `<UserSecretsId>`.
- O ejecuta:
  dotnet user-secrets list --project "authcraft-api\\authcraft-api.csproj"

Comandos útiles
- Listar todos los secretos:
  dotnet user-secrets list --project "authcraft-api\\authcraft-api.csproj"
- Obtener un secreto concreto:
  dotnet user-secrets get "Authentication:Google:ClientId" --project "authcraft-api\\authcraft-api.csproj"
- Añadir o cambiar un secreto:
  dotnet user-secrets set "Authentication:Google:ClientId" "<tu-client-id>" --project "authcraft-api\\authcraft-api.csproj"
- Eliminar un secreto:
  dotnet user-secrets remove "Authentication:Google:ClientSecret" --project "authcraft-api\\authcraft-api.csproj"

Notas de seguridad
- No comites secretos al repositorio.
- User-secrets es para desarrollo local. En producción usa variables de entorno o el almacén de secretos de tu proveedor (Azure Key Vault, AWS Secrets Manager, etc.).

Ejemplos para producción
- Windows PowerShell (persistente):
  setx Authentication__Google__ClientId "<tu-client-id>"
  setx Authentication__Google__ClientSecret "<tu-client-secret>"
  setx Jwt__Key "<tu-jwt-key>"
- Linux (bash):
  export Authentication__Google__ClientId="<tu-client-id>"
  export Authentication__Google__ClientSecret="<tu-client-secret>"
  export Jwt__Key="<tu-jwt-key>"
- Docker (run):
  docker run -e Authentication__Google__ClientId="..." -e Jwt__Key="..." ...

Comprobación rápida en la app
- En tiempo de ejecución puedes leer con IConfiguration:
  var clientId = Configuration["Authentication:Google:ClientId"];

Si quieres, puedo añadir un script PowerShell al repo con los comandos dotnet user-secrets para tu proyecto.
