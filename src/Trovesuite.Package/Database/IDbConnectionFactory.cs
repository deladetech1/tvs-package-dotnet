using Npgsql;

namespace Trovesuite.Package.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
    Task<NpgsqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);
}
