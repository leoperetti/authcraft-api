namespace authcraft_api.Bootstrap;

public sealed class SuperadminBootstrapOptions
{
    public bool Enabled { get; set; }
    public string TenantSlug { get; set; } = "platform";
    public string TenantClientId { get; set; } = "platform";
    public string TenantName { get; set; } = "Platform";
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FullName { get; set; } = "Super Admin";
}