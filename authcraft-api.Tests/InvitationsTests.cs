using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.Interfaces;
using Application.Services;
using Domain.Entities;
using Domain.Exceptions;
using Infrastructure.Data;
using Infrastructure.Health;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace authcraft_api.Tests;

public sealed class FakeEmailService : IEmailService
{
    public List<(string To, string Subject, string Body)> Sent { get; } = new();

    public void SendEmail(string toEmail, string subject, string body) => Sent.Add((toEmail, subject, body));
}

public class InvitationServiceTests
{
    private const string GymClientId = "cekide-app";

    private readonly AppDbContext _dbContext;
    private readonly FakeEmailService _email = new();
    private readonly InvitationService _service;
    private readonly Tenant _gym;
    private readonly Tenant _other;

    public InvitationServiceTests()
    {
        _dbContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

        _gym = new Tenant { ClientId = GymClientId, Slug = "cekide-app", Name = "Cekide", Status = "active", PrimaryDomain = "https://gym.example.com" };
        _other = new Tenant { ClientId = "other", Slug = "other", Name = "Other", Status = "active" };
        _dbContext.Tenants.AddRange(_gym, _other);
        _dbContext.SaveChanges();

        _service = new InvitationService(new InvitationRepository(_dbContext), new TenantRepository(_dbContext), _email);
    }

    private Invitation AddInvitation(Tenant tenant, string email, bool used = false, TimeSpan? expiresIn = null)
    {
        var invitation = new Invitation
        {
            TenantId = tenant.Id,
            Email = email,
            Code = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = DateTimeOffset.UtcNow.Add(expiresIn ?? TimeSpan.FromDays(3)),
            Used = used
        };
        _dbContext.Invitations.Add(invitation);
        _dbContext.SaveChanges();
        return invitation;
    }

    [Fact]
    public async Task Create_PersistsInvitationExpiringInSevenDays()
    {
        var before = DateTimeOffset.UtcNow;

        var result = await _service.CreateAsync(GymClientId, "alumno@mail.com");

        var after = DateTimeOffset.UtcNow;
        var saved = await _dbContext.Invitations.SingleAsync(invitation => invitation.Id == result.InvitationId);
        Assert.Equal(_gym.Id, saved.TenantId);
        Assert.Equal("alumno@mail.com", saved.Email);
        Assert.False(saved.Used);
        Assert.InRange(saved.ExpiresAt!.Value, before.AddDays(7), after.AddDays(7));
        Assert.Equal(saved.ExpiresAt, result.ExpiresAt);
    }

    [Fact]
    public async Task Create_GeneratesRandomCodes()
    {
        var first = await _service.CreateAsync(GymClientId, "uno@mail.com");
        var second = await _service.CreateAsync(GymClientId, "dos@mail.com");

        var codes = await _dbContext.Invitations
            .Where(invitation => invitation.Id == first.InvitationId || invitation.Id == second.InvitationId)
            .Select(invitation => invitation.Code)
            .ToListAsync();
        Assert.Equal(2, codes.Distinct().Count());
        Assert.All(codes, code => Assert.True(code.Length >= 32));
    }

    [Fact]
    public async Task Create_SendsEmailWithSignupLink()
    {
        await _service.CreateAsync(GymClientId, "alumno@mail.com");

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("alumno@mail.com", sent.To);
        var code = (await _dbContext.Invitations.SingleAsync()).Code;
        Assert.Contains($"https://gym.example.com/signup?invitationCode={code}", sent.Body);
    }

    [Fact]
    public async Task Create_ThrowsPendingInvitation_WhenEmailAlreadyHasPendingInvitation()
    {
        AddInvitation(_gym, "alumno@mail.com");

        await Assert.ThrowsAsync<PendingInvitationException>(() => _service.CreateAsync(GymClientId, "alumno@mail.com"));

        Assert.Empty(_email.Sent);
        Assert.Equal(1, await _dbContext.Invitations.CountAsync());
    }

