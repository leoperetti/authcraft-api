using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using authcraft_api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace authcraft_api.Tests;

public sealed class EnvironmentAuthenticationTests
{
    [Fact]
    public void JwtIssuedForOneEnvironment_IsRejectedByAnotherEnvironment()
    {
        var development = EnvironmentConfiguration.Resolve(CreateConfiguration("Development"), "Development");
        var staging = EnvironmentConfiguration.Resolve(CreateConfiguration("Staging"), "Staging");
        var token = CreateToken(development, "development-tenant");

        var exception = Record.Exception(() => ValidateToken(token, staging));

        Assert.NotNull(exception);
    }

    [Fact]
    public void LegacyJwtKeyAlias_IsUsedForBothIssuanceAndValidation()
    {
        var configuration = CreateConfiguration("Development");
        var key = configuration["Jwt:Key"];
        configuration["Jwt:Key"] = null;
        configuration["JwtKey"] = key;
        var settings = EnvironmentConfiguration.Resolve(configuration, "Development");

        var principal = ValidateToken(CreateToken(settings, "development-tenant"), settings);

        Assert.Equal("development-tenant", principal.FindFirst("tenant")?.Value);
    }

    [Fact]
    public void ExistingTokenRemainsValidWhenProductionIssuerAudienceTransitionIsNotConfigured()
    {
        var configuration = CreateConfiguration("Production");
        configuration["DatabaseConnectionString"] =
            "Server=tcp:authcraftdbbrasil.database.windows.net,1433;Database=authcraft-db;Authentication=Active Directory Managed Identity";
        configuration["Database:ExpectedServer"] = "authcraftdbbrasil.database.windows.net";
        configuration["Database:ExpectedName"] = "authcraft-db";
        configuration["KeyVault:Name"] = "authcraft-vault";
        configuration["Jwt:Issuer"] = "";
        configuration["Jwt:Audience"] = "";
        var settings = EnvironmentConfiguration.Resolve(configuration, "Production");

        var principal = ValidateToken(CreateToken(settings, "production-tenant"), settings);

        Assert.Equal("production-tenant", principal.FindFirst("tenant")?.Value);
    }

    private static IConfiguration CreateConfiguration(string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CreateConfigurationValues(environment))
            .Build();
        return configuration;
    }

    private static Dictionary<string, string?> CreateConfigurationValues(string environment)
    {
        var isProduction = environment == "Production";
        return new Dictionary<string, string?>
        {
            ["KeyVault:Name"] = environment switch
            {
                "Staging" => "authcraft-vault-staging",
                "Production" => "authcraft-vault",
                _ => null
            },
            ["Jwt:Key"] = isProduction
                ? "production-test-signing-key-with-more-than-thirty-two-bytes"
                : "development-test-signing-key-with-more-than-thirty-two-bytes",
            ["Jwt:Issuer"] = isProduction ? "" : $"authcraft-{environment.ToLowerInvariant()}",
            ["Jwt:Audience"] = isProduction ? "" : $"authcraft-{environment.ToLowerInvariant()}"
        };
    }

    private static string CreateToken(EnvironmentSettings settings, string tenant)
    {
        var token = new JwtSecurityToken(
            issuer: settings.JwtIssuer,
            audience: settings.JwtAudience,
            claims: [new Claim("tenant", tenant)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.JwtKey)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static ClaimsPrincipal ValidateToken(string token, EnvironmentSettings settings)
    {
        return new JwtSecurityTokenHandler().ValidateToken(token, settings.CreateTokenValidationParameters(), out _);
    }
}
