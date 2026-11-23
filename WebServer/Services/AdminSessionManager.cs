using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using March7thHoney.Util;
using March7thHoney.Util.Security;

namespace March7thHoney.WebServer.Services;

public static class AdminSessionManager
{
    private static readonly ConcurrentDictionary<string, AdminSession> Sessions = [];
    private static readonly ConcurrentDictionary<string, LoginFailureState> LoginFailures = [];
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    // Brute-force guard for the admin key: after MaxLoginFailures wrong keys inside LoginFailureWindow the
    // client is refused for LoginBlockDuration. Keyed by remote address; the key itself is never logged.
    private const int MaxLoginFailures = 8;
    private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan LoginBlockDuration = TimeSpan.FromMinutes(15);

    public static bool IsEnabled
    {
        get
        {
            var configuredKey = ConfigManager.Config.MuipServer.AdminKey;
            return !string.IsNullOrWhiteSpace(configuredKey) && configuredKey != "None";
        }
    }

    public static int ActiveSessionCount
    {
        get
        {
            CleanupExpiredSessions();
            return Sessions.Count;
        }
    }

    public static bool TryCreateSession(string adminKey, out AdminSession session, out string message)
    {
        return TryCreateSession(adminKey, null, out session, out message);
    }

    public static bool TryCreateSession(string adminKey, string? clientKey, out AdminSession session,
        out string message)
    {
        CleanupExpiredSessions();

        session = default;
        if (!TryVerifyAdminKey(adminKey, clientKey, out message, out _))
            return false;

        session = new AdminSession(AuthSecurity.GenerateSessionToken(), DateTimeOffset.UtcNow.Add(SessionLifetime));
        Sessions[session.Token] = session;
        message = "OK";
        return true;
    }

    /// <summary>
    ///     Checks an admin key against the configured one through the per-client failure counter shared by
    ///     every admin key login path (admin page and MUIP).
    /// </summary>
    public static bool TryVerifyAdminKey(string? adminKey, string? clientKey, out string message, out bool blocked)
    {
        CleanupExpiredLoginFailures();
        message = "Admin key is invalid";
        blocked = false;

        if (!IsEnabled)
        {
            message = "Admin page is not enabled because MuipServer.AdminKey is not configured";
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        LoginFailureState? failureState = null;
        if (!string.IsNullOrWhiteSpace(clientKey))
        {
            failureState = LoginFailures.GetOrAdd(clientKey, _ => new LoginFailureState());
            lock (failureState)
            {
                if (failureState.BlockedUntil > now)
                {
                    var remaining = (int)Math.Ceiling((failureState.BlockedUntil - now).TotalMinutes);
                    message = $"Too many failed attempts. Try again in {Math.Max(remaining, 1)} minutes";
                    blocked = true;
                    return false;
                }
            }
        }

        if (!SecureEquals(adminKey, ConfigManager.Config.MuipServer.AdminKey))
        {
            if (failureState != null)
                lock (failureState)
                {
                    if (now - failureState.WindowStart > LoginFailureWindow)
                    {
                        failureState.WindowStart = now;
                        failureState.Failures = 0;
                    }

                    failureState.Failures++;
                    if (failureState.Failures >= MaxLoginFailures)
                    {
                        failureState.BlockedUntil = now.Add(LoginBlockDuration);
                        failureState.Failures = 0;
                        failureState.WindowStart = now;
                    }
                }

            return false;
        }

        if (failureState != null)
            lock (failureState)
            {
                failureState.Failures = 0;
                failureState.BlockedUntil = DateTimeOffset.MinValue;
            }

        message = "OK";
        return true;
    }

    public static bool IsAuthorized(string? token)
    {
        return TryGetSession(token, out _);
    }

    public static bool TryGetSession(string? token, out AdminSession session)
    {
        CleanupExpiredSessions();
        session = default;

        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (!Sessions.TryGetValue(token, out var existing))
            return false;

        if (existing.ExpiresAt > DateTimeOffset.UtcNow)
        {
            session = existing;
            return true;
        }

        Sessions.TryRemove(token, out _);
        return false;
    }

    public static void Revoke(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            Sessions.TryRemove(token, out _);
    }

    /// <summary>Invalidate every session except the caller's (for example after rotating the key).</summary>
    public static int RevokeOthers(string? keepToken)
    {
        var revoked = 0;
        foreach (var token in Sessions.Keys.ToList())
        {
            if (string.Equals(token, keepToken, StringComparison.Ordinal)) continue;
            if (Sessions.TryRemove(token, out _)) revoked++;
        }

        return revoked;
    }

    private static void CleanupExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, session) in Sessions)
        {
            if (session.ExpiresAt <= now)
                Sessions.TryRemove(token, out _);
        }

        CleanupExpiredLoginFailures();
    }

    private static void CleanupExpiredLoginFailures()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, state) in LoginFailures)
        {
            lock (state)
            {
                if (state.BlockedUntil <= now && now - state.WindowStart > LoginFailureWindow)
                    LoginFailures.TryRemove(key, out _);
            }
        }
    }

    private static bool SecureEquals(string? provided, string expected)
    {
        if (provided == null)
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return providedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    public readonly record struct AdminSession(string Token, DateTimeOffset ExpiresAt);

    private sealed class LoginFailureState
    {
        public int Failures;
        public DateTimeOffset WindowStart = DateTimeOffset.UtcNow;
        public DateTimeOffset BlockedUntil = DateTimeOffset.MinValue;
    }
}
