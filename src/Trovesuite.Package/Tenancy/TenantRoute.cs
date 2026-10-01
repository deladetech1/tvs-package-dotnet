namespace Trovesuite.Package.Tenancy;

/// <summary>Tiers a host can be served in. Mirrors control_plane.ctl_tenant_routes.tier.</summary>
public static class TenantTier
{
    public const string Pooled = "POOLED";
    public const string SiloShared = "SILO_SHARED";
    public const string SiloDedicated = "SILO_DEDICATED";
    public const string SelfManaged = "SELF_MANAGED";
}

/// <summary>One row of <c>control_plane.ctl_tenant_routes</c>, resolved for a request.</summary>
public sealed record TenantRoute
{
    public required string Host { get; init; }
    public required string Tier { get; init; }
    public required string CellKey { get; init; }
    public string Status { get; init; } = "ACTIVE";

    /// <summary>
    /// Null at the apex, which serves every pooled tenant.
    /// </summary>
    /// <remarks>
    /// When this IS set, the request's token must carry the same tenant or the
    /// request is rejected — that check is what pins a tenant to its own address.
    /// </remarks>
    public string? TenantId { get; init; }

    public string? DbServerFqdn { get; init; }
    public string? DbName { get; init; }

    /// <summary>A Key Vault secret URI, never a credential.</summary>
    public string? DbSecretUri { get; init; }

    public string? StorageAccount { get; init; }
    public string? ContainerPrefix { get; init; }
    public string? StorageSecretUri { get; init; }

    /// <summary>For a host answered by another cell, or by a customer's own deployment.</summary>
    public string? ApiBase { get; init; }
    public string? SchemaVersion { get; init; }

    /// <summary>
    /// Whether this route points at a database other than the one this pod was
    /// configured with. Reads the tier rather than sniffing for a populated
    /// column, because the database refuses to store a silo row without one
    /// (ck_ctl_routes_silo_has_db).
    /// </summary>
    public bool HasOwnDatabase =>
        Tier is TenantTier.SiloShared or TenantTier.SiloDedicated;

    /// <summary>
    /// Whether the address itself names one tenant. False only for the apex;
    /// a token for a different tenant arriving at a pinned address is a 401.
    /// </summary>
    public bool IsPinnedToTenant => !string.IsNullOrEmpty(TenantId);
}
