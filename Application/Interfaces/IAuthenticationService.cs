using Domain.Entities;
using Domain.Request;
using Domain.Response;

namespace Application.Interfaces
{
    public record GoogleAuthInfo(string Subject, string? Email, bool EmailVerified, string? Name, string? Picture, string? HostedDomain);

    public interface IAuthenticationService
    {
        Task<(User user, OAuthAccount oauth)> UpsertFromGoogleAsync(GoogleAuthInfo info);
        Task<AuthResponse> LoginWithPasswordAsync(LoginRequest request, string jwtKey, string? jwtIssuer, string? jwtAudience);
    }
}
