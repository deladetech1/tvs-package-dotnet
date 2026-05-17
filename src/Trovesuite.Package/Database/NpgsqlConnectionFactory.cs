using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Trovesuite.Package.Configuration;

namespace Trovesuite.Package.Database;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlConnectionFactory> _logger;

    public NpgsqlConnectionFactory(IOptions<TrovesuiteOptions> options, ILogger<NpgsqlConnectionFactory> logger)
    {
        _logger = logger;
        _connectionString = options.Value.Database.BuildConnectionString();

        var dsBuilder = new NpgsqlDataSourceBuilder(_connectionString);
        _dataSource = dsBuilder.Build();
        _logger.LogInformation("Npgsql data source created (max pool size = {Max})", options.Value.Database.MaxPoolSize);
    }

    public NpgsqlConnection CreateConnection() => _dataSource.CreateConnection();

    public async Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        return await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }
}
