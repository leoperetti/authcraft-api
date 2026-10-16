using Domain.Entities;

namespace Application.Interfaces
{
    public interface IUserService
    {
        Task AssignRolesAsync(long identityId, IEnumerable<string> roles, CancellationToken ct = default);
        Task<IEnumerable<Identity>> GetAllWithRolesAsync(string clientId);
        Task<Identity> GetByIdWithRolesAsync(long id);
    }
}
