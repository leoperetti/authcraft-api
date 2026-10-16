using Application.Interfaces;
using Domain.Dto;
using Infrastructure.Repositories;

namespace Application.Services
{
    public class InvitationService : IInvitationService
    {
        private readonly IInvitationRepository _invitationRepository;

        public InvitationService(IInvitationRepository invitationRepository)
        {
            _invitationRepository = invitationRepository;
        }

        public async Task<ResolvedInvitationDto?> ResolveByCodeAsync(string invitationCode, CancellationToken cancellationToken = default)
        {
            return await _invitationRepository.GetByCodeAsync(invitationCode, cancellationToken);
        }
    }
}
