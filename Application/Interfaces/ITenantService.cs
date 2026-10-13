using Domain.Dto;
using Domain.Request;
using Domain.Response;

namespace Application.Interfaces
{
    public interface ITenantService
    {
        Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default);
        Task<TenantDto?> GetAsync(long id, CancellationToken cancellationToken = default);
        Task<CreateTenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default);
        Task<TenantDto?> UpdateAsync(long id, UpdateTenantRequest request, CancellationToken cancellationToken = default);
        Task<bool> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
    }
}
