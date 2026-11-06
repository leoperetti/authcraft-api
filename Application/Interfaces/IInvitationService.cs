using Domain.Dto;
using Domain.Request;
using Domain.Response;

namespace Application.Interfaces
{
    public interface IInvitationService
    {
        Task<ResolvedInvitationDto?> ResolveByCodeAsync(string invitationCode, CancellationToken cancellationToken = default);
        Task<CreateInvitationResponse> CreateAsync(string tenantClientId, string email, CancellationToken cancellationToken = default);
        Task<CreateInvitationResponse> CreateAsync(string tenantClientId, string email, StudentInvitationProfileRequest? studentProfile, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<InvitationDto>> ListPendingAsync(string tenantClientId, CancellationToken cancellationToken = default);
        Task<CreateInvitationResponse> ResendAsync(string tenantClientId, long invitationId, CancellationToken cancellationToken = default);
        Task<CreateInvitationResponse> ResendAsync(string tenantClientId, long invitationId, string email, StudentInvitationProfileRequest studentProfile, CancellationToken cancellationToken = default);
        Task<AcceptedInvitationProfileResponse?> GetAcceptedProfileAsync(string tenantClientId, long invitationId, long identityId, CancellationToken cancellationToken = default);
    }
}

