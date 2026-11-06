# Invitaciones de alumnos

`authcraft-api` es la fuente de verdad de las invitaciones y de la identidad
aceptante. `cekide-api` solicita invitaciones en nombre del personal y
sincroniza el perfil confirmado durante el primer acceso autenticado del
alumno. El signup anónimo no escribe directamente en la base del gimnasio.

## Contrato HTTP

### Crear y reenviar

`POST /api/invitations` requiere un JWT con rol `owner` o `teacher` y el claim
`tenant`. Para invitaciones de alumnos, enviar el perfil pre-cargado:

```json
{
  "email": "alumno@example.test",
  "studentProfile": {
    "studentId": 123,
    "firstName": "Ana María",
    "lastName": "De la Cruz",
    "fullName": "Ana María De la Cruz",
    "phoneNumber": "+5491100000000",
    "dni": null
  }
}
```

La respuesta `201` contiene `invitationId` y `expiresAt`. El reenvío usa
`POST /api/invitations/{id}/resend`; listar pendientes usa `GET
/api/invitations`. Estos endpoints también exigen rol de personal y tenant.
Un perfil inválido o invitación pendiente conflictiva se devuelve como error,
no como un alta satisfactoria.

### Resolver para signup

`GET /api/invitations/{invitationCode}/resolve` es público y responde con
`Cache-Control: no-store`. En caso válido, entrega `email`, `tenantName`,
`tenantSlug`, `expiresAt`, `dniRequired` y, para alumnos, `studentProfile` con
`firstName`, `lastName`, `fullName`, `phoneNumber` y `dni`.

El cliente precarga esos datos sin reemplazar lo que el usuario ya editó.
El email es de solo lectura. Un DNI existente también es de solo lectura;
si falta, el formulario lo exige. Códigos inválidos producen `400`, no
encontrados/usados `404` y expirados `410`.

### Completar el signup

`POST /api/authentication/signup` conserva el contrato existente y acepta
`invitationCode` y el campo aditivo opcional `dni`:

```json
{
  "email": "alumno@example.test",
  "phoneNumber": "+5491100000000",
  "firstName": "Ana María",
  "lastName": "De la Cruz",
  "password": "<password entered by the user>",
  "invitationCode": "<invitation-code>",
  "dni": "12345678"
}
```

Para una invitación `gym_student`, el backend requiere DNI de siete u ocho
dígitos, normaliza únicamente puntos y espacios, y verifica que coincida con
el DNI invitado cuando ya existía. El email debe coincidir con la invitación.
El alta crea una cuenta con rol `user` y consume la invitación dentro de la
misma operación. El signup genérico sin invitación conserva su comportamiento
y no exige DNI. La invitación y el perfil aceptado se validan dentro de su
tenant; este flujo no agrega una unicidad global de DNI ni modifica el signup
de los demás tenants.

## Perfil aceptado y sincronización

`GET /api/invitations/{id}/accepted-profile` requiere autenticación y no
almacena en caché la respuesta. El tenant y el `sub` del JWT deben coincidir
con la invitación aceptada. La respuesta incluye `studentId`, `identityId`,
`acceptedAt`, los valores precargados y los valores confirmados. `cekide-api`
usa esta respuesta al primer acceso autenticado a una función personal para
actualizar la ficha original; no crea otra ficha si el lookup falla. El error
debe comunicarse y el mismo acceso puede reintentarse.

## Base de datos y despliegue

Las tablas de invitaciones pertenecen al proyecto SQL existente
`authcraft-db/authcraft-db.sqlproj`; no se agregan migraciones EF Core en este
repositorio. Desplegar el cambio aditivo de esquema mediante el proceso
existente de SQL Server antes de habilitar la versión de `authcraft-api` que
usa el contrato. Después desplegar `cekide-api` y, por último, la UI. Nunca
aplicar el proyecto SQL a producción sin el proceso de revisión/aprobación del
entorno.

Las pruebas relacionales requieren `AUTHCRAFT_TEST_SQL_CONNECTION` con
`localhost` o SQL Server LocalDB y un catálogo bajo el prefijo
`authcraft_invitation_test_`. Cada fixture crea un catálogo aleatorio y lo
elimina al terminar. No usar la base de cuentas operativa ni incluir tokens,
códigos de invitación, DNI o secretos en logs y documentación.
