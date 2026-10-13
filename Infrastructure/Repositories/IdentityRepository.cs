using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class IdentityRepository : IIdentityRepository
    {
        private readonly AppDbContext _db;
        public IdentityRepository(AppDbContext db) => _db = db;

        public async Task<Identity?> GetByEmailAsync(string? email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            return await _db.Identities.SingleOrDefaultAsync(identity => identity.Email == email);
        }

        public async Task<Identity?> GetByEmailWithMembershipsAsync(string? email)
        {
            if (string.IsNullOrEmpty(email)) return null;
            return await _db.Identities
                .Include(identity => identity.Memberships)
                    .ThenInclude(membership => membership.Tenant)
                .SingleOrDefaultAsync(identity => identity.Email == email);
        }

        public Task AddAsync(Identity identity)
        {
            _db.Identities.Add(identity);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Identity identity)
        {
            _db.Identities.Update(identity);
            return Task.CompletedTask;
        }

        public async Task<Identity?> GetByIdAsync(long id)
        {
            return await _db.Identities.SingleOrDefaultAsync(identity => identity.Id == id);
        }

        public async Task<IList<Identity>> GetAllAsync()
        {
            return await _db.Identities.ToListAsync();
        }

        public async Task<IList<Identity>> GetAllWithRolesAsync()
        {
            return await _db.Identities
                .Include(identity => identity.Memberships)
                    .ThenInclude(membership => membership.Role)
                .ToListAsync();
        }

        public async Task<Identity?> GetByIdWithRolesAsync(long id)
        {
            return await _db.Identities
                .Include(identity => identity.Memberships)
                    .ThenInclude(membership => membership.Role)
                .SingleOrDefaultAsync(identity => identity.Id == id);
        }
    }
}
