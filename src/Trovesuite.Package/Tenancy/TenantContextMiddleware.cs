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
/// The first of those that NAMES A TENANT decides, and if it names one we do not
/// know, the request is refused rather than passed down the list.
///
/// <para>This used to take the first signal that RESOLVED, falling through an
/// unknown one so that a browser on an unregistered origin was still served. The
/// effect was the opposite of safe: Origin is tried first, and when it did not
/// resolve the next candidate was <c>Host</c> — which for every deployed app is
/// the API's OWN address, and that is registered, as POOLED. A browser on any
/// unregistered address under a product domain was quietly handed a session on
/// the shared database. Found on the Python side of this, where signing in at
/// king.dev.trovesuite.com worked.</para>
///
/// <para>Origin and X-Tenant-Host are claims about WHICH TENANT a request is for.
/// A claim that cannot be placed is an answer, not a gap. Host is not such a
/// claim — it says where the request arrived, not whose data it wants — so
/// falling back to it is still right when no tenant signal was offered at
/// all.</para>
/// </remarks>
public sealed class TenantContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ITenantRouteResolver _resolver;
    private readonly TenancyOptions _options;
    private readonly ILogger<TenantContextMiddleware> _logger;

    /// <summary>
    /// Signals that are a CLAIM ABOUT WHICH TENANT this request belongs to.
    /// </summary>
    /// <remarks>
    /// <c>Host</c> is deliberately not one. For every deployed application it is
    /// the API's own address, which says where the request arrived rather than
    /// whose data it wants — and treating it as a fallback for an unknown Origin
    /// is what turned "I am king.dev.trovesuite.com" into "I am the pooled
    /// database".
    /// </remarks>
    private static readonly HashSet<string> TenantClaimSignals =
        new(StringComparer.Ordinal) { "origin", "x-tenant-host", "x-forwarded-host" };

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

        // Set when a signal NAMED a tenant we have never heard of. The search
        // stops there: the next candidate is the API's own host, which would
        // answer a question nobody asked.
        string? unknownTenantSignal = null;

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

            if (TenantClaimSignals.Contains(candidateSignal))
            {
                unknownTenantSignal = candidateSignal;
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
            // A signal that NAMED a tenant we do not know is refused even when
            // enforcement is off. That switch exists so an unrecognised Host can
            // be observed during rollout without breaking anything; it was never
            // meant to permit serving a tenant address belonging to nobody.
            if (enforce || unknownTenantSignal is not null)
            {
                _logger.LogWarning(
                    "Refusing request for unrouted host {Host} (signal={Signal})",
                    host, unknownTenantSignal ?? "host");
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
            // Resolve this silo's credential BEFORE any handler runs.
            //
            // EF Core builds its DbContext options synchronously and the apps'
            // own connection helpers are synchronous too, so neither can read a
            // Key Vault secret when it needs one. Paying for it here, once per
            // request, is what lets those paths be a dictionary lookup. A no-op
            // for a pooled route and a lookup for a silo already seen.
            //
            // A failure here refuses the request rather than letting it reach a
            // handler that would fall back to this pod's own database.
            var factory = context.RequestServices
                .GetService(typeof(Database.IDbConnectionFactory)) as Database.NpgsqlConnectionFactory;
            if (factory is not null && route.HasOwnDatabase)
            {
                try
                {
                    await factory.PrepareForRouteAsync(route, context.RequestAborted)
                        .ConfigureAwait(false);
                }
                catch (SiloUnavailableException ex)
                {
                    _logger.LogError(ex,
                        "Refusing {Host}: its own database could not be reached", route.Host);
                    await RefuseAsync(context, 503,
                        "This workspace is temporarily unavailable. Please try again shortly.")
                        .ConfigureAwait(false);
                    return;
                }
            }

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
