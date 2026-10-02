namespace Trovesuite.Package.Tenancy;

/// <summary>
/// How this deployment behaves while tenant routing is being introduced.
/// </summary>
/// <remarks>
/// Bound from <c>Trovesuite:Tenancy</c>, so each switch is also settable as an
/// environment variable — <c>Trovesuite__Tenancy__Enforce</c> — which is how the
/// container apps are configured.
///
/// Every default here is the current behaviour. The package reaches a deployment
/// before anything reads the context, and a shared library that changes how
/// requests are answered the moment the pin moves is not one anybody can upgrade
/// safely.
/// </remarks>
public sealed class TenancyOptions
{
    public const string SectionName = "Trovesuite:Tenancy";

    /// <summary>
    /// Off: resolve the route, publish it, and serve every request regardless.
    /// On: an unresolved host is a 404 and a route that is not ACTIVE is refused.
    /// </summary>
    /// <remarks>
    /// This has to be turned on BEFORE anything routes database traffic on the
    /// context, and never after. Routing fails closed; the switch exists so the
    /// resolution can run in production and be watched while it still cannot
    /// hurt anybody.
    /// </remarks>
    public bool Enforce { get; set; }

    /// <summary>
    /// Whether <c>X-Forwarded-Host</c> may name the tenant.
    /// </summary>
    /// <remarks>
    /// Opt-in rather than best-effort detection, because getting it wrong hands
    /// one tenant another's database: if a client can set this header, a client
    /// can set <c>X-Forwarded-Host: victim.trovesuite.com</c>. Only enable it
    /// behind a proxy that OVERWRITES the header rather than appending to it.
    /// </remarks>
    public bool TrustForwardedHost { get; set; }

    /// <summary>Seconds a resolved host stays cached. Long enough to keep the hot
    /// path out of the database, short enough that editing a route row takes
    /// effect without a restart.</summary>
    public int CacheTtlSeconds { get; set; } = 60;

    /// <summary>
    /// Seconds an unknown host stays cached.
    /// </summary>
    /// <remarks>
    /// Misses are cached too: scanners, health probes and the load balancer's own
    /// IP generate a steady stream of junk hosts, and without this each one is a
    /// query on the hot path. Short, so a tenant that has just finished
    /// provisioning starts working in seconds rather than minutes.
    /// </remarks>
    public int NegativeCacheTtlSeconds { get; set; } = 10;

    /// <summary>Where the route rows live. Overridable for the same reason the
    /// other table names are: a test schema, or a rename.</summary>
    public string RoutesTable { get; set; } = "control_plane.ctl_tenant_routes";

    /// <summary>
    /// Paths served whatever the switches say.
    /// </summary>
    /// <remarks>
    /// A liveness probe arrives with whatever host the orchestrator felt like
    /// using, frequently an IP. A health check that fails because of tenant
    /// routing takes the whole revision down in order to protect it from a
    /// misconfiguration.
    /// </remarks>
    public List<string> ExemptPathPrefixes { get; set; } =
        new() { "/health", "/swagger", "/openapi", "/favicon.ico" };

    /// <summary>
    /// This app's half of a silo secret name, e.g. <c>db-url-zeloshr-admin</c>.
    /// </summary>
    /// <remarks>
    /// A silo has a login role per app, so the route row names the silo and this
    /// names the app: the credential is <c>db-url-&lt;prefix&gt;-&lt;silo_key&gt;</c>.
    ///
    /// Supplied by the IaC that creates the secret, because that is the only
    /// place the slug is known — it is the app's folder under
    /// <c>applications/</c> with "/" flattened, which these apps do not carry at
    /// run time. Unset means a silo route is REFUSED rather than served from the
    /// pooled database.
    /// </remarks>
    public string? DbSecretPrefix { get; set; }

    /// <summary>The Key Vault holding this environment's secrets.</summary>
    /// <remarks>
    /// Only the vault and the secret's NAME; the pod's managed identity is what
    /// decides whether it may be read.
    /// </remarks>
    public string? KeyVaultUri { get; set; }

    /// <summary>Whether to report database access that happens with no tenant in
    /// scope. On by default: the list it produces is the work Phase 2 needs, and
    /// it is one log line per call site per process.</summary>
    public bool ReportUnscopedAccess { get; set; } = true;
}
