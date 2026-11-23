using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.DailyActive;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Quests;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Quest;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Player.Components;
using March7thHoney.GameServer.Game.Quest;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.DailyActive;

/// <summary>One rung of the daily training ladder.</summary>
public sealed record DailyActiveTier(int Level, int Point, int RewardId);

/// <summary>Why a daily-mission roll offered what it did (for the GM command).</summary>
public sealed record DailyMissionBoard(int Roster, int GateSatisfied, int Running, int Finished, int Offered, int Quota);

/// <summary>
///     Daily training (每日实训): today's rolled QuestType 5 quests, the active points (virtual item 23) their rewards
///     pay, the five point tiers those points unlock, and the daily main missions offered each day.
///     <para>
///         The window is keyed by <see cref="DailyResetClock" />. On a new day the set is re-rolled deterministically
///         from (uid, day, nonce), points and claimed tiers are cleared, yesterday's dailies expire and today's are
///         re-armed from zero, and finished daily missions become acceptable again.
///     </para>
/// </summary>
public class DailyActiveManager(PlayerInstance player) : BasePlayerComponent(player)
{
    /// <summary>
    ///     Quests per day. The official DailyActiveInfoNotify carries exactly 8 ids while far more DailyQuest rows are
    ///     eligible; no Excel column holds the number.
    /// </summary>
    public const int DailyQuestCount = 8;

    /// <summary>Virtual item paid by the daily quests.</summary>
    public const int DailyActivePointItemId = 23;

    private static readonly Logger Logger = new("DailyActive");

    public DailyActiveData Data { get; } = DatabaseHelper.Instance!.GetInstanceOrCreateNew<DailyActiveData>(player.Uid);

    public int Point => Data.Point;
    public IReadOnlyList<int> QuestIds => Data.QuestIds;
    public int TodayMissionCount => Data.MissionsToday.Count;
    public int TotalMissionCount => Data.MissionsTotal;

    private void MarkDirty() => DatabaseHelper.MarkDirty(Player.Uid);

    #region Window

    /// <summary>Login: bring the window to today and offer today's daily missions, without pushing packets.</summary>
    public async ValueTask OnLogin()
    {
        await EnsureToday(false);
        await RollMissions(false, false);
    }

    /// <summary>
    ///     Rolls the window when the day changed (or when today's set is still empty, e.g. the player just reached
    ///     the minimum level). Returns whether anything was rolled.
    /// </summary>
    public async ValueTask<bool> EnsureToday(bool sync = true)
    {
        var today = DailyResetClock.Today(Player.Data);
        if (Data.DayKey == today)
        {
            if (Data.QuestIds.Count > 0) return false;
            // Same day, nothing rolled yet: fill the set but keep today's points, tiers and mission bookkeeping.
            if (EligibleQuests().Count == 0) return false;
            await RollQuests(sync);
            return true;
        }

        var previousDay = Data.DayKey;
        Data.DayKey = today;
        Data.Point = 0;
        Data.TakenLevels.Clear();
        Data.MissionsToday.Clear();
        Data.MissionsOffered.Clear();
        MarkDirty();

        await RearmDailyMissions(sync);
        await RollQuests(sync);
        Logger.Debug($"uid {Player.Uid}: daily window {previousDay} -> {today} (nonce {Data.Nonce}), " +
                    $"quests [{string.Join(",", Data.QuestIds)}]");
        return true;
    }

    /// <summary>GM reset: a new nonce gives a different set for the same day; everything day-scoped is cleared.</summary>
    public async ValueTask<List<int>> ResetNow(bool sync = true)
    {
        Data.Nonce++;
        Data.DayKey = 0;
        MarkDirty();
        await EnsureToday(sync);
        await RollMissions(false, sync);
        return [.. Data.QuestIds];
    }

