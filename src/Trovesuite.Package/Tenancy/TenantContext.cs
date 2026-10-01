using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Trovesuite.Package.Tenancy;

/// <summary>Raised when tenant-scoped work runs with no resolved route.</summary>
/// <remarks>
/// A programming error, not a user error: a code path reached tenant data
/// without going through the middleware or declaring a scope. Loud on purpose —
/// the alternative, quietly using the shared database, is the bug this prevents.
/// </remarks>
public sealed class TenancyNotResolvedException : InvalidOperationException
{
    public TenancyNotResolvedException(string message) : base(message) { }
}

/// <summary>
/// Which database a request belongs to, carried for the life of that request.
/// </summary>
/// <remarks>
/// Every data access goes through <c>IDatabaseManager</c>, which takes no tenant
/// argument. Rather than add one to every call site, the answer travels beside
/// the call stack in an <see cref="AsyncLocal{T}"/> set once per request from the
/// address the request arrived at.
///
/// Two accessors, and the difference is the whole safety argument.
/// <see cref="Current"/> returns null when nothing was resolved, for code that
/// genuinely has an answer without a tenant. <see cref="Require"/> throws, for
/// anything about to touch tenant data. There is deliberately no third accessor
/// returning "the shared database" as a default: an unresolved request quietly
/// reading pooled data is the worst failure this design can have.
/// </remarks>
public static class TenantContext
{
    private static readonly AsyncLocal<TenantRoute?> _route = new();
    private static readonly AsyncLocal<IReadOnlyDictionary<string, string>?> _signals = new();
    private static readonly AsyncLocal<Func<string?>?> _describe = new();
    private static readonly AsyncLocal<bool> _tenantless = new();
    private static readonly AsyncLocal<string?> _spansAll = new();

    /// <summary>The route resolved for this request, or null if there is none.</summary>
    public static TenantRoute? Current => _route.Value;

    /// <summary>The route resolved for this request, or throw.</summary>
    public static TenantRoute Require() =>
        _route.Value ?? throw new TenancyNotResolvedException(
            "No tenant route is in scope. An HTTP request reaches one through " +
            "TenantContextMiddleware; background work (a timer, a dispatcher, a " +
            "management command) must declare one with TenantContext.Scope().");

    /// <summary>
    /// Run a block as though a request for <paramref name="route"/> were in flight.
    /// </summary>
    /// <remarks>
    /// How every path without an HTTP request declares what it is working on.
    /// Such paths iterate tenants, so the scope goes INSIDE the loop and the
    /// try/catch OUTSIDE it, or one unreachable database stops the rest being
    /// processed.
    /// </remarks>
    public static IDisposable Scope(TenantRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        var previous = _route.Value;
        _route.Value = route;
        return new Reset(() => _route.Value = previous);
    }

    /// <summary>
    /// Mark a block as having no tenant by nature: the control plane, a health check.
    /// </summary>
    /// <remarks>
    /// There is no tenant and there never will be. The resolver's own lookup
    /// decides WHICH tenant a request belongs to, so it precedes one. A health
    /// check asks whether this pod's database answers. Finished work, not debt.
    /// </remarks>
    public static IDisposable TenantlessRead()
    {
        var previous = _tenantless.Value;
        _tenantless.Value = true;
        return new Reset(() => _tenantless.Value = previous);
    }

    /// <summary>
    /// Mark a block as deliberately sweeping every tenant at once.
    /// </summary>
    /// <remarks>
    /// Correct while one database holds them all. The moment one tenant has its
    /// own, this block reads across a boundary and must be replaced by a loop
    /// over the active routes with per-tenant failure isolation. The reason says
    /// what the sweep is for, so searching for callers reads as the conversion
    /// list rather than as line numbers.
    /// </remarks>
    public static IDisposable SpansAllTenants(string reason)
    {
        var previous = _spansAll.Value;
        _spansAll.Value = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason;
        return new Reset(() => _spansAll.Value = previous);
    }

    /// <summary>What the in-flight request offered as a tenant signal, or null if there is no request.</summary>
    public static IReadOnlyDictionary<string, string>? RequestSignals => _signals.Value;

    /// <summary>Record what the in-flight request offered, for the duration of it.</summary>
    public static IDisposable SetRequestSignals(IReadOnlyDictionary<string, string> signals)
    {
        var previous = _signals.Value;
        _signals.Value = signals;
        return new Reset(() => _signals.Value = previous);
    }

