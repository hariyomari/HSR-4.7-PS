using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.DailyActive;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Internationalization;
using March7thHoney.Proto;
using March7thHoney.Util;

// Daily training (每日实训) regression. Isolated configuration, temporary SQLite database, no network.
// Usage: DailyActiveRegression [--cache <Resource.bin>] [--stale-cache <older Resource.bin> --resources <Resources dir>]
// The cache (default Config/Resource.bin) must contain the daily tables (DailyQuest / DailyMissionData /
// DailyMissionCount); it is copied, never modified. --stale-cache also checks that a pre-daily cache is rejected.
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string? Arg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var cachePath = Path.GetFullPath(Arg("--cache") ?? Path.Combine(repository, "Config/Resource.bin"), repository);
var rawResources = Arg("--resources");
var scratch = Path.Combine(Path.GetTempPath(), "m7h-daily-active-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
Environment.CurrentDirectory = scratch;
var config = ConfigManager.Config;
config.Path.ConfigPath = scratch;
config.Path.LanguagePath = Path.Combine(repository, "Config/Languages");
config.Path.ResourcePath = Path.Combine(scratch, "no-raw-resource-json");
config.Path.DatabasePath = scratch;
config.Path.LogPath = Path.Combine(scratch, "Logs");
config.Database.DatabaseType = "sqlite";
config.Database.DatabaseName = "regression.db";
config.ServerOption.Language = "EN";
config.ServerOption.WelcomeMail.Enabled = false;
config.ServerOption.EnableMission = false;
config.ServerOption.EnableQuest = true;
config.ServerOption.DailyResetHour = 4;
I18NManager.LoadLanguage();
new DatabaseHelper().Initialize(startSaveThread: false);
ServerUtils.InitializeHandlers(); // mission finish-type handlers, as EntryPoint registers them

var failures = new List<string>();
var checks = 0;
void Check(string name, bool pass)
{
    checks++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures.Add(name);
}

// --- Cache ---------------------------------------------------------------------------------------------------
// Optional: an older cache without the daily tables must be rejected when the raw tables are available.
var staleCache = Arg("--stale-cache");
if (staleCache != null && rawResources != null)
{
    config.Path.ResourcePath = Path.GetFullPath(rawResources, repository);
    File.Copy(Path.GetFullPath(staleCache, repository), Path.Combine(scratch, "Resource.bin"), true);
    Check("cache without the daily tables is rejected while raw DailyQuest.json exists", !ResourceCache.LoadCache());
    config.Path.ResourcePath = Path.Combine(scratch, "no-raw-resource-json");
}

File.Copy(cachePath, Path.Combine(scratch, "Resource.bin"), true);
var loaded = ResourceCache.LoadCache();
Check("regenerated cache loads and passes validation", loaded);
if (!loaded) throw new InvalidOperationException($"Resource cache {cachePath} could not be loaded.");
Check("cache carries DailyQuest / DailyMissionData / DailyMissionCount",
    GameData.DailyQuestData.Count == 53 && GameData.DailyMissionDataData.Count == 88 &&
    GameData.DailyMissionCountData.GetValueOrDefault(1)?.DailyCount == 1);
Check("cache carries CocoonConfig CocoonType / FarmType",
    CocoonRow(1001).FarmType == CocoonFarmTypeEnum.COCOON_AVATAR_EXP &&
    CocoonRow(1004).FarmType == CocoonFarmTypeEnum.COCOON3 &&
    CocoonRow(1201).CocoonType == CocoonTypeEnum.TYPE_RELIC && CocoonRow(1301).CocoonType == CocoonTypeEnum.TYPE_WEEK);
Check("ExpeditionSetOrTakeReward finish ways are parsed",
    GameData.FinishWayData[GameData.QuestDataData[2100002].FinishWayID].FinishType ==
    MissionFinishTypeEnum.ExpeditionSetOrTakeReward);

// --- Day key ---------------------------------------------------------------------------------------------------
long Utc(int y, int mo, int d, int h, int mi) => new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.Zero).ToUnixTimeSeconds();
// 03:59 / 04:00 local at UTC+8 are 19:59 / 20:00 UTC the previous day.
Check("03:59 local belongs to the previous day", DailyResetClock.DayKey(Utc(2026, 9, 23, 19, 59), 8, 4) == 20260923);
Check("04:00 local starts the new day", DailyResetClock.DayKey(Utc(2026, 9, 23, 20, 0), 8, 4) == 20260924);
Check("reset hour 0 is plain local midnight", DailyResetClock.DayKey(Utc(2026, 9, 23, 16, 0), 8, 0) == 20260924);
Check("month boundary rolls back correctly", DailyResetClock.DayKey(Utc(2026, 9, 30, 19, 0), 8, 4) == 20260930 &&
                                              DailyResetClock.DayKey(Utc(2026, 9, 30, 20, 0), 8, 4) == 20261001);
