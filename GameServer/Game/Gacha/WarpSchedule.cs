using System.Globalization;
using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Enums;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Gacha;

/// <summary>
///     One version phase of the history pool. <see cref="Started" /> holds the banners that opened in the phase;
///     <see cref="Overlapping" /> adds every earlier banner whose historical window still covers
///     <see cref="StartTime" /> (a banner that ran through both phases of a version, like 4.7's Aha, and the
///     long-running pools); <see cref="WholeVersion" /> holds the banners that opened in any phase of its version.
///     ServerOption.Warps.PhaseLineup picks between them. Collab banners are never part of a phase: they are their
///     own category (<see cref="WarpPool.Collab" />, ServerOption.Warps.CollabBanners).
/// </summary>
public sealed record WarpPhase(
    string Key,
    string Version,
    int Phase,
    long StartTime,
    IReadOnlyList<BannerConfig> Started,
    IReadOnlyList<BannerConfig> Overlapping)
{
    public IReadOnlyList<BannerConfig> WholeVersion { get; init; } = [];

    public IReadOnlyList<BannerConfig> Lineup(WarpPhaseLineupEnum lineup)
    {
        return lineup switch
        {
            WarpPhaseLineupEnum.Overlap => Overlapping,
            WarpPhaseLineupEnum.Version => WholeVersion,
            _ => Started
        };
    }
}

/// <summary>
///     How far a player's story has got, for the Story filter: versions before <see cref="NextVersion" /> are
///     unlocked (all of them when it is null). A version without story of its own (1.1, 1.4-1.6, 2.4-2.6) unlocks
///     with the story version before it. With StoryByChapter, <see cref="LockedPhases" /> holds the phase keys of
///     unlocked versions that still wait for their story chapter (empty otherwise). <see cref="Unreadable" />: the
///     filter is on but the player's story cannot be read (mission system off, no gate data), so they get
///     Banners.json as written.
/// </summary>
public sealed record WarpStoryProgress(string? ReachedVersion, string? NextVersion)
{
    public static readonly WarpStoryProgress Unreadable = new(null, null);

    /// <summary>Phase keys ("1.0-1") locked by an unreached chapter gate although their version is unlocked.</summary>
    public IReadOnlySet<string> LockedPhases { get; init; } = new HashSet<string>();

    public bool IsReadable => ReachedVersion != null;

    /// <summary>Whether the phase <paramref name="phaseKey" /> of <paramref name="version" /> is unlocked.</summary>
    public bool Unlocks(string version, string phaseKey)
    {
        return (NextVersion == null || WarpSchedule.CompareVersions(version, NextVersion) < 0) &&
               !LockedPhases.Contains(phaseKey.Trim());
    }

    public bool Unlocks(WarpPhase phase)
    {
        return Unlocks(phase.Version, phase.Key);
    }

