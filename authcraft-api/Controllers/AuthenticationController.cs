using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.IdentityModel.Tokens;
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

        public AuthenticationController(IConfiguration configuration)
        {
            _configuration = configuration;
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
