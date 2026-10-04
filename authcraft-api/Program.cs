using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Infrastructure;
using Azure.Identity;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.

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

        // Registrar capa de infraestructura (DbContext) después de cargar configuración/secretos
        builder.Services.AddInfrastructure(builder.Configuration);

        builder.Services.AddControllers();

        // Configura autenticación externa (Google). Los valores se leen de la configuración:
        // Authentication:Google:ClientId y Authentication:Google:ClientSecret
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
        })
            .AddCookie()
            .AddGoogle(googleOptions =>
            {
                googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"];
                googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
                // Ruta de callback usada por Google tras autenticación; debe coincidir con el controlador
                googleOptions.CallbackPath = "/api/authentication/google/callback";
            });

        builder.Services.AddAuthorization();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
        builder.Services.AddOpenApi();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"); c.RoutePrefix = string.Empty; }); }

        app.UseHttpsRedirection();

        // Añadir autenticación antes de autorización
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}