    public bool Unlocks(BannerConfig banner)
    {
        return Unlocks(banner.Version, WarpPool.PhaseKey(banner.Version, banner.Phase));
    }
}

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
        Collab = eligible.Where(b => WarpSchedule.IsCollab(b.GachaType)).OrderBy(b => b.GachaId).ToList();
        IneligibleIds = ineligible;
        _byId = eligible.ToDictionary(b => b.GachaId);

        // Phase start = earliest begin among the banners tagged with it (eligible or not, so a missing banner
        // cannot move the phase boundary).
        var phased = Banners.Where(b => !WarpSchedule.IsCollab(b.GachaType)).ToList();
        var phases = new List<WarpPhase>();
        foreach (var group in source.Banners.Where(b => !string.IsNullOrWhiteSpace(b.Version) && b.Phase > 0)
                     .GroupBy(b => PhaseKey(b.Version, b.Phase)))
        {
            var start = group.Min(b => b.BeginTime);
            var tagged = group.Select(b => b.GachaId).ToHashSet();
            var started = phased.Where(b => tagged.Contains(b.GachaId)).OrderBy(b => b.GachaId).ToList();
            var overlapping = phased.Where(b => tagged.Contains(b.GachaId) || (b.BeginTime <= start && b.EndTime > start))
                .OrderBy(b => b.GachaId).ToList();
            if (overlapping.Count == 0) continue;
            var first = group.First();
            phases.Add(new WarpPhase(group.Key, first.Version.Trim(), first.Phase, start, started, overlapping));
        }

        var wholeVersions = phases.GroupBy(p => p.Version).ToDictionary(g => g.Key,
            g => (IReadOnlyList<BannerConfig>)g.SelectMany(p => p.Started).DistinctBy(b => b.GachaId)
                .OrderBy(b => b.GachaId).ToList());
        Phases = phases.Select(p => p with { WholeVersion = wholeVersions[p.Version] })
            .OrderBy(p => p.StartTime).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        _phasesByKey = Phases.ToDictionary(p => p.Key, StringComparer.OrdinalIgnoreCase);
    }

    internal BannersConfig Source { get; }
    internal Dictionary<int, ItemConfigExcel> Items { get; }

    // The raw-resource boot fills ItemConfigData in place, so the reference alone does not show new items.
    internal int ItemCount { get; }

    public IReadOnlyList<BannerConfig> Banners { get; }
    public IReadOnlyList<WarpPhase> Phases { get; }

    /// <summary>Eligible collab banners, outside every phase; served as a group when CollabBanners is on.</summary>
    public IReadOnlyList<BannerConfig> Collab { get; }

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

    public static WarpPhaseLineupEnum PhaseLineup =>
        TryParseLineup(Option.PhaseLineup, out var lineup) ? lineup : WarpPhaseLineupEnum.Started;

    /// <summary>Story filter in effect: switched on and the mode is not Manual (admin picks are explicit).</summary>
    public static bool StoryActive => Option.Story && Mode != WarpScheduleModeEnum.Manual;

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

    public static bool TryParseLineup(string? value, out WarpPhaseLineupEnum lineup)
    {
        lineup = WarpPhaseLineupEnum.Started;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return Enum.TryParse(value.Trim(), true, out lineup) && Enum.IsDefined(lineup);
    }

    /// <summary>The banners <paramref name="phase" /> serves under the current PhaseLineup setting.</summary>
    public static IReadOnlyList<BannerConfig> LineupOf(WarpPhase phase)
    {
        return phase.Lineup(PhaseLineup);
    }

    /// <summary>
    ///     The steps of the rotation (and the admin's phase lists) among <paramref name="phases" /> (in start
    ///     order): every phase, or with the Version lineup the first phase of each version, standing for the whole
    ///     version.
    /// </summary>
    public static IReadOnlyList<WarpPhase> RotationPhases(IEnumerable<WarpPhase> phases)
    {
        return PhaseLineup == WarpPhaseLineupEnum.Version
            ? phases.GroupBy(p => p.Version).Select(g => g.First()).ToList()
            : phases.ToList();
    }

    /// <summary>"1.0" &lt; "1.10" &lt; "2.0"; text that is not a version sorts after every version.</summary>
    public static int CompareVersions(string a, string b)
    {
        var hasA = Version.TryParse(a.Trim(), out var va);
        var hasB = Version.TryParse(b.Trim(), out var vb);
        if (hasA && hasB) return va!.CompareTo(vb);
        if (hasA != hasB) return hasA ? -1 : 1;
        return string.CompareOrdinal(a, b);
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

    public static bool IsCollab(GachaTypeEnum type)
    {
        return type is GachaTypeEnum.CollaborationAvatarUp or GachaTypeEnum.CollaborationWeaponUp;
    }

    public static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    /// <summary>
    ///     Story progress of <paramref name="player" /> for the Story filter, or null when the filter is not in
    ///     effect. The newest gate version with any main mission accepted or finished is the one reached; the first
    ///     gate (1.0) counts as reached from the start. With StoryByChapter the phases of every unreached chapter
    ///     gate are locked; a chapter gate is reached through its own main missions, through a later chapter gate
    ///     (version, then file order), or once the reached version is newer than the gate's.
    /// </summary>
    public static WarpStoryProgress? GetStoryProgress(PlayerInstance player)
    {
        if (!StoryActive) return null;
        var missions = player.MissionManager;
        if (missions == null || !player.MissionEnabled) return WarpStoryProgress.Unreadable;

        Func<IEnumerable<int>, bool> anyReached = ids =>
            ids.Any(id => missions.GetMainMissionStatus(id) is MissionPhaseEnum.Accept or MissionPhaseEnum.Finish);
        var byVersion = Comparer<string>.Create(CompareVersions);
        var config = GameData.WarpStoryGatesConfig;
        var gates = config.Gates
            .Where(gate => !string.IsNullOrWhiteSpace(gate.Version))
            .OrderBy(gate => gate.Version, byVersion)
            .ToList();
        if (gates.Count == 0) return WarpStoryProgress.Unreadable;

        var reached = 0;
        for (var i = gates.Count - 1; i > 0; i--)
        {
            if (!anyReached(gates[i].MainMissions)) continue;
            reached = i;
            break;
        }

        var reachedVersion = gates[reached].Version.Trim();
        var progress = new WarpStoryProgress(reachedVersion,
            reached + 1 < gates.Count ? gates[reached + 1].Version.Trim() : null);
        if (!Option.StoryByChapter) return progress;

        // OrderBy is stable, so chapters of one version keep their file (story) order.
        var chapterGates = config.ChapterGates
            .Where(gate => !string.IsNullOrWhiteSpace(gate.Version))
            .OrderBy(gate => gate.Version, byVersion)
            .ToList();
        var locked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var laterReached = false;
        for (var i = chapterGates.Count - 1; i >= 0; i--)
        {
            var gate = chapterGates[i];
            laterReached = laterReached || CompareVersions(reachedVersion, gate.Version) > 0 ||
                           anyReached(gate.MainMissions);
            if (laterReached) continue;
            foreach (var phase in gate.Phases.Where(phase => !string.IsNullOrWhiteSpace(phase)))
                locked.Add(phase.Trim());
        }

        return progress with { LockedPhases = locked };
    }

    /// <summary>
    ///     Banners offered to <paramref name="player" />. Rotation and Manual windows are computed on the real
    ///     clock and then moved onto the player's server clock, so an account on a fake server date still sees
    ///     them as running. The Story filter narrows the limited banners per player.
    /// </summary>
    public static List<BannerConfig> GetActiveBanners(PlayerInstance? player)
    {
        var realNow = Now();
        var playerNow = player == null ? realNow : ServerTimeProvider.GetServerUnixSec(player.Data);
        return GetActiveBanners(realNow, playerNow, player == null ? null : GetStoryProgress(player));
    }

    /// <param name="story">
    ///     Null: no Story filter. <see cref="WarpStoryProgress.Unreadable" />: Banners.json as written, whatever the
    ///     mode. Otherwise only banners of unlocked phases: unlocked versions, minus the phases StoryByChapter still
    ///     locks (Static and Rotation; Manual ignores it). Collab banners are a separate group in every case
    ///     (<see cref="GetCollabBanners" />).
    /// </param>
    public static List<BannerConfig> GetActiveBanners(long realNow, long playerNow, WarpStoryProgress? story = null)
    {
        var mode = Mode;
        if (mode == WarpScheduleModeEnum.Manual) story = null;
        if (story is { IsReadable: false })
        {
            mode = WarpScheduleModeEnum.Static;
            story = null;
        }

        var fromHistory = mode != WarpScheduleModeEnum.Static || story != null;
        var result = new List<BannerConfig>();
        foreach (var banner in GameData.BannersConfig.Banners)
        {
            if (IsCollab(banner.GachaType) || (fromHistory && IsLimited(banner.GachaType))) continue;
            if (banner.IsActiveAt(realNow)) result.Add(banner);
        }

        var shift = playerNow - realNow;
        var seen = result.Select(b => b.GachaId).ToHashSet();
        var served = fromHistory ? GetScheduledBanners(realNow, mode, story) : [];
        foreach (var banner in served.Concat(GetCollabBanners(realNow)))
        {
            if (!banner.IsActiveAt(realNow) || !seen.Add(banner.GachaId)) continue;
            result.Add(shift == 0
                ? banner
                : banner.WithWindow(banner.BeginTime + shift, banner.EndTime + shift));
        }

        return result;
    }

    /// <summary>
    ///     The collab banners of the history pool with the current rotation period as their window, served in every
    ///     mode and to every player while ServerOption.Warps.CollabBanners is on; empty when it is off.
    /// </summary>
    public static List<BannerConfig> GetCollabBanners(long now)
    {
        if (!Option.CollabBanners) return [];
        var (begin, end) = GetPeriod(now);
        return Pool.Collab.Select(b => b.WithWindow(begin, end)).ToList();
    }

    /// <summary>
    ///     Limited banners of the current mode with their served windows (real clock), including Manual entries
    ///     whose explicit window has not opened yet or has closed — the admin panel lists those too. This is the
    ///     server-wide view: the Story filter narrows it per player (<see cref="GetActiveBanners(PlayerInstance)" />).
    ///     Collab banners are not part of it (<see cref="GetCollabBanners" />).
    /// </summary>
    public static List<BannerConfig> GetScheduledBanners(long now)
    {
        return GetScheduledBanners(now, Mode, null);
    }

    private static List<BannerConfig> GetScheduledBanners(long now, WarpScheduleModeEnum mode, WarpStoryProgress? story)
    {
        switch (mode)
        {
            case WarpScheduleModeEnum.Rotation:
            {
                // A served lineup can hold locked banners (the Version lineup's whole version, Overlap's carry-overs).
                var rotation = GetRotation(now, story);
                if (rotation.Phase == null) return [];
                return LineupOf(rotation.Phase)
                    .Where(b => story == null || story.Unlocks(b))
                    .Select(b => b.WithWindow(rotation.PeriodBegin, rotation.PeriodEnd))
                    .ToList();
            }
            case WarpScheduleModeEnum.Manual:
            {
                var (begin, end) = GetPeriod(now);
                var pool = Pool;
                var result = new List<BannerConfig>();
                foreach (var entry in Option.Manual ?? [])
                {
                    var banner = pool.Find(entry.GachaId);
                    if (banner == null || IsCollab(banner.GachaType)) continue;
                    result.Add(banner.WithWindow(entry.BeginTime > 0 ? entry.BeginTime : begin,
                        entry.EndTime > 0 ? entry.EndTime : end, entry.RateUpItems5, entry.RateUpItems4));
                }

                return result;
            }
            default:
            {
                if (story == null)
                    return GameData.BannersConfig.Banners
                        .Where(b => IsLimited(b.GachaType) && !IsCollab(b.GachaType)).ToList();

                // Static + Story: every unlocked history banner at once, rolling with the rotation period.
                var (begin, end) = GetPeriod(now);
                return Pool.Banners.Where(b => !IsCollab(b.GachaType) && story.Unlocks(b))
                    .OrderBy(b => b.GachaId)
                    .Select(b => b.WithWindow(begin, end))
                    .ToList();
            }
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

    /// <summary>
    ///     Rotation position. With <paramref name="story" /> only the unlocked phases take part (StoryByChapter can
    ///     lock a phase of an unlocked version), in order, starting at RotationStartPhase when it is unlocked and at
    ///     the oldest phase otherwise; no phase at all when none is unlocked. With the Version lineup a period serves
    ///     a whole version, which takes part when any of its phases is unlocked, and a start phase stands for its
    ///     version.
    /// </summary>
    public static WarpRotationState GetRotation(long now, WarpStoryProgress? story = null)
    {
        var (begin, end) = GetPeriod(now);
        var pool = Pool;
        var phases = RotationPhases(story == null ? pool.Phases : pool.Phases.Where(p => story.Unlocks(p)));
        if (phases.Count == 0) return new WarpRotationState(null, begin, end, null);

        var period = Math.Max(1, Option.RotationDays) * DaySeconds;
        var periodIndex = (begin - GetRotationAnchor()) / period;
        var start = pool.FindPhase(Option.RotationStartPhase);
        var byVersion = PhaseLineup == WarpPhaseLineupEnum.Version;
        var startIndex = 0;
        for (var i = 0; i < phases.Count; i++)
            if (start != null && (byVersion ? phases[i].Version == start.Version : phases[i].Key == start.Key))
                startIndex = i;
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
        var banners = lineup.Where(b => pool.Find(b.GachaId) != null && !IsCollab(b.GachaType)).ToList();
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
            if (IsCollab(banner.GachaType))
                return $"Banner {entry.GachaId} is a collab banner; those follow the collab banners switch";
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
