using System.Globalization;
using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Enums;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Gacha;

/// <summary>
///     One version phase of the history pool. <see cref="Banners" /> is the lineup that was live during the phase:
///     the banners tagged with it plus every earlier banner whose historical window still covers
///     <see cref="StartTime" />, so a banner that ran through both phases of a version (4.7's Aha) and the
///     long-running collab pools appear in each phase they overlapped, not only in the phase they started in.
/// </summary>
public sealed record WarpPhase(string Key, string Version, int Phase, long StartTime, IReadOnlyList<BannerConfig> Banners);

/// <summary>Rotation position at a given time. Period bounds are inclusive Unix seconds.</summary>
public sealed record WarpRotationState(WarpPhase? Phase, long PeriodBegin, long PeriodEnd, WarpPhase? NextPhase);

/// <summary>
///     The eligible part of Config/Custom/Banners.history.json: limited banners whose featured and display items
///     all exist in the loaded game data (a banner pointing at content the client lacks would pay out the 3★
///     fallback), grouped into phases. Immutable; rebuilt when the history file or the item table is reloaded.
/// </summary>
public sealed class WarpPool
{
    private readonly Dictionary<int, BannerConfig> _byId;
    private readonly Dictionary<string, WarpPhase> _phasesByKey;

    internal WarpPool(BannersConfig source, Dictionary<int, ItemConfigExcel> items)
    {
        Source = source;
        Items = items;
        ItemCount = items.Count;

        var eligible = new List<BannerConfig>();
        var ineligible = new List<int>();
        foreach (var banner in source.Banners)
            if (IsEligible(banner, items)) eligible.Add(banner);
            else ineligible.Add(banner.GachaId);

        Banners = eligible.OrderBy(b => b.BeginTime).ThenBy(b => b.GachaId).ToList();
        IneligibleIds = ineligible;
        _byId = eligible.ToDictionary(b => b.GachaId);

        // Phase start = earliest begin among the banners tagged with it (eligible or not, so a missing banner
        // cannot move the phase boundary).
        var phases = new List<WarpPhase>();
        foreach (var group in source.Banners.Where(b => !string.IsNullOrWhiteSpace(b.Version) && b.Phase > 0)
                     .GroupBy(b => PhaseKey(b.Version, b.Phase)))
        {
            var start = group.Min(b => b.BeginTime);
            var tagged = group.Select(b => b.GachaId).ToHashSet();
            // Tagged banners count even when they opened hours after the phase start (the collab pools did).
            var lineup = Banners.Where(b => tagged.Contains(b.GachaId) || (b.BeginTime <= start && b.EndTime > start))
                .OrderBy(b => b.GachaId).ToList();
            if (lineup.Count == 0) continue;
            var first = group.First();
            phases.Add(new WarpPhase(group.Key, first.Version.Trim(), first.Phase, start, lineup));
        }

        Phases = phases.OrderBy(p => p.StartTime).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        _phasesByKey = Phases.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    internal BannersConfig Source { get; }
    internal Dictionary<int, ItemConfigExcel> Items { get; }

    // The raw-resource boot fills ItemConfigData in place, so the reference alone does not show new items.
    internal int ItemCount { get; }

    public IReadOnlyList<BannerConfig> Banners { get; }
    public IReadOnlyList<WarpPhase> Phases { get; }

    /// <summary>History rows left out because an item they feature is missing from the game data.</summary>
    public IReadOnlyList<int> IneligibleIds { get; }

    public BannerConfig? Find(int gachaId)
    {
        return _byId.GetValueOrDefault(gachaId);
    }

    public WarpPhase? FindPhase(string? key)
    {
        return key == null ? null : _phasesByKey.GetValueOrDefault(key.Trim());
    }

    public int IndexOfPhase(string? key)
    {
        var phase = FindPhase(key);
        if (phase == null) return -1;
        for (var i = 0; i < Phases.Count; i++)
            if (ReferenceEquals(Phases[i], phase)) return i;
        return -1;
    }

    /// <summary>
    ///     Light cone banner that ran beside a character banner: id + 1000 in the official numbering (2142 ↔ 3142,
    ///     collab 5001 ↔ 6001); its 5★ is the character's signature light cone. 0 when there is none.
    /// </summary>
    public int DefaultPairOf(int gachaId)
    {
        var banner = Find(gachaId);
        if (banner == null || !WarpSchedule.IsCharacterBanner(banner.GachaType)) return 0;
        var pair = Find(gachaId + 1000);
        return pair != null && WarpSchedule.IsLightConeBanner(pair.GachaType) ? pair.GachaId : 0;
    }

    public static string PhaseKey(string version, int phase)
    {
        return $"{version.Trim()}-{phase}";
    }

    private static bool IsEligible(BannerConfig banner, Dictionary<int, ItemConfigExcel> items)
    {
        if (!WarpSchedule.IsLimited(banner.GachaType) || banner.RateUpItems5.Count == 0) return false;
        return banner.RateUpItems5.Concat(banner.RateUpItems4).Concat(banner.PrizeItems).All(items.ContainsKey);
    }
}

/// <summary>
///     Decides which warp banners a player is offered (ServerOption.Warps). GetGachaInfo, DoGacha and the
///     Celestial Invitation pick all read the same list, so a banner the player cannot see cannot be pulled.
/// </summary>
public static class WarpSchedule
{
    private const long DaySeconds = 86400;
    private static volatile WarpPool? _pool;

