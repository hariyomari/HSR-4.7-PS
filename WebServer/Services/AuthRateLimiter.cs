using System.Collections.Concurrent;
using March7thHoney.Configuration;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Util;
using March7thHoney.Util.Security;
using March7thHoney.WebServer.Http;
using Microsoft.AspNetCore.Http;

namespace March7thHoney.WebServer.Services;

public enum AuthRateLimitAction
{
    Registration,
    Login,
    Email
}

public static class AuthRateLimiter
{
    private readonly record struct RateLimitRule(bool Enabled, int MaxRequests, int WindowSeconds, int BlockSeconds);

    private static readonly object SyncRoot = new();
    private static long _lastSweep;

    /// <summary>Fast in-memory index by AttemptKey, layered on top of the Uid-keyed DatabaseHelper persistence path.</summary>
    private static readonly ConcurrentDictionary<string, AuthAttemptData> AttemptIndex = new();

    /// <summary>Warm up the index from persisted rows so a restart doesn't silently un-block active limits.</summary>
    public static void Initialize()
    {
        foreach (var attempt in DatabaseHelper.GetAllInstanceFromMap<AuthAttemptData>() ?? [])
            AttemptIndex[attempt.AttemptKey] = attempt;
    }

    public sealed record SnapshotEntry(
        string Key, string Action, int Count, long WindowStart, long BlockedUntil, long UpdatedAt);

    /// <summary>Current attempt counters, blocked entries first (admin console).</summary>
    public static List<SnapshotEntry> GetSnapshot()
    {
        lock (SyncRoot)
        {
            return AttemptIndex.Values
                .Select(x => new SnapshotEntry(x.AttemptKey, x.Action, x.Count, x.WindowStart, x.BlockedUntil,
                    x.UpdatedAt))
                .OrderByDescending(x => x.BlockedUntil)
                .ThenByDescending(x => x.UpdatedAt)
                .ToList();
        }
    }

    /// <summary>Drop one counter (lifts its block). Returns false when the key is unknown.</summary>
    public static bool Clear(string attemptKey)
    {
        lock (SyncRoot)
        {
            if (!AttemptIndex.TryRemove(attemptKey, out var attempt))
                return false;

            DatabaseHelper.Instance?.DeleteInstance(attempt);
            return true;
        }
    }

    /// <summary>Drop every counter, or only the ones currently blocking, and return how many were removed.</summary>
    public static int ClearAll(bool blockedOnly)
    {
        lock (SyncRoot)
        {
            var now = Extensions.GetUnixSec();
            var targets = AttemptIndex.Values.Where(x => !blockedOnly || x.BlockedUntil > now).ToList();
            foreach (var attempt in targets)
            {
                DatabaseHelper.Instance?.DeleteInstance(attempt);
                AttemptIndex.TryRemove(attempt.AttemptKey, out _);
            }

            return targets.Count;
        }
    }

    /// <summary>
    ///     Like <see cref="TryReject" />, but only reports an identity block or an active rate-limit block; it does
    ///     not count this request. Lets a caller refuse a blocked client before checking a password and count only
    ///     the misses.
    /// </summary>
    public static bool TryRejectBlocked(HttpContext context, AuthRateLimitAction action,
        IEnumerable<string>? identityKeys, string? accountIdentifier, out IResult result)
    {
        if (TryRejectIdentityBlock(context, action, identityKeys, out result))
            return true;
        if (!GetRule(action).Enabled)
            return false;

        var now = Extensions.GetUnixSec();
        var blockedFor = 0L;
        lock (SyncRoot)
        {
            foreach (var attemptKey in BuildAttemptKeys(context, action, identityKeys, accountIdentifier))
                if (AttemptIndex.TryGetValue(attemptKey, out var attempt) && attempt.BlockedUntil > now)
                    blockedFor = Math.Max(blockedFor, attempt.BlockedUntil - now);
        }

        if (blockedFor <= 0)
            return false;

        result = BlockedResult(blockedFor);
        return true;
    }

    public static bool TryReject(HttpContext context, AuthRateLimitAction action,
        IEnumerable<string>? identityKeys, string? accountIdentifier, out IResult result)
    {
        if (TryRejectIdentityBlock(context, action, identityKeys, out result))
            return true;
        var rule = GetRule(action);
        if (!rule.Enabled)
            return false;

        var attemptKeys = BuildAttemptKeys(context, action, identityKeys, accountIdentifier).ToList();
        if (attemptKeys.Count == 0)
            return false;

        var now = Extensions.GetUnixSec();
        var blockedFor = 0L;
        lock (SyncRoot)
        {
            Sweep(now);

            var attempts = new List<(AuthAttemptData Attempt, bool IsNew)>();
            foreach (var attemptKey in attemptKeys)
            {
                var attempt = LoadOrCreateAttempt(action, attemptKey, out var isNew);
                attempts.Add((attempt, isNew));

                if (attempt.BlockedUntil > now)
                    blockedFor = Math.Max(blockedFor, attempt.BlockedUntil - now);
            }

            if (blockedFor > 0)
            {
                result = BlockedResult(blockedFor);
                return true;
            }

            foreach (var (attempt, isNew) in attempts)
            {
                if (attempt.WindowStart <= 0 || now - attempt.WindowStart >= rule.WindowSeconds)
                {
                    attempt.WindowStart = now;
                    attempt.Count = 1;
                    attempt.BlockedUntil = 0;
                }
                else
                {
                    attempt.Count++;
                }

                if (attempt.Count > rule.MaxRequests)
                {
                    attempt.BlockedUntil = now + rule.BlockSeconds;
                    attempt.WindowStart = now;
                    attempt.Count = 0;
                    blockedFor = Math.Max(blockedFor, rule.BlockSeconds);
                }

                attempt.UpdatedAt = now;
                SaveAttempt(attempt, isNew);
            }

            if (blockedFor <= 0)
                return false;
        }

        result = BlockedResult(blockedFor);
        return true;
    }

