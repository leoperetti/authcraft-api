using Domain.Entities;
using Domain.Request;

namespace Application.Interfaces
{
    public interface IUserService
    {
        Task AssignRolesAsync(long identityId, IEnumerable<string> roles, CancellationToken ct = default);
        Task<IEnumerable<Identity>> GetAllWithRolesAsync(string clientId);
        Task<Identity> GetByIdWithRolesAsync(long id);
        Task<Identity?> CheckIdentityByEmail(ForgotPasswordRequest request);
        Task<string> GenerateResetTokenAsync(Identity user, string clientId, CancellationToken ct);
    }
}
