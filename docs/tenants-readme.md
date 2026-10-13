# Manejo de tenants

## Resumen
El módulo de tenants centraliza la gestión de clientes lógicos dentro de la aplicación. Actualmente existe una capa de servicio con operaciones CRUD básicas, persistencia con Entity Framework Core y generación de API keys por tenant.

## Estado actual
### Implementado
- Servicio `ITenantService` / `TenantService`
- Persistencia sobre `AppDbContext`
- Alta de tenants
- Consulta individual por `id`
- Listado de tenants
- Actualización parcial
- Cambio de estado
- Baja de tenants
- Generación de API key inicial por tenant
- Normalización de `allowed_origins`
- Serialización de `settings` como JSON

### Pendiente o parcial
- Rotación y revocación de API keys
- Auditoría específica para operaciones de tenants
- Tests automáticos del flujo de tenants
- Paginación y filtros en el listado
- Reglas de validación de negocio más explícitas antes de persistir

## Componentes involucrados
### Entidad principal
`Domain.Entities.Tenant`
- `Id`
- `ClientId`
- `Slug`
- `Name`
- `Status`
- `Settings`
- `AllowedOrigins`
- `TenantApiKeys`

### API keys del tenant
`Domain.Entities.TenantApiKey`
- `Id`
- `TenantId`
- `KeyHash`
- `Scopes`

## Contratos actuales
### CreateTenantRequest
```json
{
  "clientId": "acme-web",
  "slug": "acme",
  "name": "Acme Inc",
  "status": "active",
  "settings": {
	"theme": "dark",
	"region": "ar"
  },
  "allowedOrigins": [
	"https://app.acme.com",
	"https://admin.acme.com/"
  ]
}
```

Reglas actuales:
- `clientId`: requerido, máximo 200 caracteres
- `slug`: requerido, máximo 100 caracteres, formato `^[a-z0-9-]+$`
- `name`: requerido, máximo 200 caracteres
- `status`: permitido `active`, `suspended`, `archived`
- `settings`: objeto JSON libre
- `allowedOrigins`: lista de orígenes permitidos

### UpdateTenantRequest
Permite actualización de:
- `name`
- `status`
- `settings`
- `allowedOrigins`

Se utiliza actualmente en:
- `PUT /api/tenants/{id}`

### UpdateTenantStatusRequest
Contrato específico para cambio de estado:
```json
{
  "status": "suspended"
}
```

Reglas actuales:
- `status`: requerido
- `status`: permitido `active`, `suspended`, `archived`

Se utiliza actualmente en:
- `PATCH /api/tenants/{id}/status`

### TenantDto
Respuesta de lectura del tenant:
```json
{
  "id": 1,
  "clientId": "acme-web",
  "slug": "acme",
  "name": "Acme Inc",
  "status": "active",
  "settings": {
	"theme": "dark"
  },
  "allowedOrigins": [
	"https://app.acme.com",
	"https://admin.acme.com"
  ]
}
```

### CreateTenantResponse
Al crear un tenant se devuelve:
```json
{
  "tenant": {
	"id": 1,
	"clientId": "acme-web",
	"slug": "acme",
	"name": "Acme Inc",
	"status": "active",
	"settings": {
	  "theme": "dark"
	},
	"allowedOrigins": [
	  "https://app.acme.com"
	]
  },
  "apiKey": "tk_live_xxxxxxxxxxxxxxxxxxxxxxxx"
}
```

## Comportamiento del servicio
### Alta de tenant
Al crear un tenant, el servicio:
1. Normaliza `allowedOrigins`
2. Genera una API key pública en formato `tk_live_*`
3. Hashea la API key con SHA-256 antes de persistirla
4. Guarda scopes iniciales:
   - `tenants:read`
   - `tenants:write`
5. Devuelve la API key en claro solo en la respuesta de creación

