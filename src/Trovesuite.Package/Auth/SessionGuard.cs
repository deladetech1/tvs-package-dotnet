using Microsoft.Extensions.Logging;
using Trovesuite.Package.Database;

namespace Trovesuite.Package.Auth;

/// <summary>
/// Whether a token that decodes correctly still counts. A port of trovesuite's
/// <c>session_guard</c>, with the same three deliberate "yes" answers.
/// </summary>
/// <remarks>
/// <para>A signed JWT proves only that Core Platform issued it. It says nothing about
/// whether somebody has since pressed "sign out", whether an administrator ended the
/// session, or whether the organisation ended every session. Core Platform records those
/// in <c>cp_user_sessions</c>, and the same token is accepted by every app -- so an app
/// that does not read it keeps a revoked session working for up to a day while the
/// screen that ended it says it is over. This package did not read it until 1.0.13.</para>
///
/// <para>Accepted, on purpose, exactly as in Python:</para>
/// <list type="bullet">
/// <item><b>No jti</b> -- tokens minted before sessions existed. They age out in a day.</item>
/// <item><b>No row</b> -- absence is not revocation; revocation is recorded positively.</item>
/// <item><b>Database failure</b> -- failing closed would turn a blip into "nobody can use
/// any app". The role read that follows fails on its own if the database is really gone,
/// so nothing is let through to do work.</item>
/// </list>
/// </remarks>
public sealed class SessionGuard
{
    /// <summary>How stale last_seen_at may get before it is worth a write.</summary>
    public const int TouchAfterMinutes = 5;

    private readonly IDatabaseManager _db;
    private readonly ILogger _logger;
    private readonly string _table;

    public SessionGuard(IDatabaseManager db, ILogger logger, string table)
    {
        _db = db;
        _logger = logger;
        _table = table;
    }

    /// <summary>
    /// The message to show if this token's session has been ended, or null when it still
    /// counts (including every case that cannot be answered).
    /// </summary>
    public async Task<string?> RevokedMessageAsync(string? jti, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jti)) return null;

        IReadOnlyList<IDictionary<string, object?>> rows;
        try
        {
            rows = await _db.ExecuteQueryAsync(
                $"SELECT revoked_at, revoke_reason, last_seen_at FROM {_table} WHERE jti = @jti",
                new { jti }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogError(e, "Session check failed for {Jti}", jti);
            return null;
        }

        if (rows.Count == 0) return null;
        var row = rows[0];

        if (row.TryGetValue("revoked_at", out var revokedAt) && revokedAt is not null and not DBNull)
        {
            row.TryGetValue("revoke_reason", out var reason);
            return MessageFor(reason as string);
        }

        await TouchAsync(jti, row.TryGetValue("last_seen_at", out var seen) ? seen : null, cancellationToken)
            .ConfigureAwait(false);
        return null;
    }

    /// <summary>What to tell the person, by the reason Core Platform recorded.</summary>
    public static string MessageFor(string? reason) => reason switch
    {
        "logout" => "You have been signed out. Please sign in again.",
        "admin" => "An administrator ended this session. Please sign in again, or contact them if you were not expecting this.",
        "all-devices" => "You were signed out of all devices. Please sign in again.",
        "lockdown" => "Your organisation has ended all sessions. Please contact your administrator.",
        "concurrent-limit" => "You signed in elsewhere and this organisation allows a limited number of sessions at once. This one was ended.",
        "password-change" => "Your password changed. Please sign in again.",
        _ => "Your session has ended. Please sign in again.",
    };

    /// <summary>
    /// Note, occasionally, that the session is still in use, so "active 4 minutes ago" is
    /// true. Skipped while last_seen_at is fresh, so the common request does one indexed
    /// read and no write. Best effort: a failed write is not a failed request.
    /// </summary>
    private async Task TouchAsync(string jti, object? lastSeenAt, CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-TouchAfterMinutes);
        var seen = lastSeenAt switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, dt.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : dt.Kind)),
            _ => (DateTimeOffset?)null,
        };
        if (seen is not null && seen > cutoff) return;

        try
        {
            await _db.ExecuteUpdateAsync(
                $@"UPDATE {_table} SET last_seen_at = now()
                   WHERE jti = @jti AND revoked_at IS NULL
                     AND last_seen_at < now() - make_interval(mins => @mins)",
                new { jti, mins = TouchAfterMinutes }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _logger.LogWarning(e, "Could not touch session {Jti}", jti);
        }
    }
}
