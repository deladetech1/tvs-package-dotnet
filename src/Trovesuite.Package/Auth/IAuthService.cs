using Trovesuite.Package.Entities;

namespace Trovesuite.Package.Auth;

public interface IAuthService
{
    IDictionary<string, string?> DecodeToken(string token);
    Task<Respons<AuthServiceReadDto>> AuthorizeAsync(AuthServiceWriteDto data, CancellationToken cancellationToken = default);
    Task<Respons<AuthServiceReadDto>> AuthorizeUserFromTokenAsync(string token, CancellationToken cancellationToken = default);
    IDictionary<string, string?> GetUserInfoFromToken(string token);
    bool CheckPermission(IEnumerable<AuthServiceReadDto> usersData, string? action = null, string? resourceType = null);
    List<string> GetUserPermissions(IEnumerable<AuthServiceReadDto> userRoles);
    bool HasAnyPermission(IEnumerable<AuthServiceReadDto> userRoles, IEnumerable<string> requiredPermissions);
    bool HasAllPermissions(IEnumerable<AuthServiceReadDto> userRoles, IEnumerable<string> requiredPermissions);
}
