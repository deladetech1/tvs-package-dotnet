using System.Text.RegularExpressions;

namespace Trovesuite.Package.Tenancy;

/// <summary>
/// Which storage the route in scope means.
/// </summary>
/// <remarks>
/// The .NET half of <c>trovesuite.tenancy.storage</c>, and the other half of the
/// tier model: own DATABASE means own CONTAINERS inside the shared account, own
/// SERVER means its own storage ACCOUNT. A route row has carried
/// <c>StorageAccount</c> and <c>ContainerPrefix</c> beside its db columns from
/// the start and nothing read them, so a silo tenant's documents went into the
/// shared app container with everybody else's.
///
/// A prefix is a RENAME, not a directory: <c>uploads</c> becomes
/// <c>shared-uploads</c>, because the shared account also holds the apps' own
/// containers and a tenant's blobs must not land in them. A dedicated silo owns
/// its whole account and gets no prefix.
/// </remarks>
public static class TenantStorage
{
    // https://<account>.blob.core.windows.net -> the account label only, so a
    // sovereign or government cloud suffix survives being redirected.
    private static readonly Regex AccountInUrl =
        new(@"^(https?://)([^.]+)(\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// <paramref name="url"/> pointed at the route's own storage account, if it has one.
    /// </summary>
    /// <remarks>
    /// Unchanged when the route has none, which is the right answer for a pooled
    /// tenant rather than a missing one. A URL whose shape we do not recognise is
    /// left alone: the worst case then is writing to the pod's own account, where
    /// rewriting it wrongly would be an unreadable URL for every tenant at once.
    /// </remarks>
    public static string AccountUrl(string url)
    {
        var account = TenantContext.Current?.StorageAccount;
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(url)) return url;
        return AccountInUrl.IsMatch(url)
            ? AccountInUrl.Replace(url, $"$1{account}$3", 1)
            : url;
    }

    /// <summary>
    /// <paramref name="name"/> with the route's container prefix applied, if it has one.
    /// </summary>
    /// <remarks>
    /// Idempotent, so a caller that has already resolved one does not end up with
    /// <c>shared-shared-uploads</c>.
    /// </remarks>
    public static string Container(string name)
    {
        var prefix = TenantContext.Current?.ContainerPrefix;
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(name)) return name;
        return name.StartsWith($"{prefix}-", StringComparison.Ordinal) ? name : $"{prefix}-{name}";
    }
}
