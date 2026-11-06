using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace authcraft_api.Configuration;

public sealed record EnvironmentSettings(
    string Name,
    string? KeyVaultName,
    string JwtKey,
    string JwtIssuer,
    string JwtAudience,
    string? GoogleClientId,
    string? GoogleClientSecret)
{
    public bool GoogleEnabled => GoogleClientId is not null && GoogleClientSecret is not null;

    public TokenValidationParameters CreateTokenValidationParameters()
    {
        return new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey)),
            ValidateIssuer = !string.IsNullOrEmpty(JwtIssuer),
            ValidIssuer = string.IsNullOrEmpty(JwtIssuer) ? null : JwtIssuer,
            ValidateLifetime = true,
            ValidateAudience = !string.IsNullOrEmpty(JwtAudience),
            ValidAudience = string.IsNullOrEmpty(JwtAudience) ? null : JwtAudience,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    }
}

public static class EnvironmentConfiguration
{
    private static readonly IReadOnlyDictionary<string, string?> VaultNames =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Development"] = null,
            ["Staging"] = "authcraft-vault-staging",
            ["Production"] = "authcraft-vault",
            ["Testing"] = null
        };

    public static string? ValidateVault(IConfiguration configuration, string environmentName)
    {
        if (!VaultNames.TryGetValue(environmentName, out var expectedVaultName))
        {
            throw new InvalidOperationException($"Unsupported ASP.NET Core environment '{environmentName}'.");
        }

        var configuredVaultName = configuration["KeyVault:Name"];
        if (!string.Equals(configuredVaultName, expectedVaultName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"KeyVault:Name does not match the approved vault for {environmentName}.");
        }

        if (environmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(configuration["Testing:AllowInMemoryDatabase"], "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Testing requires an explicit in-memory test host.");
        }

        return expectedVaultName;
    }

    public static void ValidateRuntimeEnvironment(
        string effectiveEnvironment,
        string? aspnetCoreEnvironment,
        string? dotnetEnvironment)
    {
        var configuredEnvironments = new[]
        {
            ("ASPNETCORE_ENVIRONMENT", aspnetCoreEnvironment),
            ("DOTNET_ENVIRONMENT", dotnetEnvironment)
        }.Where(item => !string.IsNullOrWhiteSpace(item.Item2)).ToArray();

        if (configuredEnvironments.Any(item =>
                !item.Item2!.Equals(effectiveEnvironment, StringComparison.OrdinalIgnoreCase)) ||
            configuredEnvironments.Select(item => item.Item2).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            throw new InvalidOperationException(
                "ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT must match the effective host environment.");
        }
    }

    public static EnvironmentSettings Resolve(IConfiguration configuration, string environmentName)
    {
        var vaultName = ValidateVault(configuration, environmentName);
        var jwtKey = ResolveAlias(configuration, "Jwt:Key", "JwtKey");
        var jwtIssuer = configuration["Jwt:Issuer"]?.Trim() ?? string.Empty;
        var jwtAudience = configuration["Jwt:Audience"]?.Trim() ?? string.Empty;
        var googleClientId = ResolveAlias(
            configuration,
            "Authentication:Google:ClientId",
            "ClientId");
        var googleClientSecret = ResolveAlias(
            configuration,
            "Authentication:Google:ClientSecret",
            "ClientSecret",
            "ClientSecretId");

        var missingKeys = new List<string>();
        if (jwtKey is null)
        {
            missingKeys.Add("Jwt:Key");
        }

        if ((environmentName.Equals("Development", StringComparison.OrdinalIgnoreCase) ||
             environmentName.Equals("Staging", StringComparison.OrdinalIgnoreCase)) &&
            (jwtIssuer.Length == 0 || jwtAudience.Length == 0))
        {
            if (jwtIssuer.Length == 0)
            {
                missingKeys.Add("Jwt:Issuer");
            }

            if (jwtAudience.Length == 0)
            {
                missingKeys.Add("Jwt:Audience");
            }
        }

        if (missingKeys.Count > 0)
        {
            throw new InvalidOperationException(
                $"Required configuration is missing or incomplete: {string.Join(", ", missingKeys)}.");
        }

        if ((googleClientId is null) != (googleClientSecret is null))
        {
            throw new InvalidOperationException(
                "Google authentication requires both Authentication:Google:ClientId and Authentication:Google:ClientSecret.");
        }

        if (Encoding.UTF8.GetByteCount(jwtKey!) < 32)
        {
            throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes.");
        }

        return new EnvironmentSettings(
            environmentName,
            vaultName,
            jwtKey!,
            jwtIssuer,
            jwtAudience,
            googleClientId,
            googleClientSecret);
    }

    private static string? ResolveAlias(IConfiguration configuration, params string[] keys)
    {
        var configured = keys
            .Select(key => (Key: key, Value: configuration[key]?.Trim()))
            .Where(item => !string.IsNullOrEmpty(item.Value))
            .ToArray();

        if (configured.Length == 0)
        {
            return null;
        }

        var selectedValue = configured[0].Value!;
        if (configured.Any(item => !string.Equals(item.Value, selectedValue, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Conflicting configuration aliases are set: {string.Join(", ", configured.Select(item => item.Key))}.");
        }

        return selectedValue;
    }
}
