using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Google.Apis.Auth;
using Microsoft.IdentityModel.Tokens;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Domain.Response;
using Domain.Request;
using Domain.Dto;

namespace authcraft_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthenticationController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly Application.Services.IAuthenticationService _authService;

        public AuthenticationController(IConfiguration configuration, Application.Services.IAuthenticationService authService)
        {
            _configuration = configuration;
            _authService = authService;
        }

        [HttpPost("signup")]
        public async Task<ActionResult<AuthResponse>> Signup([FromBody] SignupRequest request)
        {
            // TODO: implementar registro
            return StatusCode(501); // Not Implemented
        }

        [HttpPost("login")]
        public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
        {
            // TODO: implementar autenticación
            return StatusCode(501);
        }

        [HttpPost("auth/google")]
        public async Task<ActionResult<AuthResponse>> VerifyGoogleIdToken([FromBody] GoogleLoginRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.IdToken))
                return BadRequest("idToken is required");

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
                return BadRequest("Invalid ID token");
            }

            // Validaciones adicionales
            if (payload.EmailVerified != true)
                return BadRequest("Email not verified by provider");

            if (payload.Issuer != "accounts.google.com" && payload.Issuer != "https://accounts.google.com")
                return BadRequest("Invalid token issuer");

            // Delegate business logic to Application service
            var info = new Application.Services.GoogleAuthInfo(
                payload.Subject ?? string.Empty,
                payload.Email,
                payload.EmailVerified == true,
                payload.Name,
                payload.Picture,
                payload.HostedDomain);

            var (user, oauth) = await _authService.UpsertFromGoogleAsync(info);

            // Crear ClaimsPrincipal para la sesión
            var claims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, payload.Subject ?? string.Empty),
                new System.Security.Claims.Claim(ClaimTypes.Email, payload.Email ?? string.Empty),
                new System.Security.Claims.Claim(ClaimTypes.Name, payload.Name ?? string.Empty)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Generar JWT (igual que en callback)
            var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? string.Empty;
            var jwtAudience = _configuration["Jwt:Audience"] ?? string.Empty;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenClaims = new List<System.Security.Claims.Claim>
            {
                new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, payload.Subject ?? string.Empty),
                new System.Security.Claims.Claim(ClaimTypes.Email, payload.Email ?? string.Empty),
                new System.Security.Claims.Claim(ClaimTypes.Name, payload.Name ?? string.Empty),
            };

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

        // Inicia el flujo OAuth con Google (redirección)
        [HttpGet("google/signin")]
        public IActionResult GoogleSignIn([FromQuery] string returnUrl = "/")
        {
            var props = new AuthenticationProperties { RedirectUri = returnUrl };
            return Challenge(props, GoogleDefaults.AuthenticationScheme);
        }

        // Callback que Google redirige tras autenticación
        [HttpGet("google/callback")]
        public async Task<ActionResult<AuthResponse>> GoogleCallback([FromQuery] string? returnUrl = "/")
        {
            var authenticateResult = await HttpContext.AuthenticateAsync(GoogleDefaults.AuthenticationScheme);
            if (!authenticateResult.Succeeded || authenticateResult.Principal == null)
            {
                return BadRequest("Google authentication failed");
            }

            var principal = authenticateResult.Principal;

            // Extraer datos del usuario
            var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value ?? string.Empty;
            var email = principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.FindFirst("email")?.Value ?? string.Empty;
            var firstName = principal.FindFirst(ClaimTypes.GivenName)?.Value ?? principal.FindFirst("given_name")?.Value ?? string.Empty;
            var lastName = principal.FindFirst(ClaimTypes.Surname)?.Value ?? principal.FindFirst("family_name")?.Value ?? string.Empty;

            // Firmar cookie de aplicación
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

            // Generar JWT
            var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? string.Empty;
            var jwtAudience = _configuration["Jwt:Audience"] ?? string.Empty;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, id),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Name, $"{firstName} {lastName}"),
            };

            var expires = DateTime.UtcNow.AddHours(1);
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
                Id = id,
                Email = email,
                FirstName = string.IsNullOrEmpty(firstName) ? ( (principal.Identity?.Name ?? string.Empty).Split(' ')[0] ) : firstName,
                LastName = string.IsNullOrEmpty(lastName) ? ( (principal.Identity?.Name ?? string.Empty).Split(' ').Skip(1).FirstOrDefault() ?? string.Empty ) : lastName
            };

            var response = new AuthResponse
            {
                AccessToken = tokenString,
                TokenType = "Bearer",
                ExpiresIn = (int)(expires - DateTime.UtcNow).TotalSeconds,
                User = userDto
            };

            // Puedes redirigir al cliente o devolver JSON con token y user. Aquí devolvemos JSON.
            return Ok(response);
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
