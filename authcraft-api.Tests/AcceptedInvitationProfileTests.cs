using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace authcraft_api.Tests;

public sealed class AcceptedInvitationProfileTests : IClassFixture<InvitationsApiFactory>
{
    private readonly InvitationsApiFactory _factory;

    public AcceptedInvitationProfileTests(InvitationsApiFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Tenants.Any())
        {
            factory.Seed(
                new Tenant { ClientId = "cekide-app", Slug = "cekide-app", Name = "Cekide", Status = "active" },
                new Tenant { ClientId = "other", Slug = "other", Name = "Other", Status = "active" });
        }

        foreach (var tenant in db.Tenants)
        {
            foreach (var roleName in new[] { "owner", "user" })
            {
                if (!db.Roles.Any(role => role.TenantId == tenant.Id && role.Name == roleName))
                {
                    db.Roles.Add(new Role { TenantId = tenant.Id, Name = roleName, IsSystem = true });
                }
            }
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task Accepted_profile_is_limited_to_the_accepting_identity_and_tenant()
    {
        var email = $"{Guid.NewGuid():N}@mail.com";
        var owner = _factory.CreateClientFor("teacher");
        var create = await owner.PostAsJsonAsync("/api/invitations", new
        {
            email,
            studentProfile = new
            {
                studentId = 987,
                firstName = "Ana María",
                lastName = "de la Cruz",
                fullName = "Ana María de la Cruz",
                phoneNumber = "+541155550000",
                dni = "01.234.567"
            }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var invitationId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationId").GetInt64();
        var code = Regex.Match(_factory.Email.Sent.Last(message => message.To == email).Body, @"invitationCode=([0-9a-f]+)").Groups[1].Value;
        var signup = await _factory.CreateClientFor(null).PostAsJsonAsync("/api/authentication/signup", new
        {
            email,
            phoneNumber = "+541155550000",
            firstName = "Ana María",
            lastName = "de la Cruz",
            password = "test-password-123",
            invitationCode = code,
            clientId = "cekide-app",
            dni = "01234567"
        });
        Assert.Equal(HttpStatusCode.OK, signup.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var identityId = await db.Identities.Where(identity => identity.Email == email).Select(identity => identity.Id).SingleAsync();
        var user = _factory.CreateClientFor("user");
        user.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, identityId.ToString());
        var response = await user.GetAsync($"/api/invitations/{invitationId}/accepted-profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var profile = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(invitationId, profile.GetProperty("invitationId").GetInt64());
        Assert.Equal("987", profile.GetProperty("studentId").GetRawText());
        Assert.Equal(identityId.ToString(), profile.GetProperty("identityId").GetString());
        Assert.Equal("Ana María", profile.GetProperty("prefill").GetProperty("firstName").GetString());
        Assert.Equal("01234567", profile.GetProperty("registered").GetProperty("dni").GetString());

        var wrongIdentity = _factory.CreateClientFor("user");
        wrongIdentity.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, (identityId + 1).ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await wrongIdentity.GetAsync($"/api/invitations/{invitationId}/accepted-profile")).StatusCode);

        var wrongTenant = _factory.CreateClientFor("user", "other");
        wrongTenant.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, identityId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await wrongTenant.GetAsync($"/api/invitations/{invitationId}/accepted-profile")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync("/api/invitations/9223372036854775807/accepted-profile")).StatusCode);
    }
}
