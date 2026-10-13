namespace Domain.Dto
{
    public class TenantDto
    {
        public long Id { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public TenantSettingsDto? Settings { get; set; }
        public IReadOnlyList<string> AllowedOrigins { get; set; } = Array.Empty<string>();
    }
}