config.ServerOption.DailyResetHour = 99;
Check("out-of-range reset hour is clamped", DailyResetClock.ResetHour == 23);
config.ServerOption.DailyResetHour = 4;

// --- Seeded shuffle ----------------------------------------------------------------------------------------------
var pool = Enumerable.Range(2100100, 40).ToList();
var seedA = DailySeededShuffle.Seed(70001, 20260924, 0);
Check("seed is stable across runtimes (FNV-1a-64 known value)", DailySeededShuffle.Seed(0, 0, 0) == Fnv("0|0|0"));
Check("same seed yields the same set regardless of pool order",
    DailySeededShuffle.Pick(pool, 8, seedA).SequenceEqual(DailySeededShuffle.Pick(Enumerable.Reverse(pool), 8, seedA)));
Check("nonce bump yields a different set",
    !DailySeededShuffle.Pick(pool, 8, seedA).SequenceEqual(
        DailySeededShuffle.Pick(pool, 8, DailySeededShuffle.Seed(70001, 20260924, 1))));
Check("pick never repeats and caps at the pool size",
    DailySeededShuffle.Pick(pool, 8, seedA).Distinct().Count() == 8 &&
    DailySeededShuffle.Pick([1, 2, 3], 8, seedA).Count == 3);

// --- Window roll ---------------------------------------------------------------------------------------------------
var player = await NewPlayer(70001);
var daily = player.GetComponent<DailyActiveManager>();
var quests = player.QuestManager!;
player.Data.FakeTimeDate = 20260910; // fake time starts at that date's daily reset -> window 20260910
Check("QuestType 5 dailies are not auto-accepted by condition",
    quests.Data.Quests.Values.All(q => GameData.QuestDataData[q.QuestId].QuestType != 5));
Check("first EnsureToday rolls", await daily.EnsureToday(false));
var firstSet = daily.QuestIds.ToList();
Check("fake time starts the fake date's own window", daily.Data.DayKey == 20260910);
// A constant fake instant, resent in every heartbeat, pulled the client's clock back and left reward popups
// ("items obtained") impossible to close; the fake clock has to run.
var fakeStart = ServerTimeProvider.GetServerUnixMs(player.Data);
await Task.Delay(50);
var fakeLater = ServerTimeProvider.GetServerUnixMs(player.Data);
Check("fake time runs inside the fake date's own window instead of repeating one instant",
    fakeLater > fakeStart && DailyResetClock.DayKey(fakeLater / 1000, DailyResetClock.UtcOffsetHours,
        DailyResetClock.ResetHour) == 20260910);
var serverTime = ConfigManager.Config.ServerOption.ServerTime;
var (savedFake, savedDate) = (serverTime.EnableFakeServerTime, serverTime.FixedDate);
serverTime.EnableFakeServerTime = true;
int GlobalWindow() => DailyResetClock.DayKey(ServerTimeProvider.GetServerUnixSec(), DailyResetClock.UtcOffsetHours,
    DailyResetClock.ResetHour);