    /// <summary>
    ///     Today's pool: DailyQuest rows not deleted and in the player's level band, restricted to QuestType 5 rows
    ///     whose own unlock condition (usually a finished story mission) is met.
    /// </summary>
    public List<int> EligibleQuests()
    {
        var level = Player.Data.Level;
        var pool = new SortedSet<int>();
        foreach (var row in GameData.DailyQuestData.Values)
        {
            if (row.IsDelete || row.DailyID == 0) continue;
            if (level < row.MinLevel || level > row.MaxLevel) continue;
            foreach (var questId in row.QuestList)
                if (GameData.QuestDataData.TryGetValue(questId, out var quest) &&
                    quest.QuestType == QuestManager.DailyQuestType && IsUnlocked(quest))
                    pool.Add(questId);
        }

        return [.. pool];
    }

    private bool IsUnlocked(QuestDataExcel quest)
    {
        return quest.UnlockType switch
        {
            QuestUnlockTypeEnum.FinishMission => quest.UnlockParamList.All(id =>
                Player.MissionManager?.GetMainMissionStatus(id) == MissionPhaseEnum.Finish),
            QuestUnlockTypeEnum.FinishQuest => quest.UnlockParamList.All(id =>
                Player.QuestManager?.GetQuestStatus(id) is QuestStatus.QuestFinish or QuestStatus.QuestClose),
            _ => true
        };
    }

    private async ValueTask RollQuests(bool sync)
    {
        var seed = DailySeededShuffle.Seed(Player.Uid, Data.DayKey, Data.Nonce);
        Data.QuestIds = DailySeededShuffle.Pick(EligibleQuests(), DailyQuestCount, seed);
        MarkDirty();
        await ApplyQuestSet(sync);
    }

    /// <summary>
    ///     Makes the quest records match today's set: every other QuestType 5 record is closed (unclaimed rewards of
    ///     the previous window are dropped on purpose), today's dailies restart from zero, and the child quests a
    ///     FinishQuest daily counts (e.g. 2100156 → 2100161/2100162) are re-armed so yesterday's completion does not
    ///     satisfy today's daily.
    /// </summary>
    private async ValueTask ApplyQuestSet(bool sync)
    {
        var quests = Player.QuestManager;
        if (quests == null || !ConfigManager.Config.ServerOption.EnableQuest) return;

        var today = Data.QuestIds.ToHashSet();
        var changed = new List<QuestInfo>();
        foreach (var info in quests.Data.Quests.Values.ToList())
        {
            if (today.Contains(info.QuestId) || info.QuestStatus == QuestStatus.QuestClose) continue;
            if (GameData.QuestDataData.GetValueOrDefault(info.QuestId)?.QuestType != QuestManager.DailyQuestType)
                continue;
            changed.Add(quests.ResetQuestState(info.QuestId, QuestStatus.QuestClose));
        }

        foreach (var questId in Data.QuestIds)
        {
            changed.Add(quests.ResetQuestState(questId, QuestStatus.QuestDoing));

            var quest = GameData.QuestDataData.GetValueOrDefault(questId);
            var finishWay = quest == null ? null : GameData.FinishWayData.GetValueOrDefault(quest.FinishWayID);
            if (finishWay?.FinishType != MissionFinishTypeEnum.FinishQuest) continue;
            foreach (var childId in finishWay.ParamIntList)
            {
                var child = GameData.QuestDataData.GetValueOrDefault(childId);
                if (child == null || child.QuestType == QuestManager.DailyQuestType) continue;
                if (!quests.Data.Quests.TryGetValue(childId, out var childInfo)) continue; // not unlocked yet
                if (childInfo.QuestStatus == QuestStatus.QuestDoing && childInfo.Progress == 0) continue;
                changed.Add(quests.ResetQuestState(childId, QuestStatus.QuestDoing));
            }
        }

        if (sync && changed.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(changed));
    }

    #endregion

    #region Points & tiers

    /// <summary>Credits active points (item 23) to the current window and returns the new total.</summary>
    public int AddPoint(int count)
    {
        if (count == 0) return Data.Point;
        Data.Point = Math.Max(0, Data.Point + count);
        MarkDirty();
        return Data.Point;
    }

