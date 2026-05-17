using System.Text.Json.Serialization;

namespace Trovesuite.Package.Auth;

public class AuthServiceWriteDto
{
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; set; }
}
