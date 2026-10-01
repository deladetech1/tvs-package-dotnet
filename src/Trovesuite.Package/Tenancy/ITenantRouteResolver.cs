namespace Trovesuite.Package.Tenancy;

/// <summary>Host -> route lookup.</summary>
public interface ITenantRouteResolver
{
    /// <summary>The route for <paramref name="host"/>, or null if no row claims it.</summary>
    Task<TenantRoute?> ResolveAsync(string? host, CancellationToken cancellationToken = default);

    /// <summary>Drop cached answers — one host, or all of them when null.</summary>
    void Invalidate(string? host = null);

    /// <summary>Every ACTIVE route, for work that iterates tenants instead of serving a request.</summary>
    Task<IReadOnlyList<TenantRoute>> ActiveRoutesAsync(CancellationToken cancellationToken = default);
}
