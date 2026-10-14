using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Trovesuite.Package.Auth;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Database;
using Xunit;

namespace Trovesuite.Package.Tests;

/*
Does a session somebody ended stop working here?

Core Platform records sign-out, "end this session", "sign out everywhere" and an
organisation-wide lockdown in cp_user_sessions. The Python package refuses those
tokens on every request; this package never read the table, so ZelosHR kept an
ended session working until the token expired -- up to a day.
*/
public class SessionGuardTests
{
    private const string Secret = "session-guard-test-secret-session-guard-test";

    /// <summary>Answers the session query from a table of rows; records every write.</summary>
    private sealed class SessionsDb : IDatabaseManager
    {
        private readonly Dictionary<string, Dictionary<string, object?>> _rows;
        private readonly bool _broken;
        public List<string> Reads { get; } = new();
        public List<object?> Writes { get; } = new();

        public SessionsDb(Dictionary<string, Dictionary<string, object?>> rows, bool broken = false)
        {
            _rows = rows;
            _broken = broken;
        }

        public Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteQueryAsync(
            string sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            Reads.Add(sql);
            if (sql.Contains("cp_user_sessions"))
            {
                if (_broken) throw new InvalidOperationException("database unreachable");
                var jti = (string)parameters!.GetType().GetProperty("jti")!.GetValue(parameters)!;
                return Task.FromResult<IReadOnlyList<IDictionary<string, object?>>>(
                    _rows.TryGetValue(jti, out var row) ? new[] { (IDictionary<string, object?>)row } : Array.Empty<IDictionary<string, object?>>());
            }
            // Anything past the session check: an empty database.
            return Task.FromResult<IReadOnlyList<IDictionary<string, object?>>>(Array.Empty<IDictionary<string, object?>>());
        }

        public Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(string sql, object? parameters = null,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());

        public Task<int> ExecuteUpdateAsync(string sql, object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Writes.Add(parameters);
            return Task.FromResult(1);
        }

        public Task<object?> ExecuteScalarAsync(string sql, object? parameters = null,
            CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);

        public Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null,
            CancellationToken cancellationToken = default) => Task.FromResult<T?>(default);

        public Task<IDictionary<string, object?>> HealthCheckAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IDictionary<string, object?>>(new Dictionary<string, object?>());
    }

    private static readonly Dictionary<string, Dictionary<string, object?>> Sessions = new()
    {
        ["j-live"] = new() { ["revoked_at"] = null, ["revoke_reason"] = null, ["last_seen_at"] = DateTime.UtcNow.AddHours(-1) },
        ["j-fresh"] = new() { ["revoked_at"] = null, ["revoke_reason"] = null, ["last_seen_at"] = DateTime.UtcNow },
        ["j-admin"] = new() { ["revoked_at"] = DateTime.UtcNow, ["revoke_reason"] = "admin", ["last_seen_at"] = DateTime.UtcNow },
        ["j-noreason"] = new() { ["revoked_at"] = DateTime.UtcNow, ["revoke_reason"] = null, ["last_seen_at"] = DateTime.UtcNow },
    };

    private static AuthService Service(IDatabaseManager db) => new(
        db,
        Options.Create(new TrovesuiteOptions { Jwt = new JwtOptions { SecretKey = Secret } }),
        NullLogger<AuthService>.Instance);

    private static string Token(string? jti)
    {
        var claims = new List<Claim> { new("user_id", "u1"), new("tenant_id", "t1") };
        if (jti is not null) claims.Add(new Claim("jti", jti));
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds));
    }

    [Fact]
    public void DecodeToken_CarriesTheSessionId()
    {
        Assert.Equal("j-live", Service(new SessionsDb(Sessions)).DecodeToken(Token("j-live"))["jti"]);
        Assert.Null(Service(new SessionsDb(Sessions)).DecodeToken(Token(null))["jti"]);
    }

    [Theory]
    [InlineData("j-admin", "An administrator ended this session. Please sign in again, or contact them if you were not expecting this.")]
    [InlineData("j-noreason", "Your session has ended. Please sign in again.")]
    public async Task AnEndedSession_IsRefused_With401AndTheReason(string jti, string message)
    {
        var r = await Service(new SessionsDb(Sessions)).AuthorizeUserFromTokenAsync(Token(jti));
        Assert.False(r.Success);
        Assert.Equal(401, r.StatusCode);
        Assert.Equal("SESSION_REVOKED", r.Error);
        Assert.Equal(message, r.Detail);
    }

    [Theory]
    [InlineData("j-live")]           // a live session
    [InlineData("j-never-recorded")] // absence is not revocation
    [InlineData(null)]               // a token from before sessions existed
    public async Task AnythingElse_GetsPastTheSessionCheck(string? jti)
    {
        var r = await Service(new SessionsDb(Sessions)).AuthorizeUserFromTokenAsync(Token(jti));
        // The empty database is the next thing to say no.
        Assert.Equal("TENANT_NOT_FOUND", r.Error);
    }

    [Fact]
    public async Task ATokenWithoutASessionId_DoesNotQueryTheTable()
    {
        var db = new SessionsDb(Sessions);
        await Service(db).AuthorizeUserFromTokenAsync(Token(null));
        Assert.DoesNotContain(db.Reads, sql => sql.Contains("cp_user_sessions"));
    }

    [Fact]
    public async Task ADatabaseFailure_IsNotARefusal()
    {
        var r = await Service(new SessionsDb(Sessions, broken: true)).AuthorizeUserFromTokenAsync(Token("j-admin"));
        Assert.Equal("TENANT_NOT_FOUND", r.Error);
    }

    [Fact]
    public async Task AStaleSession_IsTouched_AndAFreshOneIsNot()
    {
        var stale = new SessionsDb(Sessions);
        await Service(stale).AuthorizeUserFromTokenAsync(Token("j-live"));
        Assert.Single(stale.Writes);

        var fresh = new SessionsDb(Sessions);
        await Service(fresh).AuthorizeUserFromTokenAsync(Token("j-fresh"));
        Assert.Empty(fresh.Writes);
    }
}

