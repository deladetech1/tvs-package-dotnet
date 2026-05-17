using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Database;
using Trovesuite.Package.Notification;

namespace Trovesuite.Package.Utils;

public sealed class Helper : IHelper
{
    private readonly IDatabaseManager _db;
    private readonly INotificationService _notification;
    private readonly TrovesuiteOptions _options;
    private readonly ILogger<Helper> _logger;

    public Helper(IDatabaseManager db, INotificationService notification, IOptions<TrovesuiteOptions> options, ILogger<Helper> logger)
    {
        _db = db;
        _notification = notification;
        _options = options.Value;
        _logger = logger;
    }

    public string GenerateOtp(int length = 6)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(RandomNumberGenerator.GetInt32(0, 10));
        return sb.ToString();
    }

    public CurrentDateTimeInfo CurrentDateTime()
    {
        var now = DateTime.Now;
        var day = now.Day;
        var suffix = (day % 10) switch
        {
            1 when day is not 11 => "st",
            2 when day is not 12 => "nd",
            3 when day is not 13 => "rd",
            _ => "th",
        };

        var cdate = $"{now:dddd} {day}{suffix} {now:MMMM}, {now.Year}";
        var ctime = now.ToString("hh:mm tt");
        var cdatetime = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Local);
        return new CurrentDateTimeInfo(cdate, ctime, cdatetime);
    }

    public string GenerateUniqueIdentifier(string prefix)
    {
        const int maxLength = 63;
        var reserved = prefix.Length + 1;
        var combined = $"{Guid.NewGuid()}-{Guid.NewGuid()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(combined));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{prefix}_{hex[..(maxLength - reserved)]}";
    }

    public async Task<string> GenerateUniqueResourceIdentifierAsync(string prefix, string? tenantId, IEnumerable<Func<string, Task<long>>>? extraCheckFunctions = null, CancellationToken cancellationToken = default)
    {
        var checks = extraCheckFunctions?.ToList() ?? new List<Func<string, Task<long>>>();
        var resourceTable = _options.Tables.ResourceIds;

        while (true)
        {
            var candidate = GenerateUniqueIdentifier(prefix);

            long resourceExists;
            try
            {
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    resourceExists = await _db.ExecuteScalarAsync<long>(
                        $"SELECT COUNT(1) FROM {resourceTable} WHERE tenant_id = @tenantId AND id = @candidate",
                        new { tenantId, candidate },
                        cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    resourceExists = await _db.ExecuteScalarAsync<long>(
                        $"SELECT COUNT(1) FROM {resourceTable} WHERE id = @candidate",
                        new { candidate },
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to validate uniqueness for resource identifier {Candidate}", candidate);
                throw;
            }

            if (resourceExists > 0)
                continue;

            var duplicateFound = false;
            foreach (var check in checks)
            {
                try
                {
                    var result = await check(candidate).ConfigureAwait(false);
                    if (result > 0)
                    {
                        duplicateFound = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while executing additional uniqueness check for {Candidate}", candidate);
                    duplicateFound = true;
                    break;
                }
            }

            if (!duplicateFound)
                return candidate;
        }
    }

    public List<T> MapToDto<T>(IEnumerable<IDictionary<string, object?>> rows) where T : new()
    {
        var result = new List<T>();
        if (rows is null) return result;

        var properties = typeof(T).GetProperties()
            .Where(p => p.CanWrite)
            .ToDictionary(p => SnakeCase(p.Name), p => p, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var dto = new T();
            foreach (var kvp in row)
            {
                if (!properties.TryGetValue(kvp.Key, out var prop)) continue;
                if (kvp.Value is null)
                {
                    if (!prop.PropertyType.IsValueType || Nullable.GetUnderlyingType(prop.PropertyType) is not null)
                        prop.SetValue(dto, null);
                    continue;
                }
                try
                {
                    var targetType = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                    var converted = targetType.IsInstanceOfType(kvp.Value)
                        ? kvp.Value
                        : Convert.ChangeType(kvp.Value, targetType);
                    prop.SetValue(dto, converted);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not map column {Column} to {Property}", kvp.Key, prop.Name);
                }
            }
            result.Add(dto);
        }
        return result;
    }

    public string GenerateJwtToken(IDictionary<string, object> data, TimeSpan? expiresDelta = null)
    {
        var claims = new List<Claim>();
        foreach (var kvp in data)
            claims.Add(new Claim(kvp.Key, kvp.Value?.ToString() ?? string.Empty));

        var expires = DateTime.UtcNow + (expiresDelta ?? TimeSpan.FromMinutes(15));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Jwt.SecretKey));
        var creds = new SigningCredentials(key, ResolveSecurityAlgorithm(_options.Jwt.Algorithm));
        var token = new JwtSecurityToken(claims: claims, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string ResolveSecurityAlgorithm(string algo) => algo.ToUpperInvariant() switch
    {
        "HS256" => SecurityAlgorithms.HmacSha256,
        "HS384" => SecurityAlgorithms.HmacSha384,
        "HS512" => SecurityAlgorithms.HmacSha512,
        _ => SecurityAlgorithms.HmacSha256,
    };

    public async Task LogActivityAsync(string tenantId, string action, string resourceType, object? oldData = null, object? newData = null, string? description = null, string? userId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var tables = _options.Tables;
            if (string.IsNullOrWhiteSpace(tables.ActivityLogs))
            {
                _logger.LogError("ActivityLogs table is not configured");
                return;
            }

            var logId = GenerateUniqueIdentifier("alog");
            var timeInfo = CurrentDateTime();

            string? oldJson = oldData is null ? null : JsonSerializer.Serialize(oldData);
            string? newJson = newData is null ? null : JsonSerializer.Serialize(newData);

            string? performedByEmail = null;
            string? performedByContact = null;
            string? performedByFullname = null;

            if (!string.IsNullOrWhiteSpace(userId))
            {
                try
                {
                    var rows = await _db.ExecuteQueryAsync(
                        $"SELECT email, contact, fullname FROM {tables.Users} WHERE id = @userId AND tenant_id = @tenantId",
                        new { userId, tenantId },
                        cancellationToken).ConfigureAwait(false);
                    if (rows.Count > 0)
                    {
                        var r = rows[0];
                        performedByEmail = r.TryGetValue("email", out var e) ? e?.ToString() : null;
                        performedByContact = r.TryGetValue("contact", out var c) ? c?.ToString() : null;
                        performedByFullname = r.TryGetValue("fullname", out var f) ? f?.ToString() : null;
                    }
                    else
                    {
                        _logger.LogWarning("No user found with user_id={UserId}", userId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to fetch user information for {UserId}", userId);
                }
            }

            var rowsAffected = await _db.ExecuteUpdateAsync(
                $@"INSERT INTO {tables.ActivityLogs}
                   (id, tenant_id, action, resource_type, old_data, new_data, description,
                    performed_by_email, performed_by_contact, performed_by_fullname, cdate, ctime, cdatetime)
                   VALUES (@logId, @tenantId, @action, NULLIF(@resourceType, '')::text,
                           @oldJson::jsonb, @newJson::jsonb, @description,
                           @performedByEmail, @performedByContact, @performedByFullname,
                           @cdate, @ctime, @cdatetime)",
                new
                {
                    logId,
                    tenantId,
                    action,
                    resourceType,
                    oldJson,
                    newJson,
                    description,
                    performedByEmail,
                    performedByContact,
                    performedByFullname,
                    cdate = timeInfo.CDate,
                    ctime = timeInfo.CTime,
                    cdatetime = timeInfo.CDateTime,
                },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Activity logged successfully. Rows affected: {Rows}", rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log activity");
        }
    }

    public async Task<(string? email, string? password)> GetEmailCredentialsAsync(string? tenantId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            try
            {
                var rows = await _db.ExecuteQueryAsync(
                    $@"SELECT notification_email, notification_password
                       FROM {_options.Tables.NotificationEmailCredentials}
                       WHERE tenant_id = @tenantId AND delete_status = 'NOT_DELETED' AND is_active = true",
                    new { tenantId },
                    cancellationToken).ConfigureAwait(false);

                if (rows.Count > 0)
                {
                    var email = rows[0].TryGetValue("notification_email", out var e) ? e?.ToString() : null;
                    var password = rows[0].TryGetValue("notification_password", out var p) ? p?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
                    {
                        _logger.LogInformation("Using tenant-specific email credentials for tenant {TenantId}", tenantId);
                        return (email, password);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching tenant email credentials for tenant {TenantId}", tenantId);
            }
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
            _logger.LogInformation("Using system default email credentials for tenant {TenantId}", tenantId);

        return (_options.Mail.SenderEmail, _options.Mail.SenderPassword);
    }

    public async Task SendNotificationAsync(string email, string subject, string textTemplate, string htmlTemplate, IDictionary<string, string?>? variables = null, string? tenantId = null, CancellationToken cancellationToken = default)
    {
        var time = CurrentDateTime();
        variables ??= new Dictionary<string, string?>();
        variables["cdate"] = time.CDate;
        variables["ctime"] = time.CTime;

        var textMessage = FormatTemplate(textTemplate, variables);
        var htmlMessage = FormatTemplate(htmlTemplate, variables);

        var (senderEmail, senderPwd) = await GetEmailCredentialsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(senderPwd))
        {
            _logger.LogError("Email credentials not configured. Set Trovesuite:Mail:SenderEmail and SenderPassword (or per-tenant credentials)");
            return;
        }

        await _notification.SendEmailAsync(new NotificationEmailServiceWriteDto
        {
            SenderEmail = senderEmail,
            ReceiverEmail = new List<string> { email },
            Password = senderPwd,
            TextMessage = textMessage,
            HtmlMessage = htmlMessage,
            Subject = subject,
        }, cancellationToken).ConfigureAwait(false);
    }

    public string FormatLoginTypeText(string loginType, IEnumerable<string>? specificDays = null, string? customStart = null, string? customEnd = null)
    {
        return loginType switch
        {
            "always_login" => "Anytime - You can login at any time",
            "specific_days" when specificDays is not null && specificDays.Any() => $"Specific Days - You can login on: {string.Join(", ", specificDays)}",
            "specific_days" => "Specific Days",
            "custom" when !string.IsNullOrEmpty(customStart) && !string.IsNullOrEmpty(customEnd) => $"Custom Period - From {customStart} to {customEnd}",
            "custom" when !string.IsNullOrEmpty(customStart) => $"Custom Period - Starting from {customStart}",
            "custom" => "Custom Period",
            _ => "Login access granted",
        };
    }

    public async Task<List<AdminUserInfo>> GetUsersWithAdminRolesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var t = _options.Tables;
            var sql = $@"
                SELECT DISTINCT u.id as user_id, u.email, u.fullname
                FROM {t.Users} u
                WHERE u.tenant_id = @tenantId
                  AND u.delete_status = 'NOT_DELETED'
                  AND u.is_active = true
                  AND u.can_login = true
                  AND (
                    (u.id, u.tenant_id) IN (
                      SELECT ar.user_id, ar.tenant_id
                      FROM {t.AssignRoles} ar
                      INNER JOIN {t.Roles} r ON ar.role_id = r.id AND ar.tenant_id = r.tenant_id
                      WHERE ar.tenant_id = @tenantId
                        AND ar.delete_status = 'NOT_DELETED' AND ar.is_active = true
                        AND r.delete_status = 'NOT_DELETED' AND r.is_active = true
                        AND r.role_name IN ('role-owner', 'role-admin')
                        AND ar.user_id IS NOT NULL
                    )
                    OR
                    (u.id, u.tenant_id) IN (
                      SELECT ug.user_id, ug.tenant_id
                      FROM {t.UserGroups} ug
                      INNER JOIN {t.AssignRoles} ar ON ug.group_id = ar.group_id AND ug.tenant_id = ar.tenant_id
                      INNER JOIN {t.Roles} r ON ar.role_id = r.id AND ar.tenant_id = r.tenant_id
                      WHERE ug.tenant_id = @tenantId AND ar.tenant_id = @tenantId
                        AND ug.delete_status = 'NOT_DELETED' AND ug.is_active = true
                        AND ar.delete_status = 'NOT_DELETED' AND ar.is_active = true
                        AND r.delete_status = 'NOT_DELETED' AND r.is_active = true
                        AND r.role_name IN ('role-owner', 'role-admin')
                        AND ar.group_id IS NOT NULL
                    )
                  )";

            var rows = await _db.ExecuteQueryAsync(sql, new { tenantId }, cancellationToken).ConfigureAwait(false);
            var admins = new List<AdminUserInfo>();
            foreach (var row in rows)
            {
                var userId = row.TryGetValue("user_id", out var u) ? u?.ToString() : null;
                var email = row.TryGetValue("email", out var e) ? e?.ToString() : null;
                var fullname = row.TryGetValue("fullname", out var f) ? f?.ToString() : null;
                if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email)) continue;
                var name = string.IsNullOrWhiteSpace(fullname) ? "Admin" : fullname!.Trim();
                admins.Add(new AdminUserInfo(userId, email, name));
            }
            _logger.LogInformation("Found {Count} admin users for tenant {TenantId}", admins.Count, tenantId);
            return admins;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get admin users for tenant {TenantId}", tenantId);
            return new List<AdminUserInfo>();
        }
    }

    public async Task NotifyAdminsOfDeleteStatusChangeAsync(string tenantId, string resourceType, string resourceName, string status, string? actorUserId = null, string? message = null, CancellationToken cancellationToken = default)
    {
        var statusKey = (status ?? string.Empty).ToUpperInvariant();
        var statusConfig = statusKey switch
        {
            "PENDING" => new StatusConfig("Deletion Pending Approval", "Pending deletion request for {resource_name}",
                "A deletion request has been submitted for the {resource_type} \"{resource_name}\" and is awaiting approval.",
                "Pending Deletion", "#ffc107", "⏳"),
            "NOT_DELETED" => new StatusConfig("Resource Restored", "Resource restored: {resource_name}",
                "The {resource_type} \"{resource_name}\" has been restored.",
                "Restored", "#28a745", "♻️"),
            _ => new StatusConfig("Resource Deleted", "Resource deleted: {resource_name}",
                "The {resource_type} \"{resource_name}\" has been deleted.",
                "Deleted", "#dc3545", "🗑️"),
        };

        try
        {
            var admins = await GetUsersWithAdminRolesAsync(tenantId, cancellationToken).ConfigureAwait(false);
            if (admins.Count == 0)
            {
                _logger.LogInformation("No admin users found to notify for tenant {TenantId}", tenantId);
                return;
            }

            var actorName = "System";
            var actorEmail = "no-reply@trovesuite.com";

            if (!string.IsNullOrWhiteSpace(actorUserId))
            {
                var rows = await _db.ExecuteQueryAsync(
                    $"SELECT fullname, email FROM {_options.Tables.Users} WHERE id = @actorUserId AND tenant_id = @tenantId",
                    new { actorUserId, tenantId },
                    cancellationToken).ConfigureAwait(false);
                if (rows.Count > 0)
                {
                    var actorFullname = (rows[0].TryGetValue("fullname", out var f) ? f?.ToString() : null)?.Trim();
                    var actorEmailFromDb = rows[0].TryGetValue("email", out var e) ? e?.ToString() : null;
                    if (!string.IsNullOrWhiteSpace(actorFullname)) actorName = actorFullname;
                    else if (!string.IsNullOrWhiteSpace(actorEmailFromDb)) actorName = actorEmailFromDb;
                    if (!string.IsNullOrWhiteSpace(actorEmailFromDb)) actorEmail = actorEmailFromDb;
                }
            }

            var resourceNameDisplay = string.IsNullOrWhiteSpace(resourceName) ? "Unknown resource" : resourceName;
            var resourceTypeDisplay = string.IsNullOrWhiteSpace(resourceType) ? "Resource" : resourceType;

            var messageText = string.IsNullOrWhiteSpace(message) ? string.Empty : $"Message: {message}\n";
            var messageRowStyle = string.IsNullOrWhiteSpace(message) ? "display: none;" : string.Empty;
            var messageValue = string.IsNullOrWhiteSpace(message) ? "No additional message provided." : message;

            var subject = statusConfig.Subject
                .Replace("{resource_name}", resourceNameDisplay)
                .Replace("{resource_type}", resourceTypeDisplay);

            var statusDescription = statusConfig.Description
                .Replace("{resource_name}", resourceNameDisplay)
                .Replace("{resource_type}", resourceTypeDisplay);

            foreach (var admin in admins)
            {
                await SendNotificationAsync(
                    email: admin.Email,
                    subject: subject,
                    textTemplate: Templates.ResourceStatusChangeTextTemplate,
                    htmlTemplate: Templates.ResourceStatusChangeHtmlTemplate,
                    variables: new Dictionary<string, string?>
                    {
                        ["admin_name"] = admin.Name,
                        ["resource_type"] = resourceTypeDisplay,
                        ["resource_name"] = resourceNameDisplay,
                        ["status_display"] = statusConfig.Display,
                        ["status_description"] = statusDescription,
                        ["status_title"] = statusConfig.Title,
                        ["status_color"] = statusConfig.Color,
                        ["status_icon"] = statusConfig.Icon,
                        ["actor_name"] = actorName,
                        ["actor_email"] = actorEmail,
                        ["message"] = messageValue,
                        ["message_text"] = messageText,
                        ["message_row_style"] = messageRowStyle,
                        ["app_url"] = _options.App.AppUrl,
                    },
                    tenantId: tenantId,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation("Delete status change notifications sent to {Count} admins", admins.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send delete status change notifications");
        }
    }

    internal static string FormatTemplate(string template, IDictionary<string, string?> variables)
    {
        if (string.IsNullOrEmpty(template) || variables is null || variables.Count == 0) return template;
        var sb = new StringBuilder(template);
        foreach (var kvp in variables)
            sb.Replace("{" + kvp.Key + "}", kvp.Value ?? string.Empty);
        return sb.ToString();
    }

    private static string SnakeCase(string pascalOrCamel)
    {
        if (string.IsNullOrEmpty(pascalOrCamel)) return pascalOrCamel;
        var sb = new StringBuilder(pascalOrCamel.Length + 8);
        for (int i = 0; i < pascalOrCamel.Length; i++)
        {
            var c = pascalOrCamel[i];
            if (char.IsUpper(c) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private sealed record StatusConfig(string Title, string Subject, string Description, string Display, string Color, string Icon);
}
