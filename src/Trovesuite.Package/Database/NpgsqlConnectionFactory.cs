using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Tenancy;

namespace Trovesuite.Package.Database;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlConnectionFactory> _logger;
    private readonly bool _reportUnscoped;

    public NpgsqlConnectionFactory(IOptions<TrovesuiteOptions> options, ILogger<NpgsqlConnectionFactory> logger)
    {
        _logger = logger;
        _reportUnscoped = options.Value.Tenancy.ReportUnscopedAccess;
        _connectionString = options.Value.Database.BuildConnectionString();

        var dsBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        _dataSource = dsBuilder.Build();
        _logger.LogInformation("Npgsql data source created (max pool size = {Max})", options.Value.Database.MaxPoolSize);
    }

    public NpgsqlConnection CreateConnection()
    {
        Report();
        return _dataSource.CreateConnection();
    }

    public async Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
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
    /// </remarks>
    private void Report()
    {
        if (_reportUnscoped) UnscopedAccessReporter.Note(_logger);
    }
}