    /// <summary>
    ///     The ladder for a world level, lowest first. DailyActiveConfig has a base band (no WorldLevel) plus one band
    ///     per world level; a world level without rows falls back to the base band.
    /// </summary>
    public static List<DailyActiveTier> GetTiers(int worldLevel)
    {
        if (!GameData.DailyActiveConfigData.TryGetValue(worldLevel, out var rows) || rows.Count == 0)
            GameData.DailyActiveConfigData.TryGetValue(0, out rows);
        return (rows ?? [])
            .OrderBy(row => row.Level)
            .Select(row => new DailyActiveTier(row.Level, row.DailyActivePoint, row.DailyActiveReward))
            .ToList();
    }

    public List<DailyActiveTier> CurrentTiers() => GetTiers(Player.Data.WorldLevel);

    public bool IsTaken(int level) => Data.TakenLevels.Contains(level);

    /// <summary>
    ///     Claims one tier. Everything is validated and the tier is recorded as taken before the reward is awaited, so
    ///     a duplicate request (even one interleaved with this one) can never pay twice.
    /// </summary>
    public async ValueTask<(Retcode Retcode, List<ItemData> Items)> ClaimTier(int level)
    {
        var tier = CurrentTiers().FirstOrDefault(t => t.Level == level);
        if (tier == null) return (Retcode.RetDailyActiveLevelInvalid, []);
        if (IsTaken(level)) return (Retcode.RetDailyActiveLevelRewardAlreadyTaken, []);
        if (Data.Point < tier.Point) return (Retcode.RetDailyActiveLevelApNotEnough, []);

        Data.TakenLevels.Add(level);
        MarkDirty();

        return (Retcode.RetSucc, await GrantTierReward(tier));
    }

    /// <summary>Claims every reachable, unclaimed tier in ascending order; the rewards come back concatenated.</summary>
    public async ValueTask<(List<int> Levels, List<ItemData> Items)> ClaimAll()
    {
        var claimable = CurrentTiers().Where(t => !IsTaken(t.Level) && Data.Point >= t.Point).ToList();
        if (claimable.Count == 0) return ([], []);

        foreach (var tier in claimable) Data.TakenLevels.Add(tier.Level);
        MarkDirty();

        var items = new List<ItemData>();
        foreach (var tier in claimable) items.AddRange(await GrantTierReward(tier));
        return (claimable.Select(t => t.Level).ToList(), items);
    }

    private async ValueTask<List<ItemData>> GrantTierReward(DailyActiveTier tier)
    {
        if (tier.RewardId == 0 || Player.InventoryManager == null) return [];
        return await Player.InventoryManager.HandleReward(tier.RewardId);
    }

    public List<DailyActivityInfo> BuildLevelList()
    {
        // world_level is left unset on purpose: the official rows carry only level, point and is_has_taken.
        return CurrentTiers().Select(tier => new DailyActivityInfo
        {
            Level = (uint)tier.Level,
            DailyActivePoint = (uint)tier.Point,
            IsHasTaken = IsTaken(tier.Level)
        }).ToList();
    }

    #endregion

    #region Daily missions

    /// <summary>Counts a finished daily main mission once per window. Returns false for a repeat.</summary>
    public bool RecordDailyMission(int mainMissionId)
    {
        if (Data.MissionsToday.Contains(mainMissionId)) return false;
        Data.MissionsToday.Add(mainMissionId);
        Data.MissionsTotal++;
        MarkDirty();
        return true;
    }

    private static int DailyCountFor(int type)
    {
        return GameData.DailyMissionCountData.TryGetValue(type, out var row) && row.DailyCount > 0 ? row.DailyCount : 1;
    }

    /// <summary>
    ///     A finished daily main mission stays finished forever and AcceptMainMission refuses it, so on every new day
    ///     the finished roster entries are reset to acceptable.
    /// </summary>
    private async ValueTask RearmDailyMissions(bool sync)
    {
        var missions = Player.MissionManager;
        if (missions is not { Enabled: true }) return;
        foreach (var id in GameData.DailyMissionDataData.Keys.OrderBy(id => id))
            if (missions.Data.GetMainMissionStatus(id) == MissionPhaseEnum.Finish)
                await missions.RemoveMainMission(id, sync);
    }

