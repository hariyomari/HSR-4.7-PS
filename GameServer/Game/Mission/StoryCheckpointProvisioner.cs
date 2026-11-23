using System.Diagnostics;
using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Player;
using March7thHoney.Database.Mail;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Tutorial;
using March7thHoney.GameServer.Server;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Mission;

public sealed class StoryProvisionRequest
{
    public int TargetMainMissionId { get; init; }

    /// <summary>0 = none. A sub-mission of <see cref="TargetMainMissionId" />.</summary>
    public int TargetSubMissionId { get; init; }

    public StoryStopModeEnum StopMode { get; init; } = StoryStopModeEnum.Before;

    /// <summary>Gender chosen at CreateCharacter (100010134). None picks Woman; ignored while 100010134 is not finished.</summary>
    public Gender TrailblazerGender { get; init; } = Gender.None;

    public string? TrailblazerName { get; init; }

    /// <summary>Raise level gates instead of bypassing them, and level the roster for the reached world level.</summary>
    public bool TestReady { get; init; } = true;

    public StoryTutorialsModeEnum Tutorials { get; init; } = StoryTutorialsModeEnum.FinishPrior;
    public StoryTeleportsModeEnum Teleports { get; init; } = StoryTeleportsModeEnum.ReachedPlanes;
    public bool IncludeSideMissions { get; init; }
    public bool GrantTestCurrency { get; init; }

    /// <summary>
    ///     Advance the stored save instead of wiping it: characters, items and finished missions stay, and only the
    ///     missions still open on the way to the target are played. A save already past the target is refused.
    /// </summary>
    public bool KeepSave { get; init; }
}

public sealed class StoryProvisionReport
{
    public int TargetMainMissionId { get; set; }
    public int TargetSubMissionId { get; set; }
    public int TrackingMainMissionId { get; set; }
    public int PrerequisiteCount { get; set; }
    public int MissionsFinished { get; set; }
    public int SubMissionsFinished { get; set; }
    public int TeleportsUnlocked { get; set; }
    public int TutorialsMarkedFinished { get; set; }
    public int PropStatesRestored { get; set; }
    public int SpawnPlaneId { get; set; }
    public int SpawnFloorId { get; set; }
    public int SpawnEntryId { get; set; }
    public bool GenderSet { get; set; }
    public int PlayerLevel { get; set; }
    public int WorldLevel { get; set; }

    /// <summary>Current lineup: special (trial) avatar ids where set, else base avatar ids.</summary>
    public List<int> Lineup { get; set; } = [];

    public List<string> BypassedGates { get; set; } = [];
    public List<int> StalledMissions { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool WasOnline { get; set; }
    public long ElapsedMs { get; set; }
}

public sealed record StoryCheckpointPreset(string Id, int MainMissionId, int SubMissionId, StoryStopModeEnum StopMode);

/// <summary>
///     Builds a save that stands at a story checkpoint by replaying the story headlessly: a fresh offline player
///     accepts and finishes every prerequisite main mission through <see cref="MissionManager" />, so finish actions,
///     floor saved values, group properties, mission items, story lines, trial lineups and rewards all apply as in
///     play. The save is written once at the end.
/// </summary>
/// <remarks>
///     The prologue (1000101) is played as Kafka on floor 20001001 with the trial lineup seeded by
///     PlayerInstance.SeedNewPlayerAsync; the Trailblazer only exists from CreateCharacter (100010134), whose client
///     request (HandlerSetPlayerInfoCsReq) is mirrored right before that sub is finished. A checkpoint before it keeps
///     the gender unset and the Kafka / Silver Wolf trials in the lineup.
/// </remarks>
public static class StoryCheckpointProvisioner
{
    public const int PrologueMainMission = 1000101;
    public const int CreateCharacterSubMission = 100010134;

    // Prologue trials, as SpecialAvatar ids (their table rows are id * 10 + world level).
    public const int KafkaTrial = 1001005;
    public const int SilverWolfTrial = 1001006;
    private const int SilverWolfJoinsSubMission = 100010110;
    private const int SilverWolfLeavesSubMission = 100010123;

    private const int MaxPasses = 64;

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Logger Logger = new("StoryProvision");