serverTime.FixedDate = "2026-09-15";
var fixedWindow = GlobalWindow();
// "today" is a Local-kind date; it must not throw where the zone's offset differs from its standard offset.
serverTime.FixedDate = "today";
var todayWindow = GlobalWindow();
(serverTime.EnableFakeServerTime, serverTime.FixedDate) = (savedFake, savedDate);
Check("global fake time keys the fixed date's own window, and \"today\" resolves to today",
    fixedWindow == 20260915 && todayWindow == DateTime.Today.Year * 10000 + DateTime.Today.Month * 100 + DateTime.Today.Day);
Check("8 distinct eligible dailies are rolled",
    firstSet.Count == 8 && firstSet.Distinct().Count() == 8 && firstSet.All(daily.EligibleQuests().Contains));
Check("rolled dailies are accepted as Doing",
    firstSet.All(id => quests.GetQuestStatus(id) == QuestStatus.QuestDoing));
Check("second EnsureToday on the same day is a no-op", !await daily.EnsureToday(false) &&
                                                         daily.QuestIds.SequenceEqual(firstSet));
Check("the roll matches the documented seed",
    firstSet.SequenceEqual(DailySeededShuffle.Pick(daily.EligibleQuests(), 8,
        DailySeededShuffle.Seed(player.Uid, 20260910, 0))));
Check("deleted DailyQuest rows are never eligible",
    !daily.EligibleQuests().Intersect([2100134, 2100135, 2100136]).Any());

// --- Points and tiers ----------------------------------------------------------------------------------------------
var tiers = daily.CurrentTiers();
Check("five tiers 100..500", tiers.Select(t => t.Point).SequenceEqual([100, 200, 300, 400, 500]));
Check("claim of an unknown tier returns 4101", (await daily.ClaimTier(9)).Retcode == Retcode.RetDailyActiveLevelInvalid);
Check("claim without enough points returns 4103", (await daily.ClaimTier(1)).Retcode == Retcode.RetDailyActiveLevelApNotEnough);

var claimQuest = firstSet[0];
await quests.FinishQuest(claimQuest);
var pointBefore = daily.Point;
var (takeRet, takeItems) = await quests.TakeQuestReward(claimQuest);
var paid = takeItems?.Where(i => i.ItemId == 23).Sum(i => i.Count) ?? 0;
Check("claiming a daily pays item 23 into today's points",
    takeRet == Retcode.RetSucc && paid > 0 && daily.Point == pointBefore + paid);
Check("item 23 is reported through GetItem", player.InventoryManager!.GetItem(23)?.Count == daily.Point);
await player.InventoryManager.AddItem(23, 100, false);
Check("adding item 23 directly increases points", daily.Point == pointBefore + paid + 100);

var exp = player.Data.Exp;
var level = player.Data.Level;
var (claim1, claim1Items) = await daily.ClaimTier(1);
Check("reachable tier claims and pays", claim1 == Retcode.RetSucc && claim1Items.Count > 0 && daily.IsTaken(1));
Check("repeat claim of the same tier returns 4102",
    (await daily.ClaimTier(1)).Retcode == Retcode.RetDailyActiveLevelRewardAlreadyTaken);

await player.InventoryManager.AddItem(23, 600, false);
var (allLevels, allItems) = await daily.ClaimAll();
Check("claim-all takes every remaining tier in ascending order", allLevels.SequenceEqual([2, 3, 4, 5]) && allItems.Count > 0);
var hcoin = player.Data.Hcoin;
var expAfter = player.Data.Exp;
var levelAfter = player.Data.Level;
var (dupLevels, dupItems) = await daily.ClaimAll();
Check("duplicate claim-all pays nothing", dupLevels.Count == 0 && dupItems.Count == 0 &&
                                         player.Data.Hcoin == hcoin && player.Data.Exp == expAfter &&
                                         player.Data.Level == levelAfter);
Check("tier rewards paid Trailblaze EXP", expAfter != exp || levelAfter != level);
Check("level list reports every tier as taken", daily.BuildLevelList().All(l => l.IsHasTaken) &&
                                               daily.BuildLevelList().All(l => l.WorldLevel == 0));