    /// <summary>
    ///     Offers today's daily missions: per DailyMissionType, DailyMissionCount.DailyCount rows (minus what this
    ///     window already offered) drawn with the same seeded shuffle from roster rows that are not running or
    ///     finished and whose UnlockMainMission is finished (<paramref name="ignoreGates" /> skips that check).
    /// </summary>
    public async ValueTask<List<int>> RollMissions(bool ignoreGates, bool sync = true)
    {
        var missions = Player.MissionManager;
        if (missions is not { Enabled: true } || GameData.DailyMissionDataData.Count == 0) return [];
        if (Data.DayKey != DailyResetClock.Today(Player.Data)) await EnsureToday(sync);

        var offered = Data.MissionsOffered.ToHashSet();
        var offeredCount = new Dictionary<int, int>();
        foreach (var id in Data.MissionsOffered)
            if (GameData.DailyMissionDataData.TryGetValue(id, out var row))
                offeredCount[row.DailyMissionType] = offeredCount.GetValueOrDefault(row.DailyMissionType) + 1;

        var byType = new SortedDictionary<int, List<int>>();
        foreach (var row in GameData.DailyMissionDataData.Values)
        {
            if (row.ID == 0 || offered.Contains(row.ID)) continue;
            if (missions.Data.GetMainMissionStatus(row.ID) != MissionPhaseEnum.None) continue;
            if (!ignoreGates && row.UnlockMainMission != 0 &&
                missions.Data.GetMainMissionStatus(row.UnlockMainMission) != MissionPhaseEnum.Finish) continue;
            if (!byType.TryGetValue(row.DailyMissionType, out var list)) byType[row.DailyMissionType] = list = [];
            list.Add(row.ID);
        }

        var accepted = new List<int>();
        foreach (var (type, pool) in byType)
        {
            var need = DailyCountFor(type) - offeredCount.GetValueOrDefault(type);
            if (need <= 0) continue;
            // The type is folded into the salt so two types never mirror each other's pick.
            var seed = DailySeededShuffle.Seed(Player.Uid + (long)type * 1000, Data.DayKey, Data.Nonce);
            foreach (var id in DailySeededShuffle.Pick(pool, need, seed))
            {
                await missions.AcceptMainMission(id, sync);
                if (missions.Data.GetMainMissionStatus(id) == MissionPhaseEnum.None) continue;
                Data.MissionsOffered.Add(id);
                accepted.Add(id);
            }
        }

        if (accepted.Count > 0)
        {
            MarkDirty();
            Logger.Debug($"uid {Player.Uid}: offered daily missions [{string.Join(",", accepted)}] " +
                        $"(gates {(ignoreGates ? "ignored" : "checked")})");
        }

        return accepted;
    }

    /// <summary>Roster diagnostics for the GM command; changes nothing.</summary>
    public DailyMissionBoard GetMissionBoard()
    {
        var missions = Player.MissionManager;
        var roster = GameData.DailyMissionDataData.Values.ToList();
        var quota = roster.Select(row => row.DailyMissionType).Distinct().Sum(DailyCountFor);
        if (missions == null) return new DailyMissionBoard(roster.Count, 0, 0, 0, Data.MissionsOffered.Count, quota);

        var gateSatisfied = roster.Count(row => row.UnlockMainMission == 0 ||
                                                missions.Data.GetMainMissionStatus(row.UnlockMainMission) ==
                                                MissionPhaseEnum.Finish);
        var running = roster.Count(row => missions.Data.GetMainMissionStatus(row.ID) == MissionPhaseEnum.Accept);
        var finished = roster.Count(row => missions.Data.GetMainMissionStatus(row.ID) == MissionPhaseEnum.Finish);
        return new DailyMissionBoard(roster.Count, gateSatisfied, running, finished, Data.MissionsOffered.Count, quota);
    }

    #endregion
}
