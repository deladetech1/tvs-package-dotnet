namespace Trovesuite.Package.Entities;

public sealed class ResponseException : Exception
{
    public Respons<object> Response { get; }

    public ResponseException(string message, Respons<object> response) : base(message)
    {
        Response = response;
    }

    public static void RaiseWithResponse(string message, int statusCode = 500, string? details = "An error occurred", IEnumerable<object>? data = null)
    {
        var response = new Respons<object>
        {
            Detail = details,
            Error = message,
            Data = data is null ? new List<object>() : new List<object>(data),
            StatusCode = statusCode,
            Success = false,
        };
        throw new ResponseException(message, response);
    }
}
