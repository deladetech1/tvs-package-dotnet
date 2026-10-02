using System.Collections.Concurrent;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Trovesuite.Package.Tenancy;

/// <summary>
/// A tenant's own database could not be reached.
/// </summary>
/// <remarks>
/// Deliberately not a fallback. Serving the request from the pod's own database
/// would be a cross-tenant read dressed up as success — a Key Vault blip, a
/// rotated password or a firewall rule would quietly hand one tenant another's
/// data, and nothing in the logs would look wrong. Every other failure mode in
/// this file is preferable to that one.
/// </remarks>
public sealed class SiloUnavailableException : InvalidOperationException
{
    public SiloUnavailableException(string message) : base(message) { }
    public SiloUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// One Npgsql data source per silo credential, built on first use and kept.
/// </summary>
/// <remarks>
/// The .NET half of what <c>trovesuite.tenancy.connections</c> does for the
/// Python apps, and it exists for the same reason: resolution decided WHICH
/// tenant a request belonged to and the connection went to the pod's configured
/// database anyway. That was correct while every route was POOLED and there was
/// only one database. It stopped being correct the moment a silo existed.
///
/// Keyed on the secret reference, not the host: several hosts can address one
/// silo, and one host must not end up with several pools.
///
/// **Each app resolves its OWN credential.** A silo has a login role per app, so
/// the route row names the silo and the app names itself:
///
///     db-url-&lt;app-slug&gt;-&lt;silo_key&gt;
///
/// which is the pooled <c>db-url-&lt;app-slug&gt;</c> plus a suffix. So a leaked
/// credential is one app's access to one tenant, rather than every app's to all
/// of them. A row carrying a single <c>DbSecretUri</c> is still honoured, for a
/// silo whose apps share one login.
///
/// The slug has to be configured, not derived. Nothing in a running container
/// spells it the same way the secret does — the ZelosHR apps do not set an app
/// name at all, and core-platform's is "core-platform" where its secret says
/// "coreplatform" — so the IaC that names the secret also supplies
/// <c>Trovesuite:Tenancy:DbSecretPrefix</c>.
/// </remarks>
public sealed class SiloDataSources : IAsyncDisposable
{
    private readonly TenancyOptions _options;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, NpgsqlDataSource> _sources = new();

    /// <summary>Reads a secret's value. Overridable so tests need no vault.</summary>
    private readonly Func<string, string, CancellationToken, Task<string>> _readSecret;

    public SiloDataSources(TenancyOptions options, ILogger logger,
        Func<string, string, CancellationToken, Task<string>>? readSecret = null)
    {
        _options = options;
        _logger = logger;
        _readSecret = readSecret ?? ReadFromKeyVaultAsync;
    }

    /// <summary>How many silo databases this process currently holds a source for.</summary>
    public int OpenSources => _sources.Count;

    /// <summary>
    /// The Key Vault URI of the credential THIS app should use for <paramref name="route"/>.
    /// </summary>
    /// <remarks>
    /// A row with its own secret URI is taken at its word. Otherwise the name is
    /// composed, and both halves must be configured — a missing one throws rather
    /// than falling back, because the fallback is the shared database.
    /// </remarks>
    public string SecretReference(TenantRoute route)
    {
        if (!string.IsNullOrWhiteSpace(route.DbSecretUri)) return route.DbSecretUri!;

        if (string.IsNullOrWhiteSpace(route.SiloKey))
        {
            // ck_ctl_routes_silo_has_db refuses a silo row with neither, so
            // reaching here means the row was written around the constraint or
            // its tier was changed in place.
            throw new SiloUnavailableException(
                $"Route '{route.Host}' is {route.Tier} but names neither a " +
                "db_secret_uri nor a silo_key");
        }

        var prefix = _options.DbSecretPrefix?.Trim();
        var vault = _options.KeyVaultUri?.Trim().TrimEnd('/');
        var missing = new List<string>();
        if (string.IsNullOrEmpty(prefix)) missing.Add("Trovesuite:Tenancy:DbSecretPrefix");
        if (string.IsNullOrEmpty(vault)) missing.Add("Trovesuite:Tenancy:KeyVaultUri");
        if (missing.Count > 0)
        {
            throw new SiloUnavailableException(
                $"Route '{route.Host}' is silo '{route.SiloKey}', so this app must " +
                $"resolve its own credential, but {string.Join(" and ", missing)} " +
                "is not set. Serving it from the pooled database would be a " +
                "cross-tenant read.");
        }

        return $"{vault}/secrets/{prefix}-{route.SiloKey}";
    }

    /// <summary>
    /// The data source for <paramref name="route"/>'s own database, or null if it has none.
    /// </summary>
    /// <remarks>
    /// Null means POOLED: the caller should use the pod's own data source, which
    /// is the right answer rather than a missing one.
    /// </remarks>
    public async Task<NpgsqlDataSource?> ForRouteAsync(TenantRoute? route, CancellationToken cancellationToken = default)
    {
        if (route is null || !route.HasOwnDatabase) return null;

        var reference = SecretReference(route);
        if (_sources.TryGetValue(reference, out var existing)) return existing;

        var dsn = await FetchDsnAsync(reference, cancellationToken).ConfigureAwait(false);

        NpgsqlDataSource built;
        try
        {
            // The secret is a libpq URI, which Npgsql does not accept. See
            // PostgresUri: handed one raw it reads the whole string as a single
            // keyword and throws, which is how the first silo request failed.
            built = new NpgsqlDataSourceBuilder(PostgresUri.Normalize(dsn)).Build();
        }
        catch (Exception ex)
        {
            // Neither ex.Message NOR ex as the inner exception.
            //
            // Npgsql puts the offending connection string in its
            // ArgumentException, so the first silo request printed this tenant's
            // database password into the container log. Keeping the inner
            // exception does not help: the middleware logs the exception, and
            // logging an exception walks the whole chain, so the inner message
            // is published either way.
            //
            // The type name and the route are what anyone debugging needs, and
            // neither carries a credential.
            throw new SiloUnavailableException(
                $"Could not open '{route.DbName}' on '{route.DbServerFqdn}' for " +
                $"'{route.Host}': {ex.GetType().Name}");
        }

        // Another request may have won the race; keep theirs and dispose ours
        // rather than leaking a second pool against the same database.
        var winner = _sources.GetOrAdd(reference, built);
        if (!ReferenceEquals(winner, built))
        {
            await built.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "Opened a data source for {Host} ({Database} on {Server})",
                route.Host, route.DbName, route.DbServerFqdn ?? "its own server");
        }
        return winner;
    }

