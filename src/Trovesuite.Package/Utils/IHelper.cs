namespace Trovesuite.Package.Utils;

public interface IHelper
{
    string GenerateOtp(int length = 6);
    CurrentDateTimeInfo CurrentDateTime();
    string GenerateUniqueIdentifier(string prefix);
    Task<string> GenerateUniqueResourceIdentifierAsync(string prefix, string? tenantId, IEnumerable<Func<string, Task<long>>>? extraCheckFunctions = null, CancellationToken cancellationToken = default);
    List<T> MapToDto<T>(IEnumerable<IDictionary<string, object?>> rows) where T : new();
    string GenerateJwtToken(IDictionary<string, object> data, TimeSpan? expiresDelta = null);
    Task LogActivityAsync(string tenantId, string action, string resourceType, object? oldData = null, object? newData = null, string? description = null, string? userId = null, CancellationToken cancellationToken = default);
    Task<(string? email, string? password)> GetEmailCredentialsAsync(string? tenantId = null, CancellationToken cancellationToken = default);
    Task SendNotificationAsync(string email, string subject, string textTemplate, string htmlTemplate, IDictionary<string, string?>? variables = null, string? tenantId = null, CancellationToken cancellationToken = default);
    string FormatLoginTypeText(string loginType, IEnumerable<string>? specificDays = null, string? customStart = null, string? customEnd = null);
    Task<List<AdminUserInfo>> GetUsersWithAdminRolesAsync(string tenantId, CancellationToken cancellationToken = default);
    Task NotifyAdminsOfDeleteStatusChangeAsync(string tenantId, string resourceType, string resourceName, string status, string? actorUserId = null, string? message = null, CancellationToken cancellationToken = default);
}

public sealed record CurrentDateTimeInfo(string CDate, string CTime, DateTime CDateTime);

public sealed record AdminUserInfo(string UserId, string Email, string Name);