    [Fact]
    public async Task Create_Succeeds_WhenPreviousInvitationExpiredOrBelongsToOtherTenant()
    {
        AddInvitation(_gym, "expirada@mail.com", expiresIn: TimeSpan.FromDays(-1));
        AddInvitation(_other, "otro@mail.com");

        await _service.CreateAsync(GymClientId, "expirada@mail.com");
        await _service.CreateAsync(GymClientId, "otro@mail.com");

        Assert.Equal(2, _email.Sent.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    public async Task Create_ThrowsInvalidEmail_WhenEmailIsInvalid(string email)
    {
        await Assert.ThrowsAsync<InvalidInvitationEmailException>(() => _service.CreateAsync(GymClientId, email));

        Assert.Empty(_email.Sent);
        Assert.Empty(_dbContext.Invitations);
    }

    [Fact]
    public async Task Create_ThrowsNotFound_WhenTenantDoesNotExist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync("missing", "alumno@mail.com"));
    }

    [Fact]
    public async Task ListPending_ReturnsOnlyPendingInvitationsOfTenant()
    {
        var pending = AddInvitation(_gym, "pendiente@mail.com");
        AddInvitation(_gym, "usada@mail.com", used: true);
        AddInvitation(_gym, "vencida@mail.com", expiresIn: TimeSpan.FromDays(-1));
        AddInvitation(_other, "otro@mail.com");

        var result = await _service.ListPendingAsync(GymClientId);

        var item = Assert.Single(result);
        Assert.Equal(pending.Id, item.Id);
        Assert.Equal("pendiente@mail.com", item.Email);
        Assert.Equal(pending.CreatedAt, item.CreatedAt);
        Assert.Equal(pending.ExpiresAt, item.ExpiresAt);
    }

    [Fact]
    public async Task Resend_InvalidatesPendingInvitationAndCreatesNewOneForSevenDays()
    {
        var original = AddInvitation(_gym, "alumno@mail.com");
        var originalCode = original.Code;
        var before = DateTimeOffset.UtcNow;

        var result = await _service.ResendAsync(GymClientId, original.Id);

        var after = DateTimeOffset.UtcNow;
        Assert.NotEqual(original.Id, result.InvitationId);
        Assert.True((await _dbContext.Invitations.AsNoTracking().SingleAsync(invitation => invitation.Id == original.Id)).Used);
        var created = await _dbContext.Invitations.AsNoTracking().SingleAsync(invitation => invitation.Id == result.InvitationId);
        Assert.False(created.Used);
        Assert.Equal("alumno@mail.com", created.Email);
        Assert.NotEqual(originalCode, created.Code);
        Assert.InRange(created.ExpiresAt!.Value, before.AddDays(7), after.AddDays(7));
        Assert.Equal(created.ExpiresAt, result.ExpiresAt);

        var sent = Assert.Single(_email.Sent);
        Assert.Equal("alumno@mail.com", sent.To);
        Assert.Contains($"/signup?invitationCode={created.Code}", sent.Body);
    }

    [Fact]
    public async Task Resend_Works_WhenPendingInvitationAlreadyExpired()
    {
        var expired = AddInvitation(_gym, "alumno@mail.com", expiresIn: TimeSpan.FromDays(-1));

        var result = await _service.ResendAsync(GymClientId, expired.Id);

        Assert.NotEqual(expired.Id, result.InvitationId);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task Resend_ThrowsNotFound_WhenInvitationMissingUsedOrFromOtherTenant()
    {
        var used = AddInvitation(_gym, "usada@mail.com", used: true);
        var foreign = AddInvitation(_other, "otro@mail.com");

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ResendAsync(GymClientId, 9999));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ResendAsync(GymClientId, used.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ResendAsync(GymClientId, foreign.Id));

        Assert.Empty(_email.Sent);
    }
}

