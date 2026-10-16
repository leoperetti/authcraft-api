using Domain.Dto;

namespace Domain.Response
{
    public class CreateTenantResponse
    {
        public CreateTenantResponse(TenantDto tenant, string apiKey, string? invitationUrl = null)
        {
            Tenant = tenant;
            ApiKey = apiKey;
            InvitationUrl = invitationUrl;
        }

        public TenantDto Tenant { get; set; }
        public string ApiKey { get; set; }
        public string? InvitationUrl { get; set; }
    }
}
