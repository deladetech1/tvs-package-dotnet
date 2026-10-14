namespace Trovesuite.Package.Auth;

/// <summary>
/// A token for one tenant was presented at an address that belongs to another.
/// </summary>
/// <remarks>
/// An <see cref="UnauthorizedAccessException"/>, so code that already treats a failed
/// <c>DecodeToken</c> as "not signed in" keeps doing so.
/// </remarks>
public sealed class TenantMismatchException : UnauthorizedAccessException
{
    public const string Code = "TENANT_MISMATCH";

    public const string Detail = "This sign-in is not valid at this address. Please sign in again here.";

    public TenantMismatchException() : base(Detail) { }
}
