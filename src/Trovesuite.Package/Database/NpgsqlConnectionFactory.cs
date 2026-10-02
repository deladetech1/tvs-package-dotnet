using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Tenancy;

namespace Trovesuite.Package.Database;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlConnectionFactory> _logger;
    private readonly bool _reportUnscoped;
    private readonly SiloDataSources _silos;

    public NpgsqlConnectionFactory(IOptions<TrovesuiteOptions> options, ILogger<NpgsqlConnectionFactory> logger)
    {
        _logger = logger;
        _reportUnscoped = options.Value.Tenancy.ReportUnscopedAccess;
        _connectionString = options.Value.Database.BuildConnectionString();

        var dsBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        _dataSource = dsBuilder.Build();
        _silos = new SiloDataSources(options.Value.Tenancy, logger);
        _logger.LogInformation("Npgsql data source created (max pool size = {Max})", options.Value.Database.MaxPoolSize);
    }

    /// <summary>Drop a silo's pooled credential, so the next call re-reads it.</summary>
    public Task<bool> ForgetSiloAsync(TenantRoute route) => _silos.ForgetAsync(route);

    /// <summary>How many silo databases this process holds a data source for.</summary>
    public int OpenSiloSources => _silos.OpenSources;

    /// <summary>
    /// A connection to the database the request in scope belongs to.
    /// </summary>
    /// <remarks>
    /// Synchronous, so it cannot read a silo credential itself. It does not need
    /// to: the middleware prepares the route's data source before the request
    /// reaches any handler, so this is a lookup.
    ///
    /// If a silo route has nothing prepared, this THROWS rather than handing back
    /// the pod's own connection, which would be the cross-tenant read the whole
    /// mechanism exists to prevent. That happens when the connection is opened
    /// outside a request — a timer or a one-off command — where the caller has to
    /// say which tenant it means.
    /// </remarks>
    public NpgsqlConnection CreateConnection()
    {
        var route = TenantContext.Current;
        if (!_silos.TryGetPrepared(route, out var prepared))
        {
            throw new SiloUnavailableException(
                $"Route '{route!.Host}' is {route.Tier} but its credential could not " +
                "be resolved. Returning this pod's own connection would read another " +
                "tenant's database.");
        }
        if (prepared is not null) return prepared.CreateConnection();

        if (route is not null && route.HasOwnDatabase)
        {
            throw new SiloUnavailableException(
                $"Route '{route.Host}' is {route.Tier} but no data source has been " +
                "prepared for it. Inside a request the middleware does this; outside " +
                "one, await CreateOpenConnectionAsync first.");
        }

        Report();
        return _dataSource.CreateConnection();
    }

    /// <summary>
    /// The data source for the route in scope, or null for pooled. For EF Core.
    /// </summary>
    /// <remarks>
    /// EF builds its options synchronously, so it cannot resolve a credential;
    /// this hands it the one the middleware already prepared. Throws for a silo
    /// route with nothing prepared, for the same reason as above.
    /// </remarks>
    public NpgsqlDataSource? DataSourceForCurrentRoute()
    {
        var route = TenantContext.Current;
        if (!_silos.TryGetPrepared(route, out var prepared) ||
            (prepared is null && route is not null && route.HasOwnDatabase))
        {
            throw new SiloUnavailableException(
                $"Route '{route!.Host}' is {route.Tier} but no data source has been " +
                "prepared for it, so EF would have used the pooled database.");
        }
        return prepared;
    }

    /// <summary>Resolve the route's credential now. Called by the middleware.</summary>
    public Task PrepareForRouteAsync(TenantRoute? route, CancellationToken cancellationToken = default)
        => _silos.PrepareAsync(route, cancellationToken);

    /// <summary>
    /// An open connection to the database the request in scope belongs to.
    /// </summary>
    /// <remarks>
    /// A POOLED route — or no route at all — gets the pod's own data source, as
    /// it always did. A SILO route gets one against the database its row names,
    /// opened with the credential THIS app resolves for that silo.
    ///
    /// If that cannot be opened this THROWS. It does not quietly fall back to the
    /// pod's own database, because that would serve one tenant from the shared
    /// one and look like success. See <see cref="SiloDataSources"/>.
    /// </remarks>
    public async Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var route = TenantContext.Current;
        var silo = await _silos.ForRouteAsync(route, cancellationToken).ConfigureAwait(false);
        if (silo is not null)
        {
            return await silo.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }

        Report();
        return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The one place every query in this package reaches the database, which is
    /// what makes it the only place worth asking "whose data is this?".
    /// </summary>
    /// <remarks>
    /// Before anything can be routed by tenant, every path that reaches a
    /// database has to have a tenant to route by. Requests get one from the
    /// middleware; timers, dispatchers and one-off commands have to say which
    /// tenant they mean, and that list is not reliably discoverable by reading the
    /// code. So the running system is asked instead.
    ///
    /// Only reached for the pooled path now. A silo connection has a tenant by
    /// construction — it could not have been opened without one.
    /// </remarks>
    private void Report()
    {
        if (_reportUnscoped) UnscopedAccessReporter.Note(_logger);
    }

    public ValueTask DisposeAsync() => _silos.DisposeAsync();
}
