using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using March7thHoney.Database.Player;

namespace March7thHoney.Util;

/// <summary>
///     The clock the server reports and keys its daily logic on. Fake time (per-account /faketime or the global
///     ServerTime option) is a running clock: it starts at the fake date's daily reset and advances with real elapsed
///     time from the moment it is anchored (first use after server start, a date change, or the account's next login
///     after /faketime). A constant instant would be sent again in every heartbeat, pulling the client's server clock
///     back each time; reward popups ("items obtained") could then never be closed.
/// </summary>
public static class ServerTimeProvider
{
    private static readonly string[] DateInputFormats = ["yyyyMMdd", "yyyy-MM-dd"];
    private static readonly ConcurrentDictionary<int, FakeClock> AccountClocks = [];
    private static FakeClock? _globalClock;

    /// <summary>Whole-hour offset of the local standard time; PlayerLoginScRsp sends it as cur_timezone.</summary>
    public static int UtcOffsetHours => (int)TimeZoneInfo.Local.BaseUtcOffset.TotalHours;

    public static long GetServerUnixSec(PlayerData? data = null) => GetServerNow(data).ToUnixTimeSeconds();

    public static long GetServerUnixMs(PlayerData? data = null) => GetServerNow(data).ToUnixTimeMilliseconds();

    /// <summary>Restarts the account's fake clock at its date's daily reset on next use (/faketime set or off).</summary>
    public static void RestartAccountClock(int uid) => AccountClocks.TryRemove(uid, out _);

    public static bool TryParseDateInput(string? input, out int dateStamp)
    {
        dateStamp = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;

        if (!DateTime.TryParseExact(input.Trim(), DateInputFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;

        dateStamp = parsed.Year * 10000 + parsed.Month * 100 + parsed.Day;
        return true;
    }

    public static bool TryGetDate(int dateStamp, out DateTime date)
    {
        date = default;
        if (dateStamp <= 0) return false;

        return DateTime.TryParseExact(dateStamp.ToString("00000000", CultureInfo.InvariantCulture), "yyyyMMdd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    // Per-account fake time wins over the global config; neither means the real clock.
    private static DateTimeOffset GetServerNow(PlayerData? data)
    {
        if (data is { FakeTimeDate: > 0 } && TryGetDate(data.FakeTimeDate, out var accountDate))
            return AccountClocks.AddOrUpdate(data.Uid, _ => new FakeClock(accountDate),
                (_, clock) => clock.Date == accountDate ? clock : new FakeClock(accountDate)).Now;

        if (!ConfigManager.Config.ServerOption.ServerTime.EnableFakeServerTime) return DateTimeOffset.UtcNow;

        var date = GetGlobalFixedLocalDate();
        var global = _globalClock;
        if (global == null || global.Date != date) _globalClock = global = new FakeClock(date);
        return global.Now;
    }

    /// <summary>
    ///     Starts at <see cref="Date" />'s daily reset in the cur_timezone offset, so the daily window is the fake date
    ///     itself, and runs on the monotonic clock so a wall-clock change cannot move it.
    /// </summary>
    private sealed class FakeClock(DateTime date)
    {
        // Unspecified kind: DateTimeOffset rejects a Local-kind date ("today" is DateTime.Today) whose zone offset
        // differs from the standard offset, as it does during daylight saving time.
        private readonly DateTimeOffset _start = new(DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified)
            .AddHours(ConfigManager.Config.ServerOption.ValidDailyResetHour()), TimeSpan.FromHours(UtcOffsetHours));

        private readonly long _anchor = Stopwatch.GetTimestamp();

        public DateTime Date { get; } = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

        public DateTimeOffset Now => _start + Stopwatch.GetElapsedTime(_anchor);
    }

    private static DateTime GetGlobalFixedLocalDate()
    {
        var fixedDate = ConfigManager.Config.ServerOption.ServerTime.FixedDate?.Trim();
        if (string.IsNullOrWhiteSpace(fixedDate) ||
            fixedDate.Equals("today", StringComparison.OrdinalIgnoreCase))
            return DateTime.Today;

        if (DateTime.TryParseExact(
                fixedDate,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
            return parsed.Date;

        if (DateTime.TryParse(fixedDate, out parsed)) return parsed.Date;

        return DateTime.Today;
    }
}