    /// <summary>
    /// The already-built data source for <paramref name="route"/>, without any I/O.
    /// </summary>
    /// <remarks>
    /// For the paths that cannot await: EF Core's options factory and the apps'
    /// own synchronous connection helpers. Those run per request, after the
    /// middleware, so <see cref="PrepareAsync"/> has already resolved the
    /// credential and this is a dictionary lookup.
    ///
    /// Returns null both for "pooled" and for "not prepared", which the caller
    /// has to tell apart — a silo route with nothing cached must be refused, not
    /// served from the pod's database. <see cref="TryGetPrepared"/> says which.
    /// </remarks>
    public bool TryGetPrepared(TenantRoute? route, out NpgsqlDataSource? source)
    {
        source = null;
        if (route is null || !route.HasOwnDatabase) return true;  // pooled: null is the answer

        string reference;
        try { reference = SecretReference(route); }
        catch (SiloUnavailableException) { return false; }

        return _sources.TryGetValue(reference, out source);
    }

    /// <summary>
    /// Resolve and open this route's credential now, so later synchronous paths can use it.
    /// </summary>
    /// <remarks>
    /// Called once per request by the middleware. A no-op for a pooled route, and
    /// a dictionary hit for a silo already seen. It is deliberately the middleware
    /// that pays this cost: EF builds its DbContext options synchronously, and a
    /// credential that can only be read asynchronously has to be in hand before
    /// then or the only options are blocking the thread or serving the wrong
    /// database.
    /// </remarks>
    public Task PrepareAsync(TenantRoute? route, CancellationToken cancellationToken = default)
        => route is null || !route.HasOwnDatabase
            ? Task.CompletedTask
            : ForRouteAsync(route, cancellationToken);

    /// <summary>
    /// Drop the data source for <paramref name="route"/>, so the next request re-reads its credential.
    /// </summary>
    /// <remarks>
    /// Needed because a composed secret reference carries no VERSION, unlike the
    /// versioned URIs rows used to hold: the name is unchanged across a rotation,
    /// so nothing about the key changes and the stale pool would be reused with
    /// the old password until the process restarted. Call this when a silo
    /// connection fails to authenticate.
    /// </remarks>
    public async Task<bool> ForgetAsync(TenantRoute route)
    {
        string reference;
        try { reference = SecretReference(route); }
        catch (SiloUnavailableException) { return false; }

        if (!_sources.TryRemove(reference, out var source)) return false;
        await source.DisposeAsync().ConfigureAwait(false);
        _logger.LogInformation(
            "Dropped the data source for {Host}; its credential will be re-read", route.Host);
        return true;
    }

    private async Task<string> FetchDsnAsync(string secretUri, CancellationToken cancellationToken)
    {
        // https://<vault>.vault.azure.net/secrets/<name>[/<version>]
        string vaultUrl, name, version;
        try
        {
            var uri = new Uri(secretUri);
            var parts = uri.AbsolutePath.Trim('/').Split('/');
            name = parts[1];
            version = parts.Length > 2 ? parts[2] : string.Empty;
            vaultUrl = $"{uri.Scheme}://{uri.Host}";
        }
        catch (Exception ex)
        {
            throw new SiloUnavailableException($"Malformed secret URI '{secretUri}'", ex);
        }

        var value = await _readSecret(vaultUrl, version.Length > 0 ? $"{name}/{version}" : name, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SiloUnavailableException($"{name} in {vaultUrl} is empty");
        }
        return value;
    }

    /// <summary>
    /// Reads a secret with the pod's managed identity.
    /// </summary>
    /// <remarks>
    /// The route row names the secret; the identity is what is allowed to read it.
    /// That split is why the route table can be readable to every app without
    /// being worth stealing: a leaked row says where a credential lives, not what
    /// it is.
    /// </remarks>
    private static async Task<string> ReadFromKeyVaultAsync(string vaultUrl, string nameAndVersion, CancellationToken cancellationToken)
    {
        var slash = nameAndVersion.IndexOf('/');
        var name = slash < 0 ? nameAndVersion : nameAndVersion[..slash];
        var version = slash < 0 ? null : nameAndVersion[(slash + 1)..];
        try
        {
            var client = new SecretClient(new Uri(vaultUrl), new DefaultAzureCredential());
            var secret = await client.GetSecretAsync(name, version, cancellationToken).ConfigureAwait(false);
            return secret.Value.Value;
        }
        catch (Exception ex)
        {
            throw new SiloUnavailableException($"Could not read {name} from {vaultUrl}: {ex.Message}", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var key in _sources.Keys)
        {
            if (_sources.TryRemove(key, out var source))
            {
                await source.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