public sealed class InvitationsApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = Guid.NewGuid().ToString();

    public FakeEmailService Email { get; } = new();
    public FakeDatabaseReadinessCheck ReadinessCheck { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("DatabaseConnectionString", "Server=unused;Database=unused");
        builder.UseSetting("Testing:AllowInMemoryDatabase", "true");
        builder.UseSetting("Release:CommitSha", "untrusted-config-version");
        builder.UseSetting("Jwt:Key", "integration-tests-signing-key-0123456789-abcdef");
        builder.UseSetting("SuperadminBootstrap:Enabled", "false");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            foreach (var descriptor in services
                .Where(d => d.ServiceType.IsGenericType
                    && d.ServiceType.GetGenericArguments().SequenceEqual(new[] { typeof(AppDbContext) })
                    && d.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration"))
                .ToList())
            {
                services.Remove(descriptor);
            }
            services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.RemoveAll<DatabaseReadinessCheck.IChecker>();
            services.AddSingleton<DatabaseReadinessCheck.IChecker>(ReadinessCheck);

            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Email);

            services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            services.Configure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
                options.DefaultForbidScheme = TestAuthHandler.Scheme;
            });
        });
    }

    public void Seed(params Tenant[] tenants)
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.Tenants.AddRange(tenants);
        dbContext.SaveChanges();
    }

    public HttpClient CreateClientFor(string? role, string tenant = "cekide-app")
    {
        var client = CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, role);
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenant);
        }

        return client;
    }
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string Scheme = "Test";
    public const string RolesHeader = "X-Test-Roles";
    public const string TenantHeader = "X-Test-Tenant";
    public const string SubjectHeader = "X-Test-Subject";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var subject = Request.Headers[SubjectHeader].FirstOrDefault() ?? "1";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject),
            new("sub", subject),
            new("tenant", Request.Headers[TenantHeader].ToString())
        };
        claims.AddRange(roles.ToString().Split(',').Select(role => new Claim(ClaimTypes.Role, role)));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class InvitationsApiTests : IClassFixture<InvitationsApiFactory>
{
    private readonly InvitationsApiFactory _factory;

    public InvitationsApiTests(InvitationsApiFactory factory)
    {
        _factory = factory;
        lock (factory)
        {
            using var scope = factory.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!dbContext.Tenants.Any())
            {
                factory.Seed(
                    new Tenant { ClientId = "cekide-app", Slug = "cekide-app", Name = "Cekide", Status = "active", PrimaryDomain = "https://gym.example.com" },
                    new Tenant { ClientId = "other", Slug = "other", Name = "Other", Status = "active" });
            }
        }
    }

    private static string UniqueEmail() => $"{Guid.NewGuid():N}@mail.com";

    private static async Task<long> ReadInvitationIdAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("expiresAt", out _));
        return json.GetProperty("invitationId").GetInt64();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("teacher")]
    public async Task Post_AsOwnerOrTeacher_Returns201AndSendsEmail(string role)
    {
        var email = UniqueEmail();

        var response = await _factory.CreateClientFor(role).PostAsJsonAsync("/api/invitations", new { email });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await ReadInvitationIdAsync(response);
        var sent = Assert.Single(_factory.Email.Sent, message => message.To == email);
        Assert.Matches(new Regex(@"/signup\?invitationCode=[0-9a-f]{32,}"), sent.Body);
    }

    [Fact]
    public async Task Post_AsUser_Returns403()
    {
        var response = await _factory.CreateClientFor("user").PostAsJsonAsync("/api/invitations", new { email = UniqueEmail() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Post_Anonymous_Returns401()
    {
        var response = await _factory.CreateClientFor(null).PostAsJsonAsync("/api/invitations", new { email = UniqueEmail() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithInvalidEmail_Returns400()
    {
        var response = await _factory.CreateClientFor("owner").PostAsJsonAsync("/api/invitations", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_WithPendingInvitation_Returns409()
    {
        var client = _factory.CreateClientFor("teacher");
        var email = UniqueEmail();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/invitations", new { email })).StatusCode);

        var response = await client.PostAsJsonAsync("/api/invitations", new { email });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Get_AsTeacher_ListsPendingInvitationsOfTenant()
    {
        var email = UniqueEmail();
        var otherEmail = UniqueEmail();
        await _factory.CreateClientFor("owner").PostAsJsonAsync("/api/invitations", new { email });
        await _factory.CreateClientFor("owner", "other").PostAsJsonAsync("/api/invitations", new { email = otherEmail });

        var response = await _factory.CreateClientFor("teacher").GetAsync("/api/invitations");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("email").GetString() == email);
        Assert.DoesNotContain(items, item => item.GetProperty("email").GetString() == otherEmail);
        var listed = items.First(item => item.GetProperty("email").GetString() == email);
        Assert.True(listed.TryGetProperty("id", out _));
        Assert.True(listed.TryGetProperty("createdAt", out _));
        Assert.True(listed.TryGetProperty("expiresAt", out _));
    }

    [Fact]
    public async Task Get_AsUser_Returns403()
    {
        var response = await _factory.CreateClientFor("user").GetAsync("/api/invitations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resend_AsOwner_Returns201WithNewInvitation()
    {
        var client = _factory.CreateClientFor("owner");
        var email = UniqueEmail();
        var originalId = await ReadInvitationIdAsync(await client.PostAsJsonAsync("/api/invitations", new { email }));

        var response = await client.PostAsync($"/api/invitations/{originalId}/resend", null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotEqual(originalId, await ReadInvitationIdAsync(response));
        Assert.Equal(2, _factory.Email.Sent.Count(message => message.To == email));
    }

    [Fact]
    public async Task Resend_UnknownInvitation_Returns404()
    {
        var response = await _factory.CreateClientFor("owner").PostAsync("/api/invitations/987654/resend", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resend_AsUser_Returns403()
    {
        var response = await _factory.CreateClientFor("user").PostAsync("/api/invitations/1/resend", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_RemainsAnonymousAndUnchanged()
    {
        var owner = _factory.CreateClientFor("owner");
        var email = UniqueEmail();
        await owner.PostAsJsonAsync("/api/invitations", new { email });
        var code = Regex.Match(_factory.Email.Sent.Last(message => message.To == email).Body, @"invitationCode=([0-9a-f]+)").Groups[1].Value;

        var response = await _factory.CreateClientFor(null).GetAsync($"/api/invitations/{code}/resolve");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, json.GetProperty("email").GetString());
        Assert.Equal("cekide-app", json.GetProperty("tenantSlug").GetString());
    }

    [Fact]
    public async Task Create_and_resolve_preserve_a_typed_gym_student_profile_without_exposing_student_id()
    {
        var client = _factory.CreateClientFor("teacher");
        var email = UniqueEmail();
        var response = await client.PostAsJsonAsync("/api/invitations", new
        {
            email,
            studentProfile = new
            {
                studentId = 123,
                firstName = "Ana María",
                lastName = "de la Cruz",
                fullName = "Ana María de la Cruz",
                phoneNumber = "+541155550000",
                dni = "01.234.567"
            }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var code = Regex.Match(_factory.Email.Sent.Last(message => message.To == email).Body, @"invitationCode=([0-9a-f]+)").Groups[1].Value;
        using var anonymous = _factory.CreateClientFor(null);
        response = await anonymous.GetAsync($"/api/invitations/{code}/resolve");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(email, json.GetProperty("email").GetString());
        Assert.True(json.GetProperty("dniRequired").GetBoolean());
        Assert.False(json.GetProperty("studentProfile").TryGetProperty("studentId", out _));
        Assert.Equal("Ana María", json.GetProperty("studentProfile").GetProperty("firstName").GetString());
        Assert.Equal("01.234.567", json.GetProperty("studentProfile").GetProperty("dni").GetString());
        Assert.Equal("no-store", response.Headers.CacheControl?.NoStore == true ? "no-store" : null);
    }

    [Fact]
    public async Task Non_gym_tenant_cannot_create_a_student_profile_and_error_does_not_echo_pii()
    {
        var email = UniqueEmail();
        var response = await _factory.CreateClientFor("owner", "other").PostAsJsonAsync("/api/invitations", new
        {
            email,
            studentProfile = new
            {
                studentId = 1, firstName = "Private Name", lastName = "Example",
                fullName = "Private Name Example", phoneNumber = "+541155550000", dni = "01234567"
            }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Private Name", body, StringComparison.Ordinal);
        Assert.DoesNotContain("01234567", body, StringComparison.Ordinal);
        Assert.DoesNotContain(_factory.Email.Sent, message => message.To == email);
    }

    [Fact]
    public async Task Resend_without_body_preserves_student_profile_and_requires_its_DNI()
    {
        var client = _factory.CreateClientFor("owner");
        var email = UniqueEmail();
        var created = await client.PostAsJsonAsync("/api/invitations", new
        {
            email,
            studentProfile = new
            {
                studentId = 321, firstName = (string?)null, lastName = (string?)null,
                fullName = "Legacy Student", phoneNumber = (string?)null, dni = (string?)null
            }
        });
        var originalId = await ReadInvitationIdAsync(created);
        var response = await client.PostAsync($"/api/invitations/{originalId}/resend", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var code = Regex.Match(_factory.Email.Sent.Last(message => message.To == email).Body, @"invitationCode=([0-9a-f]+)").Groups[1].Value;
        response = await _factory.CreateClientFor(null).GetAsync($"/api/invitations/{code}/resolve");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("dniRequired").GetBoolean());
        Assert.Equal("Legacy Student", json.GetProperty("studentProfile").GetProperty("fullName").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("studentProfile").GetProperty("dni").ValueKind);
    }

    [Fact]
    public async Task Resend_replaces_email_and_current_profile_and_invalidates_the_previous_code()
    {
        var client = _factory.CreateClientFor("owner");
        var oldEmail = UniqueEmail();
        var newEmail = UniqueEmail();
        var created = await client.PostAsJsonAsync("/api/invitations", new
        {
            email = oldEmail,
            studentProfile = new
            {
                studentId = 654, firstName = "Ana", lastName = "Vieja",
                fullName = "Ana Vieja", phoneNumber = "+541155550000", dni = "01234567"
            }
        });
        var originalId = await ReadInvitationIdAsync(created);
        var oldCode = Regex.Match(_factory.Email.Sent.Last(message => message.To == oldEmail).Body, @"invitationCode=([0-9a-f]+)").Groups[1].Value;

        var response = await client.PostAsJsonAsync($"/api/invitations/{originalId}/resend", new
        {
            email = newEmail,
            studentProfile = new
            {
                studentId = 654, firstName = "Ana María", lastName = "Nueva",
                fullName = "Ana María Nueva", phoneNumber = "+541166660000", dni = "01.234.567"
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains(_factory.Email.Sent, message => message.To == newEmail);
        using var anonymous = _factory.CreateClientFor(null);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/invitations/{oldCode}/resolve")).StatusCode);
        var newCode = Regex.Match(_factory.Email.Sent.Last(message => message.To == newEmail).Body, @"invitationCode=([0-9a-f]+)").Groups[1].Value;
        response = await anonymous.GetAsync($"/api/invitations/{newCode}/resolve");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(newEmail, json.GetProperty("email").GetString());
        Assert.Equal("Ana María", json.GetProperty("studentProfile").GetProperty("firstName").GetString());
        Assert.Equal("01.234.567", json.GetProperty("studentProfile").GetProperty("dni").GetString());
    }
}
