using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public interface IPasswordResetTokenRepository
    {
        void AddToken(PasswordResetToken token);
        Task<bool> HasActiveResetToken(long id);
        Task<PasswordResetToken?> GetByCodeAsync(string code, CancellationToken cancellationToken);
        Task UpdateTokenAsync(PasswordResetToken token, CancellationToken cancellationToken);
    }

    public class PasswordResetTokenRepository : IPasswordResetTokenRepository
    {
        private readonly AppDbContext _db;
        public PasswordResetTokenRepository(AppDbContext db) => _db = db;

        public void AddToken(PasswordResetToken token)
        {
            _db.PasswordResetTokens.Add(token);
        }

        public async Task<bool> HasActiveResetToken(long id)
        {
            return await _db.PasswordResetTokens.AnyAsync(x => x.IdentityId == id && !x.Used && x.ExpiresAt > DateTimeOffset.UtcNow);
        }

        public async Task<PasswordResetToken?> GetByCodeAsync(string code, CancellationToken cancellationToken)
        {
            return await _db.PasswordResetTokens.FirstOrDefaultAsync(passwordResetToken => passwordResetToken.Code == code, cancellationToken);
        }

        public Task UpdateTokenAsync(PasswordResetToken token, CancellationToken cancellationToken)
        {
            _db.PasswordResetTokens.Update(token);
            return Task.CompletedTask;
        }
    }
}
