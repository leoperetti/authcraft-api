using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Repositories
{
    public interface IUserRepository
    {
        Task<User?> GetByEmailAsync(string? email);
        Task AddAsync(User user);
        Task UpdateAsync(User user);
    }
}