    private static WarpScheduleOption Option => ConfigManager.Config.ServerOption.Warps;

    public static WarpScheduleModeEnum Mode => TryParseMode(Option.Mode, out var mode) ? mode : WarpScheduleModeEnum.Static;

    /// <summary>Eligible history pool; rebuilt lazily after a banner reload or a resource reload.</summary>
    public static WarpPool Pool
    {
        get
        {
            var source = GameData.BannerHistoryConfig;
            var items = GameData.ItemConfigData;
            var pool = _pool;
            if (pool != null && ReferenceEquals(pool.Source, source) && ReferenceEquals(pool.Items, items) &&
                pool.ItemCount == items.Count)
                return pool;

            pool = new WarpPool(source, items);
            _pool = pool;
            return pool;
        }
    }

    public static bool TryParseMode(string? value, out WarpScheduleModeEnum mode)
    {
        mode = WarpScheduleModeEnum.Static;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Trim(), true, out mode) && Enum.IsDefined(mode);
    }

    public static bool IsLimited(GachaTypeEnum type)
    {
        return IsCharacterBanner(type) || IsLightConeBanner(type);
    }

    public static bool IsCharacterBanner(GachaTypeEnum type)
    {
        return type is GachaTypeEnum.AvatarUp or GachaTypeEnum.CollaborationAvatarUp;
    }

    public static bool IsLightConeBanner(GachaTypeEnum type)
    {
        return type is GachaTypeEnum.WeaponUp or GachaTypeEnum.CollaborationWeaponUp;
    }

    public static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    ///     Banners offered to <paramref name="player" />. Rotation and Manual windows are computed on the real
    ///     clock and then moved onto the player's server clock, so an account on a fake server date still sees
    ///     them as running. Story mode will narrow this per player.
    /// </summary>
    public static List<BannerConfig> GetActiveBanners(PlayerInstance? player)
    {
        var realNow = Now();
        var playerNow = player == null ? realNow : ServerTimeProvider.GetServerUnixSec(player.Data);
        return GetActiveBanners(realNow, playerNow);
    }

    public static List<BannerConfig> GetActiveBanners(long realNow, long playerNow)
    {
        var result = new List<BannerConfig>();
        var mode = Mode;
        foreach (var banner in GameData.BannersConfig.Banners)
        {
            if (mode != WarpScheduleModeEnum.Static && IsLimited(banner.GachaType)) continue;
            if (banner.IsActiveAt(realNow)) result.Add(banner);
        }

        if (mode == WarpScheduleModeEnum.Static) return result;

        var shift = playerNow - realNow;
        var seen = result.Select(b => b.GachaId).ToHashSet();
        foreach (var banner in GetScheduledBanners(realNow))
        {
            if (!banner.IsActiveAt(realNow) || !seen.Add(banner.GachaId)) continue;
            result.Add(shift == 0
                ? banner
                : banner.WithWindow(banner.BeginTime + shift, banner.EndTime + shift));
        }

        return result;
    }

    /// <summary>
    ///     Limited banners of the current mode with their served windows (real clock), including Manual entries
    ///     whose explicit window has not opened yet or has closed — the admin panel lists those too.
    /// </summary>
    public static List<BannerConfig> GetScheduledBanners(long now)
    {
        switch (Mode)
        {
            case WarpScheduleModeEnum.Rotation:
            {
                var rotation = GetRotation(now);
                return rotation.Phase?.Banners.Select(b => b.WithWindow(rotation.PeriodBegin, rotation.PeriodEnd))
                    .ToList() ?? [];
            }
            case WarpScheduleModeEnum.Manual:
            {
                var (begin, end) = GetPeriod(now);
                var pool = Pool;
                var result = new List<BannerConfig>();
                foreach (var entry in Option.Manual ?? [])
                {
                    var banner = pool.Find(entry.GachaId);
                    if (banner == null) continue;
                    result.Add(banner.WithWindow(entry.BeginTime > 0 ? entry.BeginTime : begin,
                        entry.EndTime > 0 ? entry.EndTime : end, entry.RateUpItems5, entry.RateUpItems4));
                }

                return result;
            }
            default:
                return GameData.BannersConfig.Banners.Where(b => IsLimited(b.GachaType)).ToList();
        }
    }

    /// <summary>Rotation period containing <paramref name="now" />, inclusive bounds.</summary>
    public static (long Begin, long End) GetPeriod(long now)
    {
        var period = Math.Max(1, Option.RotationDays) * DaySeconds;
        var anchor = GetRotationAnchor();
        var offset = now - anchor;
        var index = offset / period - (offset % period < 0 ? 1 : 0);
        var begin = anchor + index * period;
        return (begin, begin + period - 1);
    }