// --- Optional quest reward ------------------------------------------------------------------------------------------
var optionalQuest = firstSet[2];
await quests.FinishQuest(optionalQuest);
var optionalQuestRewardId = GameData.QuestDataData[optionalQuest].RewardID;
var foreignRewardId = GameData.RewardDataData.Keys.First(id => id != optionalQuestRewardId);
var hcoinBeforeOptional = player.Data.Hcoin;
var pointBeforeOptional = daily.Point;
var (foreignRet, foreignItems) = await quests.TakeQuestOptionalReward(optionalQuest, foreignRewardId);
Check("optional reward rejects a reward id the quest does not define",
    foreignRet == Retcode.RetReqParaInvalid && foreignItems.Count == 0 &&
    quests.GetQuestStatus(optionalQuest) == QuestStatus.QuestFinish);
var (itemRet, itemItems) = await quests.TakeQuestOptionalReward(optionalQuest, 1);
Check("optional reward rejects a raw item id and grants nothing",
    itemRet == Retcode.RetReqParaInvalid && itemItems.Count == 0 && player.Data.Hcoin == hcoinBeforeOptional &&
    daily.Point == pointBeforeOptional && quests.GetQuestStatus(optionalQuest) == QuestStatus.QuestFinish);
var (plainRet, plainItems) = await quests.TakeQuestOptionalReward(optionalQuest, 0);
Check("optional reward without a choice pays only the quest RewardID once",
    plainRet == Retcode.RetSucc && plainItems.Count > 0 && quests.GetQuestStatus(optionalQuest) == QuestStatus.QuestClose &&
    (await quests.TakeQuestOptionalReward(optionalQuest, 0)).Item1 == Retcode.RetQuestRewardAlreadyTaken);

// --- Rollover ---------------------------------------------------------------------------------------------------------
var finishedToday = firstSet[1];
await quests.FinishQuest(finishedToday);
player.Data.FakeTimeDate = 20260911; // window 20260911
Check("day change rolls a new window", await daily.EnsureToday(false));
var secondSet = daily.QuestIds.ToList();
Check("rollover clears points and claimed tiers", daily.Point == 0 && daily.Data.TakenLevels.Count == 0);
Check("rollover rolls a set for the new day",
    secondSet.SequenceEqual(DailySeededShuffle.Pick(daily.EligibleQuests(), 8,
        DailySeededShuffle.Seed(player.Uid, 20260911, 0))));
Check("yesterday's dailies not rolled today expire (Close)",
    firstSet.Except(secondSet).All(id => quests.GetQuestStatus(id) == QuestStatus.QuestClose));
Check("today's dailies are re-armed from zero, including ones claimed before",
    secondSet.All(id => quests.GetQuestStatus(id) == QuestStatus.QuestDoing && quests.GetQuestProgress(id) == 0));
Check("no stray daily stays claimable outside today's set",
    quests.Data.Quests.Values.Where(q => GameData.QuestDataData[q.QuestId].QuestType == 5 &&
                                         !secondSet.Contains(q.QuestId))
        .All(q => q.QuestStatus == QuestStatus.QuestClose));

// --- GM reset (nonce) ------------------------------------------------------------------------------------------------
var nonceBefore = daily.Data.Nonce;
var resetSet = await daily.ResetNow(false);
Check("GM reset bumps the nonce and rolls a different set for the same day",
    daily.Data.Nonce == nonceBefore + 1 && daily.Data.DayKey == 20260911 && !resetSet.SequenceEqual(secondSet));

// FinishQuest dailies (2100156 / 2100159) re-arm the child quests they count.
var childDaily = 0;
for (var i = 0; i < 200; i++)
{
    childDaily = daily.QuestIds.FirstOrDefault(id => id is 2100156 or 2100159);
    if (childDaily != 0) break;
    await daily.ResetNow(false);
}

