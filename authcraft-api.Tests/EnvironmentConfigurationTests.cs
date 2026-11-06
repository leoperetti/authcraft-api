using authcraft_api.Configuration;
using Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace authcraft_api.Tests;

public sealed class EnvironmentConfigurationTests
{
    [Fact]
    public void Resolve_RejectsMissingJwtSecretWithoutRevealingValues()
    {
        var configuration = CreateConfiguration();
        configuration["Jwt:Key"] = null;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentConfiguration.Resolve(configuration, "Development"));

        Assert.Contains("Jwt:Key", exception.Message);
        Assert.DoesNotContain("LocalDb", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment-test-signing-key", exception.Message);
    }

    [Fact]
    public void DatabaseResolver_RejectsMissingConnectionString()
    {
        var configuration = CreateConfiguration();
        configuration["DatabaseConnectionString"] = null;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.ResolveAndValidate(configuration, null, "Development"));

        Assert.Contains("DatabaseConnectionString", exception.Message);
        Assert.DoesNotContain("LocalDb", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_AcceptsEqualLegacyAndHierarchicalJwtAliases()
    {
        var configuration = CreateConfiguration();
        configuration["JwtKey"] = configuration["Jwt:Key"];

        var settings = EnvironmentConfiguration.Resolve(configuration, "Development");

        Assert.Equal(configuration["Jwt:Key"], settings.JwtKey);
    }

    [Fact]
    public void Resolve_RejectsConflictingJwtAliasesWithoutRevealingEitherValue()
    {
        var configuration = CreateConfiguration();
        configuration["JwtKey"] = "different-secret-value-never-report";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentConfiguration.Resolve(configuration, "Development"));

        Assert.Contains("JwtKey", exception.Message);
        Assert.Contains("Jwt:Key", exception.Message);
        Assert.DoesNotContain("different-secret-value-never-report", exception.Message);
        Assert.DoesNotContain("environment-test-signing-key", exception.Message);
    }

    [Fact]
    public void Resolve_RejectsRemoteVaultInDevelopment()
    {
        var configuration = CreateConfiguration();
        configuration["KeyVault:Name"] = "authcraft-vault";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentConfiguration.Resolve(configuration, "Development"));

        Assert.Contains("KeyVault:Name", exception.Message);
        Assert.DoesNotContain("authcraft-vault", exception.Message);
    }

    [Fact]
    public void Resolve_RejectsUnknownEnvironment()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentConfiguration.Resolve(CreateConfiguration(), "Preview"));

        Assert.Contains("Preview", exception.Message);
    }

    [Fact]
    public void ValidateVault_RejectsTestingWithoutExplicitInMemoryHost()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentConfiguration.ValidateVault(CreateConfiguration(), "Testing"));

        Assert.Contains("explicit in-memory test host", exception.Message);
    }

    [Fact]
    public void ValidateRuntimeEnvironment_RejectsContradictoryHostSettings()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentConfiguration.ValidateRuntimeEnvironment("Staging", "Staging", "Production"));

        Assert.Contains("ASPNETCORE_ENVIRONMENT and DOTNET_ENVIRONMENT", exception.Message);
    }

    [Fact]
    public void Resolve_AllowsGoogleWhenBothCredentialsAreAbsent()
    {
        var settings = EnvironmentConfiguration.Resolve(CreateConfiguration(), "Development");

        Assert.Null(settings.GoogleClientId);
        Assert.Null(settings.GoogleClientSecret);
    }

    [Theory]
    [InlineData("Authentication:Google:ClientId", null)]
    [InlineData("Authentication:Google:ClientSecret", null)]
    public void Resolve_RejectsPartiallyConfiguredGoogle(string configuredKey, string? absentKey)
    {
        var configuration = CreateConfiguration();
        configuration[configuredKey] = configuredKey.EndsWith("ClientId", StringComparison.Ordinal)
            ? "test-client-id"
            : "test-client-secret";
        if (absentKey is not null)
        {
            configuration[absentKey] = null;
        }

        var exception = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentConfiguration.Resolve(configuration, "Development"));

        Assert.Contains("Authentication:Google:ClientId", exception.Message);
        Assert.Contains("Authentication:Google:ClientSecret", exception.Message);
        Assert.DoesNotContain("test-client-", exception.Message);
    }

    [Fact]
    public void Resolve_UsesNormalConfigurationProviderPrecedenceForSameKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(CreateConfiguration().AsEnumerable())
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "second-provider-signing-key-with-adequate-length"
            })
            .Build();

        var settings = EnvironmentConfiguration.Resolve(configuration, "Development");

        Assert.Equal("second-provider-signing-key-with-adequate-length", settings.JwtKey);
    }

    [Fact]
    public void DatabaseResolver_RejectsConflictingConnectionAliases()
    {
        var configuration = CreateConfiguration();
        configuration["ConnectionStrings:DefaultConnection"] =
            "Server=localhost;Database=another_local_database;Trusted_Connection=True";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.ResolveAndValidate(configuration, null, "Development"));

        Assert.Contains("DatabaseConnectionString", exception.Message);
        Assert.Contains("ConnectionStrings:DefaultConnection", exception.Message);
        Assert.DoesNotContain("another_local_database", exception.Message);
    }

    [Fact]
    public void DatabaseResolver_RejectsRemoteSqlFromDevelopment()
    {
        var configuration = CreateConfiguration();
        configuration["DatabaseConnectionString"] =
            "Server=tcp:authcraftdbbrasil.database.windows.net,1433;Database=authcraft_development;Trusted_Connection=True";
        configuration["Database:ExpectedServer"] = "authcraftdbbrasil.database.windows.net";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.ResolveAndValidate(configuration, null, "Development"));

        Assert.Contains("dedicated local SQL database", exception.Message);
        Assert.DoesNotContain("authcraftdbbrasil", exception.Message);
    }

    [Fact]
    public void DatabaseResolver_RejectsCrossedStagingTarget()
    {
        var configuration = CreateConfiguration();
        configuration["DatabaseConnectionString"] =
            "Server=tcp:authcraftdbbrasil.database.windows.net,1433;Database=authcraft-db;Authentication=Active Directory Managed Identity";
        configuration["Database:ExpectedServer"] = "authcraftdbbrasil.database.windows.net";
        configuration["Database:ExpectedName"] = "authcraft-db";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseConfiguration.ResolveAndValidate(configuration, null, "Staging"));

        Assert.Contains("approved Staging SQL target", exception.Message);
        Assert.DoesNotContain("authcraft-db", exception.Message);
    }

    private static ConfigurationManager CreateConfiguration()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseConnectionString"] = "Server=(localdb)\\MSSQLLocalDB;Database=authcraft_development;Trusted_Connection=True",
            ["Database:ExpectedServer"] = "(localdb)\\MSSQLLocalDB",
            ["Database:ExpectedName"] = "authcraft_development",
            ["Jwt:Key"] = "environment-test-signing-key-with-more-than-thirty-two-bytes",
            ["Jwt:Issuer"] = "authcraft-development",
            ["Jwt:Audience"] = "authcraft-development",
            ["Authentication:Google:ClientId"] = "",
            ["Authentication:Google:ClientSecret"] = ""
        });
        return configuration;
    }
}