    /// <summary>
    /// Record what the in-flight request is doing, for a complaint later.
    /// </summary>
    /// <remarks>
    /// A function rather than a string because the answer is not known yet when the
    /// middleware runs: routing has not happened, so the endpoint is still null. It
    /// is called at the moment the database is reached, by which time it is set.
    ///
    /// A <c>Func</c> rather than the <c>HttpContext</c> so this file keeps knowing
    /// nothing about ASP.NET; the middleware is the only part that needs to.
    /// </remarks>
    public static IDisposable SetRequestDescriber(Func<string?> describe)
    {
        var previous = _describe.Value;
        _describe.Value = describe;
        return new Reset(() => _describe.Value = previous);
    }

    internal static string? DescribeRequest()
    {
        try { return _describe.Value?.Invoke(); }
        catch { return null; }
    }

    internal static bool IsDeclared => _tenantless.Value || _spansAll.Value is not null;

    private sealed class Reset(Action undo) : IDisposable
    {
        private Action? _undo = undo;
        public void Dispose() { Interlocked.Exchange(ref _undo, null)?.Invoke(); }
    }
}

/// <summary>
/// Reports, once per call site, that the database was reached with no tenant.
/// </summary>
/// <remarks>
/// Before anything can be routed by tenant, every path that reaches a database
/// has to have a tenant to route by. Requests get one from the middleware;
/// timers, dispatchers and one-off commands have to say which tenant they mean,
/// and that list is not reliably discoverable by reading the code. So the
/// running system is asked instead.
///
/// A warning, not an error. Every route is POOLED today, so an unscoped caller
/// gets the only database there is and is correct. The moment a tenant has one of
/// its own, that silence becomes a cross-tenant read and this becomes a refusal.
/// Instrumentation with a deadline.
/// </remarks>
public static class UnscopedAccessReporter
{
    private const int MaxSites = 200;
    private static readonly HashSet<string> _seen = new(StringComparer.Ordinal);
    private static readonly Lock _gate = new();

    /// <summary>Every distinct unscoped call site this process has seen.</summary>
    public static IReadOnlyList<string> Sites
    {
        get { lock (_gate) { return _seen.OrderBy(s => s, StringComparer.Ordinal).ToList(); } }
    }

    /// <summary>Report this call, if it has no tenant and did not say it needs none.</summary>
    /// <remarks>
    /// Public because the package's pool is not the only one. ZelosHR owns a
    /// second <c>NpgsqlDataSource</c> of its own, and almost all of its queries go
    /// through that one — so the list is incomplete unless its connection factory
    /// calls this too.
    /// </remarks>
    public static void Note(ILogger logger)
    {
        if (TenantContext.Current is not null) return;
        if (TenantContext.IsDeclared) return;

        lock (_gate) { if (_seen.Count >= MaxSites) return; }

        // The endpoint first, when there is one. It is the better unit of work: what
        // has to change is "this endpoint may be reached without a tenant", not the
        // line inside a repository that five endpoints share. It is also the only
        // answer available on an EF path, where the stack at the moment the
        // connection opens holds framework frames and compiled-query delegates and
        // not one line anybody wrote.
        string? site = TenantContext.DescribeRequest() ?? Caller();
        if (site is null) return;

        var why = Why();
        var key = $"{site}|{why}";
        lock (_gate)
        {
            if (!_seen.Add(key)) return;
        }
        logger.LogWarning(
            "Database reached with no tenant in scope, from {Site} [{Why}]", site, why);
    }

    private static string Why()
    {
        var signals = TenantContext.RequestSignals;
        if (signals is null) return "no request in flight -- needs TenantContext.Scope()";
        if (signals.Count == 0) return "request carried no host, origin or x-tenant-host at all";
        var offered = string.Join(", ", signals.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                                               .Select(kv => $"{kv.Key}={kv.Value}"));
        return $"request offered but none resolved: {offered}";
    }