Check("a FinishQuest daily was rolled within 200 nonces", childDaily != 0);
if (childDaily != 0)
{
    var children = GameData.FinishWayData[GameData.QuestDataData[childDaily].FinishWayID].ParamIntList;
    foreach (var child in children) quests.ResetQuestState(child, QuestStatus.QuestFinish);
    // Re-rolling with the same (day, nonce) reproduces the same set, now over completed children.
    daily.Data.Nonce--;
    await daily.ResetNow(false);
    Check("same day and nonce reproduce the same set", daily.QuestIds.Contains(childDaily));
    Check("child quests of a rolled FinishQuest daily are re-armed",
        children.All(child => quests.GetQuestStatus(child) == QuestStatus.QuestDoing &&
                              quests.GetQuestProgress(child) == 0));
    await player.MissionManager!.HandleAllFinishType();
    Check("re-armed children do not satisfy the parent daily on the next heartbeat",
        quests.GetQuestStatus(childDaily) == QuestStatus.QuestDoing);
}

// --- Event-based finish types ---------------------------------------------------------------------------------------
var events = await NewPlayer(70002);
events.Data.FakeTimeDate = 20260910;
var eventQuests = events.QuestManager!;
int[] eventDailies = [2100108, 2100110, 2100107, 2100150, 2100109, 2100148, 2100002, 2100119];
foreach (var id in eventDailies) eventQuests.ResetQuestState(id, QuestStatus.QuestDoing);
for (var i = 0; i < 3; i++) await events.MissionManager!.HandleAllFinishType();
await events.MissionManager!.HandleAllFinishType(new MissionCountEventProbe());
Check("finish-type handlers are registered", March7thHoney.GameServer.Game.Mission.MissionManager.FinishTypeHandlers
    .ContainsKey(MissionFinishTypeEnum.AnyRelicLevelUp));
Check("heartbeats and foreign args never advance event-based dailies",
    eventDailies.All(id => eventQuests.GetQuestStatus(id) == QuestStatus.QuestDoing && eventQuests.GetQuestProgress(id) == 0));
await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.AnyEquipmentLevelUp);
Check("an event only advances its own finish type",
    eventQuests.GetQuestStatus(2100110) == QuestStatus.QuestFinish &&
    eventQuests.GetQuestStatus(2100108) == QuestStatus.QuestDoing);
foreach (var (type, quest) in new[]
         {
             (MissionFinishTypeEnum.AnyRelicLevelUp, 2100108), (MissionFinishTypeEnum.AnyAvatarLevelUp, 2100107),
             (MissionFinishTypeEnum.RogueFinishCnt, 2100150), (MissionFinishTypeEnum.ResolveRelic, 2100109),
             (MissionFinishTypeEnum.AnyItemComposeCount, 2100148),
             (MissionFinishTypeEnum.ExpeditionSetOrTakeReward, 2100002)
         })
{
    await events.MissionManager.TriggerCountEvent(type);
    Check($"{type} event finishes daily {quest}", eventQuests.GetQuestStatus(quest) == QuestStatus.QuestFinish);
}

await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.ChallengeFinishCnt, 1, 2);
Check("ChallengeFinishCnt ignores a challenge type the row does not list",
    eventQuests.GetQuestStatus(2100119) == QuestStatus.QuestDoing);
await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.ChallengeFinishCnt, 1, 1);
Check("ChallengeFinishCnt counts Forgotten Hall", eventQuests.GetQuestStatus(2100119) == QuestStatus.QuestFinish);

// Cocoon battles: CocoonFinish 2100131 (golden ids), FarmTypeCocoonFinish 2100157 [6,7,8],
// AnyCocoonFinish 2100102 [3] (Cavern of Corrosion).
int[] cocoonDailies = [2100131, 2100157, 2100158, 2100102];
foreach (var id in cocoonDailies) eventQuests.ResetQuestState(id, QuestStatus.QuestDoing);
await events.MissionManager.HandleAllFinishType(Battle(events, 1001, BattleEndStatus.BattleEndLose));
await events.MissionManager.HandleAllFinishType(Battle(events, 0, BattleEndStatus.BattleEndWin)); // farm element
Check("lost cocoon battles and farm-element battles count for nothing",
    cocoonDailies.All(id => eventQuests.GetQuestStatus(id) == QuestStatus.QuestDoing && eventQuests.GetQuestProgress(id) == 0));
