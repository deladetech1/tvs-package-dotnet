using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Database;
using Trovesuite.Package.Tenancy;
using Xunit;

namespace Trovesuite.Package.Tests;

/*
A route lookup that FAILS is a 503, never the 404 for an unknown address.

On 2026-10-23 the dev database server was briefly unreachable and every app
answered every address with 404 "This address is not configured", because a
failed lookup came back as "no route". Driven through the REAL
TenantRouteResolver and TenantContextMiddleware; only IDatabaseManager (the
database itself) is replaced. Same cases as trovesuite's
tests/test_route_lookup_failure.py and tvs-package-go.
*/
public class RouteLookupFailureTests
{
    private enum Mode { Ok, Down, Missing, Denied }

    private sealed class FakeDatabase : IDatabaseManager
    {
        public Mode Mode = Mode.Ok;

        private static readonly Dictionary<string, object?> Acme = new()
        {
            ["host"] = "acme.dev.trovesuite.com", ["tenant_id"] = "t-acme", ["tier"] = "SILO_SHARED",
            ["cell_key"] = "c1", ["db_name"] = "acme", ["status"] = "ACTIVE", ["is_wildcard"] = false,
            ["silo_key"] = "acme",
        };

        public Task<IReadOnlyList<IDictionary<string, object?>>> ExecuteQueryAsync(
            string sql, object? parameters = null, CancellationToken cancellationToken = default)
        {
            switch (Mode)
            {
                case Mode.Down:
                    throw new NpgsqlException("Failed to connect to dev-sql:5432",
                        new TimeoutException("The operation has timed out."));
                case Mode.Missing:
                    throw new PostgresException(
                        "relation \"control_plane.ctl_tenant_routes\" does not exist", "ERROR", "ERROR", "42P01");
                case Mode.Denied:
                    throw new PostgresException(
                        "permission denied for schema control_plane", "ERROR", "ERROR", "42501");
            }
            var host = (string)parameters!.GetType().GetProperty("host")!.GetValue(parameters)!;
            IReadOnlyList<IDictionary<string, object?>> rows = host == "acme.dev.trovesuite.com"
                ? new List<IDictionary<string, object?>> { new Dictionary<string, object?>(Acme) }
                : new List<IDictionary<string, object?>>();
            return Task.FromResult(rows);
        }

