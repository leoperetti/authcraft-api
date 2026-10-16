using Domain.Dto;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class InvitationRepository : IInvitationRepository
    {
        private readonly AppDbContext _dbContext;

        public InvitationRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task AddAsync(Invitation invitation, CancellationToken cancellationToken = default)
        {
            _dbContext.Invitations.Add(invitation);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> HasPendingAsync(long tenantId, string email, CancellationToken cancellationToken = default)
        {
            if (tenantId <= 0 || string.IsNullOrWhiteSpace(email))
            {
                return false;
            }

            var normalizedEmail = email.Trim();
            var now = DateTimeOffset.UtcNow;

            return await _dbContext.Invitations.AnyAsync(invitation =>
                invitation.TenantId == tenantId
                && invitation.Email == normalizedEmail
                && !invitation.Used
                && (!invitation.ExpiresAt.HasValue || invitation.ExpiresAt > now), cancellationToken);
        }

        public async Task<ResolvedInvitationDto?> GetByCodeAsync(string invitationCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(invitationCode))
            {
                return null;
            }

            var normalizedInvitationCode = invitationCode.Trim();

            return await _dbContext.Invitations
                .AsNoTracking()
                .Where(invitation => invitation.Code == normalizedInvitationCode)
                .Select(invitation => new ResolvedInvitationDto
                {
                    Email = invitation.Email,
                    ExpiresAt = invitation.ExpiresAt,
                    Used = invitation.Used,
                    TenantName = invitation.Tenant.Name,
                    TenantSlug = invitation.Tenant.Slug
                })
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
