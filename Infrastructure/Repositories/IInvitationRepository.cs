using Domain.Dto;
using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface IInvitationRepository
    {
        Task AddAsync(Invitation invitation, CancellationToken cancellationToken = default);
        Task<bool> HasPendingAsync(long tenantId, string email, CancellationToken cancellationToken = default);
        Task<ResolvedInvitationDto?> GetByCodeAsync(string invitationCode, CancellationToken cancellationToken = default);
    }
}