        public Task<IReadOnlyList<T>> ExecuteQueryAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ExecuteUpdateAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<object?> ExecuteScalarAsync(string sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<T?> ExecuteScalarAsync<T>(string sql, object? parameters = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDictionary<string, object?>> HealthCheckAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed record Result(int Status, bool Ran, string? Route, string Body, string? RetryAfter);

    private static async Task<Result> Serve(
        TenantRouteResolver resolver, string? origin, bool enforce = false, string? tenantHost = null)
    {
        var ran = false;
        string? seen = null;
        var options = Options.Create(new TrovesuiteOptions());
        options.Value.Tenancy.Enforce = enforce;
        var middleware = new TenantContextMiddleware(
            _ => { ran = true; seen = TenantContext.Current?.Host; return Task.CompletedTask; },
            resolver, options, NullLogger<TenantContextMiddleware>.Instance);

        var context = new DefaultHttpContext { RequestServices = new NoServices() };
        context.Request.Path = "/api/x";
        context.Request.Host = new HostString("api.dev.trovesuite.com");
        if (origin is not null) context.Request.Headers["Origin"] = origin;
        if (tenantHost is not null) context.Request.Headers["X-Tenant-Host"] = tenantHost;
        var body = new MemoryStream();
        context.Response.Body = body;

        await middleware.InvokeAsync(context);
        var retry = context.Response.Headers.TryGetValue("Retry-After", out var v) ? v.ToString() : null;
        return new Result(context.Response.StatusCode, ran, seen,
            System.Text.Encoding.UTF8.GetString(body.ToArray()), retry);
    }

    private static (FakeDatabase, TenantRouteResolver) Make()
    {
        var db = new FakeDatabase();
        var resolver = new TenantRouteResolver(db, Options.Create(new TrovesuiteOptions()),
            NullLogger<TenantRouteResolver>.Instance);
        return (db, resolver);
    }

    private const string Acme = "https://acme.dev.trovesuite.com";

    [Fact]
    public async Task DatabaseDownIsA503WithRetryAfterAndNoHandler()
    {
        var (db, resolver) = Make();
        db.Mode = Mode.Down;
        var r = await Serve(resolver, Acme);

        Assert.Equal(503, r.Status);
        Assert.False(r.Ran);
        Assert.Equal("30", r.RetryAfter);
        using var json = JsonDocument.Parse(r.Body);
        Assert.Equal("service_unavailable", json.RootElement.GetProperty("error").GetString());
        Assert.Equal(TenantContextMiddleware.UnavailableDetail, json.RootElement.GetProperty("detail").GetString());
        Assert.Equal(503, json.RootElement.GetProperty("status_code").GetInt32());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("not configured", r.Body);
    }

    [Fact]
    public async Task DatabaseDownWithAnUnknownOriginIsAlsoA503()
    {
        var (db, resolver) = Make();
        db.Mode = Mode.Down;
        var r = await Serve(resolver, "https://nobody.dev.trovesuite.com");
        Assert.Equal(503, r.Status);
        Assert.False(r.Ran);
    }

    [Fact]
    public async Task DatabaseDownHostOnlyEnforcedIsA503()
    {
        var (db, resolver) = Make();
        db.Mode = Mode.Down;
        var r = await Serve(resolver, null, enforce: true);
        Assert.Equal(503, r.Status);
        Assert.False(r.Ran);
    }

    [Fact]
    public async Task DatabaseDownHostOnlyNotEnforcedPassesThroughLikeAnUnknownHost()
    {
        var (db, resolver) = Make();
        db.Mode = Mode.Down;
        var r = await Serve(resolver, null);
        Assert.Equal(200, r.Status);
        Assert.True(r.Ran);
        Assert.Null(r.Route);
    }

    [Fact]
    public async Task AFailureIsNotCachedSoRecoveryIsImmediate()
    {
        var (db, resolver) = Make();
        db.Mode = Mode.Down;
        Assert.Equal(503, (await Serve(resolver, Acme)).Status);
        db.Mode = Mode.Ok;
        var r = await Serve(resolver, Acme);
        Assert.Equal(200, r.Status);
        Assert.Equal("acme.dev.trovesuite.com", r.Route);
    }

    [Fact]
    public async Task AHitCachedBeforeTheOutageServesWithinItsTtl()
    {
        var (db, resolver) = Make();
        Assert.Equal(200, (await Serve(resolver, Acme)).Status);
        db.Mode = Mode.Down;
        Assert.Equal(200, (await Serve(resolver, Acme)).Status);
    }

    [Fact]
    public async Task UnknownHostWithTheDatabaseFineIsStillA404()
    {
        var (_, resolver) = Make();
        var r = await Serve(resolver, "https://nobody.dev.trovesuite.com");
        Assert.Equal(404, r.Status);
        Assert.False(r.Ran);
        Assert.Contains("not configured", r.Body);
        Assert.Null(r.RetryAfter);
    }

    [Fact]
    public async Task AMissingTableKeepsItsPreMigrationMeaning()
    {
        var (db, resolver) = Make();
        db.Mode = Mode.Missing;
        Assert.Equal(404, (await Serve(resolver, Acme)).Status);
        var hostOnly = await Serve(resolver, null);
        Assert.Equal(200, hostOnly.Status);
        Assert.True(hostOnly.Ran);
    }

    [Fact]
    public async Task PermissionDeniedIsAFault503()
    {
        var (db, resolver) = Make();
        db.Mode = Mode.Denied;
        var r = await Serve(resolver, Acme);
        Assert.Equal(503, r.Status);
        Assert.False(r.Ran);
    }
}