    /// <summary>
    /// The first application frame, walking outwards.
    /// </summary>
    /// <remarks>
    /// Skipping only this package is not enough. The call arrives through a
    /// connection factory, Dapper or EF Core, so the first non-package frame
    /// would be a framework one and every call site would collapse into the same
    /// meaningless line. System, Microsoft, Dapper and Npgsql frames go for the
    /// same reason.
    ///
    /// The type that CALLED this is skipped on the first pass, which took running
    /// it against a real app to see. A consumer with a connection pool of its own
    /// hooks this from its own plumbing -- an EF connection interceptor, a shared
    /// connection factory -- and every query in the application then reported
    /// that one class instead of the repository that asked for data: the same
    /// failure as the resolver being the loudest entry in its own list, one layer
    /// out.
    ///
    /// On the FIRST pass only, though, because a stack is not a call history.
    /// Once the plumbing has awaited, the repository that called it is no longer
    /// on the stack at all, and skipping the plumbing leaves nothing but
    /// framework frames. The second pass allows it back: naming the pool is
    /// imprecise, naming nothing is silence, and silence is the thing this whole
    /// mechanism exists to prevent.
    /// </remarks>
    private static string? Caller()
    {
        try
        {
            var trace = new StackTrace(fNeedFileInfo: true);
            // Frame 0 is this method, 1 is Note, 2 is whoever called Note.
            var reporter = RootType(trace.GetFrame(2)?.GetMethod()?.DeclaringType);
            return Walk(trace, reporter) ?? Walk(trace, null);
        }
        catch
        {
            // Instrumentation must never break a query.
            return null;
        }
    }

    private static string? Walk(StackTrace trace, Type? skip)
    {
        for (var i = 0; i < trace.FrameCount; i++)
        {
            var frame = trace.GetFrame(i);
            var method = frame?.GetMethod();
            // A dynamic method -- a compiled EF query, an expression tree, anything
            // from reflection emit -- has no declaring type and so matches none of
            // the skips below. Left in, it wins the walk and the report reads
            // ".?.lambda_method33", which names nothing at all.
            if (method?.DeclaringType is null) continue;
            var ns = method.DeclaringType.Namespace ?? "";
            if (ns.StartsWith("Trovesuite.Package", StringComparison.Ordinal) ||
                ns.StartsWith("System", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft", StringComparison.Ordinal) ||
                ns.StartsWith("Dapper", StringComparison.Ordinal) ||
                ns.StartsWith("Npgsql", StringComparison.Ordinal))
            {
                continue;
            }
            if (skip is not null && RootType(method.DeclaringType) == skip)
            {
                continue;
            }

            var (type, name) = Unmangle(method);
            var file = frame?.GetFileName();
            var line = frame?.GetFileLineNumber() ?? 0;
            return file is not null && line > 0
                ? $"{file}:{line} in {type}.{name}"
                : $"{ns}.{type}.{name}";
        }
        return null;
    }

    /// <summary>
    /// The type as it was written, with async and lambda state machines unwrapped.
    /// </summary>
    /// <remarks>
    /// Two frames inside the same method are not the same <c>DeclaringType</c>
    /// once the compiler has been through it: an async body lives on
    /// <c>&lt;Name&gt;d__7</c>, nested inside the class it was written in. Comparing
    /// the raw types would skip nothing.
    /// </remarks>
    private static Type? RootType(Type? type)
    {
        while (type is not null && type.Name.StartsWith('<') && type.DeclaringType is not null)
            type = type.DeclaringType;
        return type;
    }

    /// <summary>
    /// The type and method a frame came from, as they were written.
    /// </summary>
    /// <remarks>
    /// An async method or a lambda compiles into a nested state machine, so the
    /// frame reports <c>&lt;DispatchDueAsync&gt;d__7.MoveNext</c> on a class called
    /// <c>&lt;&gt;c__DisplayClass3_0</c>. In a Release container with no PDB there is
    /// no file and line to fall back on, which is exactly when the name is the
    /// only thing to go on -- so the original is recovered from between the angle
    /// brackets, and the real enclosing type is walked up to.
    /// </remarks>
    private static (string Type, string Name) Unmangle(System.Reflection.MethodBase? method)
    {
        var name = method?.Name ?? "?";
        var declaring = method?.DeclaringType;

        if (declaring is not null && declaring.Name.StartsWith('<'))
        {
            // <DispatchDueAsync>d__7 -> DispatchDueAsync. Leading brackets are
            // stripped rather than indexed past, because a lambda inside a
            // top-level program nests them: <<Main>$>b__0_10.
            var mangled = declaring.Name.TrimStart('<');
            var close = mangled.IndexOf('>');
            if (close > 0) name = mangled[..close];
            // <>c__DisplayClass3_0 carries no method name; the frame's own does.
            if (name.Length == 0) name = method?.Name ?? "?";
        }

        return (RootType(declaring)?.Name ?? "?", name);
    }
}
