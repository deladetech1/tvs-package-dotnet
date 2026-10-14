using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Trovesuite.Package.Auth;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Database;
using Trovesuite.Package.Tenancy;
using Xunit;

namespace Trovesuite.Package.Tests;

/*
Is a token for one tenant refused at an address that names another?

TenantRoute.IsPinnedToTenant said "a token for a different tenant arriving at a
pinned address is a 401" and nothing enforced it -- here or in the Python
package. Tenant A's token sent from tenant B's address was authorised against
B's route, as A. The same rule is now in all three packages.

And does a SILO_BYOD route get its own database? It did not: HasOwnDatabase
listed only SILO_SHARED and SILO_DEDICATED, so a client hosting its own database
was quietly served from the pooled one.
*/
public class AddressTenantTests
{
    private const string Secret = "address-tenant-test-secret-address-tenant-test";

    /// <summary>A database with nothing in it: anything past the address check is TENANT_NOT_FOUND.</summary>
    private sealed class EmptyDb : IDatabaseManager
    {
        public Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteQueryAsync(
            string sql, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IDictionary<string, object?>>>(Array.Empty<IDictionary<string, object?>>());

        public Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(
            string sql, object? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());

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

    private static AuthService Service() => new(
        new EmptyDb(),
        Options.Create(new TrovesuiteOptions { Jwt = new JwtOptions { SecretKey = Secret } }),
        NullLogger<AuthService>.Instance);

    private static string Token(string tenantId)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            claims: new[] { new Claim("user_id", "u1"), new Claim("tenant_id", tenantId) },
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static readonly TenantRoute Apex = new() { Host = "api.dev.trovesuite.com", Tier = TenantTier.Pooled, CellKey = "c1" };
    private static readonly TenantRoute Acme = new() { Host = "acme.dev.trovesuite.com", Tier = TenantTier.Pooled, CellKey = "c1", TenantId = "t-acme" };
    private static readonly TenantRoute Portal = new()
    {
        Host = "dev.zeloshr.com", Tier = TenantTier.Pooled, CellKey = "c1",
        IsWildcard = true, RequestedHost = "globex.dev.zeloshr.com",
    };

    [Fact]
    public void Decode_IsUnchanged_WithNoRouteOrAnAddressThatNamesNoTenant()
    {
        Assert.Equal("t-globex", Service().DecodeToken(Token("t-globex"))["tenant_id"]);
        using (TenantContext.Scope(Apex))
            Assert.Equal("t-globex", Service().DecodeToken(Token("t-globex"))["tenant_id"]);
        using (TenantContext.Scope(Portal))
            Assert.Equal("t-globex", Service().DecodeToken(Token("t-globex"))["tenant_id"]);
    }

    [Fact]
    public void Decode_AcceptsTheAddressOwnTenant()
    {
        using var _ = TenantContext.Scope(Acme);
        Assert.Equal("t-acme", Service().DecodeToken(Token("t-acme"))["tenant_id"]);
    }

    [Fact]
    public void Decode_RefusesAnotherTenantsToken_AsAnUnauthorizedAccessException()
    {
        using var _ = TenantContext.Scope(Acme);
        var e = Assert.Throws<TenantMismatchException>(() => Service().DecodeToken(Token("t-globex")));
        Assert.IsAssignableFrom<UnauthorizedAccessException>(e);
    }

    [Fact]
    public async Task Authorize_RefusesAnotherTenantsToken_With401AndItsCode()
    {
        using var _ = TenantContext.Scope(Acme);
        var r = await Service().AuthorizeUserFromTokenAsync(Token("t-globex"));
        Assert.False(r.Success);
        Assert.Equal(401, r.StatusCode);
        Assert.Equal("TENANT_MISMATCH", r.Error);
        Assert.Equal(TenantMismatchException.Detail, r.Detail);
    }

    [Fact]
    public async Task Authorize_OwnTenant_GetsPastTheAddressCheck()
    {
        using var _ = TenantContext.Scope(Acme);
        var r = await Service().AuthorizeUserFromTokenAsync(Token("t-acme"));
        // The empty database is the next thing to say no.
        Assert.Equal("TENANT_NOT_FOUND", r.Error);
    }

    [Fact]
    public void EscapeHatch_TurnsTheCheckOff()
    {
        Environment.SetEnvironmentVariable("TENANCY_PIN_TOKENS", "false");
        try
        {
            using var _ = TenantContext.Scope(Acme);
            Assert.Equal("t-globex", Service().DecodeToken(Token("t-globex"))["tenant_id"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TENANCY_PIN_TOKENS", null);
        }
    }

    [Theory]
    [InlineData("SILO_SHARED", true)]
    [InlineData("SILO_DEDICATED", true)]
    [InlineData("SILO_BYOD", true)]
    [InlineData("POOLED", false)]
    [InlineData("SELF_MANAGED", false)]
    public void EveryOwnDatabaseTier_HasItsOwnDatabase(string tier, bool expected)
    {
        var route = new TenantRoute { Host = "x", Tier = tier, CellKey = "c1" };
        Assert.Equal(expected, route.HasOwnDatabase);
    }
}
