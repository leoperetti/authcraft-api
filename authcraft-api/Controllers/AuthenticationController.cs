using Application.Audit;
using Application.Interfaces;
using Domain.Audit;
using Domain.Dto;
using Domain.Request;
using Domain.Response;
using Domain.Exceptions;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticationController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly Application.Interfaces.IAuthenticationService _authService;
        private readonly ILocalAuthService _localAuthService;
        private readonly IRoleService _roleService;
        private readonly IAuditService _auditService;
        private readonly IUserService _userService;

        public AuthenticationController(IConfiguration configuration, Application.Interfaces.IAuthenticationService authService, ILocalAuthService localAuthService, IRoleService roleService, IAuditService auditService, IUserService userService)
        {
            _configuration = configuration;
            _authService = authService;
            _localAuthService = localAuthService;
            _roleService = roleService;
            _auditService = auditService;
            _userService = userService;
        }

        private string GetJwtKey()
        {
            // Prefer hierarchical key, then fallbacks that map better to Key Vault secret names
            return _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
        }

        [HttpPost("signup")]
        public async Task<ActionResult<AuthResponse>> Signup([FromBody] SignupRequest request)
        {
            try
            {
                var identity = await _localAuthService.SignupAsync(request);
                return Ok(new { message = "Registro exitoso", email = identity.Email });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Error interno al registrar usuario" });
            }
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] Domain.Request.LoginRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Identifier) || string.IsNullOrWhiteSpace(request.Password))
                    return BadRequest(new { message = "Se requieren identifier y password" });

                if (string.IsNullOrWhiteSpace(request.ClientId))
                    return BadRequest(new { message = "Se requiere clientId" });

                var jwtKey = GetJwtKey();
                var jwtIssuer = _configuration["Jwt:Issuer"] ?? string.Empty;
                var jwtAudience = _configuration["Jwt:Audience"] ?? string.Empty;

                var response = await _authService.LoginWithPasswordAsync(request, jwtKey, jwtIssuer, jwtAudience);
                return Ok(response);
            }
            catch (InvalidOperationException ex)
            {
                await _auditService.LogIndependentAsync(new AuditEntry
                {
                    Action = AuditActions.LoginFailed,
                    EntityType = "Identity",
                    EntityId = null,
                    NewValues = new { request.Identifier },
                    Success = false,
                    FailureReason = ex.Message
                });

                return Unauthorized(new { message = ex.Message });
            }
            catch (Exception)
            {
                return StatusCode(500, new { message = "Error interno al autenticar usuario" });
            }
        }

        [HttpPost("auth/google")]
        public async Task<ActionResult<AuthResponse>> VerifyGoogleIdToken([FromBody] GoogleLoginRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.IdToken))
                return BadRequest("Se requiere idToken");

            if (string.IsNullOrWhiteSpace(request.ClientId))
                return BadRequest("Se requiere clientId");

            var clientId = _configuration["Authentication:Google:ClientId"];
            if (string.IsNullOrEmpty(clientId))
                throw new InvalidOperationException("Authentication:Google:ClientId not configured");

            GoogleJsonWebSignature.Payload payload;
            try
            {
                var settings = new GoogleJsonWebSignature.ValidationSettings()
                {
                    Audience = new[] { clientId }
                };
                payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
            }
            catch (InvalidJwtException)
            {
                return BadRequest("Token de ID inválido");
            }

            // Validaciones adicionales
            if (payload.EmailVerified != true)
                return BadRequest("El proveedor no ha verificado el correo electrónico");

            if (payload.Issuer != "accounts.google.com" && payload.Issuer != "https://accounts.google.com")
                return BadRequest("Emisor del token inválido");

            // Delegate business logic to Application service
            var info = new GoogleAuthInfo(
                payload.Subject ?? string.Empty,
                payload.Email,
                payload.EmailVerified == true,
                payload.Name,
                payload.Picture,
                payload.HostedDomain);

            var (identityRecord, oauth) = await _authService.UpsertFromGoogleAsync(info, request.ClientId);

            // Crear ClaimsPrincipal para la sesión
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, payload.Subject ?? string.Empty),
                new Claim(ClaimTypes.Email, payload.Email ?? string.Empty),
                new Claim(ClaimTypes.Name, payload.Name ?? string.Empty),
                new Claim("tenant", request.ClientId)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Generar JWT (igual que en callback)
            var jwtKey = GetJwtKey();
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? string.Empty;
            var jwtAudience = _configuration["Jwt:Audience"] ?? string.Empty;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, identityRecord.Id.ToString()),
                new Claim(ClaimTypes.Email, payload.Email ?? string.Empty),
                new Claim(ClaimTypes.Name, payload.Name ?? string.Empty),
                new Claim("tenant", request.ClientId)
            };

            // Añadir claims de roles del usuario si existen
            var roles = await _roleService.GetRolesAsync(identityRecord.Id);
            foreach (var rn in roles)
            {
                tokenClaims.Add(new Claim(ClaimTypes.Role, rn));
            }

            var expires = DateTime.UtcNow.AddHours(10);
            var token = new JwtSecurityToken(
                issuer: string.IsNullOrEmpty(jwtIssuer) ? null : jwtIssuer,
                audience: string.IsNullOrEmpty(jwtAudience) ? null : jwtAudience,
                claims: tokenClaims,
                expires: expires,
                signingCredentials: signingCredentials
            );

            var tokenString = tokenHandler.WriteToken(token);

            var userDto = new AuthUserDto
            {
                Id = payload.Subject ?? string.Empty,
                Email = payload.Email ?? string.Empty,
                FirstName = payload.GivenName ?? string.Empty,
                LastName = payload.FamilyName ?? string.Empty
            };

            var response = new AuthResponse
            {
                AccessToken = tokenString,
                TokenType = "Bearer",
                ExpiresIn = (int)(expires - DateTime.UtcNow).TotalSeconds,
                User = userDto
            };

            return Ok(response);
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken ct)
        {
            try
            {
                var user = await _userService.CheckIdentityByEmail(request);

                var resetLink = await _userService.GenerateResetTokenAsync(user, request.ClientId, ct);

                return Ok(new { ResetLink = resetLink });
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        public sealed record ResetPasswordRequest(string Code, string NewPassword);

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(
            [FromBody] ResetPasswordRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.NewPassword))
                return BadRequest(new { message = "Datos incompletos." });

            if (request.NewPassword.Length < 8)
                return BadRequest(new { message = "La contraseña debe tener al menos 8 caracteres." });

            ResetPasswordResult result;
            try
            {
                result = await _authService.ResetPasswordAsync(request.Code, request.NewPassword, ct);
            }
            catch (NotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ValidationException ex) { return BadRequest(new { message = ex.Message }); }

            return result switch
            {
                ResetPasswordResult.Success => NoContent(),          // 204
                ResetPasswordResult.InvalidCode => BadRequest(new { message = "El enlace no es válido o ya expiró." }),
                ResetPasswordResult.CodeExpired => BadRequest(new { message = "El enlace expiró. Solicitá uno nuevo." }),
                ResetPasswordResult.CodeAlreadyUsed => BadRequest(new { message = "Este enlace ya fue utilizado." }),
                ResetPasswordResult.WeakPassword => BadRequest(new { message = "La contraseña no cumple los requisitos." }),
                _ => StatusCode(500, new { message = "Error inesperado." }),
            };
        }

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            // Cerrar la cookie de la aplicación
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            // Opcional: solicitar sign-out del proveedor externo
            await HttpContext.SignOutAsync(GoogleDefaults.AuthenticationScheme);

            // JWT es stateless: el cliente debe eliminarlo localmente. Devolvemos NoContent.
            return NoContent();
        }
    }
}
