using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Database;

namespace Trovesuite.Package.Tenancy;

/// <summary>
/// Host -> route lookup, cached per process.
/// </summary>
/// <remarks>
/// This runs before anything else on every single request, so it has to be cheap
/// and it has to be impossible to get wrong in an interesting way. Three
/// decisions carry most of that weight.
///
/// <para><b>Normalisation happens once, here.</b> A host arrives with a port,
/// with mixed case, sometimes with a trailing dot. If normalisation lived at the
/// call sites there would eventually be two versions of it and a lookup that
/// misses on a capital letter. The seeded rows are stored in exactly the form
/// <see cref="NormaliseHost"/> produces.</para>
///
/// <para><b>Unknown hosts are cached too, briefly.</b> See
/// <see cref="TenancyOptions.NegativeCacheTtlSeconds"/>.</para>
///
/// <para><b>A missing table is not an error.</b> This ships in the shared
/// package, and the package can reach a deployment before the migration that
/// creates the table does. If the lookup threw, every request would 500 during
/// that window. It returns null instead and says so once. Safe only because
/// nothing routes on the result yet; the moment something does,
/// <see cref="TenancyOptions.Enforce"/> turns an unresolved host into a refusal
/// rather than a shrug.</para>
/// </remarks>
public sealed class TenantRouteResolver : ITenantRouteResolver
{
    /// <summary>How far up the domain to look for a wildcard parent. See <see cref="Parents"/>.</summary>
    private const int MaxParents = 10;

    private const string Columns =
        "host, tenant_id, tier, cell_key, db_server_fqdn, db_name, db_secret_uri, " +
        "storage_account, container_prefix, storage_secret_uri, api_base, status, " +
        "schema_version, is_wildcard";

    private readonly IDatabaseManager _database;
    private readonly TenancyOptions _options;
    private readonly ILogger<TenantRouteResolver> _logger;

    /// <summary>host -> (route or null, expiry). Null values are cached misses.</summary>
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    /// <summary>Set once the table has been found unreadable, so the warning is
    /// logged once per process rather than once per request.</summary>
    private int _tableMissingLogged;

    public TenantRouteResolver(
        IDatabaseManager database,
        IOptions<TrovesuiteOptions> options,
        ILogger<TenantRouteResolver> logger)
    {
        _database = database;
        _options = options.Value.Tenancy;
        _logger = logger;
    }

    /// <summary>
    /// Reduce a Host header to the form route rows are stored in: lower-cased,
    /// no port, no trailing dot, trimmed.
    /// </summary>
    /// <remarks>
    /// IPv6 literals keep their brackets, which is what distinguishes
    /// <c>[::1]:8000</c>'s port colon from its address colons.
    /// </remarks>
    public static string NormaliseHost(string? value)
    {
        var host = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (host.Length == 0) return string.Empty;

        if (host[0] == '[')
        {
            var closing = host.IndexOf(']');
            if (closing != -1) host = host[..(closing + 1)];
        }
        else
        {
            var colon = host.IndexOf(':');
            if (colon != -1) host = host[..colon];
        }

        return host.TrimEnd('.');
    }

    /// <summary>
    /// <paramref name="host"/>, then each parent domain that still has a dot in it.
    /// </summary>
    /// <remarks>
    /// <c>deladetech.dev.zeloshr.com</c> gives deladetech.dev.zeloshr.com,
    /// dev.zeloshr.com, zeloshr.com. It stops before the single-label tail for the
    /// same reason the database refuses to flag one as a wildcard: <c>com</c> is not
    /// a domain anybody owns, and a row for it would claim the entire internet.
    ///
    /// Capped, because the list goes into a query and the Host header is
    /// attacker-controlled. A legal hostname cannot have many more labels than this
    /// anyway; the cap is here so a crafted one cannot make the list interesting.
    /// </remarks>
    public static List<string> Parents(string host)
    {
        var out_ = new List<string> { host };
        var rest = host;
        while (out_.Count <= MaxParents)
        {
            var dot = rest.IndexOf('.');
            if (dot < 0) break;
            rest = rest[(dot + 1)..];
            if (!rest.Contains('.')) break;
            out_.Add(rest);
        }
        return out_;
    }

    public async Task<TenantRoute?> ResolveAsync(string? host, CancellationToken cancellationToken = default)
    {
        var key = NormaliseHost(host);
        if (key.Length == 0) return null;

        var now = DateTime.UtcNow;
        if (_cache.TryGetValue(key, out var cached) && cached.ExpiresAt > now)
            return cached.Route;

        var route = await FetchAsync(key, cancellationToken).ConfigureAwait(false);

        var ttl = route is not null
            ? Math.Max(1, _options.CacheTtlSeconds)
            : Math.Max(1, _options.NegativeCacheTtlSeconds);
        _cache[key] = new CacheEntry(route, DateTime.UtcNow.AddSeconds(ttl));

        return route;
    }

