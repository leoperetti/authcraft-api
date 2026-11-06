using System.Net;
using System.Text.Json;
using Infrastructure.Health;

namespace authcraft_api.Tests;

public sealed class EnvironmentHealthTests
{
    [Fact]
    public async Task Live_ReturnsVersionWithoutQueryingDatabaseOrUsingConfigurationVersion()
    {
        using var factory = new InvitationsApiFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", body.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("version").GetString()));
        Assert.NotEqual("untrusted-config-version", body.RootElement.GetProperty("version").GetString());
        Assert.Equal(0, factory.ReadinessCheck.Calls);
    }

    [Fact]
    public async Task Ready_QueriesDatabaseOnEveryRequest()
    {
        using var factory = new InvitationsApiFactory();
        using var client = factory.CreateClient();

        using var firstResponse = await client.GetAsync("/health/ready");
        using var secondResponse = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal(2, factory.ReadinessCheck.Calls);
    }

    [Fact]
    public async Task Ready_ReturnsSanitized503WhenDatabaseIsNotReady()
    {
        using var factory = new InvitationsApiFactory();
        factory.ReadinessCheck.Check = _ => Task.FromResult(false);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"status\":\"unhealthy\"", body);
        Assert.DoesNotContain("authcraft-db-staging", body);
        Assert.DoesNotContain("secret", body, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FakeDatabaseReadinessCheck : DatabaseReadinessCheck.IChecker
{
    public int Calls { get; private set; }

    public Func<CancellationToken, Task<bool>> Check { get; set; } =
        _ => Task.FromResult(true);

    public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Check(cancellationToken);
    }
}
