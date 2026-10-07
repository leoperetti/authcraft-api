using Azure.Identity;
using Infrastructure;
using Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Si estamos en producción, cargar secretos desde Azure Key Vault
        if (builder.Environment.IsProduction())
        {
            var keyVaultName = builder.Configuration["KeyVault:Name"];
            if (!string.IsNullOrWhiteSpace(keyVaultName))
            {
                var kvUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
                builder.Configuration.AddAzureKeyVault(kvUri, new DefaultAzureCredential());
            }
        }

        // Leer posibles secretos desde Key Vault (si existen) y pasarlos a la capa de infraestructura
        // Soportamos secreto llamado "ConnectionString" en Key Vault, o la tradicional ConnectionStrings:DefaultConnection / AZURE_SQL_CONNECTIONSTRING
        // Key Vault secret name for DB connection string can be 'DatabaseConnectionString' or 'ConnectionString'
        var connectionStringFromKeyVault = builder.Configuration["DatabaseConnectionString"];

        // Registrar capa de infraestructura (DbContext) después de cargar configuración/secretos
        builder.Services.AddInfrastructure(builder.Configuration, connectionStringFromKeyVault);

        builder.Services.AddControllers();

        // Configura autenticación externa (Google). Preferimos leer ClientId/ClientSecret desde Key Vault.
        // Soportamos estos nombres en Key Vault: ClientId, ClientSecret, ClientSecretId
        var googleClientId = builder.Configuration["ClientId"] ?? builder.Configuration["Authentication:Google:ClientId"];
        var googleClientSecret = builder.Configuration["ClientSecret"] ?? builder.Configuration["ClientSecretId"] ?? builder.Configuration["Authentication:Google:ClientSecret"];

        var googleConfigured = !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);

        // Configurar esquema de autenticación; solo usar Google como challenge si está configurado
        var authBuilder = builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            if (googleConfigured)
            {
                options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
            }
        });

        authBuilder.AddCookie();

        if (googleConfigured)
        {
            authBuilder.AddGoogle(googleOptions =>
            {
                googleOptions.ClientId = googleClientId;
                googleOptions.ClientSecret = googleClientSecret;
                // Ruta de callback usada por Google tras autenticación; debe coincidir con el controlador
                googleOptions.CallbackPath = "/api/authentication/google/callback";
            });
        }

        builder.Services.AddAuthorization();

        builder.Services.AddOpenApi();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"); c.RoutePrefix = string.Empty; }); }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}