    public static WarpRotationState GetRotation(long now)
    {
        var (begin, end) = GetPeriod(now);
        var phases = Pool.Phases;
        if (phases.Count == 0) return new WarpRotationState(null, begin, end, null);

        var period = Math.Max(1, Option.RotationDays) * DaySeconds;
        var periodIndex = (begin - GetRotationAnchor()) / period;
        var startIndex = Math.Max(0, Pool.IndexOfPhase(Option.RotationStartPhase));
        var current = Mod(startIndex + periodIndex, phases.Count);
        return new WarpRotationState(phases[current], begin, end, phases[Mod(current + 1, phases.Count)]);
    }

    /// <summary>Server-local midnight of RotationStart (yyyy-MM-dd); 1970-01-01 UTC when unset or invalid.</summary>
    public static long GetRotationAnchor()
    {
        if (!ServerTimeProvider.TryParseDateInput(Option.RotationStart, out var stamp) ||
            !ServerTimeProvider.TryGetDate(stamp, out var date))
            return 0;
        return new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Local)).ToUnixTimeSeconds();
    }

    /// <summary>
    ///     Switches the mode. Entering Manual with an empty list seeds it with what is running now, so the switch
    ///     itself changes nothing the players see. Entering Rotation without a start date starts it today. The
    ///     caller persists the config.
    /// </summary>
    public static void SetMode(WarpScheduleModeEnum mode, long now)
    {
        if (mode == WarpScheduleModeEnum.Manual && (Option.Manual?.Count ?? 0) == 0)
            Option.Manual = BuildManualEntries(GetScheduledBanners(now).Where(b => b.IsActiveAt(now)));

        if (mode == WarpScheduleModeEnum.Rotation && GetRotationAnchor() == 0)
            Option.RotationStart = LocalDate(now);

        Option.Mode = mode.ToString();
    }

    /// <summary>Rotation from <paramref name="phaseKey" />, period 0 starting today. Caller persists.</summary>
    public static string? RestartRotation(string? phaseKey, long now)
    {
        var phase = Pool.FindPhase(phaseKey);
        if (phase == null) return $"Unknown phase '{phaseKey}'";

        Option.RotationStart = LocalDate(now);
        Option.RotationStartPhase = phase.Key;
        Option.Mode = nameof(WarpScheduleModeEnum.Rotation);
        return null;
    }

    /// <summary>Manual entries for a lineup: rolling windows, history featured items, default LC pairing.</summary>
    public static List<WarpManualEntry> BuildManualEntries(IEnumerable<BannerConfig> lineup)
    {
        var pool = Pool;
        var banners = lineup.Where(b => pool.Find(b.GachaId) != null).ToList();
        var ids = banners.Select(b => b.GachaId).ToHashSet();
        return banners.Select(b => new WarpManualEntry
        {
            GachaId = b.GachaId,
            PairedGachaId = pool.DefaultPairOf(b.GachaId) is var pair && ids.Contains(pair) ? pair : 0
        }).ToList();
    }

    /// <summary>Checks a Manual list against the pool and the item table. Returns an error or null.</summary>
    public static string? ValidateManual(IReadOnlyList<WarpManualEntry> entries)
    {
        var pool = Pool;
        var seen = new HashSet<int>();
        foreach (var entry in entries)
        {
            var banner = pool.Find(entry.GachaId);
            if (banner == null) return $"Banner {entry.GachaId} is not in the eligible history pool";
            if (!seen.Add(entry.GachaId)) return $"Banner {entry.GachaId} is listed twice";
            if (entry.BeginTime < 0 || entry.EndTime < 0) return $"Banner {entry.GachaId}: negative time";
            if (entry.BeginTime > 0 && entry.EndTime > 0 && entry.BeginTime >= entry.EndTime)
                return $"Banner {entry.GachaId}: begin time must be before end time";
            if (entry.RateUpItems5 is { Count: 0 }) return $"Banner {entry.GachaId}: needs at least one featured 5★";

            var unknown = (entry.RateUpItems5 ?? []).Concat(entry.RateUpItems4 ?? [])
                .FirstOrDefault(id => !pool.Items.ContainsKey(id));
            if (unknown != 0) return $"Banner {entry.GachaId}: item {unknown} does not exist in the game data";

            if (entry.PairedGachaId == 0) continue;
            var pair = pool.Find(entry.PairedGachaId);
            if (!IsCharacterBanner(banner.GachaType) || pair == null || !IsLightConeBanner(pair.GachaType))
                return $"Banner {entry.GachaId}: {entry.PairedGachaId} is not a light cone banner it can pair with";
        }

        return null;
    }

    private static string LocalDate(long unixSeconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime()
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static int Mod(long value, int count)
    {
        return (int)(((value % count) + count) % count);
    }
}