/*
A token that does not verify -- tampered, expired, or not a JWT at all -- is a 401
refusal from AuthorizeUserFromTokenAsync, not an exception. A malformed one used to
escape as an ArgumentException and reach the browser as a 400 with the identity
library's message; ZelosHR turned the others into a 403.
*/
public class InvalidTokenTests
{
    private const string Secret = "invalid-token-test-secret-invalid-token-test";

    private static AuthService Service() => new(
        new ThrowingDb(),
        Options.Create(new TrovesuiteOptions { Jwt = new JwtOptions { SecretKey = Secret } }),
        NullLogger<AuthService>.Instance);

    /// <summary>Never reached: every case here is refused before the database.</summary>
    private sealed class ThrowingDb : IDatabaseManager
    {
        private static Exception Reached() => new InvalidOperationException("the database must not be reached");
        public Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteQueryAsync(string sql, object? p = null, CancellationToken c = default) => throw Reached();
        public Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(string sql, object? p = null, CancellationToken c = default) => throw Reached();
        public Task<int> ExecuteUpdateAsync(string sql, object? p = null, CancellationToken c = default) => throw Reached();
        public Task<object?> ExecuteScalarAsync(string sql, object? p = null, CancellationToken c = default) => throw Reached();
        public Task<T?> ExecuteScalarAsync<T>(string sql, object? p = null, CancellationToken c = default) => throw Reached();
        public Task<IDictionary<string, object?>> HealthCheckAsync(CancellationToken c = default) => throw Reached();
    }

    private static string Signed(string key, DateTime expires, bool withTenant = true)
    {
        var claims = new List<Claim> { new("user_id", "u1") };
        if (withTenant) claims.Add(new Claim("tenant_id", "t1"));
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: claims, notBefore: expires.AddHours(-2), expires: expires, signingCredentials: creds));
    }

    public static IEnumerable<object[]> BadTokens() => new[]
    {
        new object[] { "malformed", "x.y.z" },
        new object[] { "not a token", "hello" },
        new object[] { "empty", "" },
        new object[] { "wrong secret", Signed("another-secret-another-secret-another-secret", DateTime.UtcNow.AddHours(1)) },
        new object[] { "expired", Signed(Secret, DateTime.UtcNow.AddMinutes(-5)) },
        new object[] { "no tenant", Signed(Secret, DateTime.UtcNow.AddHours(1), withTenant: false) },
    };

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task IsA401Refusal(string _, string token)
    {
        var r = await Service().AuthorizeUserFromTokenAsync(token);
        Assert.False(r.Success);
        Assert.Equal(401, r.StatusCode);
        Assert.Equal("INVALID_TOKEN", r.Error);
        Assert.Equal("Could not validate credentials", r.Detail);
    }

    [Fact]
    public void DecodeToken_StillThrowsUnauthorizedForAMalformedToken()
    {
        Assert.Throws<UnauthorizedAccessException>(() => Service().DecodeToken("x.y.z"));
    }
}
