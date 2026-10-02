using Microsoft.Extensions.Logging;

namespace Trovesuite.Package.Tenancy;

/// <summary>
/// Running background work once per database, rather than once per request.
/// </summary>
/// <remarks>
/// The mirror of <c>trovesuite.tenancy.for_each_database</c> on the Python side,
/// so both halves of the suite convert the same way.
///
/// A timer has no request, so it has no tenant, and the markers left by Phase 1
/// (<see cref="TenantContext.SpansAllTenants"/>) record every place that is true.
/// Converting them needs an answer to "once per what?", and the obvious answer is
/// wrong.
///
/// <para><b>Not once per tenant.</b> On dev there are twenty-plus active routes,
/// three tenants, and not one route that names a tenant — every pooled row has a
/// null <c>tenant_id</c>, because a pooled address serves all of them and the
/// token says which. The route table enumerates ADDRESSES. It cannot enumerate
/// tenants and was never meant to.</para>
///
/// <para><b>Once per database.</b> That is the thing work actually has to repeat
/// itself for: a query reaches one database, and when a tenant gets its own, the
/// job has to reach that one too. Within a database the existing query is already
/// right — tenants sharing a database are meant to be handled together.</para>
///
/// So the conversion is small and its first effect is nothing at all: one distinct
/// database today, so the loop runs once and behaves exactly as it did.
/// </remarks>
public static class DatabaseFanout
{
    /// <summary>One route per distinct database among the ACTIVE routes.</summary>
    /// <remarks>
    /// Keyed on (server, database name), with both null meaning "the database this
    /// pod was configured with" — which is every POOLED row, and therefore one
    /// entry however many hosts point at it. The representative route is arbitrary
    /// within a key, and that is fine: what the scope decides is which database to
    /// talk to, and every row sharing a key agrees about that by construction.
    /// </remarks>
    public static async Task<IReadOnlyList<TenantRoute>> ActiveDatabaseScopesAsync(
        ITenantRouteResolver resolver, CancellationToken cancellationToken = default)
    {
        var routes = await resolver.ActiveRoutesAsync(cancellationToken).ConfigureAwait(false);
        var seen = new Dictionary<(string?, string?), TenantRoute>();
        foreach (var route in routes)
        {
            var key = (route.DbServerFqdn, route.DbName);
            if (!seen.ContainsKey(key)) seen[key] = route;
        }
        return seen.Values.ToList();
    }

    /// <summary>
    /// Run <paramref name="work"/> once per distinct database, isolating failures.
    /// </summary>
    /// <remarks>
    /// The try/catch is OUTSIDE the scope and INSIDE the loop, deliberately. One
    /// database being unreachable — a silo mid-migration, a credential rotated an
    /// hour ago — must not stop the others, or one tenant's outage becomes
    /// everybody's.
    ///
    /// If the control plane cannot be read the work still runs, ONCE, unscoped.
    /// That is what it did before any of this existed. Work that silently stops
    /// because a lookup failed is a far worse failure than the one it would be
    /// guarding against: nothing in the log looks like an error, and the job simply
    /// never happens.
    /// </remarks>
    public static async Task<IReadOnlyList<T>> ForEachDatabaseAsync<T>(
        string job,
        ITenantRouteResolver resolver,
        ILogger logger,
        Func<Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        var targets = await ActiveDatabaseScopesAsync(resolver, cancellationToken)
                            .ConfigureAwait(false);

        if (targets.Count == 0)
        {
            logger.LogWarning(
                "{Job}: no active routes to iterate, so running once against this pod's " +
                "own database. Expected before the control-plane migration has run; a " +
                "fault afterwards.", job);
            return new[] { await work().ConfigureAwait(false) };
        }

        var results = new List<T>();
        foreach (var route in targets)
        {
            try
            {
                using (TenantContext.Scope(route))
                {
                    results.Add(await work().ConfigureAwait(false));
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "{Job} failed for database {Database} on {Server} (host {Host}); " +
                    "continuing with the rest",
                    job, route.DbName ?? "<this pod's own>",
                    route.DbServerFqdn ?? "<configured>", route.Host);
            }
        }
        return results;
    }

    /// <summary>The void-returning form, for work whose result nobody reads.</summary>
    public static async Task ForEachDatabaseAsync(
        string job,
        ITenantRouteResolver resolver,
        ILogger logger,
        Func<Task> work,
        CancellationToken cancellationToken = default)
    {
        await ForEachDatabaseAsync<bool>(
            job, resolver, logger,
            async () => { await work().ConfigureAwait(false); return true; },
            cancellationToken).ConfigureAwait(false);
    }
}