    /// <remarks>
    /// Only clears THIS process: other replicas wait out their own TTL, which is
    /// why the TTL is a minute and not an hour.
    /// </remarks>
    public void Invalidate(string? host = null)
    {
        if (host is null) _cache.Clear();
        else _cache.TryRemove(NormaliseHost(host), out _);
    }

    /// <remarks>
    /// Uncached on purpose: a timer job runs every few minutes and wants the
    /// current list, not a minute-old one, and it is one query per run.
    /// </remarks>
    public async Task<IReadOnlyList<TenantRoute>> ActiveRoutesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using (TenantContext.TenantlessRead())
            {
                var rows = await _database.ExecuteQueryAsync(
                    $"SELECT {Columns} FROM {_options.RoutesTable} " +
                    "WHERE status = 'ACTIVE' ORDER BY host",
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return rows.Select(row => ToRoute(row)).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list tenant routes");
            return Array.Empty<TenantRoute>();
        }
    }

    /// <summary>
    /// The row that claims <paramref name="host"/>: its own, or a parent that claims
    /// its subdomains.
    /// </summary>
    /// <remarks>
    /// One query, not two. The candidate list is the host plus its parent domains,
    /// and the ordering puts the longest match first — which is the exact row when
    /// there is one, so an exact row always beats a wildcard and a tenant moving to
    /// its own database needs nothing but that row.
    ///
    /// <c>host = ANY(...)</c> rather than <c>LIKE '%.' || host</c>: a stored host
    /// could contain an underscore, which LIKE reads as "any character", and
    /// <c>foo_bar.com</c> would then answer for <c>fooXbar.com</c>. An equality check
    /// against a short list cannot be tricked that way, and it uses the primary key.
    /// </remarks>
    private async Task<TenantRoute?> FetchAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            // Tenant-less on purpose: this read is what DECIDES the tenant, so it
            // cannot have one. Marked so it is not reported as an omission — and
            // it would otherwise be the loudest entry in its own list.
            using (TenantContext.TenantlessRead())
            {
                var rows = await _database.ExecuteQueryAsync(
                    $"SELECT {Columns} FROM {_options.RoutesTable} " +
                    "WHERE host = ANY(@candidates) AND (host = @host OR is_wildcard) " +
                    "ORDER BY length(host) DESC LIMIT 1",
                    new { candidates = Parents(host), host },
                    cancellationToken).ConfigureAwait(false);
                return rows.Count == 0 ? null : ToRoute(rows[0], host);
            }
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _tableMissingLogged, 1) == 0)
            {
                _logger.LogWarning(
                    ex,
                    "Tenant route lookup failed. Treating every host as unresolved. " +
                    "If the control-plane migration has not been applied yet, or this " +
                    "app's database role has no USAGE on {Table}'s schema, this is " +
                    "expected; once both are in place, it is a fault.",
                    _options.RoutesTable);
            }
            return null;
        }
    }

    /// <summary>
    /// Mapped by hand from the dictionary form rather than through Dapper's
    /// object mapper, which does not match <c>db_server_fqdn</c> to
    /// <c>DbServerFqdn</c> unless a global static is flipped. Doing it here keeps
    /// the package from depending on a setting a consumer could change.
    /// </summary>
    private static TenantRoute ToRoute(IDictionary<string, object?> row, string? requestedHost = null)
    {
        return new TenantRoute
        {
            Host = Str(row, "host") ?? string.Empty,
            TenantId = Str(row, "tenant_id"),
            Tier = Str(row, "tier") ?? TenantTier.Pooled,
            CellKey = Str(row, "cell_key") ?? string.Empty,
            Status = Str(row, "status") ?? "ACTIVE",
            DbServerFqdn = Str(row, "db_server_fqdn"),
            DbName = Str(row, "db_name"),
            DbSecretUri = Str(row, "db_secret_uri"),
            StorageAccount = Str(row, "storage_account"),
            ContainerPrefix = Str(row, "container_prefix"),
            StorageSecretUri = Str(row, "storage_secret_uri"),
            ApiBase = Str(row, "api_base"),
            SchemaVersion = Str(row, "schema_version"),
            IsWildcard = row.TryGetValue("is_wildcard", out var wild)
                         && wild is bool b && b,
            RequestedHost = requestedHost ?? Str(row, "host"),
        };
    }

    private static string? Str(IDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null and not DBNull
            ? value.ToString()
            : null;

    private readonly record struct CacheEntry(TenantRoute? Route, DateTime ExpiresAt);
}
