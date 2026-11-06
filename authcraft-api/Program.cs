using Azure.Identity;
using Infrastructure;
using Application;
using authcraft_api.Bootstrap;
using authcraft_api.Cors;
using authcraft_api.Configuration;
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
        EnvironmentConfiguration.ValidateRuntimeEnvironment(
            builder.Environment.EnvironmentName,
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"));

        var keyVaultName = EnvironmentConfiguration.ValidateVault(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        if (keyVaultName is not null)
        {
            var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
            builder.Configuration.AddAzureKeyVault(keyVaultUri, new DefaultAzureCredential());
        }

        var environmentSettings = EnvironmentConfiguration.Resolve(
            builder.Configuration,
            builder.Environment.EnvironmentName);
        var effectiveConfiguration = new Dictionary<string, string?>
        {
            ["Jwt:Key"] = environmentSettings.JwtKey,
            ["Jwt:Issuer"] = environmentSettings.JwtIssuer,
            ["Jwt:Audience"] = environmentSettings.JwtAudience,
            ["Authentication:Google:ClientId"] = environmentSettings.GoogleClientId,
            ["Authentication:Google:ClientSecret"] = environmentSettings.GoogleClientSecret
        };
        builder.Configuration.AddInMemoryCollection(effectiveConfiguration);

        builder.Services.AddInfrastructure(
            builder.Configuration,
            environmentName: builder.Environment.EnvironmentName);
        builder.Services.AddApplicationDependencies();
        builder.Services.Configure<SuperadminBootstrapOptions>(builder.Configuration.GetSection("SuperadminBootstrap"));
        builder.Services.AddScoped<SuperadminBootstrapper>();

        builder.Services.AddControllers();

        // Configurar esquema de autenticación; solo usar Google como challenge si está configurado
        var authBuilder = builder.Services.AddAuthentication(options =>
        {
            // Authenticate API requests with JWTs by default; use cookies for sign-in flows
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            if (environmentSettings.GoogleEnabled)
            {
                options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
            }
        });

        authBuilder.AddCookie();

        // Add JWT bearer authentication so the API can validate incoming JWTs
        authBuilder.AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = true;
            options.SaveToken = true;
            options.TokenValidationParameters = environmentSettings.CreateTokenValidationParameters();
        });

        if (environmentSettings.GoogleEnabled)
        {
            authBuilder.AddGoogle(googleOptions =>
            {
                googleOptions.ClientId = environmentSettings.GoogleClientId!;
                googleOptions.ClientSecret = environmentSettings.GoogleClientSecret!;
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