    /// <summary>
    ///     One-click checkpoints for the admin console. Version entries are resolved from
    ///     Config/Custom/WarpStoryGates.json (the earliest chained mission of the version), so they follow the data.
    /// </summary>
    public static List<StoryCheckpointPreset> Presets()
    {
        int Entry(string version) => MissionManager.StoryEntryMission(GameData.WarpStoryGatesConfig.Gates
            .Where(gate => gate.Version == version).SelectMany(gate => gate.MainMissions));

        StoryCheckpointPreset[] presets =
        [
            new("prologue-kafka-start", PrologueMainMission, 100010102, StoryStopModeEnum.Before),
            new("prologue-receptacle", PrologueMainMission, CreateCharacterSubMission, StoryStopModeEnum.Before),
            new("post-prologue-wakeup", 1000201, 0, StoryStopModeEnum.Before),
            new("warp-tutorial", 1000301, 0, StoryStopModeEnum.After),
            new("jarilo-vi", 1010002, 0, StoryStopModeEnum.Before),
            new("xianzhou-luofu", 1020101, 0, StoryStopModeEnum.Before),
            new("penacony", Entry("2.0"), 0, StoryStopModeEnum.Before),
            new("amphoreus", Entry("3.0"), 0, StoryStopModeEnum.Before),
            new("version-4-0", Entry("4.0"), 0, StoryStopModeEnum.Before),
            new("version-4-7", Entry("4.7"), 0, StoryStopModeEnum.Before)
        ];
        return presets.Where(preset => GameData.MainMissionData.ContainsKey(preset.MainMissionId)).ToList();
    }

    /// <summary>Returns why the request cannot be provisioned, or null.</summary>
    public static string? Validate(StoryProvisionRequest request)
    {
        if (!GameData.MainMissionData.TryGetValue(request.TargetMainMissionId, out var main))
            return $"Unknown main mission {request.TargetMainMissionId}";
        if (request.TargetSubMissionId != 0 && !main.SubMissionIds.Contains(request.TargetSubMissionId))
            return $"Sub-mission {request.TargetSubMissionId} does not belong to main mission {request.TargetMainMissionId}";
        if (request.TrailblazerName is { Length: > 32 }) return "Trailblazer name is too long";
        return null;
    }