await events.MissionManager.HandleAllFinishType(Battle(events, 1001, BattleEndStatus.BattleEndWin));
Check("a won golden calyx finishes CocoonFinish and golden FarmTypeCocoonFinish only",
    eventQuests.GetQuestStatus(2100131) == QuestStatus.QuestFinish &&
    eventQuests.GetQuestStatus(2100157) == QuestStatus.QuestFinish &&
    eventQuests.GetQuestStatus(2100158) == QuestStatus.QuestDoing &&
    eventQuests.GetQuestStatus(2100102) == QuestStatus.QuestDoing);
await events.MissionManager.HandleAllFinishType(Battle(events, 1004, BattleEndStatus.BattleEndWin));
Check("a won crimson calyx finishes the crimson farm-type daily",
    eventQuests.GetQuestStatus(2100158) == QuestStatus.QuestFinish &&
    eventQuests.GetQuestStatus(2100102) == QuestStatus.QuestDoing);
await events.MissionManager.HandleAllFinishType(Battle(events, 1201, BattleEndStatus.BattleEndWin));
Check("a won Cavern of Corrosion finishes AnyCocoonFinish [3]", eventQuests.GetQuestStatus(2100102) == QuestStatus.QuestFinish);

// Stagnant Shadow 2100101: cocoon battles also carry a MappingInfoId, so only a won non-cocoon battle counts.
eventQuests.ResetQuestState(2100101, QuestStatus.QuestDoing);
await events.MissionManager.HandleAllFinishType(Battle(events, 1001, BattleEndStatus.BattleEndWin));
await events.MissionManager.HandleAllFinishType(Battle(events, 0, BattleEndStatus.BattleEndLose));
Check("AnyFarmElementFinish ignores won calyxes and lost farm-element battles",
    eventQuests.GetQuestStatus(2100101) == QuestStatus.QuestDoing);
await events.MissionManager.HandleAllFinishType(Battle(events, 0, BattleEndStatus.BattleEndWin));
Check("a won farm-element battle finishes AnyFarmElementFinish", eventQuests.GetQuestStatus(2100101) == QuestStatus.QuestFinish);

// Count events with no earlier trigger: techniques 2100117 (x2), destructibles 2100125 (x3), SU rooms 2100126,
// support characters 2100128.
foreach (var id in new[] { 2100117, 2100125, 2100126, 2100128 }) eventQuests.ResetQuestState(id, QuestStatus.QuestDoing);
await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.UseMazeSkill);
Check("one technique cast is half of UseMazeSkill x2", eventQuests.GetQuestStatus(2100117) == QuestStatus.QuestDoing);
await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.UseMazeSkill);
await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.PropDestruct, 3);
await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.RogueExploreRoomFinishCnt);
await events.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.WinBattleWithAssistAvatarCnt);
Check("technique, destructible, SU room and support-character events finish their dailies",
    new[] { 2100117, 2100125, 2100126, 2100128 }.All(id => eventQuests.GetQuestStatus(id) == QuestStatus.QuestFinish));

// "Consume 120 Trailblaze Power" 2100139 counts the stamina actually spent, not one per spend.
eventQuests.ResetQuestState(2100139, QuestStatus.QuestDoing);
events.Data.Stamina = 240;
await events.SpendStamina(60);
Check("spending 60 Trailblaze Power is half of daily 2100139",
    eventQuests.GetQuestStatus(2100139) == QuestStatus.QuestDoing && eventQuests.GetQuestProgress(2100139) == 60);
await events.SpendStamina(60);
Check("spending 120 Trailblaze Power finishes daily 2100139", eventQuests.GetQuestStatus(2100139) == QuestStatus.QuestFinish);

