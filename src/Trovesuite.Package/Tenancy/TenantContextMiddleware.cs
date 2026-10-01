using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trovesuite.Package.Configuration;

namespace Trovesuite.Package.Tenancy;

/// <summary>
/// Resolve which tenant a request belongs to, once, before anything else runs.
/// </summary>
/// <remarks>
/// The address the request arrived AT is not it, which took deploying the Python
/// side of this to find out. A frontend is reached at the tenant's own host —
/// ddt.dev.trovesuite.com — but it calls the API at its own hostname, so the Host
/// header arriving here names the APP, not the tenant. Every request resolved to
/// nothing, and every request path was correctly reported as having no tenant.
///
/// What does carry the tenant, in the order this trusts it:
///
/// <para><c>Origin</c> — the frontend's own address, and the strongest signal
/// available. These calls are cross-origin by construction, different host from
/// the API, so the browser always sends it and page script cannot forge it.</para>
///
/// <para><c>X-Tenant-Host</c> — for callers with no Origin: the desktop and
/// mobile apps, and server-to-server calls. Client-supplied, and that is
/// acceptable here: naming a tenant's host before authenticating is no more than
/// browsing to that tenant's login page, which anybody can do. What stops it
/// mattering is the rule after it — once a token exists, a route that names a
/// tenant must match the token's tenant.</para>
///
/// <para><c>Host</c> — last, and almost never a tenant. Kept because it costs
/// nothing and because a deployment where the API does share the tenant's host
/// still works.</para>
///
/// The first of those that RESOLVES wins, rather than the first that is present,
/// so a browser calling from an origin nobody has registered still falls through
/// to the remaining signals instead of being refused.
/// </remarks>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantRouteResolver _resolver;
    private readonly TenancyOptions _options;
    private readonly ILogger<TenantContextMiddleware> _logger;

    /// <summary>Where the resolved route is published for handlers that want it.</summary>
    public const string RouteItemKey = "tenant_route";

    /// <summary>
    /// Which header answered. Worth having when a request routes somewhere
    /// surprising: "Origin said so" and "Host said so" are very different
    /// situations to debug.
    /// </summary>
    public const string SignalItemKey = "tenant_route_signal";

    public TenantContextMiddleware(
        RequestDelegate next,
        ITenantRouteResolver resolver,
        IOptions<TrovesuiteOptions> options,
        ILogger<TenantContextMiddleware> logger)
    {
        _next = next;
        _resolver = resolver;
        _options = options.Value.Tenancy;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.HasValue ? context.Request.Path.Value! : string.Empty;
        var exempt = _options.ExemptPathPrefixes.Any(
            prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        var candidates = Candidates(context.Request);

        TenantRoute? route = null;
        string? signal = null;
        var host = candidates.Count > 0 ? candidates[0].Host : string.Empty;

        foreach (var (candidateSignal, candidate) in candidates)
        {
            var found = await _resolver.ResolveAsync(candidate, context.RequestAborted)
                                       .ConfigureAwait(false);
            if (found is not null)
            {
                route = found;
                signal = candidateSignal;
                host = candidate;
                break;
            }
        }

        // Recorded whether or not anything resolved, so a later complaint about
        // having no tenant can say WHY: a request that offered nothing usable is a
        // different fault from a timer that never had a request at all.
        using var signals = TenantContext.SetRequestSignals(
            candidates.ToDictionary(c => c.Signal, c => c.Host, StringComparer.Ordinal));

        // Which endpoint is running, evaluated when the database is reached rather
        // than now: routing happens after this middleware, so the endpoint is still
        // null here and set by the time it is asked for.
        using var describe = TenantContext.SetRequestDescriber(
            () => context.GetEndpoint()?.DisplayName);

        var enforce = _options.Enforce && !exempt;

        if (route is null)
        {
            if (enforce)
            {
                _logger.LogWarning("Refusing request for unrouted host {Host}", host);
                await RefuseAsync(context, 404, "This address is not configured. Check the URL.")
                    .ConfigureAwait(false);
                return;
            }

            // Phase 0: nothing routes on the context, so an unknown host is not yet
            // a problem. Debug because scanners generate a lot of these.
            _logger.LogDebug("No tenant route for host {Host}; continuing unresolved", host);
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (enforce && route.Status != "ACTIVE")
        {
            if (route.Status is "PROVISIONING" or "MIGRATING")
            {
                _logger.LogInformation(
                    "Host {Host} is {Status}; asking the caller to retry", host, route.Status);
                await RefuseAsync(
                    context, 503,
                    "This workspace is being prepared. Please try again shortly.")
                    .ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Host {Host} is {Status}; refusing", host, route.Status);
                await RefuseAsync(
                    context, 403,
                    "This workspace is not currently available. Please contact your administrator.")
                    .ConfigureAwait(false);
            }
            return;
        }

        // Available to downstream code either way: handlers that want it can read
        // it, and nothing is obliged to.
        context.Items[RouteItemKey] = route;
        context.Items[SignalItemKey] = signal;

        using (TenantContext.Scope(route))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    /// <summary>(signal, host) pairs to try, most trustworthy first.</summary>
    private List<(string Signal, string Host)> Candidates(HttpRequest request)
    {
        var all = new List<(string Signal, string Host)>
        {
            ("origin", OriginHost(request)),
            ("x-tenant-host", First(request, "X-Tenant-Host")),
        };

        if (_options.TrustForwardedHost)
        {
            // A proxy chain may append rather than overwrite; the first entry is the
            // address the client actually asked for.
            var forwarded = First(request, "X-Forwarded-Host");
            var comma = forwarded.IndexOf(',');
            all.Add(("x-forwarded-host", comma == -1 ? forwarded : forwarded[..comma].Trim()));
        }

        all.Add(("host", request.Host.HasValue ? request.Host.Value! : string.Empty));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return all.Where(c => c.Host.Length > 0 && seen.Add(c.Signal)).ToList();
    }

    private static string First(HttpRequest request, string name) =>
        request.Headers.TryGetValue(name, out var values) && values.Count > 0
            ? (values[0] ?? string.Empty).Trim()
            : string.Empty;

    /// <summary>
    /// The hostname an Origin names, or "" if there is not a usable one.
    /// </summary>
    /// <remarks>
    /// Origin is scheme://host[:port], so it needs parsing rather than reading. A
    /// sandboxed iframe sends the literal "null", which is not a host and must not
    /// be treated as one.
    /// </remarks>
    private static string OriginHost(HttpRequest request)
    {
        var origin = First(request, "Origin");
        if (origin.Length == 0 || origin == "null") return string.Empty;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
    }

    /// <summary>
    /// Answer in the envelope the frontends already parse.
    /// </summary>
    /// <remarks>
    /// Mirrors <c>Respons</c> — detail, data, success, status_code, error — so a
    /// refusal here surfaces the same way as one from any endpoint instead of as an
    /// unrecognised shape the client renders as "undefined".
    /// </remarks>
    private static async Task RefuseAsync(HttpContext context, int status, string detail)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            detail,
            data = Array.Empty<object>(),
            success = false,
            status_code = status,
            error = (string?)null,
        });

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.ContentLength = body.Length;
        await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
    }
}

/// <summary>Registration helper, so consumers do not have to name the type.</summary>
public static class TenantContextMiddlewareExtensions
{
    /// <summary>
    /// Add tenant resolution to the pipeline.
    /// </summary>
    /// <remarks>
    /// Place it INSIDE <c>UseCors</c> — after it in the pipeline — or a refusal
    /// from here reaches the browser with no
    /// <c>Access-Control-Allow-Origin</c> header and is reported as a CORS
    /// failure rather than as the 404 it is. Place it before authentication, so
    /// an authenticated handler can compare the token's tenant to the route's.
    /// </remarks>
    public static Microsoft.AspNetCore.Builder.IApplicationBuilder UseTrovesuiteTenancy(
        this Microsoft.AspNetCore.Builder.IApplicationBuilder app) =>
        Microsoft.AspNetCore.Builder.UseMiddlewareExtensions
            .UseMiddleware<TenantContextMiddleware>(app);
}
