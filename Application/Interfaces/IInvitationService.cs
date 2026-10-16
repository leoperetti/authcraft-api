using Domain.Dto;

namespace Application.Interfaces
{
    public interface IInvitationService
    {
        Task<ResolvedInvitationDto?> ResolveByCodeAsync(string invitationCode, CancellationToken cancellationToken = default);
    }
}