### Normalización de orígenes
Los valores de `allowedOrigins`:
- eliminan espacios laterales
- eliminan la barra final `/`
- eliminan duplicados ignorando mayúsculas/minúsculas
- se almacenan serializados como JSON

Ejemplo:
- Entrada:
  - `https://app.acme.com/`
  - `https://APP.acme.com`
- Salida persistida:
  - `https://app.acme.com`

### Settings
- `settings` se almacena como JSON
- si no existe valor, se guarda `null`
- al leer, se deserializa a `TenantSettingsDto`
- el contrato actual permite un objeto flexible mediante extensión JSON

### Estados
Estados contemplados actualmente:
- `active`
- `suspended`
- `archived`

## Seguridad
- La API key no se persiste en claro
- Solo se almacena `KeyHash`
- La API key se devuelve una sola vez en la creación del tenant
- Si se pierde, será necesario implementar rotación o regeneración

## Integración actual con API
`TenantsController` ya expone endpoints protegidos con la policy `SuperadminOnly`.

### Endpoints disponibles
- `GET /api/tenants`
  - devuelve `200 OK` con `IReadOnlyList<TenantDto>`
- `GET /api/tenants/{id}`
  - devuelve `200 OK` con `TenantDto`
  - devuelve `404 Not Found` si no existe
- `POST /api/tenants`
  - recibe `CreateTenantRequest`
  - devuelve `201 Created` con `CreateTenantResponse`
  - devuelve `400 Bad Request` por validaciones del modelo
  - devuelve `409 Conflict` si existe conflicto por `slug` o `client_id`
- `PUT /api/tenants/{id}`
  - recibe `UpdateTenantRequest`
  - devuelve `200 OK` con `TenantDto`
  - devuelve `404 Not Found` si no existe
- `PATCH /api/tenants/{id}/status`
  - recibe `UpdateTenantStatusRequest`
  - devuelve `204 No Content`
  - devuelve `404 Not Found` si no existe
- `DELETE /api/tenants/{id}`
  - devuelve `204 No Content`
  - devuelve `404 Not Found` si no existe

### Observaciones del controlador
- El alta usa `CreatedAtAction(nameof(Get), new { id = res.Tenant.Id }, res)`.
- El controlador captura `DbUpdateException` para traducir conflictos de unicidad a `409 Conflict`.
- La detección de duplicados depende actualmente de los nombres `UQ_tenants_slug` e `IX_tenants_client_id` presentes en el error interno de base de datos.

## Consideraciones técnicas
- El servicio usa `AsNoTracking()` en lecturas
- La persistencia depende de `Infrastructure.Data.AppDbContext`
- `settings` y `allowedOrigins` se serializan con `System.Text.Json`
- El diseño actual usa actualización parcial en `UpdateAsync`

## Mejoras recomendadas
- Validar unicidad de `ClientId` y `Slug` también a nivel de servicio antes de persistir
- Confirmar y documentar los índices o constraints únicos de base de datos
- Agregar tests de controlador, servicio e integración
- Implementar rotación y revocación de API keys
- Auditar altas, cambios y bajas
- Definir scopes más granulares
- Agregar paginación y filtros en el listado
- Estandarizar respuestas de error para validaciones y conflictos

## Archivos relevantes
- `Application/Interfaces/ITenantService.cs`
- `Application/Services/TenantService.cs`
- `Domain/Entities/Tenant.cs`
- `Domain/Entities/TenantApiKey.cs`
- `Domain/Dto/TenantDto.cs`
- `Domain/Dto/TenantSettingsDto.cs`
- `Domain/Request/CreateTenantRequest.cs`
- `Domain/Request/UpdateTenantRequest.cs`
- `Domain/Request/UpdateTenantStatusRequest.cs`
- `Domain/Response/CreateTenantResponse.cs`
- `Infrastructure/Data/AppDbContext.cs`
- `authcraft-api/Controllers/TenantsController.cs`
