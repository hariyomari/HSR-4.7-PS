using March7thHoney.Database.Player;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.DailyActive;

/// <summary>
///     Daily boundary math. The window is keyed by the local date in the time zone PlayerLoginScRsp advertises as
///     cur_timezone, shifted back one day while the local clock is still before ServerOption.DailyResetHour, so the
///     server's rollover matches the client's reset countdown.
/// </summary>
public static class DailyResetClock
{
    /// <summary>Same value PacketPlayerLoginScRsp sends as cur_timezone.</summary>
    public static int UtcOffsetHours => ServerTimeProvider.UtcOffsetHours;

    public static int ResetHour => ConfigManager.Config.ServerOption.ValidDailyResetHour();

    /// <summary>yyyyMMdd of the daily window containing <paramref name="unixSec" />.</summary>
    public static int DayKey(long unixSec, int utcOffsetHours, int resetHour)
    {
        var local = DateTimeOffset.FromUnixTimeSeconds(unixSec).ToOffset(TimeSpan.FromHours(utcOffsetHours));
        var date = local.Hour < resetHour ? local.Date.AddDays(-1) : local.Date;
        return date.Year * 10000 + date.Month * 100 + date.Day;
    }

    /// <summary>Unix time at which the daily window containing <paramref name="unixSec" /> began.</summary>
    public static long WindowStart(long unixSec, int utcOffsetHours, int resetHour)
    {
        const long secondsPerDay = 24 * 60 * 60;
        var intoWindow = (unixSec + (utcOffsetHours - resetHour) * 3600L) % secondsPerDay;
        if (intoWindow < 0) intoWindow += secondsPerDay;
        return unixSec - intoWindow;
    }

    /// <summary>The window the player is in now (honours per-account and global fake time).</summary>
    public static int Today(PlayerData? data)
    {
        return DayKey(ServerTimeProvider.GetServerUnixSec(data), UtcOffsetHours, ResetHour);
    }
}

/// <summary>
///     Deterministic, runtime-independent shuffle: FNV-1a-64 of "{salt}|{dayKey}|{nonce}" seeds SplitMix64, which
///     drives a Fisher–Yates shuffle of the sorted pool. The same player, day and nonce always yield the same set on
///     any process or .NET version, so reconnecting or restarting cannot re-roll the day.
/// </summary>
public static class DailySeededShuffle
{
    public static ulong Seed(long salt, int dayKey, int nonce)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offsetBasis;
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(FormattableString.Invariant($"{salt}|{dayKey}|{nonce}")))
        {
            hash ^= b;
            hash = unchecked(hash * prime);
        }

        return hash;
    }

    public static List<int> Pick(IEnumerable<int> pool, int count, ulong seed)
    {
        var ids = pool.Distinct().OrderBy(id => id).ToList();
        if (count <= 0 || ids.Count == 0) return [];

        var state = seed;
        for (var i = ids.Count - 1; i > 0; i--)
        {
            var j = (int)(NextSplitMix64(ref state) % (ulong)(i + 1));
            (ids[i], ids[j]) = (ids[j], ids[i]);
        }

        return ids.Count > count ? ids.GetRange(0, count) : ids;
    }

    private static ulong NextSplitMix64(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
