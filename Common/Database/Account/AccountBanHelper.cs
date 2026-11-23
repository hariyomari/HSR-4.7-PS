using MemoryPack;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using March7thHoney.Internationalization;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.Database.Account;

public readonly record struct AccountBanStatus(
    bool IsActive,
    int BannedUid,
    string Reason,
    long ExpireAt,
    long CreatedAt,
    bool IdentityMatched)
{
    public static AccountBanStatus Inactive => new(false, 0, string.Empty, 0, 0, false);

    public int? RuleId { get; init; }
    public string? IdentityKey { get; init; }
    public bool KnownDeviceMatched { get; init; }

    public bool IsPermanent => IsActive && ExpireAt <= 0;

    public long RemainingSeconds => IsPermanent ? 0 : Math.Max(0, ExpireAt - Extensions.GetUnixSec());

    public string FormatLoginMessage()
    {
        if (!IsActive) return string.Empty;

        var target = IdentityMatched
            ? "A ban is active for this device or network"
            : "This account is banned";
        var reason = string.IsNullOrWhiteSpace(Reason) ? "No reason provided" : Reason.Trim();

        if (IsPermanent)
            return $"{target}. Reason: {reason}. Ban duration: permanent.";

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(ExpireAt).UtcDateTime;
        return $"{target}. Reason: {reason}. Time remaining: {AccountBanHelper.FormatDuration(RemainingSeconds)}. Expires: {expiresAt:yyyy-MM-dd HH:mm:ss} UTC.";
    }
}

public static class AccountBanHelper
{
    private const int MaxStoredIdentityKeys = 32;
    private static readonly char[] IdentitySeparators = [','];
    private static readonly Logger Logger = new("Auth");
    // These values are shared by clients and must never identify one device, including in old records.
    private static readonly HashSet<string> IgnoredDeviceValues = new(StringComparer.Ordinal)
    {
        "null", "undefined", "unknown", "none", "n/a", "{}", "[]",
        "testdevicefp", "asd9123cfa767", "38d7ecd67b187",
        "0", "00000000", "0000000000000000", "00000000000000000000000000000000",
        "00000000-0000-0000-0000-000000000000", "00:00:00:00:00:00"
    };
    private static readonly HashSet<string> IgnoredDeviceKeys = IgnoredDeviceValues
        .Select(value => "device:" + HashIdentity(value)).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyCollection<string> BuildIdentityKeys(IEnumerable<string?> deviceValues, string? ipAddress)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var devices = deviceValues.Select(NormalizeDeviceValue).OfType<string>().Distinct(StringComparer.Ordinal).Take(16).ToList();

        foreach (var value in devices)
        {
            var key = CreateDeviceIdentityKey(value);
            if (!string.IsNullOrWhiteSpace(key)) keys.Add(key);
        }

        var ipKey = CreateIpIdentityKey(ipAddress);
        if (!string.IsNullOrWhiteSpace(ipKey)) keys.Add(ipKey);

