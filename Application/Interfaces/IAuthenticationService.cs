using Domain.Entities;
using Domain.Request;
using Domain.Response;

namespace Application.Interfaces
{
    public record GoogleAuthInfo(string Subject, string? Email, bool EmailVerified, string? Name, string? Picture, string? HostedDomain);

    public enum ResetPasswordResult
    {
        Success,
        InvalidCode,
        CodeExpired,
        CodeAlreadyUsed,
        WeakPassword
    }

    public interface IAuthenticationService
    {
        Task<(Identity identity, OAuthAccount oauth)> UpsertFromGoogleAsync(GoogleAuthInfo info, string clientId);
        Task<AuthResponse> LoginWithPasswordAsync(LoginRequest request, string jwtKey, string? jwtIssuer, string? jwtAudience);
        Task<ResetPasswordResult> ResetPasswordAsync(string code, string newPassword, CancellationToken cancellationToken);
    }
}
