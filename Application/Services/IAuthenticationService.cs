using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Services
{
    public record GoogleAuthInfo(string Subject, string? Email, bool EmailVerified, string? Name, string? Picture, string? HostedDomain);

    public interface IAuthenticationService
    {
        Task<(User user, OAuthAccount oauth)> UpsertFromGoogleAsync(GoogleAuthInfo info);
    }
}
