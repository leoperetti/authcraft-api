using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace authcraft_api.Tests;

public class InvitationSignupTests : IClassFixture<InvitationsApiFactory>
{
    private const string GymClientId = "cekide-app";
    private const string OtherClientId = "other";
    private readonly InvitationsApiFactory _factory;

    public InvitationSignupTests(InvitationsApiFactory factory)
    {
        _factory = factory;
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.Tenants.Any())
        {
            factory.Seed(
                new Tenant { ClientId = GymClientId, Slug = GymClientId, Name = "Cekide", Status = "active" },
                new Tenant { ClientId = OtherClientId, Slug = OtherClientId, Name = "Other", Status = "active" });
        }

        var tenants = db.Tenants.ToDictionary(tenant => tenant.ClientId);
        foreach (var tenant in tenants.Values)
        {
            foreach (var name in new[] { "owner", "user" })
            {
                if (!db.Roles.Any(role => role.TenantId == tenant.Id && role.Name == name))
                {
                    db.Roles.Add(new Role { TenantId = tenant.Id, Name = name, IsSystem = true });
                }
            }
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task Gym_student_signup_requires_DNI_and_does_not_consume_invitation_when_missing_or_invalid()
    {
        var email = UniqueEmail();
        var invitationCode = await CreateStudentInvitationAsync(email, "01.234.567");
        var client = _factory.CreateClientFor(null);

        var missing = await SignupAsync(client, email, invitationCode, dni: null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        AssertInvitationUnconsumed(invitationCode);
        AssertIdentityNotCreated(email);

        var invalid = await SignupAsync(client, email, invitationCode, dni: "12A4567");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        AssertInvitationUnconsumed(invitationCode);
        AssertIdentityNotCreated(email);
    }

    [Fact]
    public async Task Gym_student_signup_requires_DNI_to_match_prefill_and_rejects_email_or_tenant_mismatch()
    {
        var email = UniqueEmail();
        var invitationCode = await CreateStudentInvitationAsync(email, "01234567");
        var client = _factory.CreateClientFor(null);

        var mismatchedDni = await SignupAsync(client, email, invitationCode, dni: "76543210");
        Assert.Equal(HttpStatusCode.BadRequest, mismatchedDni.StatusCode);
        AssertInvitationUnconsumed(invitationCode);
        AssertIdentityNotCreated(email);

        var wrongEmail = await SignupAsync(client, UniqueEmail(), invitationCode, dni: "01234567");
        Assert.Equal(HttpStatusCode.Conflict, wrongEmail.StatusCode);
        AssertInvitationUnconsumed(invitationCode);

        var wrongTenant = await SignupAsync(client, email, invitationCode, dni: "01234567", clientId: OtherClientId);
        Assert.Equal(HttpStatusCode.Conflict, wrongTenant.StatusCode);
        AssertInvitationUnconsumed(invitationCode);
        AssertIdentityNotCreated(email);
    }

    [Fact]
    public async Task Accepted_gym_student_profile_is_immutable_and_gets_user_role()
    {
        var email = UniqueEmail();
        var invitationCode = await CreateStudentInvitationAsync(email, "01.234.567");
        var client = _factory.CreateClientFor(null);

        var response = await SignupAsync(client, email, invitationCode, dni: "01234567");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var identity = await db.Identities.SingleAsync(item => item.Email == email);
        var invitation = await db.Invitations.SingleAsync(item => item.Code == invitationCode);
        Assert.True(invitation.Used);
        Assert.Equal(identity.Id, invitation.AcceptedIdentityId);
        Assert.NotNull(invitation.AcceptedAt);
        Assert.Equal("01234567", invitation.RegisteredDni);
        Assert.Equal("01.234.567", invitation.Dni);
        Assert.Equal("gym_student", invitation.Purpose);
        var roles = await db.MembershipRoles
            .Where(item => item.MembershipId == identity.Id)
            .Select(item => item.Role.Name)
            .ToListAsync();
        Assert.Contains("user", roles);
        Assert.DoesNotContain("owner", roles);
    }

    [Fact]
    public async Task Generic_signup_without_DNI_keeps_the_first_owner_bootstrap()
    {
        var email = UniqueEmail();
        var clientId = $"generic-{Guid.NewGuid():N}";
        using (var setupScope = _factory.Services.CreateScope())
        {
            var setupDb = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tenant = new Tenant { ClientId = clientId, Slug = clientId, Name = "Generic", Status = "active" };
            setupDb.Tenants.Add(tenant);
            await setupDb.SaveChangesAsync();
            setupDb.Roles.AddRange(
                new Role { TenantId = tenant.Id, Name = "owner", IsSystem = true },
                new Role { TenantId = tenant.Id, Name = "user", IsSystem = true });
            await setupDb.SaveChangesAsync();
        }

        var invitationResponse = await _factory.CreateClientFor("owner", clientId)
            .PostAsJsonAsync("/api/invitations", new { email });
        Assert.Equal(HttpStatusCode.Created, invitationResponse.StatusCode);
        var invitationCode = Regex.Match(
            _factory.Email.Sent.Last(message => message.To == email).Body,
            @"invitationCode=([0-9a-f]+)").Groups[1].Value;
        var response = await SignupAsync(
            _factory.CreateClientFor(null),
            email,
            invitationCode,
            dni: null,
            clientId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var identity = await db.Identities.SingleAsync(item => item.Email == email);
        var roles = await db.MembershipRoles
            .Where(item => item.MembershipId == identity.Id)
            .Select(item => item.Role.Name)
            .ToListAsync();
        Assert.Contains("owner", roles);
    }

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@mail.com";

    private async Task<string> CreateStudentInvitationAsync(string email, string? dni)
    {
        var response = await _factory.CreateClientFor("teacher").PostAsJsonAsync("/api/invitations", new
        {
            email,
            studentProfile = new
            {
                studentId = Random.Shared.Next(1, int.MaxValue),
                firstName = "Ana María",
                lastName = "de la Cruz",
                fullName = "Ana María de la Cruz",
                phoneNumber = "+541155550000",
                dni
            }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return Regex.Match(_factory.Email.Sent.Last(message => message.To == email).Body, @"invitationCode=([0-9a-f]+)").Groups[1].Value;
    }

    private static Task<HttpResponseMessage> SignupAsync(
        HttpClient client,
        string email,
        string? invitationCode,
        string? dni,
        string clientId = GymClientId)
    {
        return client.PostAsJsonAsync("/api/authentication/signup", new
        {
            email,
            phoneNumber = "+541155550000",
            firstName = "Ana María",
            lastName = "de la Cruz",
            password = "test-password-123",
            clientId,
            invitationCode,
            dni
        });
    }

    private void AssertInvitationUnconsumed(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(db.Invitations.Single(item => item.Code == code).Used);
    }

    private void AssertIdentityNotCreated(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(db.Identities.Any(item => item.Email == email));
    }
}
