using System.Text.Json.Serialization;

namespace Trovesuite.Package.Entities;

public class Respons<T>
{
    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("data")]
    public List<T>? Data { get; set; }

    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; } = 200;

    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("pagination")]
    public PaginationMeta? Pagination { get; set; }

    public static Respons<T> Ok(IEnumerable<T>? data = null, string? detail = null, int statusCode = 200, PaginationMeta? pagination = null) => new()
    {
        Detail = detail,
        Data = data is null ? new List<T>() : new List<T>(data),
        Success = true,
        StatusCode = statusCode,
        Pagination = pagination,
    };

    public static Respons<T> Fail(string error, string? detail = null, int statusCode = 500, IEnumerable<T>? data = null) => new()
    {
        Detail = detail,
        Error = error,
        Data = data is null ? new List<T>() : new List<T>(data),
        Success = false,
        StatusCode = statusCode,
    };
}