        return new LoginIdentityContext(keys, NormalizeIpAddress(ipAddress), devices);
    }

    public static string? CreateDeviceIdentityKey(string? value)
    {
        var normalized = NormalizeDeviceValue(value);
        return string.IsNullOrWhiteSpace(normalized) ? null : "device:" + HashIdentity(normalized);
    }

    public static string? CreateIpIdentityKey(string? ipAddress)
    {
        var normalized = NormalizeIpAddress(ipAddress);
        return string.IsNullOrWhiteSpace(normalized) ? null : "ip:" + HashIdentity(normalized);
    }

    public static string MergeIdentityKeys(string? existingIdentityKeys, IEnumerable<string>? newIdentityKeys)
    {
        var incoming = NormalizeIdentityKeys(newIdentityKeys).ToList();
        var merged = ParseIdentityKeys(existingIdentityKeys)
            .Except(incoming, StringComparer.Ordinal)
            .Concat(incoming)
            .TakeLast(MaxStoredIdentityKeys)
            .ToList();

        return string.Join(",", merged);
    }

    public static IReadOnlyCollection<string> ParseIdentityKeys(string? identityKeys)
    {
        if (string.IsNullOrWhiteSpace(identityKeys))
            return [];

        return identityKeys
            .Split(IdentitySeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsIdentityKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static bool TryGetActiveBan(AccountData account, IEnumerable<string>? identityKeys,
        out AccountBanStatus status, string stage = "authentication")
    {
        status = account.GetBanStatus();
        if (status.IsActive)
        {
            LogRejection(stage, account.Uid, status, identityKeys);
            return true;
        }

        // A client cannot evade a device block by omitting its ID on the next token exchange.
        if (IdentityBlockHelper.TryGetActiveBan(ParseIdentityKeys(account.KnownIdentityKeys)
                .Where(IsDeviceIdentityKey), out status))
        {
            status = status with { KnownDeviceMatched = true };
            LogRejection(stage, account.Uid, status, identityKeys);
            return true;
        }

        // Account bans apply only to their UID. Broader restrictions require an independent rule.
        if (IdentityBlockHelper.TryGetActiveBan(identityKeys, out status))
        {
            LogRejection(stage, account.Uid, status, identityKeys);
            return true;
        }

        return false;
    }

    public static void LogRejection(string stage, int? uid, AccountBanStatus status,
        IEnumerable<string>? identityKeys = null)
    {
        var source = !status.IdentityMatched ? "account_ban" :
            status.IdentityKey?.StartsWith("ip:", StringComparison.Ordinal) == true ? "ip_block" : "device_block";
        var match = !status.IdentityMatched ? "uid" : status.KnownDeviceMatched ? "known_device" : "current";
        var remote = (identityKeys as LoginIdentityContext)?.IpAddress ?? "unknown";
        Logger.Warn(I18NManager.Translate("Server.Log.LoginBlocked",
            $"stage={stage.Replace('\r', ' ').Replace('\n', ' ')} uid={uid?.ToString() ?? "unknown"} remote={remote} " +
            $"source={source} match={match} rule_id={status.RuleId?.ToString() ?? "none"} " +
            $"identity_key={status.IdentityKey ?? "none"} expire_at={status.ExpireAt}"));
    }

    public static bool TryFindAccountByIdentityKeys(IEnumerable<string>? identityKeys, string? mode,
        out AccountData account)
    {
        account = default!;
        var currentKeys = NormalizeIdentityKeys(identityKeys).ToHashSet(StringComparer.Ordinal);
        if (currentKeys.Count == 0)
            return false;

        var currentDeviceKeys = currentKeys.Where(IsDeviceIdentityKey).ToHashSet(StringComparer.Ordinal);
        var currentIpKeys = currentKeys.Where(IsIpIdentityKey).ToHashSet(StringComparer.Ordinal);
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "DeviceOrIp" : mode.Trim();

        var accounts = DatabaseHelper.GetAllInstanceFromMap<AccountData>() ??
                       DatabaseHelper.GetAllInstance<AccountData>() ?? [];
        foreach (var candidate in accounts.GroupBy(candidate => candidate.Uid).Select(group => group.First()))
        {
            var knownKeys = ParseIdentityKeys(candidate.KnownIdentityKeys);
            if (knownKeys.Count == 0)
                continue;

            var knownDeviceKeys = knownKeys.Where(IsDeviceIdentityKey).ToHashSet(StringComparer.Ordinal);
            var knownIpKeys = knownKeys.Where(IsIpIdentityKey).ToHashSet(StringComparer.Ordinal);
            var matched = normalizedMode switch
            {
                "DeviceOnly" => currentDeviceKeys.Count > 0 && knownDeviceKeys.Any(currentDeviceKeys.Contains),
                "DeviceAndIp" => currentDeviceKeys.Count > 0 &&
                                 currentIpKeys.Count > 0 &&
                                 knownDeviceKeys.Any(currentDeviceKeys.Contains) &&
                                 knownIpKeys.Any(currentIpKeys.Contains),
                _ => knownKeys.Any(currentKeys.Contains)
            };

            if (!matched)
                continue;

            account = candidate;
            return true;
        }

        return false;
    }

    public static string FormatDuration(long totalSeconds)
    {
        totalSeconds = Math.Max(0, totalSeconds);
        if (totalSeconds == 0) return "0 seconds";

        var remaining = TimeSpan.FromSeconds(totalSeconds);
        var parts = new List<string>();

        if (remaining.Days > 0) parts.Add($"{remaining.Days} day{Plural(remaining.Days)}");
        if (remaining.Hours > 0) parts.Add($"{remaining.Hours} hour{Plural(remaining.Hours)}");
        if (remaining.Minutes > 0) parts.Add($"{remaining.Minutes} minute{Plural(remaining.Minutes)}");
        if (parts.Count == 0 || remaining.Seconds > 0) parts.Add($"{remaining.Seconds} second{Plural(remaining.Seconds)}");

        return string.Join(", ", parts.Take(3));
    }

    public static BlackInfo ToBlackInfo(AccountBanStatus banStatus)
    {
        var now = Extensions.GetUnixSec();
        return new BlackInfo
        {
            BeginTime = banStatus.CreatedAt > 0 ? banStatus.CreatedAt : now,
            EndTime = banStatus.IsPermanent ? 0 : Math.Max(0, banStatus.ExpireAt),
            LimitLevel = 0,
            BanType = 1
        };
    }

    internal static IEnumerable<string> NormalizeIdentityKeys(IEnumerable<string>? identityKeys)
    {
        return (identityKeys ?? [])
            .Where(IsIdentityKey)
            .Distinct(StringComparer.Ordinal);
    }

    private static bool IsIdentityKey(string? key)
    {
        if (!IsDeviceIdentityKey(key) && !IsIpIdentityKey(key)) return false;
        if (IgnoredDeviceKeys.Contains(key!)) return false;
        var hash = key![(key!.IndexOf(':') + 1)..];
        Span<byte> buffer = stackalloc byte[32];
        return Convert.TryFromBase64String(hash, buffer, out var written) && written == 32;
    }

    private static bool IsDeviceIdentityKey(string? key)
    {
        return !string.IsNullOrWhiteSpace(key) &&
               key.StartsWith("device:", StringComparison.Ordinal);
    }

    private static bool IsIpIdentityKey(string? key)
    {
        return !string.IsNullOrWhiteSpace(key) &&
               key.StartsWith("ip:", StringComparison.Ordinal);
    }

    public static string? NormalizeDeviceValue(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Length > 512 || value.Any(char.IsControl))
            return null;

        var lowered = value.ToLowerInvariant();
        return IgnoredDeviceValues.Contains(lowered) ? null : lowered;
    }

    public static string? NormalizeIpAddress(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        ipAddress = ipAddress.Trim();
        if (!IPAddress.TryParse(ipAddress, out var parsed))
            return null;

        if (parsed.IsIPv4MappedToIPv6)
            parsed = parsed.MapToIPv4();

        return parsed.ToString().ToLowerInvariant();
    }

    private static string HashIdentity(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hash);
    }

    private static string Plural(int value)
    {
        return value == 1 ? string.Empty : "s";
    }
}
