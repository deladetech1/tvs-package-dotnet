using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Tenancy;
using Xunit;

namespace Trovesuite.Package.Tests;

/*
A silo tenant on a SHARED app frontend reaches its own database, and nobody else's.

An app frontend served from one address for every tenant resolves to a route
that names no tenant. When that is the Origin's answer, X-Tenant-Host decides:
a TENANT route wins, anything else is a 404 -- never the pool. Same rule as
trovesuite 1.0.63 and tvs-package-go. The token side (a token for another
tenant at a pinned route is a 401) is AddressTenantTests.
*/
public class SharedFrontendTenantHostTests
{
    private sealed class FixedResolver : ITenantRouteResolver
    {
        private readonly Dictionary<string, TenantRoute> _routes;
        public FixedResolver(Dictionary<string, TenantRoute> routes) => _routes = routes;

        public Task<TenantRoute?> ResolveAsync(string? host, CancellationToken cancellationToken = default)
        {
            var key = (host ?? string.Empty).Trim().ToLowerInvariant();
            var colon = key.IndexOf(':');
            if (colon >= 0) key = key[..colon];
            return Task.FromResult(_routes.TryGetValue(key, out var r) ? r : null);
        }

        public void Invalidate(string? host = null) { }

        public Task<IReadOnlyList<TenantRoute>> ActiveRoutesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TenantRoute>>(_routes.Values.ToList());
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static readonly Dictionary<string, TenantRoute> Routes = new()
    {
        ["api.dev.trovesuite.com"] = new() { Host = "api.dev.trovesuite.com", Tier = "POOLED", CellKey = "c1" },
        ["dev.mystoreguard.com"] = new() { Host = "dev.mystoreguard.com", Tier = "POOLED", CellKey = "c1" },
        ["acme.dev.trovesuite.com"] = new()
        {
            Host = "acme.dev.trovesuite.com", Tier = "POOLED", CellKey = "c1", TenantId = "t-acme",
        },
        ["closed.dev.trovesuite.com"] = new()
        {
            Host = "closed.dev.trovesuite.com", Tier = "POOLED", CellKey = "c1", TenantId = "t-closed",
            Status = "SUSPENDED",
        },
    };

    private static async Task<(int Status, string? Host)> Serve(
        string? origin, string? tenantHost, bool enforce = false)
    {
        string? seen = null;
        var options = Options.Create(new TrovesuiteOptions());
        options.Value.Tenancy.Enforce = enforce;
        var middleware = new TenantContextMiddleware(
            _ => { seen = TenantContext.Current?.Host; return Task.CompletedTask; },
            new FixedResolver(Routes), options, NullLogger<TenantContextMiddleware>.Instance);

        var context = new DefaultHttpContext { RequestServices = new NoServices() };
        context.Request.Path = "/api/x";
        context.Request.Host = new HostString("api.dev.trovesuite.com");
        if (origin is not null) context.Request.Headers["Origin"] = origin;
        if (tenantHost is not null) context.Request.Headers["X-Tenant-Host"] = tenantHost;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);
        return (context.Response.StatusCode, seen);
    }

    private const string Shared = "https://dev.mystoreguard.com";

    [Theory]
    [InlineData(Shared, "acme.dev.trovesuite.com", 200, "acme.dev.trovesuite.com")]
    [InlineData(Shared, "ACME.dev.trovesuite.com:443", 200, "acme.dev.trovesuite.com")]
    [InlineData(Shared, "nobody.dev.trovesuite.com", 404, null)]
    [InlineData(Shared, "api.dev.trovesuite.com", 404, null)]
    [InlineData(Shared, "dev.mystoreguard.com", 404, null)]
    [InlineData(Shared, "https://acme.dev.trovesuite.com", 404, null)]
    [InlineData(Shared, null, 200, "dev.mystoreguard.com")]
    [InlineData("https://acme.dev.trovesuite.com", "closed.dev.trovesuite.com", 200, "acme.dev.trovesuite.com")]
    [InlineData("https://evil.example.com", "acme.dev.trovesuite.com", 404, null)]
    [InlineData(null, "acme.dev.trovesuite.com", 200, "acme.dev.trovesuite.com")]
    public async Task HeaderDecidesOnlyForASharedOrigin(
        string? origin, string? tenantHost, int status, string? routeHost)
    {
        var (got, host) = await Serve(origin, tenantHost);
        Assert.Equal(status, got);
        Assert.Equal(routeHost, host);
    }

    [Fact]
    public async Task ASuspendedTenantNamedByTheHeaderIsRefused()
    {
        var (got, host) = await Serve(Shared, "closed.dev.trovesuite.com", enforce: true);
        Assert.Equal(403, got);
        Assert.Null(host);
    }
}