    private static bool TryRejectIdentityBlock(HttpContext context, AuthRateLimitAction action,
        IEnumerable<string>? identityKeys, out IResult result)
    {
        result = default!;
        if (!IdentityBlockHelper.TryGetActiveBan(identityKeys, out var block))
            return false;

        AccountBanHelper.LogRejection($"{action}:{context.Request.Path}", null, block, identityKeys);
        result = WebResults.SdkError(block.FormatLoginMessage(), WebConstants.RetcodeBlocked);
        return true;
    }

    private static IResult BlockedResult(long remainingSeconds)
    {
        var message = $"Too many requests. Try again in {AccountBanHelper.FormatDuration(remainingSeconds)}.";
        return WebResults.SdkError(message, WebConstants.RetcodeBlocked);
    }

    private static RateLimitRule GetRule(AuthRateLimitAction action)
    {
        var option = ConfigManager.Config.ServerOption.Auth.AntiAbuse;
        return action switch
        {
            AuthRateLimitAction.Login => new RateLimitRule(
                option.LoginRateLimitEnabled,
                Math.Max(option.LoginRateLimitMaxRequests, 1),
                Math.Max(option.LoginRateLimitWindowSeconds, 1),
                Math.Max(option.LoginRateLimitBlockSeconds, 1)),
            AuthRateLimitAction.Email => new RateLimitRule(
                option.EmailRateLimitEnabled,
                Math.Max(option.EmailRateLimitMaxRequests, 1),
                Math.Max(option.EmailRateLimitWindowSeconds, 1),
                Math.Max(option.EmailRateLimitBlockSeconds, 1)),
            _ => new RateLimitRule(
                option.RateLimitEnabled,
                Math.Max(option.RateLimitMaxRequests, 1),
                Math.Max(option.RateLimitWindowSeconds, 1),
                Math.Max(option.RateLimitBlockSeconds, 1))
        };
    }

    private static IEnumerable<string> BuildAttemptKeys(HttpContext context, AuthRateLimitAction action,
        IEnumerable<string>? identityKeys, string? accountIdentifier)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var normalizedIdentityKeys = AccountBanHelper.ParseIdentityKeys(string.Join(",", identityKeys ?? []));
        foreach (var identityKey in normalizedIdentityKeys)
            keys.Add(BuildStorageKey(action, "identity", identityKey));

        var ipKey = AccountBanHelper.CreateIpIdentityKey(context.Connection.RemoteIpAddress?.ToString());
        if (!string.IsNullOrWhiteSpace(ipKey))
            keys.Add(BuildStorageKey(action, "identity", ipKey));

        var normalizedAccount = NormalizeAccountIdentifier(accountIdentifier);
        if (!string.IsNullOrWhiteSpace(normalizedAccount))
            keys.Add(BuildStorageKey(action, "account", normalizedAccount));

        return keys;
    }

    private static string BuildStorageKey(AuthRateLimitAction action, string keyType, string value)
    {
        return AuthSecurity.HashToken($"{action}:{keyType}:{value}");
    }

    private static string? NormalizeAccountIdentifier(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.ToLowerInvariant();
    }

    private static AuthAttemptData LoadOrCreateAttempt(AuthRateLimitAction action, string attemptKey, out bool isNew)
    {
        if (AttemptIndex.TryGetValue(attemptKey, out var attempt))
        {
            isNew = false;
            return attempt;
        }

        isNew = true;
        var uid = BuildStableUid(attemptKey);
        while (AttemptIndex.Values.Any(x => x.Uid == uid && x.AttemptKey != attemptKey))
            uid = uid == int.MaxValue ? 1 : uid + 1;

        return new AuthAttemptData
        {
            Uid = uid,
            AttemptKey = attemptKey,
            Action = action.ToString()
        };
    }

    private static void SaveAttempt(AuthAttemptData attempt, bool isNew)
    {
        if (isNew)
        {
            DatabaseHelper.SaveInstance(attempt);
            AttemptIndex[attempt.AttemptKey] = attempt;
        }
        else
        {
            DatabaseHelper.SaveDatabaseType(attempt);
        }
    }

    private static int BuildStableUid(string attemptKey)
    {
        var hash = Convert.FromBase64String(AuthSecurity.HashToken(attemptKey));
        var uid = BitConverter.ToInt32(hash, 0) & int.MaxValue;
        return uid == 0 ? 1 : uid;
    }

    private static void Sweep(long now)
    {
        if (now - _lastSweep < 300)
            return;

        _lastSweep = now;
        var maxWindowSeconds = MaxConfiguredWindowSeconds();
        var staleBefore = now - Math.Max(maxWindowSeconds, 1) * 2L;
        var stale = AttemptIndex.Values
            .Where(x => x.BlockedUntil <= now && x.WindowStart <= staleBefore).ToList();
        foreach (var attempt in stale)
        {
            DatabaseHelper.Instance?.DeleteInstance(attempt);
            AttemptIndex.TryRemove(attempt.AttemptKey, out _);
        }
    }

    private static int MaxConfiguredWindowSeconds()
    {
        var option = ConfigManager.Config.ServerOption.Auth.AntiAbuse;
        return Math.Max(option.RateLimitWindowSeconds,
            Math.Max(option.LoginRateLimitWindowSeconds, option.EmailRateLimitWindowSeconds));
    }
}
