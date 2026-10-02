using Npgsql;

namespace Trovesuite.Package.Tenancy;

/// <summary>
/// Turns a libpq <c>postgresql://</c> URI into a connection string Npgsql accepts.
/// </summary>
/// <remarks>
/// Every <c>db-url-*</c> secret in Key Vault is a libpq URI, because that is what
/// psycopg2 takes natively and the Python apps read the same secrets. Npgsql does
/// NOT take one: <c>NpgsqlDataSourceBuilder</c> wants keyword/value pairs, and
/// handed a URI it reads the whole thing as one keyword and throws
///
///     ArgumentException: Couldn't set postgresql://... ---> KeyNotFoundException
///
/// which is how the first silo request to a .NET app failed. A keyword/value
/// string is passed through untouched, so this is safe to apply to anything.
///
/// The two ZelosHR apps each carry their own copy of this for their own
/// configured connection string; this one is the package's, for the silo
/// credentials the package resolves itself.
/// </remarks>
internal static class PostgresUri
{
    internal static bool LooksLikeUri(string value) =>
        value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    /// <summary>A connection string Npgsql understands, whichever form came in.</summary>
    internal static string Normalize(string connectionString)
    {
        var trimmed = connectionString.Trim().Trim('"', '\'');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Connection string is empty.", nameof(connectionString));
        }

        return LooksLikeUri(trimmed)
            ? ParseUri(trimmed).ConnectionString
            : new NpgsqlConnectionStringBuilder(trimmed).ConnectionString;
    }

    private static NpgsqlConnectionStringBuilder ParseUri(string uriString)
    {
        var uri = new Uri(uriString);
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = uri.AbsolutePath.TrimStart('/'),
        };

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var colon = uri.UserInfo.IndexOf(':');
            if (colon >= 0)
            {
                builder.Username = Uri.UnescapeDataString(uri.UserInfo[..colon]);
                builder.Password = Uri.UnescapeDataString(uri.UserInfo[(colon + 1)..]);
            }
            else
            {
                builder.Username = Uri.UnescapeDataString(uri.UserInfo);
            }
        }

        // sslmode=require is on every one of these secrets, and dropping it would
        // make the connection fail against a server that demands TLS.
        foreach (var segment in uri.Query.TrimStart('?')
                     .Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = segment.IndexOf('=');
            var key = Uri.UnescapeDataString(equals >= 0 ? segment[..equals] : segment);
            var value = equals >= 0 ? Uri.UnescapeDataString(segment[(equals + 1)..]) : string.Empty;

            switch (key.ToLowerInvariant())
            {
                case "sslmode" when Enum.TryParse<SslMode>(value, ignoreCase: true, out var sslMode):
                    builder.SslMode = sslMode;
                    break;
                case "ssl" when string.Equals(value, "true", StringComparison.OrdinalIgnoreCase):
                    builder.SslMode = SslMode.Require;
                    break;
            }
        }

        return builder;
    }
}
