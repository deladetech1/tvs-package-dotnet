using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trovesuite.Package.Auth;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Database;
using Xunit;

namespace Trovesuite.Package.Tests;

/*
Does a locked tenant get refused on a token it already holds?

The lock was enforced only at sign-in. A token already in somebody's browser is
good for up to twenty-four hours, so a client suspended at 09:00 went on working
all day. The fix reads is_locked on the tenant row AuthorizeAsync already reads
on every request.

This app matters more than the others here: it validates tokens itself and never
asks Core Platform whether one still counts, so a lock enforced only in Core
Platform would be enforced only in Core Platform.

Three things worth proving, because each is a way it could be quietly wrong:

  1. a locked tenant is refused, with the reason, on a token that still decodes;
  2. a database that has NOT had the lock migration still works -- the package
     reaches every database including self-hosted ones, and naming a column that
     is not there would turn "one release behind" into "nobody can sign in";
  3. a database that is genuinely down still fails, rather than being mistaken
     for an old one and waved through.
*/
public class TenantLockTests
{
    /// <summary>A database that answers however the test says, and counts.</summary>
    private sealed class FakeDb : IDatabaseManager
    {
        private readonly Func<string, IReadOnlyList<IDictionary<string, object?>>> _answer;
        public List<string> Queries { get; } = new();

        public FakeDb(Func<string, IReadOnlyList<IDictionary<string, object?>>> answer)
            => _answer = answer;

        public Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteQueryAsync(
            string sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Queries.Add(sql);
            return Task.FromResult(_answer(sql));
        }

        public Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(
            string sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Queries.Add(sql);
            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }

        public Task<int> ExecuteUpdateAsync(string sql, object? parameters = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task<object?> ExecuteScalarAsync(string sql, object? parameters = null,
            CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);

        public Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null,
            CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);

        public Task<IDictionary<string, object?>> HealthCheckAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IDictionary<string, object?>>(new Dictionary<string, object?>());
    }

    private static AuthService Service(FakeDb db) => new(
        db,
        Options.Create(new TrovesuiteOptions()),
        NullLogger<AuthService>.Instance);

    /// <summary>
    /// The probe is cached per process, which is the point of it -- so each test
    /// clears it rather than depending on the order they happen to run in.
    /// </summary>
    private static void ForgetTheProbe() =>
        typeof(AuthService)
            .GetField("_tenantHasLock",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!
            .SetValue(null, null);

    private static Dictionary<string, object?> Tenant(bool locked, string? reason) =>
        new() { ["is_verified"] = true, ["is_locked"] = locked, ["lock_reason"] = reason };

    [Fact]
    public async Task A_locked_tenant_is_refused_on_a_token_it_already_holds()
    {
        ForgetTheProbe();
        var db = new FakeDb(sql => sql.Contains("is_verified")
            ? new[] { Tenant(true, "unpaid since June") }
            : Array.Empty<IDictionary<string, object?>>());

        var res = await Service(db).AuthorizeAsync(
            new AuthServiceWriteDto { UserId = "usr_1", TenantId = "tnt_locked" });

        Assert.False(res.Success);
        Assert.Equal(403, res.StatusCode);
        Assert.Equal("TENANT_LOCKED", res.Error);
        // The reason travels with the refusal, so support is not guessing.
        Assert.Contains("unpaid since June", res.Detail);
        Assert.Contains(db.Queries, q => q.Contains("is_locked"));
    }

    [Fact]
    public async Task An_unlocked_tenant_is_not_refused_as_locked()
    {
        ForgetTheProbe();
        var db = new FakeDb(sql => sql.Contains("is_verified")
            ? new[] { Tenant(false, null) }
            : Array.Empty<IDictionary<string, object?>>());

        var res = await Service(db).AuthorizeAsync(
            new AuthServiceWriteDto { UserId = "usr_1", TenantId = "tnt_fine" });

        // It fails later for its own reasons -- no groups, no roles. What matters
        // is that being locked is not the answer.
        Assert.NotEqual("TENANT_LOCKED", res.Error);
        Assert.True(db.Queries.Count > 1, "authorization stopped at the tenant row");
    }

    [Fact]
    public async Task A_database_without_the_lock_column_does_not_lock_everybody_out()
    {
        ForgetTheProbe();
        var db = new FakeDb(sql =>
        {
            if (sql.Contains("is_locked"))
                throw new Npgsql.PostgresException(
                    "column \"is_locked\" does not exist", "ERROR", "ERROR", "42703");
            return sql.Contains("is_verified")
                ? new IDictionary<string, object?>[]
                    { new Dictionary<string, object?> { ["is_verified"] = true } }
                : Array.Empty<IDictionary<string, object?>>();
        });

        var res = await Service(db).AuthorizeAsync(
            new AuthServiceWriteDto { UserId = "usr_1", TenantId = "tnt_old" });

        Assert.NotEqual("TENANT_LOCKED", res.Error);
        Assert.NotEqual(500, res.StatusCode);
        // It retried without the column rather than giving up.
        Assert.Equal(2, db.Queries.Count(q => q.Contains("is_verified")));

        // ...and remembered, so the retry is not paid on every request.
        var again = new FakeDb(sql => sql.Contains("is_verified")
            ? new IDictionary<string, object?>[]
                { new Dictionary<string, object?> { ["is_verified"] = true } }
            : Array.Empty<IDictionary<string, object?>>());
        await Service(again).AuthorizeAsync(
            new AuthServiceWriteDto { UserId = "usr_1", TenantId = "tnt_old" });
        Assert.DoesNotContain(again.Queries, q => q.Contains("is_locked"));
    }

    [Fact]
    public async Task A_down_database_is_not_mistaken_for_an_old_one()
    {
        ForgetTheProbe();
        var db = new FakeDb(_ => throw new Npgsql.PostgresException(
            "the database system is shutting down", "FATAL", "FATAL", "57P03"));

        var res = await Service(db).AuthorizeAsync(
            new AuthServiceWriteDto { UserId = "usr_1", TenantId = "tnt_any" });

        // Whatever it answers, it must not be a pass.
        Assert.False(res.Success);

        // And the database must not have been written off as one without the
        // column, which would stop the lock being enforced there afterwards.
        var probe = typeof(AuthService)
            .GetField("_tenantHasLock",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!
            .GetValue(null);
        Assert.NotEqual((object?)false, probe);
    }
}
