using Domain.Dto;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public interface IInvitationRepository
    {
        Task AddAsync(Invitation invitation, CancellationToken cancellationToken = default);
        Task<bool> HasPendingAsync(long tenantId, string email, CancellationToken cancellationToken = default);
        Task<ResolvedInvitationDto?> GetByCodeAsync(string invitationCode, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<InvitationDto>> ListPendingAsync(long tenantId, CancellationToken cancellationToken = default);
        Task<Invitation?> GetUnusedByIdAsync(long tenantId, long invitationId, CancellationToken cancellationToken = default);
        Task<Invitation?> GetAcceptedByIdAsync(long tenantId, long invitationId, long identityId, CancellationToken cancellationToken = default);
    }

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

        public async Task<IReadOnlyList<InvitationDto>> ListPendingAsync(long tenantId, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;

            var pending = await _dbContext.Invitations
                .AsNoTracking()
                .Where(invitation => invitation.TenantId == tenantId
                    && !invitation.Used
                    && (!invitation.ExpiresAt.HasValue || invitation.ExpiresAt > now))
                .ToListAsync(cancellationToken);

            return pending
                .OrderByDescending(invitation => invitation.CreatedAt)
                .Select(invitation => new InvitationDto
                {
                    Id = invitation.Id,
                    Email = invitation.Email,
                    CreatedAt = invitation.CreatedAt,
                    ExpiresAt = invitation.ExpiresAt
                })
                .ToList();
        }

        public async Task<Invitation?> GetUnusedByIdAsync(long tenantId, long invitationId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Invitations.FirstOrDefaultAsync(invitation =>
                invitation.Id == invitationId
                && invitation.TenantId == tenantId
                && !invitation.Used, cancellationToken);
        }

        public async Task<Invitation?> GetAcceptedByIdAsync(long tenantId, long invitationId, long identityId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Invitations
                .AsNoTracking()
                .SingleOrDefaultAsync(invitation =>
                    invitation.Id == invitationId
                    && invitation.TenantId == tenantId
                    && invitation.Purpose == "gym_student"
                    && invitation.Used
                    && invitation.AcceptedIdentityId == identityId
                    && invitation.AcceptedAt != null,
                    cancellationToken);
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
                    TenantSlug = invitation.Tenant.Slug,
                    StudentProfile = invitation.Purpose == "gym_student"
                        ? new Domain.Response.ResolvedStudentInvitationProfileResponse
                        {
                            FirstName = invitation.FirstName,
                            LastName = invitation.LastName,
                            FullName = invitation.FullName ?? string.Empty,
                            PhoneNumber = invitation.PhoneNumber,
                            Dni = invitation.Dni
                        }
                        : null,
                    DniRequired = invitation.Purpose == "gym_student"
                })
                .FirstOrDefaultAsync(cancellationToken);
        }
    }
}
