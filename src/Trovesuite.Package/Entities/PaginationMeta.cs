using System.Text.Json.Serialization;

namespace Trovesuite.Package.Entities;

public sealed class PaginationMeta
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("has_next")]
    public bool HasNext { get; set; }
}
