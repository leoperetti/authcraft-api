using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _db;
        public UserRepository(AppDbContext db) => _db = db;

        public async Task<User?> GetByEmailAsync(string? email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            return await _db.Users.SingleOrDefaultAsync(u => u.Email == email);
        }

        public async Task AddAsync(User user)
        {
            _db.Users.Add(user);
            await _db.SaveChangesAsync();
        }

        public async Task UpdateAsync(User user)
        {
            _db.Users.Update(user);
            await _db.SaveChangesAsync();
        }

        public async Task<User?> GetByIdAsync(long id)
        {
            return await _db.Users.SingleOrDefaultAsync(u => u.Id == id);
        }

        public async Task<IList<User>> GetAllAsync()
        {
            return await _db.Users.ToListAsync();
        }

        public async Task<IList<User>> GetAllWithRolesAsync()
        {
            return await _db.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .ToListAsync();
        }

        public async Task<User?> GetByIdWithRolesAsync(long id)
        {
            return await _db.Users
                .Include(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                .SingleOrDefaultAsync(u => u.Id == id);
        }
    }
}
