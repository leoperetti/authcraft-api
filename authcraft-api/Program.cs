using Azure.Identity;
using Infrastructure;
using Application;
using authcraft_api.Bootstrap;
using authcraft_api.Cors;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Si estamos en producción, cargar secretos desde Azure Key Vault
        if (builder.Environment.IsProduction() || builder.Environment.IsStaging())
        {
            var keyVaultName = builder.Configuration["KeyVault:Name"];
            if (!string.IsNullOrWhiteSpace(keyVaultName))
            {
                var kvUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
                builder.Configuration.AddAzureKeyVault(kvUri, new DefaultAzureCredential());
            }
        }

        // Leer posibles secretos desde Key Vault (si existen) y pasarlos a la capa de infraestructura
        var connectionStringFromKeyVault = builder.Configuration["DatabaseConnectionString"];

        var jwtKeyFromKv = builder.Configuration["JwtKey"] ?? builder.Configuration["Jwt:Key"];
        if (!string.IsNullOrWhiteSpace(jwtKeyFromKv) && string.IsNullOrWhiteSpace(builder.Configuration["Jwt:Key"]))
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string>
            {
                { "Jwt:Key", jwtKeyFromKv }
            });
        }

        builder.Services.AddInfrastructure(builder.Configuration, connectionStringFromKeyVault);
        builder.Services.AddApplicationDependencies();
        builder.Services.Configure<SuperadminBootstrapOptions>(builder.Configuration.GetSection("SuperadminBootstrap"));
        builder.Services.AddScoped<SuperadminBootstrapper>();

        builder.Services.AddControllers();

        var googleClientId = builder.Configuration["ClientId"] ?? builder.Configuration["Authentication:Google:ClientId"];
        var googleClientSecret = builder.Configuration["ClientSecret"] ?? builder.Configuration["ClientSecretId"] ?? builder.Configuration["Authentication:Google:ClientSecret"];

        var googleConfigured = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

        // Configurar esquema de autenticación; solo usar Google como challenge si está configurado
        var authBuilder = builder.Services.AddAuthentication(options =>
        {
            // Authenticate API requests with JWTs by default; use cookies for sign-in flows
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            if (googleConfigured)
            {
                options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
            }
        });

        authBuilder.AddCookie();

        // Add JWT bearer authentication so the API can validate incoming JWTs
        authBuilder.AddJwtBearer(options =>
        {
            var jwtKey = jwtKeyFromKv ?? string.Empty;
            var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? string.Empty;
            var jwtAudience = builder.Configuration["Jwt:Audience"] ?? string.Empty;

            options.RequireHttpsMetadata = true;
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateIssuer = !string.IsNullOrEmpty(jwtIssuer),
                ValidIssuer = string.IsNullOrEmpty(jwtIssuer) ? null : jwtIssuer,
                ValidateLifetime = true,
                ValidateAudience = !string.IsNullOrEmpty(jwtAudience),
                ValidAudience = string.IsNullOrEmpty(jwtAudience) ? null : jwtAudience,
                NameClaimType = ClaimTypes.Name,
                RoleClaimType = ClaimTypes.Role
            };
        });

        if (googleConfigured)
        {
            authBuilder.AddGoogle(googleOptions =>
            {
                googleOptions.ClientId = googleClientId;
                googleOptions.ClientSecret = googleClientSecret;
                googleOptions.CallbackPath = "/api/authentication/google/callback";
            });
        }

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("SuperadminOnly", policy =>
                policy.RequireAuthenticatedUser()
                  .RequireRole("superadmin"));
        });

        builder.Services.AddOpenApi();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddCors();
        builder.Services.AddSingleton<ICorsPolicyProvider, TenantCorsPolicyProvider>();
        builder.Services.AddScoped<TenantOriginValidationMiddleware>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<SuperadminBootstrapper>();
            bootstrapper.BootstrapAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwagger(); app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"); c.RoutePrefix = string.Empty; });    
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseMiddleware<TenantOriginValidationMiddleware>();
        app.UseCors();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}