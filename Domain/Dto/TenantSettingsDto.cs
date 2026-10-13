using System.Text.Json;
using System.Text.Json.Serialization;

namespace Domain.Dto
{
    public class TenantSettingsDto
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement> Values { get; set; } = new();
    }
}
