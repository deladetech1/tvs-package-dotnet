using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Trovesuite.Package.Configuration;
using Trovesuite.Package.Database;
using Trovesuite.Package.Entities;

namespace Trovesuite.Package.Auth;

public sealed class AuthService : IAuthService
{
    private const string SystemTenantId = "system-tenant-id";

    private readonly IDatabaseManager _db;
    private readonly TrovesuiteOptions _options;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IDatabaseManager db, IOptions<TrovesuiteOptions> options, ILogger<AuthService> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public IDictionary<string, string?> DecodeToken(string token)
    {
        var principal = ValidateToken(token);
        var userId = principal.FindFirst("user_id")?.Value;
        var tenantId = principal.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantId))
            throw new UnauthorizedAccessException("Could not validate credentials");

        return new Dictionary<string, string?>
        {
            ["user_id"] = userId,
            ["tenant_id"] = tenantId,
        };
    }

    public IDictionary<string, string?> GetUserInfoFromToken(string token) => DecodeToken(token);

    public async Task<Respons<AuthServiceReadDto>> AuthorizeUserFromTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var info = DecodeToken(token);
        var data = new AuthServiceWriteDto
        {
            UserId = info["user_id"],
            TenantId = info["tenant_id"],
        };
        return await AuthorizeAsync(data, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Respons<AuthServiceReadDto>> AuthorizeAsync(AuthServiceWriteDto data, CancellationToken cancellationToken = default)
    {
        var userId = data.UserId;
        var tenantId = data.TenantId;

        if (string.IsNullOrWhiteSpace(userId))
            return Respons<AuthServiceReadDto>.Fail("INVALID_USER_ID", "Invalid user_id: must be a non-empty string", 400);

        if (string.IsNullOrWhiteSpace(tenantId))
            return Respons<AuthServiceReadDto>.Fail("INVALID_TENANT_ID", "Invalid tenant_id: must be a non-empty string", 400);

        try
        {
            var t = _options.Tables;

            // The read-only verb list, once per process. Here rather than inside the
            // synchronous permission check, which must not block on a query.
            await LoadReadOnlyActionsAsync(cancellationToken).ConfigureAwait(false);

            var tenantRows = await _db.ExecuteQueryAsync(
                $"SELECT is_verified FROM {t.Tenants} WHERE delete_status = 'NOT_DELETED' AND id = @tenantId",
                new { tenantId },
                cancellationToken).ConfigureAwait(false);

            if (tenantRows.Count == 0)
            {
                _logger.LogWarning("Authorization failed - tenant not found: {TenantId}", tenantId);
                return Respons<AuthServiceReadDto>.Fail("TENANT_NOT_FOUND", $"Tenant '{tenantId}' not found or has been deleted", 404);
            }

            var isVerified = ToBool(tenantRows[0]["is_verified"]);
            if (!isVerified)
            {
                _logger.LogWarning("Authorization failed - tenant not verified for user {UserId}, tenant {TenantId}", userId, tenantId);
                return Respons<AuthServiceReadDto>.Fail("TENANT_NOT_VERIFIED", $"Tenant '{tenantId}' is not verified. Please contact your administrator.", 403);
            }

            var userGroupsRows = await _db.ExecuteQueryAsync(
                $@"SELECT group_id FROM {t.UserGroups}
                   WHERE tenant_id = @tenantId AND delete_status = 'NOT_DELETED' AND is_active = true
                     AND user_id = @userId AND (is_system = false OR is_system IS NULL)",
                new { tenantId, userId },
                cancellationToken).ConfigureAwait(false);

            var groupIds = userGroupsRows
                .Select(r => r["group_id"]?.ToString())
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Select(g => g!)
                .ToArray();

            IReadOnlyList<IDictionary<string, object?>> loginSettings;
            if (groupIds.Length > 0)
            {
                loginSettings = await _db.ExecuteQueryAsync(
                    $@"SELECT user_id, group_id, is_suspended, can_always_login,
                              is_multi_factor_enabled, is_login_before, working_days,
                              login_on, logout_on
                       FROM {t.LoginSettings}
                       WHERE tenant_id = @tenantId AND delete_status = 'NOT_DELETED' AND is_active = true
                         AND (user_id = @userId OR group_id = ANY(@groupIds))
                       ORDER BY user_id NULLS LAST
                       LIMIT 1",
                    new { tenantId, userId, groupIds },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                loginSettings = await _db.ExecuteQueryAsync(
                    $@"SELECT user_id, group_id, is_suspended, can_always_login,
                              is_multi_factor_enabled, is_login_before, working_days,
                              login_on, logout_on
                       FROM {t.LoginSettings}
                       WHERE tenant_id = @tenantId AND delete_status = 'NOT_DELETED' AND is_active = true
                         AND user_id = @userId",
                    new { tenantId, userId },
                    cancellationToken).ConfigureAwait(false);
            }

            if (loginSettings.Count == 0)
            {
                _logger.LogWarning("Authorization failed - user not found: {UserId} in tenant {TenantId}", userId, tenantId);
                return Respons<AuthServiceReadDto>.Fail("USER_NOT_FOUND", $"User '{userId}' not found in tenant '{tenantId}' or account is inactive", 404);
            }

            var firstSetting = loginSettings[0];
            if (ToBool(firstSetting["is_suspended"]))
            {
                _logger.LogWarning("Authorization failed - user suspended: {UserId}", userId);
                return Respons<AuthServiceReadDto>.Fail("USER_SUSPENDED", "Your account has been suspended. Please contact your administrator.", 403);
            }

            if (!ToBool(firstSetting["can_always_login"]))
            {
                var loginOn = firstSetting["login_on"] as DateTime?;
                var logoutOn = firstSetting["logout_on"] as DateTime?;
                var workingDays = firstSetting["working_days"] as string[];

                if (loginOn.HasValue && logoutOn.HasValue)
                {
                    var now = DateTime.UtcNow;
                    now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);
                    if (now < loginOn.Value || now > logoutOn.Value)
                    {
                        _logger.LogWarning("Authorization failed - outside allowed period for user {UserId}", userId);
                        return Respons<AuthServiceReadDto>.Fail("LOGIN_TIME_RESTRICTED", "Access is not allowed at this time. Please check your access schedule.", 403);
                    }
                }
                else if (workingDays is { Length: > 0 })
                {
                    var currentDay = DateTime.Now.DayOfWeek.ToString().ToUpperInvariant();
                    if (!workingDays.Any(d => string.Equals(d, currentDay, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogWarning("Authorization failed - not a working day for user {UserId}", userId);
                        return Respons<AuthServiceReadDto>.Fail("LOGIN_DAY_RESTRICTED", "Access is not allowed on this day. Please contact your administrator.", 403);
                    }
                }
            }

            IReadOnlyList<IDictionary<string, object?>> userRoles;
            if (groupIds.Length > 0)
            {
                userRoles = await _db.ExecuteQueryAsync(
                    $@"SELECT DISTINCT ON (group_id, user_id, role_id)
                              group_id, user_id, role_id, resource_type
                       FROM {t.AssignRoles}
                       WHERE tenant_id = @tenantId AND delete_status = 'NOT_DELETED'
                         AND is_active = true
                         AND (is_system = false OR is_system IS NULL)
                         AND (user_id = @userId OR group_id = ANY(@groupIds))
                       ORDER BY group_id, user_id, role_id;",
                    new { tenantId, userId, groupIds },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                userRoles = await _db.ExecuteQueryAsync(
                    $@"SELECT DISTINCT ON (user_id, role_id)
                              user_id, role_id, resource_type
                       FROM {t.AssignRoles}
                       WHERE tenant_id = @tenantId AND delete_status = 'NOT_DELETED'
                         AND is_active = true
                         AND (is_system = false OR is_system IS NULL)
                         AND user_id = @userId
                       ORDER BY user_id, role_id;",
                    new { tenantId, userId },
                    cancellationToken).ConfigureAwait(false);
            }

            var systemRoles = await _db.ExecuteQueryAsync(
                $@"SELECT DISTINCT COALESCE(sar.group_id::TEXT, NULL) as group_id, sar.user_id, sar.role_id, sar.resource_type
                   FROM {t.AssignRoles} sar
                   LEFT JOIN {t.UserGroups} sug
                     ON sar.group_id = sug.group_id AND sug.user_id = @userId
                        AND sug.is_system = true AND sug.is_active = true AND sug.delete_status = 'NOT_DELETED'
                   WHERE sar.tenant_id = @systemTenantId
                     AND sar.is_system = true
                     AND sar.delete_status = 'NOT_DELETED'
                     AND sar.is_active = true
                     AND (sar.user_id = @userId OR sug.user_id IS NOT NULL)",
                new { userId, systemTenantId = SystemTenantId },
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Found {Count} system-level role(s) for user {UserId}", systemRoles.Count, userId);

            var allRoles = userRoles.Concat(systemRoles).ToList();
            _logger.LogInformation("Total roles (tenant + system) for user {UserId}: {Count}", userId, allRoles.Count);

            var roleIds = allRoles
                .Select(r => r.TryGetValue("role_id", out var v) ? v?.ToString() : null)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct()
                .ToArray();

            var systemRoleIds = new HashSet<string>(StringComparer.Ordinal);
            if (roleIds.Length > 0)
            {
                try
                {
                    var systemRoleCheck = await _db.ExecuteQueryAsync(
                        $@"SELECT id FROM {t.Roles}
                           WHERE id = ANY(@roleIds) AND is_system = true AND delete_status = 'NOT_DELETED'",
                        new { roleIds },
                        cancellationToken).ConfigureAwait(false);

                    foreach (var row in systemRoleCheck)
                    {
                        var id = row.TryGetValue("id", out var v) ? v?.ToString() : null;
                        if (!string.IsNullOrWhiteSpace(id))
                            systemRoleIds.Add(id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error checking system roles; falling back to system_roles query result");
                    foreach (var r in systemRoles)
                    {
                        var id = r.TryGetValue("role_id", out var v) ? v?.ToString() : null;
                        if (!string.IsNullOrWhiteSpace(id))
                            systemRoleIds.Add(id);
                    }
                }
            }

            var result = new List<AuthServiceReadDto>();
            foreach (var role in allRoles)
            {
                var roleId = role.TryGetValue("role_id", out var rv) ? rv?.ToString() : null;
                if (string.IsNullOrWhiteSpace(roleId))
                {
                    _logger.LogWarning("Skipping role with missing role_id");
                    continue;
                }

                var isSystemRole = systemRoleIds.Contains(roleId);
                var primaryTenantId = isSystemRole ? SystemTenantId : tenantId;
                var fallbackTenantId = isSystemRole ? tenantId : SystemTenantId;

                IReadOnlyList<IDictionary<string, object?>> permissions;
                try
                {
                    permissions = await _db.ExecuteQueryAsync(
                        $@"SELECT permission_id, app_prefix, resource_key, action, target, scope
                           FROM {t.RolePermissions}
                           WHERE role_id = @roleId AND tenant_id = @primaryTenantId AND delete_status = 'NOT_DELETED'",
                        new { roleId, primaryTenantId },
                        cancellationToken).ConfigureAwait(false);

                    if (permissions.Count == 0)
                    {
                        var fallback = await _db.ExecuteQueryAsync(
                            $@"SELECT permission_id, app_prefix, resource_key, action, target, scope
                               FROM {t.RolePermissions}
                               WHERE role_id = @roleId AND tenant_id = @fallbackTenantId AND delete_status = 'NOT_DELETED'",
                            new { roleId, fallbackTenantId },
                            cancellationToken).ConfigureAwait(false);
                        if (fallback.Count > 0) permissions = fallback;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error querying permissions for role {RoleId}", roleId);
                    permissions = Array.Empty<IDictionary<string, object?>>();
                }

                var roleUserId = role.TryGetValue("user_id", out var ruv) ? ruv?.ToString() : null;
                var groupIdFromRole = role.TryGetValue("group_id", out var giv) ? giv?.ToString() : null;
                var resourceType = role.TryGetValue("resource_type", out var rtv) ? rtv?.ToString() : null;

                result.Add(new AuthServiceReadDto
                {
                    GroupId = groupIdFromRole,
                    UserId = roleUserId ?? userId,
                    RoleId = roleId,
                    TenantId = tenantId,
                    ResourceType = resourceType,
                    Permissions = PermissionKeys(permissions),
                });
            }

            _logger.LogInformation("Authorization successful for user: {UserId} with {Count} total role entries", userId, result.Count);

            return Respons<AuthServiceReadDto>.Ok(result, "Authorized", 200);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authorization check failed for user {UserId}", userId);
            return Respons<AuthServiceReadDto>.Fail("Authorization check failed due to an internal error", null, 500);
        }
    }

    public bool CheckPermission(IEnumerable<AuthServiceReadDto> usersData, string? action = null, string? resourceType = null)
    {
        foreach (var entry in usersData)
        {
            if (!string.IsNullOrEmpty(resourceType) && !string.IsNullOrEmpty(entry.ResourceType) &&
                !string.Equals(entry.ResourceType, resourceType, StringComparison.Ordinal))
                continue;

            if (!string.IsNullOrEmpty(action) && entry.Permissions is not null && entry.Permissions.Contains(action))
                return true;
        }
        return false;
    }

    public List<string> GetUserPermissions(IEnumerable<AuthServiceReadDto> userRoles)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var role in userRoles)
        {
            if (role.Permissions is null) continue;
            foreach (var p in role.Permissions) set.Add(p);
        }
        return set.ToList();
    }

    /// <summary>
    /// How a permission is named when it is named by what it grants rather than by its id.
    /// </summary>
    /// <remarks>
    /// "msg|store-sales|create||any" -- app, resource, verb, target, scope. The generated
    /// permission modules build the same string on both sides of the wire, so a C# app and
    /// a Python one ask the same question of the same rows without either knowing the
    /// other's spelling of it. Must stay byte-identical to AuthService.pair_key in
    /// tvs-package, separator included.
    /// </remarks>
    public const char PairSeparator = '|';

    /// <summary>The canonical name for a permission, from its parts.</summary>
    public static string PairKey(string? appPrefix, string? resourceKey, string? action, string? target, string? scope) =>
        string.Join(PairSeparator, new[]
        {
            appPrefix ?? string.Empty,
            resourceKey ?? string.Empty,
            action ?? string.Empty,
            target ?? string.Empty,
            string.IsNullOrEmpty(scope) ? "any" : scope,
        });

    /// <summary>Every way a grant can be named: its id AND its pair.</summary>
    /// <remarks>
    /// Both, deliberately, for the length of one release cycle. This package is a hard
    /// pin, so an app on an older build still asks for permission ids while one on a newer
    /// generated module asks for pairs; emitting only one of them would mean whichever app
    /// moved second matched nothing and refused every request. The ids come out once
    /// nothing asks for them. The Python package carries the same pair of names.
    /// </remarks>
    private static List<string> PermissionKeys(IReadOnlyList<IDictionary<string, object?>> rows)
    {
        var keys = new List<string>();
        foreach (var r in rows ?? Array.Empty<IDictionary<string, object?>>())
        {
            var id = Field(r, "permission_id");
            if (!string.IsNullOrWhiteSpace(id)) keys.Add(id!);

            var resource = Field(r, "resource_key");
            if (!string.IsNullOrWhiteSpace(resource))
                keys.Add(PairKey(Field(r, "app_prefix"), resource, Field(r, "action"),
                                 Field(r, "target"), Field(r, "scope")));
        }
        return keys;
    }

    private static string? Field(IDictionary<string, object?> row, string name) =>
        row.TryGetValue(name, out var v) ? v?.ToString() : null;

    /// <summary>The role that may do anything.</summary>
    /// <remarks>
    /// Checked by role, not by a wildcard permission row. The auto-assign trigger grants
    /// Admin every permission inserted except log modification, so a "permission-all" row
    /// would have made Admin omnipotent the moment it landed.
    /// </remarks>
    public const string OwnerRoleId = "role-owner";

    /// <summary>An app's admin may do anything inside that app.</summary>
    /// <remarks>
    /// These roles already held every permission of their own app, kept complete by seeds
    /// and triggers that have to be got right on every change. The role says it once.
    /// </remarks>
    /// <summary>Admin: every app, but never a write to logs.</summary>
    /// <remarks>
    /// Kept apart from Owner because the exception is real -- an owner may delete an audit
    /// trail and an admin may not, and collapsing the two would quietly hand that back.
    /// </remarks>
    private static readonly HashSet<string> EveryAppAdminRoles =
        new(StringComparer.Ordinal) { "role-admin" };

    private static readonly Dictionary<string, string> AppAdminRoles = new(StringComparer.Ordinal)
    {
        ["role-subscribed-app-msg-admin"] = "msg",
        ["role-subscribed-app-loandrift-admin"] = "loandrift",
        ["role-subscribed-app-zeloshr-admin"] = "zeloshr",
    };

    public static bool IsOwner(IEnumerable<AuthServiceReadDto> userRoles) =>
        userRoles is not null && userRoles.Any(r =>
            string.Equals(r.RoleId, OwnerRoleId, StringComparison.Ordinal));

    /// <summary>Roles that cover Core Platform, whose permissions carry '' or 'cp'.</summary>
    private static readonly HashSet<string> CoreAdminRoles =
        new(StringComparer.Ordinal) { "role-cp-admin" };

    /// <summary>Resources whose modification no admin gets by being an admin. Reading is fine.</summary>
    /// <remarks>
    /// Two keys for one idea, because the apps do not agree on the name: Core Platform,
    /// MyStoreGuard and LoanDrift call it `logs`, ZelosHR calls its own `audit`. Checking only
    /// `logs` meant an admin was covered for `zeloshr|audit|delete` -- the audit trail the
    /// rule exists to protect -- and the id-prefix rule this replaced had the same hole, since
    /// `permission-zeloshr-audit-delete` does not contain `-logs-` either.
    /// </remarks>
    private static readonly HashSet<string> ProtectedResources =
        new(StringComparer.Ordinal) { "logs", "audit" };

    /// <summary>The verbs cp_actions marks read-only, read once per process and kept.</summary>
    /// <remarks>
    /// Read from the database rather than listed here: a hand-written list of read verbs
    /// missed `statistics` once already, and the consequence there was a wrong report --
    /// here it would be an admin quietly able to delete an audit trail.
    ///
    /// Filled during authorization, which is async and already holds the connection, so
    /// the check itself stays synchronous and never blocks on a query. Until it is filled,
    /// and if the table cannot be read at all, it is empty -- which makes every verb count
    /// as a write and the log exception refuse everything on logs. That is the safe
    /// direction: an admin is told no and says so, rather than deleting logs nobody meant
    /// them to reach.
    /// </remarks>
    private static volatile HashSet<string>? _readOnlyActions;

    private static HashSet<string> Reads() =>
        _readOnlyActions ?? new HashSet<string>(StringComparer.Ordinal);

    private async Task LoadReadOnlyActionsAsync(CancellationToken cancellationToken)
    {
        if (_readOnlyActions is not null) return;
        try
        {
            var rows = await _db.ExecuteQueryAsync(
                $"SELECT action FROM {_options.Tables.Actions} WHERE is_read_only",
                new { },
                cancellationToken).ConfigureAwait(false);

            _readOnlyActions = new HashSet<string>(
                rows.Select(r => Field(r, "action"))
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .Select(a => a!),
                StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            // Left null so a later request retries; Reads() reads empty meanwhile, which
            // refuses log writes rather than allowing them.
            _logger.LogError(ex, "Could not read {Table}; log writes will be refused to admins until it can be read", _options.Tables.Actions);
        }
    }

    /// <summary>Whether an app-admin role covers this permission.</summary>
    /// <remarks>
    /// `required` must be a pair -- "msg|store-sales|create||any". An id is not parsed and
    /// never matches here, so an app still asking for ids falls through to its own grants
    /// rather than being handed a whole app on a string that merely looks similar. The
    /// previous version matched the id prefix `permission-{app}-`, which meant a permission
    /// could only be covered by the app whose name was spelled into its id -- so moving one
    /// between apps needed the id re-keyed. The pair carries the app itself.
    ///
    /// Mirrors AuthService._app_admin_allows in tvs-package; the two must agree, because the
    /// same role means the same thing whichever app the request reaches.
    /// </remarks>
    private static bool AppAdminAllows(IEnumerable<AuthServiceReadDto> userRoles, string required)
    {
        if (string.IsNullOrEmpty(required) || userRoles is null) return false;
        if (required.IndexOf(PairSeparator) < 0) return false;

        var parts = required.Split(PairSeparator);
        if (parts.Length < 3) return false;
        var (app, resource, action) = (parts[0], parts[1], parts[2]);

        foreach (var role in userRoles)
        {
            if (role.RoleId is null) continue;

            if (EveryAppAdminRoles.Contains(role.RoleId))
            {
                // Any app, and Core Platform; the log rule below still applies.
            }
            else if (app.Length == 0 || app == "cp")
            {
                // Core Platform's own permissions, which only a Core Platform admin covers.
                if (!CoreAdminRoles.Contains(role.RoleId)) continue;
            }
            else if (!AppAdminRoles.TryGetValue(role.RoleId, out var owned) || owned != app)
            {
                continue;
            }

            // Everything in their app, except changing its logs.
            if (ProtectedResources.Contains(resource) && !Reads().Contains(action)) continue;

            return true;
        }
        return false;
    }

    /// <summary>Whether these roles allow this one permission.</summary>
    /// <remarks>
    /// Static, and the single place the rules are applied, because a caller that tests a
    /// flattened permission list itself gets none of them. ZelosHR did exactly that --
    /// middleware flattened every role's permissions into HttpContext and the attribute
    /// tested membership there -- so the rules added to HasAnyPermission never ran on a
    /// single one of its routes, and its admin was locked out the moment the rows went.
    /// </remarks>
    public static bool Allows(
        IEnumerable<AuthServiceReadDto> userRoles,
        string requiredPermission,
        IEnumerable<string>? ownedPermissions = null)
    {
        if (IsOwner(userRoles)) return true;
        if (AppAdminAllows(userRoles, requiredPermission)) return true;

        var owned = ownedPermissions
            ?? (userRoles ?? Array.Empty<AuthServiceReadDto>())
                .SelectMany(r => r.Permissions ?? new List<string>());
        return owned.Contains(requiredPermission, StringComparer.OrdinalIgnoreCase);
    }

    public bool HasAnyPermission(IEnumerable<AuthServiceReadDto> userRoles, IEnumerable<string> requiredPermissions)
    {
        if (IsOwner(userRoles)) return true;
        var owned = new HashSet<string>(GetUserPermissions(userRoles), StringComparer.Ordinal);
        return requiredPermissions.Any(p => owned.Contains(p) || AppAdminAllows(userRoles, p));
    }

    public bool HasAllPermissions(IEnumerable<AuthServiceReadDto> userRoles, IEnumerable<string> requiredPermissions)
    {
        if (IsOwner(userRoles)) return true;
        var owned = new HashSet<string>(GetUserPermissions(userRoles), StringComparer.Ordinal);
        return requiredPermissions.All(p => owned.Contains(p) || AppAdminAllows(userRoles, p));
    }

    private ClaimsPrincipal ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Jwt.SecretKey));
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
            return handler.ValidateToken(token, validationParameters, out _);
        }
        catch (SecurityTokenException ex)
        {
            throw new UnauthorizedAccessException("Could not validate credentials", ex);
        }
    }

    private static bool ToBool(object? value)
    {
        if (value is null) return false;
        if (value is bool b) return b;
        return bool.TryParse(value.ToString(), out var parsed) && parsed;
    }
}
