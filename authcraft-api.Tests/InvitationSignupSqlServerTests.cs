using Application.Services;
using Domain.Entities;
using Domain.Request;
using Infrastructure.Data;
using Infrastructure.Repositories;
using authcraft_api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace authcraft_api.Tests;

[Collection("InvitationSignupSqlServer")]
public sealed class InvitationSignupSqlServerTests
{
    private readonly InvitationSignupSqlServerFixture _fixture;

    public InvitationSignupSqlServerTests(InvitationSignupSqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Concurrent_signup_accepts_an_invitation_only_once()
    {
        var data = await CreateStudentInvitationAsync();
        using var first = _fixture.CreateContext();
        using var second = _fixture.CreateContext();
        var firstTask = TrySignupAsync(CreateAuthService(first), data.Email, data.Code);
        var secondTask = TrySignupAsync(CreateAuthService(second), data.Email, data.Code);
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is not null);
        await using var verify = _fixture.CreateContext();
        var invitation = await verify.Invitations.SingleAsync(item => item.Code == data.Code);
        Assert.True(invitation.Used);
        Assert.NotNull(invitation.AcceptedIdentityId);
        var identityId = await verify.Identities.Where(item => item.Email == data.Email).Select(item => item.Id).SingleAsync();
        Assert.Equal(1, await verify.LocalAccounts.CountAsync(account => account.IdentityId == identityId));
        Assert.Equal(1, await verify.Memberships.CountAsync(membership => membership.IdentityId == identityId));
    }

    [Fact]
    public async Task Signup_rolls_back_identity_account_membership_and_invitation_after_save_failure()
    {
        var data = await CreateStudentInvitationAsync();
        await using (var context = _fixture.CreateContext())
        {
            var service = CreateAuthService(context, new SaveThenFailUnitOfWork(context));
            await Assert.ThrowsAsync<InjectedSaveFailureException>(() =>
                service.SignupAsync(SignupRequest(data.Email, data.Code)));
        }

        await using var verify = _fixture.CreateContext();
        var invitation = await verify.Invitations.SingleAsync(item => item.Code == data.Code);
        Assert.False(invitation.Used);
        Assert.Null(invitation.AcceptedIdentityId);
        var identityId = await verify.Identities.Where(item => item.Email == data.Email).Select(item => (long?)item.Id).SingleOrDefaultAsync();
        Assert.Null(identityId);
        Assert.Equal(0, await verify.LocalAccounts.CountAsync(account => account.IdentityId == identityId));
        Assert.Equal(0, await verify.Memberships.CountAsync(membership => membership.IdentityId == identityId));
    }

    [Fact]
    public async Task Resend_persists_profile_and_invalidation_together()
    {
        var data = await CreateStudentInvitationAsync();
        await using var context = _fixture.CreateContext();
        var email = new FakeEmailService();
        var service = new InvitationService(
            new InvitationRepository(context),
            new TenantRepository(context),
            email);

        await service.ResendAsync(GymClientId, data.InvitationId);

        await using var verify = _fixture.CreateContext();
        var previous = await verify.Invitations.SingleAsync(item => item.Code == data.Code);
        var replacement = await verify.Invitations.SingleAsync(item => item.Id != data.InvitationId && item.StudentId == data.StudentId);
        Assert.True(previous.Used);
        Assert.Equal(previous.StudentId, replacement.StudentId);
        Assert.Equal(previous.Purpose, replacement.Purpose);
        Assert.Equal(previous.Dni, replacement.Dni);
        Assert.False(replacement.Used);
        Assert.Single(email.Sent);
    }

    private async Task<(string Email, string Code, long InvitationId, long StudentId)> CreateStudentInvitationAsync()
    {
        await using var context = _fixture.CreateContext();
        var tenant = await context.Tenants.SingleOrDefaultAsync(item => item.ClientId == GymClientId);
        if (tenant is null)
        {
            tenant = new Tenant { ClientId = GymClientId, Slug = GymClientId, Name = "Cekide test", Status = "active" };
            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        foreach (var roleName in new[] { "owner", "user" })
        {
            if (!await context.Roles.AnyAsync(role => role.TenantId == tenant.Id && role.Name == roleName))
            {
                context.Roles.Add(new Role { TenantId = tenant.Id, Name = roleName, IsSystem = true });
            }
        }

        var email = $"{Guid.NewGuid():N}@example.test";
        var code = Guid.NewGuid().ToString("N");
        var studentId = Random.Shared.Next(1, int.MaxValue);
        var invitation = new Invitation
        {
            TenantId = tenant.Id,
            Email = email,
            Code = code,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Purpose = "gym_student",
            StudentId = studentId,
            FirstName = "Ana María",
            LastName = "de la Cruz",
            FullName = "Ana María de la Cruz",
            PhoneNumber = "+541155550000",
            Dni = "01234567"
        };
        context.Invitations.Add(invitation);
        await context.SaveChangesAsync();
        var invitationId = invitation.Id;

        _fixture.RegisterCleanup(async db =>
        {
            var identityIds = await db.Identities
                .Where(identity => identity.Email == email)
                .Select(identity => identity.Id)
                .ToArrayAsync();
            await db.MembershipRoles.Where(item => identityIds.Contains(item.MembershipId)).ExecuteDeleteAsync();
            await db.LocalAccounts.Where(item => identityIds.Contains(item.IdentityId)).ExecuteDeleteAsync();
            await db.Memberships.Where(item => identityIds.Contains(item.IdentityId)).ExecuteDeleteAsync();
            await db.Invitations.Where(item => item.Id == invitationId || item.StudentId == studentId && item.TenantId == tenant.Id)
                .ExecuteDeleteAsync();
            await db.Identities.Where(item => identityIds.Contains(item.Id)).ExecuteDeleteAsync();
        });

        return (email, code, invitationId, studentId);
    }

    private static LocalAuthenticationService CreateAuthService(AppDbContext context, IUnitOfWork? unitOfWork = null)
    {
        var identities = new IdentityRepository(context);
        var roles = new RoleService(context, identities, new RoleRepository(context));
        return new LocalAuthenticationService(
            identities,
            new LocalAccountRepository(context),
            context,
            roles,
            unitOfWork ?? new UnitOfWork(context));
    }

    private static async Task<Exception?> TrySignupAsync(LocalAuthenticationService service, string email, string code)
    {
        try
        {
            await service.SignupAsync(SignupRequest(email, code));
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static SignupRequest SignupRequest(string email, string code) => new()
    {
        Email = email,
        PhoneNumber = "+541155550000",
        FirstName = "Ana María",
        LastName = "de la Cruz",
        Password = "test-password-123",
        ClientId = GymClientId,
        InvitationCode = code,
        Dni = "01234567"
    };

    private const string GymClientId = "cekide-app";

    private sealed class SaveThenFailUnitOfWork(AppDbContext context) : IUnitOfWork
    {
        public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            await context.SaveChangesAsync(cancellationToken);
            throw new InjectedSaveFailureException();
        }
    }

    private sealed class InjectedSaveFailureException : Exception
    {
    }
}
