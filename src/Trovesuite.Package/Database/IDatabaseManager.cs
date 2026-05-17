namespace Trovesuite.Package.Database;

public interface IDatabaseManager
{
    Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteQueryAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);
    Task<int> ExecuteUpdateAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);
    Task<object?> ExecuteScalarAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default);
    Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default);
    Task<IDictionary<string, object?>> HealthCheckAsync(CancellationToken cancellationToken = default);
}
