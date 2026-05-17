using System.Text.Json.Serialization;

namespace Trovesuite.Package.Auth;

public class AuthServiceReadDto
{
    [JsonPropertyName("org_id")]
    public string? OrgId { get; set; }

    [JsonPropertyName("bus_id")]
    public string? BusId { get; set; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; set; }

    [JsonPropertyName("shared_resource_id")]
    public string? SharedResourceId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("group_id")]
    public string? GroupId { get; set; }

    [JsonPropertyName("role_id")]
    public string? RoleId { get; set; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; set; }

    [JsonPropertyName("permissions")]
    public List<string>? Permissions { get; set; }

    [JsonPropertyName("resource_id")]
    public string? ResourceId { get; set; }

    [JsonPropertyName("resource_type")]
    public string? ResourceType { get; set; }
}
