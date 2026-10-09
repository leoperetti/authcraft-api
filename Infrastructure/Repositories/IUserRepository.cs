using System.Threading.Tasks;
using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByEmailAsync(string? email);
        Task AddAsync(User user);
        Task UpdateAsync(User user);
        Task<User?> GetByIdAsync(long userId);
        Task<IList<User>> GetAllAsync();
        Task<IList<User>> GetAllWithRolesAsync();
        Task<User?> GetByIdWithRolesAsync(long id);
    }
}
