using Azure.Identity;
using System.Collections.Generic;
using Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;

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

        builder.Services.AddControllers();

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
            app.UseSwagger(); app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"); c.RoutePrefix = string.Empty; });
        }

        app.UseHttpsRedirection();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}