// --- Daily mission counters ------------------------------------------------------------------------------------------
var eventDaily = events.GetComponent<DailyActiveManager>();
await eventDaily.EnsureToday(false);
eventQuests.ResetQuestState(2100003, QuestStatus.QuestDoing);
await events.MissionManager.HandleAllFinishType();
Check("FinishTodayDailyMissionCnt waits for a finished daily mission", eventQuests.GetQuestStatus(2100003) == QuestStatus.QuestDoing);
Check("a finished daily mission is recorded once per window",
    eventDaily.RecordDailyMission(3000201) && !eventDaily.RecordDailyMission(3000201) &&
    eventDaily.TodayMissionCount == 1 && eventDaily.TotalMissionCount == 1);
await events.MissionManager.HandleFinishType(MissionFinishTypeEnum.FinishTodayDailyMissionCnt);
Check("FinishTodayDailyMissionCnt completes after the daily mission", eventQuests.GetQuestStatus(2100003) == QuestStatus.QuestFinish);
events.Data.FakeTimeDate = 20260912;
await eventDaily.EnsureToday(false);
Check("today's mission count resets on rollover but the lifetime total survives",
    eventDaily.TodayMissionCount == 0 && eventDaily.TotalMissionCount == 1);

// --- Daily missions (mission system on) ------------------------------------------------------------------------------
var story = new PlayerInstance(70003) { PendingMissionEnabledOverride = true };
await story.InitializeAsync();
story.Data.Level = 70;
story.Data.FakeTimeDate = 20260910;
var storyDaily = story.GetComponent<DailyActiveManager>();
await storyDaily.EnsureToday(false);
Check("no daily mission is offered while its unlock mission is unfinished", (await storyDaily.RollMissions(false, false)).Count == 0);
var offered = await storyDaily.RollMissions(true, false);
Check("forced roll offers DailyMissionCount.DailyCount missions",
    offered.Count == 1 && story.MissionManager!.Data.GetMainMissionStatus(offered[0]) == MissionPhaseEnum.Accept);
Check("repeat roll the same day tops up nothing", (await storyDaily.RollMissions(true, false)).Count == 0);
await story.MissionManager!.FinishMainMission(offered[0]);
Check("finishing a daily main mission feeds the daily counters",
    storyDaily.TodayMissionCount == 1 && storyDaily.TotalMissionCount == 1);
story.Data.FakeTimeDate = 20260911;
await storyDaily.EnsureToday(false);
Check("finished daily missions are re-armed on rollover",
    story.MissionManager.Data.GetMainMissionStatus(offered[0]) == MissionPhaseEnum.None);
Check("a new day offers a mission again", (await storyDaily.RollMissions(true, false)).Count == 1);

// --- Persistence -----------------------------------------------------------------------------------------------------
DatabaseHelper.Instance!.SaveUidData(player.Uid);
Check("daily window is stored in its own table",
    DatabaseHelper.Instance.GetInstance<DailyActiveData>(player.Uid)?.QuestIds.Count == 8);

Console.WriteLine($"Scratch: {scratch}");
Console.WriteLine($"Daily active regression checks: {checks}, failures: {failures.Count}");
foreach (var failure in failures) Console.WriteLine($"  - {failure}");
return failures.Count == 0 ? 0 : 1;

static CocoonConfigExcel CocoonRow(int id) =>
    GameData.CocoonConfigData.Values.First(row => row.ID == id);

static ulong Fnv(string text)
{
    var hash = 14695981039346656037UL;
    foreach (var b in System.Text.Encoding.UTF8.GetBytes(text)) hash = unchecked((hash ^ b) * 1099511628211UL);
    return hash;
}

static async Task<PlayerInstance> NewPlayer(int uid)
{
    var p = new PlayerInstance(uid);
    await p.InitializeAsync();
    p.Data.Level = 70;
    return p;
}

static BattleInstance Battle(PlayerInstance p, int cocoonId, BattleEndStatus status) =>
    new(p, p.LineupManager!.GetCurLineup()!, new List<StageConfigExcel>())
    {
        CocoonId = cocoonId,
        CocoonWave = 1,
        WorldLevel = 0,
        MappingInfoId = cocoonId == 0 ? 1 : 0,
        BattleResult = new PVEBattleResultCsReq { EndStatus = status, Stt = new BattleStatistics() }
    };

internal sealed class MissionCountEventProbe;
