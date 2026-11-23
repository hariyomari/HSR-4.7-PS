using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Enums;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Mission;

public partial class MissionManager
{
    /// <summary>
    ///     A mission with no schedule row is unbound and follows its gates. A bound mission is takeable
    ///     only while its activity module's window contains now. A missing activity table fails open.
    /// </summary>
    public bool IsMainMissionScheduled(int mainId)
    {
        MainMissionScheduleExcel.EnsureLoaded();
        if (!GameData.MainMissionScheduleData.TryGetValue(mainId, out var row) || row.ActivityModuleID == 0)
            return true;

        return IsActivityModuleScheduled(row.ActivityModuleID);
    }

    internal static bool IsActivityModuleScheduled(int activityModuleId)
    {
        var schedules = GameData.ActivityConfig.ScheduleData;
        if (schedules.Count == 0) return true;

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var schedule in schedules)
            if (schedule.ActivityId == activityModuleId && now >= schedule.BeginTime && now <= schedule.EndTime)
                return true;
        return false;
    }

    /// <summary>
    ///     An activity's standing entrance (see <see cref="ContentPackageConfigExcel.TryGetStandingEntrance" />)
    ///     that this player can use: the activity is inside its schedule and the hidden hub mission owning the
    ///     entrance group has been reached. The official server keeps that mission in progress for as long as
    ///     the activity runs. Here it reads as finished instead - every mission does while missions are
    ///     disabled, and a hub whose sub-mission graph is missing from the resources gets closed - so both
    ///     states count; a mission that has not been reached (None) does not.
    /// </summary>
    private IEnumerable<(int FloorId, int GroupId, int MainMissionId)> OpenStandingEntrances()
    {
        foreach (var package in GameData.ContentPackageConfigData.Values)
        {
            if (!package.TryGetStandingEntrance(out var floorId, out var groupId, out var mainMissionId)) continue;
            if (GetMainMissionStatus(mainMissionId) is not (MissionPhaseEnum.Accept or MissionPhaseEnum.Finish))
                continue;
            if (!IsActivityModuleScheduled(package.ActivityModuleID)) continue;
            yield return (floorId, groupId, mainMissionId);
        }
    }

    /// <summary>The hub mission of an open standing entrance: the client must keep seeing it in progress.</summary>
    public bool IsOpenStandingEntranceMission(int mainMissionId)
    {
        foreach (var entrance in OpenStandingEntrances())
            if (entrance.MainMissionId == mainMissionId)
                return true;
        return false;
    }

    /// <summary>
    ///     The state the client has to see for a sub-mission of an open standing entrance's hub mission; null
    ///     for anything else. The client activates a mission-owned group from a doing sub-mission whose own
    ///     config lists that group (MapDef.InitActivateHoyoGroupByMission reads SubMissionInfoConfig.GroupIDList).
    ///     In the hub mission that is its last step: 8048209 runs 804820901 (waits for the guide) and then
    ///     804820902, a talk step that lists the entrance group 159 and stays open for as long as the activity
    ///     runs. So the finish gate reads as doing and the steps before it as finished. When the mission
    ///     system is on and the sub-mission is in the resources, its real state is used instead.
    /// </summary>
    public MissionStatus? GetStandingEntranceSubMissionStatus(int subMissionId)
    {
        if (Enabled && GameData.SubMissionInfoData.ContainsKey(subMissionId)) return null;

        foreach (var entrance in OpenStandingEntrances())
        {
            if (!GameData.MainMissionData.TryGetValue(entrance.MainMissionId, out var main)) continue;
            if (main.MissionInfo.FinishSubMissionList.Contains(subMissionId)) return MissionStatus.MissionDoing;
            if (main.MissionInfo.StartSubMissionList.Contains(subMissionId)) return MissionStatus.MissionFinish;
        }

        return null;
    }

    /// <summary>
    ///     Whether a group's owner main mission lets the group exist. That normally means the mission is in
    ///     progress; the group of an open standing entrance also counts while its hub mission reads as
    ///     finished. Without that the entrance NPC's group never reaches the map data, the activity panel's
    ///     goto cannot find the entrance icon, and the button does nothing.
    /// </summary>
    public bool IsGroupOwnerMissionActive(int floorId, GroupInfo group)
    {
        if (group.OwnerMainMissionID == 0) return true;
        if (GetMainMissionStatus(group.OwnerMainMissionID) == MissionPhaseEnum.Accept) return true;

        foreach (var entrance in OpenStandingEntrances())
            if (entrance.FloorId == floorId && entrance.GroupId == group.Id &&
                entrance.MainMissionId == group.OwnerMainMissionID)
                return true;
        return false;
    }

    /// <summary>
    ///     Adds what the client needs to show the standing entrances on a floor: the hub mission as
    ///     unfinished, its last step as doing and the steps before it as finished.
    /// </summary>
    private void AddStandingEntranceMissions(MissionStatusBySceneInfo missionInfo, int floorId, HashSet<int> subIds)
    {
        foreach (var entrance in OpenStandingEntrances())
        {
            if (entrance.FloorId != floorId) continue;

            var mainId = (uint)entrance.MainMissionId;
            missionInfo.FinishedMainMissionIdList.Remove(mainId);
            if (!missionInfo.UnfinishedMainMissionIdList.Contains(mainId))
                missionInfo.UnfinishedMainMissionIdList.Add(mainId);

            if (!GameData.MainMissionData.TryGetValue(entrance.MainMissionId, out var main)) continue;
            foreach (var subId in main.MissionInfo.StartSubMissionList.Concat(main.MissionInfo.FinishSubMissionList))
            {
                if (GetStandingEntranceSubMissionStatus(subId) is not { } status || !subIds.Add(subId)) continue;
                missionInfo.SubMissionStatusList.Add(new Proto.Mission { Id = (uint)subId, Status = status });
            }
        }
    }

    /// <summary>
    ///     Official Prepared shape: Type Main, every TakeParam MultiSequence is already finished, and
    ///     every still-unmet take/begin entry is PlayerLevel or WorldLevel. Branch and Daily stay hidden.
    /// </summary>
    public bool IsPreparedEligible(MainMissionExcel excel)
    {
        if (excel.Type != MainMissionTypeEnum.Main) return false;

        var hasChain = false;
        foreach (var param in excel.TakeParam)
        {
            if (param.Type != MissionTakeTypeEnum.MultiSequence) continue;
            hasChain = true;
            if (!param.IsEqual(Data)) return false;
        }

        if (!hasChain) return false;
        return GateListNumericOnly(excel.TakeParam, excel.TakeOperation != OperationEnum.And) &&
               GateListNumericOnly(excel.BeginParam, excel.BeginOperation == OperationEnum.Or);
    }

    private bool GateListNumericOnly(List<MissionParam> parameters, bool orMode)
    {
        if (parameters.Count == 0) return true;
        var anySatisfied = false;
        foreach (var param in parameters)
        {
            if (param.IsEqual(Data))
            {
                anySatisfied = true;
                continue;
            }

            if (param.Type is not (MissionTakeTypeEnum.PlayerLevel or MissionTakeTypeEnum.WorldLevel))
                return false;
        }

        return !orMode || anySatisfied;
    }

    private void AcceptPrepared(MainMissionExcel excel, MissionSync sync)
    {
        Data.SetMainMissionStatus(excel.MainMissionID, MissionPhaseEnum.Prepared);
        foreach (var subId in excel.MissionInfo.StartSubMissionList)
            MissionSyncBuilder.NotePreparedSub(sync, subId);
    }

    /// <summary>
    ///     Closes sub-missions a finished main left running. Idempotent: a second login changes nothing.
    /// </summary>
    public async ValueTask NormalizeFinishedMainSubs(bool sendPacket)
    {
        if (!Enabled) return;
        var sync = new MissionSync();
        foreach (var mainId in Data.FinishedMainMissionIds.ToList())
        {
            if (!GameData.MainMissionData.TryGetValue(mainId, out var main)) continue;
            foreach (var subId in main.SubMissionIds)
            {
                if (Data.GetSubMissionStatus(subId) == MissionPhaseEnum.Finish) continue;
                Data.SetSubMissionStatus(subId, MissionPhaseEnum.Finish);
                sync.MissionList.Add(new Proto.Mission
                {
                    Id = (uint)subId,
                    Status = MissionStatus.MissionFinish,
                    Progress = 1
                });
            }
        }

        if (sync.MissionList.Count == 0) return;
        Logger.Debug($"Closed stale sub-missions of finished mains for uid {Player.Uid}: {sync.MissionList.Count}");
        MarkDirty();
        if (sendPacket) await Player.SendPacket(new PacketPlayerSyncScNotify(sync));
    }

    /// <summary>
    ///     Drops an untouched running mission whose event module is dark and is not flagged IsNotDelete.
    /// </summary>
    public async ValueTask CleanupUnavailableScheduledMissions(bool sendPacket)
    {
        if (!Enabled) return;
        MainMissionScheduleExcel.EnsureLoaded();
        foreach (var mainId in Data.RunningMainMissionIds.ToList())
        {
            if (!GameData.MainMissionScheduleData.TryGetValue(mainId, out var row) ||
                row.ActivityModuleID == 0 || row.IsNotDelete || IsMainMissionScheduled(mainId))
                continue;
            if (!GameData.MainMissionData.TryGetValue(mainId, out var main)) continue;

            var touched = false;
            foreach (var subId in main.SubMissionIds)
            {
                if (Data.GetSubMissionStatus(subId) == MissionPhaseEnum.Finish) touched = true;
                if (Data.SubMissionProgressDict.TryGetValue(subId, out var progress) && progress > 0) touched = true;
                if (touched) break;
            }

            if (touched) continue;
            Logger.Debug($"Removed scheduled mission {mainId} (module {row.ActivityModuleID} is dark) for uid {Player.Uid}");
            await RemoveMainMission(mainId, sendPacket);
        }
    }

    /// <summary>
    ///     Fills floor saved values a finished sub's SetFloorSavedValue action owns when the key is
    ///     missing. Existing keys are left alone.
    /// </summary>
    public void RepairMissingFloorSavedValues()
    {
        if (!Enabled || Player.SceneData == null) return;
        var changed = false;
        foreach (var subId in Data.FinishedSubMissionIds)
        {
            if (!GameData.SubMissionInfoData.TryGetValue(subId, out var sub) || sub.SubMissionInfo == null) continue;
            foreach (var action in sub.SubMissionInfo.FinishActionList ?? [])
            {
                if (action.FinishActionType != FinishActionTypeEnum.SetFloorSavedValue) continue;
                var text = action.FinishActionParaString;
                if (text.Count < 4) continue;
                if (!int.TryParse(text[1], out var floor) || floor == 0) continue;
                if (!int.TryParse(text[3], out var value)) continue;
                var key = text[2];
                if (string.IsNullOrEmpty(key)) continue;

                if (!Player.SceneData.FloorSavedData.TryGetValue(floor, out var saved))
                {
                    saved = [];
                    Player.SceneData.FloorSavedData[floor] = saved;
                }

                if (saved.ContainsKey(key)) continue;
                saved[key] = value;
                changed = true;
            }
        }

        if (changed) DatabaseHelper.MarkDirty(Player.Uid);
    }
}