    /// <summary>
    ///     Wipes <paramref name="uid" />'s save (kicking an online session) and provisions the checkpoint. If the replay
    ///     itself fails, the previous save is put back; otherwise it becomes the uid's editor undo point.
    /// </summary>
    public static async ValueTask<StoryProvisionReport> ProvisionAsync(int uid, StoryProvisionRequest request)
    {
        if (Validate(request) is { } error) throw new ArgumentException(error);
        if (DatabaseHelper.Instance == null) throw new InvalidOperationException("Database is not initialized");

        await Gate.WaitAsync();
        try
        {
            using var lease = OfflinePlayerSession.Acquire(uid);
            var backup = DatabaseHelper.CloneUidGameplayData(uid);
            var run = new Run(uid, request);
            StoryProvisionReport report;
            try
            {
                report = await run.Execute();
            }
            catch
            {
                // Only a save the run already wiped or loaded is put back; before that (a session that would not
                // stop) the live rows are untouched and must stay the ones the online player writes to.
                if (run.Changed && backup.Count > 0) DatabaseHelper.ReplaceUidGameplayData(uid, backup);
                throw;
            }

            OfflinePlayerSession.KeepUndoPoint(uid, $"story checkpoint {request.TargetMainMissionId}", backup);
            return report;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Records every teleport checkpoint of the floors in scope as reached and saves it enabled, so the map list and
    ///     the Space Anchor in the world agree. ReachedPlanes: the floors entered, played by a finished sub-mission or
    ///     stood on. Returns how many teleports were newly unlocked.
    /// </summary>
    public static async ValueTask<int> UnlockTeleports(PlayerInstance player, StoryTeleportsModeEnum mode)
    {
        if (mode == StoryTeleportsModeEnum.None) return 0;

        var missions = player.MissionManager!;
        var floors = new HashSet<int>();
        if (mode == StoryTeleportsModeEnum.All)
        {
            floors.UnionWith(GameData.MapEntranceData.Values.Select(entrance => entrance.FloorID));
        }
        else
        {
            foreach (var planeFloors in player.SceneData!.EnteredSceneData.Values) floors.UnionWith(planeFloors);
            foreach (var subId in missions.Data.FinishedSubMissionIds)
                if (missions.GetSubMissionInfo(subId) is { LevelFloorID: > 0 } info)
                    floors.Add(info.LevelFloorID);
            floors.Add(player.Data.FloorId);
        }

        var unlocked = 0;
        foreach (var floorId in floors)
        {
            var entrance = MissionManager.FindEntranceForFloor(floorId);
            if (entrance == null) continue;
            if (GameData.MazePlaneData.GetValueOrDefault(entrance.PlaneID)?.PlaneType is not
                (PlaneTypeEnum.Maze or PlaneTypeEnum.Town or PlaneTypeEnum.Train)) continue;
            GameData.GetFloorInfo(entrance.PlaneID, floorId, out var floor);
            if (floor == null) continue;

            foreach (var group in floor.Groups.Values)
            foreach (var prop in group.PropList)
            {
                if (prop.IsDelete || prop.MappingInfoID == 0 ||
                    prop.State is not (PropStateEnum.CheckPointDisable or PropStateEnum.CheckPointEnable)) continue;
                if (await player.RecordTeleportUnlock(floorId, prop.MappingInfoID, false)) unlocked++;
                player.SetScenePropData(floorId, group.Id, prop.ID, PropStateEnum.CheckPointEnable);
            }
        }

        return unlocked;
    }

    private sealed class Run(int uid, StoryProvisionRequest request)
    {
        private readonly StoryProvisionReport _report = new()
        {
            TargetMainMissionId = request.TargetMainMissionId,
            TargetSubMissionId = request.TargetSubMissionId
        };

        private PlayerInstance _player = null!;

        /// <summary>Whether the stored save has been wiped or loaded for the replay, so a failure must put it back.</summary>
        public bool Changed { get; private set; }

        private MissionManager Missions => _player.MissionManager!;

        public async ValueTask<StoryProvisionReport> Execute()
        {
            var timer = Stopwatch.StartNew();
            await LoadPlayer();

            var chain = MissionManager.StoryPrerequisites(request.TargetMainMissionId, request.IncludeSideMissions);
            _report.PrerequisiteCount = chain.Count;
            foreach (var mainId in chain)
            {
                await CompleteMain(mainId);
                await LeaveFinishedRaid();
            }

            await ReachTarget();

            if (Missions.GetSubMissionStatus(CreateCharacterSubMission) == MissionPhaseEnum.Finish)
                await PrepareTrailblazer();
            else
                await PreparePrologue();

            RestorePropStates();
            await LeaveFinishedRuns();
            ApplyTutorials();
            if (request.TestReady) ApplyTestLoadout(chain);
            if (request.GrantTestCurrency) await GrantTestCurrency();
            PlaceAtTarget();
            _report.TeleportsUnlocked = await UnlockTeleports(_player, request.Teleports);
            Finish(timer);
            return _report;
        }

        private async ValueTask LoadPlayer()
        {
            _report.WasOnline = await OfflinePlayerSession.StopSessionsAsync(uid);
            if (request.KeepSave)
            {
                var data = DatabaseHelper.Instance!.GetInstance<PlayerData>(uid) ??
                           throw new InvalidOperationException($"UID {uid} has no save to advance");
                Changed = true;
                _player = new PlayerInstance(data) { PendingMissionEnabledOverride = true };
                await _player.InitializeAsync();
                var mainDone = Missions.GetMainMissionStatus(request.TargetMainMissionId) == MissionPhaseEnum.Finish;
                var subDone = request.TargetSubMissionId != 0 && request.StopMode == StoryStopModeEnum.Before &&
                              Missions.GetSubMissionStatus(request.TargetSubMissionId) == MissionPhaseEnum.Finish;
                if (mainDone || subDone)
                    throw new InvalidOperationException(
                        "The save is already past this story point; reset it to go back");
                return;
            }

            DatabaseHelper.Instance!.DeleteUidGameplayData(uid);
            Changed = true;
            _player = new PlayerInstance(uid) { PendingMissionEnabledOverride = true };
            await _player.InitializeAsync();
            _player.Data.Level = ConfigManager.Config.ServerOption.StartTrailblazerLevel;
            _player.Data.Exp = 0;
            _player.OnLevelChange();
            TutorialHelper.SeedFreshAccount(_player);
            MailHelper.SendWelcomeMail(uid, AccountData.GetAccountByUid(uid)?.Username ?? uid.ToString());
        }

        // Accepts (if needed) and plays one prerequisite main to its end.
        private async ValueTask CompleteMain(int mainId)
        {
            if (Missions.GetMainMissionStatus(mainId) == MissionPhaseEnum.Finish) return;
            if (!await EnsureAccepted(mainId)) return;

            do
            {
                await FinishRunningSubs(mainId, _ => true, null);
                if (Missions.GetMainMissionStatus(mainId) != MissionPhaseEnum.Accept) return;
            } while (await TakeDialogueChoice(mainId, _ => true, []));

            var open = RunningSubs(mainId, _ => true);
            _report.StalledMissions.Add(mainId);
            _report.Warnings.Add(open.Count > 0
                ? $"{mainId}: closed by force with sub-missions still running [{string.Join(", ", open)}]"
                : $"{mainId}: closed by force, its finish gate was never satisfied");
            await Guard($"finishing main {mainId}", () => Missions.FinishMainMission(mainId));
        }

        /// <summary>
        ///     A dialogue choice opens one CustomValue sub (TakeParamStrList triples name/value/prerequisite). With
        ///     nothing left running, pick the first open alternative whose prerequisite is finished, the way the
        ///     client's answer would, preferring <paramref name="preferred" /> (the target sub's own branch).
        /// </summary>
        private async ValueTask<bool> TakeDialogueChoice(int mainId, Func<int, bool> filter, HashSet<int> preferred)
        {
            if (!GameData.MainMissionData.TryGetValue(mainId, out var main)) return false;
            var choices = main.MissionInfo.SubMissionList
                .Where(sub => sub.TakeType == SubMissionTakeTypeEnum.CustomValue && sub.TakeParamStrList.Count >= 3)
                .ToList();

            List<(string Name, int Prerequisite)> Gates(Data.Config.SubMissionInfo sub) =>
                Triples(sub).Select(gate => (gate.Name, gate.Prerequisite)).ToList();

            static List<(string Name, uint Value, int Prerequisite)> Triples(Data.Config.SubMissionInfo sub)
            {
                var gates = new List<(string, uint, int)>();
                for (var i = 0; i + 2 < sub.TakeParamStrList.Count; i += 3)
                    gates.Add((sub.TakeParamStrList[i], uint.TryParse(sub.TakeParamStrList[i + 1], out var v) ? v : 0,
                        int.TryParse(sub.TakeParamStrList[i + 2], out var p) ? p : 0));
                return gates;
            }

            // A question is answered once any alternative sharing one of its gates has left None.
            var answered = choices.Where(sub => Missions.GetSubMissionStatus(sub.ID) != MissionPhaseEnum.None)
                .SelectMany(Gates).ToHashSet();
            var pick = choices
                .Where(sub => Missions.GetSubMissionStatus(sub.ID) == MissionPhaseEnum.None &&
                              (filter(sub.ID) || preferred.Contains(sub.ID)))
                .Where(sub => Gates(sub).Any(gate =>
                    (gate.Prerequisite == 0 ||
                     Missions.GetSubMissionStatus(gate.Prerequisite) == MissionPhaseEnum.Finish) &&
                    !answered.Contains(gate)))
                .OrderBy(sub => preferred.Contains(sub.ID) ? 0 : 1).ThenBy(sub => sub.ID)
                .FirstOrDefault();
            if (pick == null) return false;

            // Answer the way the client does (HandleCustomValue also records the choice on the main), falling back
            // to a direct accept only if the gate is not one the custom-value path recognises.
            var answer = Triples(pick).First(gate => !answered.Contains((gate.Name, gate.Prerequisite)) &&
                                                     (gate.Prerequisite == 0 || Missions.GetSubMissionStatus(gate.Prerequisite) == MissionPhaseEnum.Finish));
            await Guard($"answering choice {pick.ID}", () => Missions.HandleCustomValue(
                [new MissionCustomValue { BGDFHBAANLB = answer.Name, CustomValue = answer.Value }], pick.ID));
            if (Missions.GetSubMissionStatus(pick.ID) == MissionPhaseEnum.None)
                await Guard($"accepting choice {pick.ID}", async () => await Missions.AcceptSubMission(pick.ID, false));
            return Missions.GetSubMissionStatus(pick.ID) != MissionPhaseEnum.None;
        }

        // The client leaves a raid once its last main is done; leaving a finished run resets the raid's own mains.
        private async ValueTask LeaveFinishedRaid()
        {
            var raid = _player.RaidManager!;
            if (raid.Data.CurRaidId == 0) return;
            if (!raid.Data.RaidRecordDatas.TryGetValue(raid.Data.CurRaidId, out var records) ||
                !records.TryGetValue(raid.Data.CurRaidWorldLevel, out var record) ||
                record.Status != RaidStatus.Finish) return;
            await Guard($"leaving raid {raid.Data.CurRaidId}", () => raid.LeaveRaid(true));
        }

        private async ValueTask<bool> EnsureAccepted(int mainId)
        {
            var status = Missions.GetMainMissionStatus(mainId);
            if (status is MissionPhaseEnum.Accept or MissionPhaseEnum.Finish) return true;

            if (request.TestReady) RaiseLevelGates(mainId);
            foreach (var gate in Missions.UnmetGateList(mainId)) _report.BypassedGates.Add($"{mainId}: {gate}");

            await Guard($"accepting main {mainId}", async () => await Missions.AcceptMainMission(mainId, false));
            status = Missions.GetMainMissionStatus(mainId);
            if (status is MissionPhaseEnum.Accept or MissionPhaseEnum.Finish) return true;

            _report.StalledMissions.Add(mainId);
            _report.Warnings.Add($"{mainId}: accept refused");
            return false;
        }

        private void RaiseLevelGates(int mainId)
        {
            if (!GameData.MainMissionData.TryGetValue(mainId, out var excel)) return;
            foreach (var param in excel.TakeParam.Concat(excel.BeginParam))
                RaiseLevelGate(param.Type, param.Value);
        }

        private void RaiseLevelGate(MissionTakeTypeEnum type, int value)
        {
            if (type == MissionTakeTypeEnum.PlayerLevel && _player.Data.Level < value)
            {
                _player.Data.Level = value;
                _player.Data.Exp = 0;
                _player.OnLevelChange();
            }
            else if (type == MissionTakeTypeEnum.WorldLevel && _player.Data.WorldLevel < value)
            {
                _player.Data.WorldLevel = value;
            }
        }

        /// <summary>
        ///     Finishes the running subs of <paramref name="mainId" /> that pass <paramref name="filter" />, in id order,
        ///     until none is left or <paramref name="reached" /> holds. Returns whether it holds.
        /// </summary>
        private async ValueTask<bool> FinishRunningSubs(int mainId, Func<int, bool> filter, Func<bool>? reached)
        {
            for (var pass = 0; pass < MaxPasses; pass++)
            {
                if (reached?.Invoke() == true) return true;
                var running = RunningSubs(mainId, filter);
                if (running.Count == 0) break;
                foreach (var subId in running)
                {
                    if (reached?.Invoke() == true) return true;
                    if (Missions.GetSubMissionStatus(subId) != MissionPhaseEnum.Accept) continue;
                    await FinishSub(subId);
                }
            }

            return reached?.Invoke() == true;
        }

        private List<int> RunningSubs(int mainId, Func<int, bool> filter)
        {
            return GameData.MainMissionData.TryGetValue(mainId, out var main)
                ? main.SubMissionIds.Where(id => Missions.GetSubMissionStatus(id) == MissionPhaseEnum.Accept && filter(id))
                    .Order().ToList()
                : [];
        }

        private async ValueTask FinishSub(int subId)
        {
            if (subId == CreateCharacterSubMission && !_player.Data.IsGenderSet) await CreateCharacter();
            await Guard($"finishing sub {subId}", () => Missions.FinishSubMission(subId));
        }

        // A story hook that throws must not abort the whole replay: the status change it was part of is already
        // recorded, so the walk records the failure and goes on.
        private async ValueTask Guard(string step, Func<ValueTask> action)
        {
            try
            {
                await action();
            }
            catch (Exception e)
            {
                _report.Warnings.Add($"{step} threw {e.GetType().Name}: {e.Message}");
                Logger.Warn($"uid {uid}: {step} threw", e);
            }
        }

        // The gender/name page of 100010134: what HandlerSetPlayerInfoCsReq does before it finishes the sub.
        private async ValueTask CreateCharacter()
        {
            var gender = request.TrailblazerGender == Gender.Man ? Gender.Man : Gender.Woman;
            if (!string.IsNullOrWhiteSpace(request.TrailblazerName)) _player.Data.Name = request.TrailblazerName.Trim();
            await _player.SetHeroGender(gender);
            await _player.ChangeAvatarPathType(8001, MultiPathAvatarTypeEnum.Warrior);
            await _player.LineupManager!.AddAvatarToRealTeam(gender == Gender.Woman ? 8002 : 8001, false);
            await _player.LineupManager.AddAvatarToRealTeam(1001, false);
        }

        private async ValueTask ReachTarget()
        {
            var mainId = request.TargetMainMissionId;
            var subId = request.TargetSubMissionId;
            if (subId == 0)
            {
                if (request.StopMode == StoryStopModeEnum.After) await CompleteMain(mainId);
                else await EnsureAccepted(mainId);
                return;
            }

            if (!await EnsureAccepted(mainId)) return;
            bool Reached() => Missions.GetSubMissionStatus(subId) is MissionPhaseEnum.Accept or MissionPhaseEnum.Finish;

            // Only the sub's own prerequisites, so parallel objectives stay where a player would have left them.
            var ancestors = SubPrerequisites(subId);
            // The target itself may be a dialogue branch: it can be answered, never finished on the way.
            var preferred = new HashSet<int>(ancestors) { subId };
            if (!await ReachSub(mainId, ancestors.Contains, Reached, preferred))
            {
                _report.Warnings.Add($"{subId}: not reached through its prerequisites, finishing other running sub-missions");
                if (!await ReachSub(mainId, id => id != subId, Reached, preferred))
                {
                    _report.StalledMissions.Add(mainId);
                    _report.Warnings.Add($"{subId}: never accepted");
                    return;
                }
            }

            if (Missions.GetSubMissionStatus(subId) == MissionPhaseEnum.Finish)
                _report.Warnings.Add($"{subId}: finishes itself on accept, so it is already finished");
            else if (request.StopMode == StoryStopModeEnum.After)
                await FinishSub(subId);
        }

        private async ValueTask<bool> ReachSub(int mainId, Func<int, bool> filter, Func<bool> reached,
            HashSet<int> preferred)
        {
            while (true)
            {
                if (await FinishRunningSubs(mainId, filter, reached)) return true;
                if (!await TakeDialogueChoice(mainId, filter, preferred)) return reached();
            }
        }

        // Every sub that leads to subId: sequence prerequisites and the subs a dialogue choice is asked after.
        private static HashSet<int> SubPrerequisites(int subId)
        {
            var result = new HashSet<int>();
            var pending = new Stack<int>([subId]);
            while (pending.Count > 0)
            {
                var info = GameData.SubMissionInfoData.GetValueOrDefault(pending.Pop())?.SubMissionInfo;
                if (info == null) continue;
                var previous = info.TakeType switch
                {
                    SubMissionTakeTypeEnum.AnySequence or SubMissionTakeTypeEnum.MultiSequence =>
                        info.TakeParamIntList ?? [],
                    SubMissionTakeTypeEnum.CustomValue => info.TakeParamStrList.Where((_, i) => i % 3 == 2)
                        .Select(text => int.TryParse(text, out var id) ? id : 0).ToList(),
                    _ => []
                };
                foreach (var id in previous)
                    if (GameData.SubMissionInfoData.ContainsKey(id) && result.Add(id))
                        pending.Push(id);
            }

            return result;
        }

        // Before CreateCharacter the player is Kafka: no gender, the prologue trials in the lineup.
        private async ValueTask PreparePrologue()
        {
            _player.Data.IsGenderSet = false;
            var lineup = _player.LineupManager!;
            bool InLineup(int trial) =>
                lineup.GetCurLineup()?.BaseAvatars?.Any(slot => slot.SpecialAvatarId == trial) == true;

            if (!InLineup(KafkaTrial)) await lineup.AddSpecialAvatarToCurTeam(KafkaTrial * 10, false);
            if (Missions.GetSubMissionStatus(SilverWolfJoinsSubMission) == MissionPhaseEnum.Finish &&
                Missions.GetSubMissionStatus(SilverWolfLeavesSubMission) != MissionPhaseEnum.Finish &&
                !InLineup(SilverWolfTrial))
                await lineup.AddTrialAvatarToCurTeam(SilverWolfTrial, false);
        }

        private async ValueTask PrepareTrailblazer()
        {
            var lineup = _player.LineupManager!;
            foreach (var trial in new[] { KafkaTrial, SilverWolfTrial })
                await lineup.RemoveTrialAvatarFromCurTeam(trial, false);

            // A prologue closed by FinishMainMission skips the CreateCharacter hook.
            if (!_player.Data.IsGenderSet) await CreateCharacter();
            else if (!string.IsNullOrWhiteSpace(request.TrailblazerName))
                _player.Data.Name = request.TrailblazerName.Trim();

            var hero = _player.Data.CurrentGender == Gender.Man ? 8001 : 8002;
            var current = lineup.GetCurLineup();
            if (current is { } real && !real.IsExtraLineup() &&
                real.BaseAvatars?.Any(slot => slot.BaseAvatarId is 8001 or 8002 && slot.SpecialAvatarId == 0) != true)
                await lineup.AddAvatarToRealTeam(hero, false);

            await _player.AvatarManager!.GrantUnlockedStoryPaths(false);
        }

        /// <summary>
        ///     A PropState sub finished without a loaded scene never moved its prop. Persist each one's target state in
        ///     finish order (and open the doors a console unlocks), as an interaction in play would have.
        /// </summary>
        private void RestorePropStates()
        {
            foreach (var subId in Missions.Data.FinishedSubMissionIds)
            {
                var info = GameData.SubMissionInfoData.GetValueOrDefault(subId)?.SubMissionInfo;
                if (info is not { FinishType: MissionFinishTypeEnum.PropState, ParamInt1: > 0, ParamInt2: > 0 }) continue;
                GameData.GetFloorInfo(info.LevelPlaneID, info.LevelFloorID, out var floor);
                if (floor == null || !floor.Groups.TryGetValue(info.ParamInt1, out var group)) continue;
                if (group.SaveType == SaveTypeEnum.Reset) continue;
                var prop = group.PropList.Find(item => item.ID == info.ParamInt2);
                if (prop == null) continue;

                var state = (PropStateEnum)info.ParamInt3;
                _player.SetScenePropData(info.LevelFloorID, group.Id, prop.ID, state);
                _report.PropStatesRestored++;
                if (state != PropStateEnum.Open) continue;
                foreach (var (doorGroup, doors) in prop.UnlockDoorID)
                foreach (var door in doors)
                    _player.SetScenePropData(info.LevelFloorID, doorGroup, door, PropStateEnum.Open);
            }
        }

        // A raid or story line the replay left open would trap the login inside an old chapter's instance.
        private async ValueTask LeaveFinishedRuns()
        {
            await LeaveFinishedRaid();
            var raid = _player.RaidManager!;
            if (raid.Data.CurRaidId != 0)
            {
                var raidId = raid.Data.CurRaidId;
                var config = GameData.RaidConfigData.GetValueOrDefault(raidId * 100 + raid.Data.CurRaidWorldLevel);
                if (config?.MainMissionIDList.Contains(request.TargetMainMissionId) != true)
                {
                    await raid.AbandonAtLogin();
                    _report.Warnings.Add($"abandoned unfinished raid {raidId} opened by an earlier chapter");
                }
            }

            var storyLine = _player.StoryLineManager!;
            var current = storyLine.StoryLineData.CurStoryLineId;
            if (current != 0 && GameData.StoryLineData.TryGetValue(current, out var excel) &&
                Missions.GetSubMissionStatus(excel.EndCondition.Param) == MissionPhaseEnum.Finish)
            {
                await storyLine.FinishStoryLine(tp: false);
                _report.Warnings.Add($"left finished story line {current}");
            }
        }

        private void ApplyTutorials()
        {
            var tutorials = _player.TutorialData;
            if (tutorials == null || request.Tutorials == StoryTutorialsModeEnum.KeepOpen) return;

            foreach (var config in GameData.TutorialDataData.Values)
            {
                if (tutorials.Tutorials.TryGetValue(config.TutorialID, out var status) &&
                    status == TutorialStatus.TutorialFinish) continue;
                if (request.Tutorials == StoryTutorialsModeEnum.FinishPrior && !IsBehindPlayer(config)) continue;

                tutorials.Tutorials[config.TutorialID] = TutorialStatus.TutorialFinish;
                _report.TutorialsMarkedFinished++;
            }
        }

        // A tutorial is behind the player once a mission it opens or closes on is finished. The target's own
        // tutorials (the warp tutorial 3002 opens on FinishMainMission 1000301) stay untouched so they open in play.
        private bool IsBehindPlayer(Data.Excel.TutorialDataExcel config)
        {
            var targets = new HashSet<string>
                { request.TargetMainMissionId.ToString(), request.TargetSubMissionId.ToString() };
            if (config.TriggerParams.Any(trigger => IsMissionTrigger(trigger.TriggerType) &&
                                                    targets.Contains(trigger.TriggerParam)))
                return false;

            return config.TriggerParams.Concat(config.FinishTriggerParams)
                .Any(trigger => IsMissionTrigger(trigger.TriggerType) && int.TryParse(trigger.TriggerParam, out var id) &&
                                IsFinished(trigger.TriggerType, id));
        }

        private static bool IsMissionTrigger(string type) =>
            type is "FinishMainMission" or "TakeMainMission" or "FinishSubMission" or "TakeSubMission";

        private bool IsFinished(string type, int id) => type.EndsWith("MainMission", StringComparison.Ordinal)
            ? Missions.GetMainMissionStatus(id) == MissionPhaseEnum.Finish
            : Missions.GetSubMissionStatus(id) == MissionPhaseEnum.Finish;

        // Level and world level cover every gate of the chain; the roster is levelled to the world level's cap.
        private void ApplyTestLoadout(List<int> chain)
        {
            foreach (var mainId in chain.Append(request.TargetMainMissionId)) RaiseLevelGates(mainId);

            var cap = Math.Min(80, 20 + 10 * _player.Data.WorldLevel);
            foreach (var avatar in _player.AvatarManager!.Data.FormalAvatars)
            {
                if (avatar.Level >= cap) continue;
                avatar.Level = cap;
                avatar.Promotion = GameData.GetMinPromotionForLevel(cap);
            }
        }

        private async ValueTask GrantTestCurrency()
        {
            // Stellar Jade, Credits, Star Rail Pass, Star Rail Special Pass.
            foreach (var (item, count) in new[] { (1, 16000), (2, 1_000_000), (101, 50), (102, 50) })
                await _player.InventoryManager!.AddItem(item, count, false, sync: false);
        }

        // Track the target and stand where it is played: the target sub's waypoint (or the nearest one its finished
        // prerequisites point at), otherwise the start anchor of the mission floor's entrance.
        private void PlaceAtTarget()
        {
            var data = Missions.Data;
            data.TrackingMainMissionId = request.TargetMainMissionId;
            if (Missions.GetMainMissionStatus(request.TargetMainMissionId) != MissionPhaseEnum.Accept)
                data.TrackingMainMissionId = Missions.ResolveTrackingMainMissionId(0);
            var tracked = data.TrackingMainMissionId;
            _report.TrackingMainMissionId = tracked;

            var anchorSub = request.TargetSubMissionId != 0 && request.StopMode == StoryStopModeEnum.Before
                ? request.TargetSubMissionId
                : RunningSubs(tracked, _ => true).FirstOrDefault();
            var anchorInfo = Missions.GetSubMissionInfo(anchorSub);
            var floorId = anchorInfo?.WayPointFloorID > 0 ? anchorInfo.WayPointFloorID : anchorInfo?.LevelFloorID ?? 0;
            if (floorId == 0) floorId = Missions.GetMissionFloorId(tracked);

            var entrance = MissionManager.FindEntranceForFloor(floorId);
            if (entrance == null)
            {
                _report.Warnings.Add($"no map entrance for floor {floorId}; using the default spawn");
                _player.TryPlaceAtEntrance(PlayerInstance.DefaultSpawnEntryId);
                return;
            }

            GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floor);
            var anchor = floor == null ? null : WaypointAnchor(floor, anchorSub);
            if (!_player.TryPlaceAtEntrance(entrance.ID, anchor?.ToPositionProto(), anchor?.ToRotationProto()))
            {
                _report.Warnings.Add($"entrance {entrance.ID} has no usable anchor; using the default spawn");
                _player.TryPlaceAtEntrance(PlayerInstance.DefaultSpawnEntryId);
            }
        }

        private AnchorInfo? WaypointAnchor(FloorInfo floor, int subId)
        {
            var visited = new HashSet<int>();
            var pending = new Queue<int>([subId]);
            while (pending.Count > 0)
            {
                var id = pending.Dequeue();
                if (!visited.Add(id)) continue;
                var info = Missions.GetSubMissionInfo(id);
                if (info == null) continue;

                if (info.WayPointGroupID > 0 && info.WayPointEntityID > 0 &&
                    (info.WayPointFloorID == 0 || info.WayPointFloorID == floor.FloorID) &&
                    floor.Groups.TryGetValue(info.WayPointGroupID, out var group))
                {
                    var anchor = group.AnchorList.Find(item => item.ID == info.WayPointEntityID);
                    if (anchor != null) return anchor;
                    PositionInfo? target = group.PropList.Find(item => item.ID == info.WayPointEntityID) ??
                                           (PositionInfo?)group.NPCList.Find(item => item.ID == info.WayPointEntityID) ??
                                           group.MonsterList.Find(item => item.ID == info.WayPointEntityID);
                    if (target != null) return NearestAnchor(floor, target);
                }

                // No waypoint of its own (talks, CreateCharacter): the player is where the previous step left them.
                foreach (var previous in info.TakeParamIntList ?? [])
                    if (Missions.GetSubMissionStatus(previous) == MissionPhaseEnum.Finish)
                        pending.Enqueue(previous);
            }

            return null;
        }

        private static AnchorInfo? NearestAnchor(FloorInfo floor, PositionInfo target)
        {
            AnchorInfo? best = null;
            var bestDistance = float.MaxValue;
            foreach (var anchor in floor.Groups.Values.SelectMany(group => group.AnchorList))
            {
                var dx = anchor.PosX - target.PosX;
                var dy = anchor.PosY - target.PosY;
                var dz = anchor.PosZ - target.PosZ;
                var distance = dx * dx + dy * dy + dz * dz;
                if (distance >= bestDistance) continue;
                best = anchor;
                bestDistance = distance;
            }

            return best;
        }

        private void Finish(Stopwatch timer)
        {
            var data = Missions.Data;
            _report.MissionsFinished = data.FinishedMainMissionIds.Count;
            _report.SubMissionsFinished = data.FinishedSubMissionIds.Count;
            _report.SpawnPlaneId = _player.Data.PlaneId;
            _report.SpawnFloorId = _player.Data.FloorId;
            _report.SpawnEntryId = _player.Data.EntryId;
            _report.GenderSet = _player.Data.IsGenderSet;
            _report.PlayerLevel = _player.Data.Level;
            _report.WorldLevel = _player.Data.WorldLevel;
            _report.Lineup = _player.LineupManager!.GetCurLineup()?.BaseAvatars?
                .Select(slot => slot.SpecialAvatarId > 0 ? slot.SpecialAvatarId : slot.BaseAvatarId).ToList() ?? [];

            DatabaseHelper.MarkDirty(uid);
            DatabaseHelper.Instance!.SaveUidData(uid);
            _report.ElapsedMs = timer.ElapsedMilliseconds;
            Logger.Info($"uid {uid} provisioned at main {request.TargetMainMissionId}" +
                        (request.TargetSubMissionId != 0 ? $" sub {request.TargetSubMissionId}" : "") +
                        $" ({request.StopMode}): {_report.MissionsFinished} mains finished, " +
                        $"{_report.StalledMissions.Count} stalled, {_report.ElapsedMs} ms");
        }
    }
}
