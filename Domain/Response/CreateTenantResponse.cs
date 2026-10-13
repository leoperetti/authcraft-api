using Domain.Dto;

namespace Domain.Response
{
    public class CreateTenantResponse
    {
        public CreateTenantResponse(TenantDto tenant, string apiKey)
        {
            Tenant = tenant;
            ApiKey = apiKey;
        }

        public TenantDto Tenant { get; set; }
        public string ApiKey { get; set; }
    }
}
