using Dapper;
using Microsoft.Extensions.Logging;

namespace Trovesuite.Package.Database;

public sealed class DatabaseManager : IDatabaseManager
{
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<DatabaseManager> _logger;

    public DatabaseManager(IDbConnectionFactory factory, ILogger<DatabaseManager> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteQueryAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await conn.QueryAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
        var result = new List<IDictionary<string, object?>>();
        foreach (var row in rows)
        {
            var dict = (IDictionary<string, object?>)row!;
            result.Add(dict);
        }
        return result;
    }

    public async Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var rows = await conn.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<int> ExecuteUpdateAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await conn.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<object?> ExecuteScalarAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default)
    {
        await using var conn = await _factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await conn.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IDictionary<string, object?>> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = await _factory.CreateOpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var row = await conn.QuerySingleAsync(new CommandDefinition(
                "SELECT version() AS version, current_database() AS current_database, current_user AS current_user",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var dict = (IDictionary<string, object?>)row;
            return new Dictionary<string, object?>
            {
                ["status"] = "healthy",
                ["database"] = dict.TryGetValue("current_database", out var db) ? db : "unknown",
                ["user"] = dict.TryGetValue("current_user", out var user) ? user : "unknown",
                ["version"] = dict.TryGetValue("version", out var ver) ? ver : "unknown",
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return new Dictionary<string, object?>
            {
                ["status"] = "unhealthy",
                ["error"] = ex.Message,
            };
        }
    }
}
