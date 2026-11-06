using authcraft_api.Tests.Infrastructure;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace authcraft_api.Tests;

public sealed class InvitationSignupSqlServerFixtureTests
{
    [Fact]
    public async Task TransactionRollsBackAcrossIndependentContexts()
    {
        await using var fixture = new InvitationSignupSqlServerFixture();
        await fixture.InitializeAsync();
        var clientId = $"fixture-{Guid.NewGuid():N}";
        await using (var first = fixture.CreateContext())
        {
            await using var transaction = await first.Database.BeginTransactionAsync();
            first.Tenants.Add(new Tenant
            {
                ClientId = clientId,
                Slug = clientId,
                Name = "Fixture gym",
                Status = "active"
            });
            await first.SaveChangesAsync();
            await transaction.RollbackAsync();
        }

        await using var second = fixture.CreateContext();
        Assert.False(await second.Tenants.AnyAsync(t => t.ClientId == clientId));
    }

    [Fact]
    public void MissingConnectionFailsExplicitly()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => InvitationSignupSqlServerFixture.ValidateConnectionString(null));
        Assert.Contains("AUTHCRAFT_TEST_SQL_CONNECTION", error.Message);
    }

    [Theory]
    [InlineData("Server=localhost;Database=authcraft_invitation_test_local;Integrated Security=True")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=authcraft_invitation_test_local;Integrated Security=True")]
    public void LocalTestServersAreAccepted(string connection)
    {
        var validated = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(
            InvitationSignupSqlServerFixture.ValidateConnectionString(connection));

        Assert.StartsWith("authcraft_invitation_test_", validated.InitialCatalog, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Server=localhost;Database=authcraft;Integrated Security=True")]
    [InlineData("Server=localhost;Database=master;Integrated Security=True")]
    [InlineData("Server=remote.example.test;Database=authcraft_invitation_test_local;Integrated Security=True")]
    public void DatabaseOutsideLocalTestScopeIsRejected(string connection)
    {
        Assert.Throws<InvalidOperationException>(
            () => InvitationSignupSqlServerFixture.ValidateConnectionString(connection));
    }
}
