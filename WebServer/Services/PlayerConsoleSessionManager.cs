using System.Collections.Concurrent;
using System.Globalization;
using March7thHoney.Database.Account;
using March7thHoney.Util.Security;

namespace March7thHoney.WebServer.Services;

/// <summary>
/// Short-lived player console sessions. The credential fingerprint binds a token to the password
/// version used at sign-in, so every password change path revokes it without a database migration.
/// </summary>
public static class PlayerConsoleSessionManager
{
    private static readonly ConcurrentDictionary<string, StoredSession> Sessions = [];
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(2);

    public static PlayerConsoleSession Create(AccountData account) => Create(account, GetCredentialFingerprint(account));

    // Login supplies the fingerprint captured before password verification. A concurrent password
    // change must not issue a new token bound to credentials that were never actually verified.
    internal static PlayerConsoleSession Create(AccountData account, string verifiedCredentialFingerprint)
    {
        CleanupExpiredSessions();
        var session = new PlayerConsoleSession(AuthSecurity.GenerateSessionToken(), account.Uid,
            DateTimeOffset.UtcNow.Add(SessionLifetime));
        Sessions[session.Token] = new StoredSession(session, verifiedCredentialFingerprint);
        return session;
    }

    internal static string GetCredentialFingerprint(AccountData account) => AuthSecurity.HashToken(
        string.Join("\n", account.PasswordSalt ?? "", account.PasswordHash ?? "",
            account.PasswordIterations.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Unknown, expired, deleted-account and changed-password sessions all fail closed.</summary>
    public static bool TryResolve(string? token, out PlayerConsoleSession session, out AccountData account)
    {
        CleanupExpiredSessions();
        session = default;
        account = null!;
        if (string.IsNullOrWhiteSpace(token) || !Sessions.TryGetValue(token, out var stored))
            return false;

        var resolved = AccountData.GetAccountByUid(stored.Session.Uid);
        if (stored.Session.ExpiresAt <= DateTimeOffset.UtcNow || resolved == null ||
            !string.Equals(stored.CredentialFingerprint, GetCredentialFingerprint(resolved), StringComparison.Ordinal))
        {
            Sessions.TryRemove(token, out _);
            return false;
        }

        session = stored.Session;
        account = resolved;
        return true;
    }

    public static void Revoke(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            Sessions.TryRemove(token, out _);
    }

    public static void RevokeForUid(int uid)
    {
        foreach (var (token, stored) in Sessions)
            if (stored.Session.Uid == uid)
                Sessions.TryRemove(token, out _);
    }

    private static void CleanupExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, stored) in Sessions)
            if (stored.Session.ExpiresAt <= now)
                Sessions.TryRemove(token, out _);
    }

    private readonly record struct StoredSession(PlayerConsoleSession Session, string CredentialFingerprint);
    public readonly record struct PlayerConsoleSession(string Token, int Uid, DateTimeOffset ExpiresAt);
}
