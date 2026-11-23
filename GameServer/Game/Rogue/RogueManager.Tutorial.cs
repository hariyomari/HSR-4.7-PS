using March7thHoney.Data.Config.Scene;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Kcp;
using March7thHoney.GameServer.Server.Packet.Send.Rogue;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Rogue;

public partial class RogueManager
{
    // FloorInfo.Load strips InitLevelGraph; Resource.bin does not retain BlockLine ValueSource links.
    // Exact identities below match the level exports and official 4.6 tutorial entity deltas:
    // map 1 seq 15071/15182/15283/15463; map 2 seq 28466/28599/28740/28942/29040/29130/29247.
    private static readonly (int MapId, int GroupId, int MonsterId, int KillMissionId, int MarkerId)[] TutorialEncounters =
    [
        (1, 3, 200001, 403000104, 300001),
        (1, 4, 200001, 403000107, 300001),
        (1, 5, 200001, 403000110, 300001),
        (1, 10, 200001, 403000113, 300002),
        (2, 3, 200002, 403000204, 300001),
        (2, 4, 200001, 403000209, 300001),
        (2, 19, 200005, 403000210, 300001),
        (2, 6, 200001, 403000212, 300001),
        (2, 7, 200001, 403000214, 300001),
        (2, 18, 200005, 403000217, 300001),
        (2, 29, 200001, 403000219, 300002)
    ];

    private static readonly (int MapId, int GroupId, int BarrierId, int MissionId)[] TutorialBarriers =
    [
        (1, 10, 300005, 403000104),
        (1, 10, 300006, 403000107),
        (1, 10, 300007, 403000110),
        (2, 19, 300004, 403000204),
        // This wall follows the pick machine (4,300002), not the group-4 kill marker.
        (2, 19, 300005, 403000207),
        (2, 18, 300008, 403000212),
        (2, 18, 300009, 403000214)
    ];

    private bool IsTutorial(RogueInstance? inst) =>
        Player.MissionEnabled && inst is { Trial: true, AreaId: 100, MapId: 1 or 2 };

    private bool IsFirstTutorial(RogueInstance? inst) => IsTutorial(inst) && inst!.MapId == 1;

    private bool TutorialMissionFinished(int missionId) =>
        Player.MissionManager!.GetSubMissionStatus(missionId) == MissionPhaseEnum.Finish;

    private bool IsTutorialGroupDefeated(RogueInstance inst, int groupId) => TutorialEncounters.Any(encounter =>
        encounter.MapId == inst.MapId && encounter.GroupId == groupId &&
        TutorialMissionFinished(encounter.KillMissionId));

    internal bool IsDefeatedTutorialMonster(int floorId, int groupId, int instId) =>
        IsTutorial(RogueInstance) && floorId == 80001000 + RogueInstance!.MapId &&
        TutorialEncounters.Any(encounter => encounter.MapId == RogueInstance.MapId &&
            encounter.GroupId == groupId && encounter.MonsterId == instId && TutorialMissionFinished(encounter.KillMissionId));

    private bool IsTutorialRoomCleared(RogueInstance inst) => IsTutorial(inst) &&
        TutorialMissionFinished(inst.GetCurrentRoom()?.RoomId switch
        {
            100 => 403000113,
            201 => 403000210,
            202 => 403000217,
            203 => 403000219,
            _ => 0
        });

    private bool HasPendingTutorialSettlement(RogueInstance inst) => IsTutorial(inst) &&
        Player.MissionManager!.GetMainMissionStatus(inst.MapId == 1 ? 4030001 : 4030002) == MissionPhaseEnum.Accept &&
        Player.MissionManager.GetSubMissionStatus(inst.MapId == 1 ? 403000114 : 403000220) == MissionPhaseEnum.Accept &&
        TutorialMissionFinished(inst.MapId == 1 ? 403000113 : 403000219);

    internal PropStateEnum? GetTutorialPropState(int floorId, int groupId, PropInfo prop)
    {
        if (!IsTutorial(RogueInstance) || floorId != 80001000 + RogueInstance!.MapId) return null;
        var inst = RogueInstance;
        foreach (var encounter in TutorialEncounters.Where(encounter => encounter.MapId == inst.MapId))
        {
            var markerGraph = encounter.MarkerId == 300002
                ? "Maze_Rogue_Prop_BossRoomFinishToast.json"
                : "Maze_Rogue_Prop_TriggerBuffSelect.json";
            if (groupId == encounter.GroupId && prop.ID == encounter.MarkerId && prop.PropID == 100 &&
                MatchesTutorialGraph(prop, markerGraph))
                return TutorialMissionFinished(encounter.KillMissionId) ? PropStateEnum.Open : PropStateEnum.Closed;
        }
        foreach (var barrier in TutorialBarriers.Where(barrier => barrier.MapId == inst.MapId))
            if (groupId == barrier.GroupId && prop.ID == barrier.BarrierId && prop.PropID == 100006 &&
                MatchesTutorialGraph(prop, "Maze_Rogue_Tutorial_BlockLine.json"))
                return TutorialMissionFinished(barrier.MissionId) ? PropStateEnum.Open : PropStateEnum.Closed;

        if (inst.MapId == 2 && groupId == 4 && prop.ID == 300002 && prop.PropID == 1001 &&
            MatchesTutorialGraph(prop, "Maze_Rogue_Tutorial_PickMachine.json"))
            return TutorialMissionFinished(403000207) ? PropStateEnum.Open :
                TutorialMissionFinished(403000209) && inst.BuffSelect?.HasSelection != true
                    ? PropStateEnum.Closed : PropStateEnum.Locked;

        if (inst.MapId == 2 && ((groupId is 19 or 18 && prop.ID == 300002) ||
                               (groupId == 29 && prop.ID == 300004)) && prop.PropID is 1000 or 1021)
            return IsTutorialRoomCleared(inst) && inst.BuffSelect?.HasSelection != true
                ? PropStateEnum.Open : PropStateEnum.Closed;
        return null;
    }

    private static bool MatchesTutorialGraph(PropInfo prop, string graph) =>
        string.IsNullOrEmpty(prop.InitLevelGraph) || prop.InitLevelGraph.EndsWith(graph, StringComparison.Ordinal);

    public async ValueTask RefreshTutorialProps(bool afterBattle = false, EntityProp? pickedMachine = null)
    {
        var scene = Player.SceneInstance;
        if (scene == null || GetActiveRoomExcel(scene.EntryId) == null) return;
        var changed = new List<BaseGameEntity>();
        foreach (var prop in scene.Entities.Values.OfType<EntityProp>())
        {
            // Unlocking the machine belongs to the resolved blessing draft, not the preceding kill.
            if (afterBattle && prop is { GroupId: 4, InstId: 300002 } && prop.PropInfo.PropID == 1001) continue;
            if (afterBattle && prop.Excel.PropType == PropTypeEnum.PROP_ROGUE_DOOR &&
                RogueInstance?.GetCurrentRoom()?.NextSiteIds.Count > 0) continue;
            if (GetTutorialPropState(scene.FloorId, prop.GroupId, prop.PropInfo) is not { } state || prop.State == state) continue;
            await prop.SetState(state, false);
            changed.Add(prop);
        }
        if (pickedMachine != null && !changed.Contains(pickedMachine)) changed.Add(pickedMachine);
        if (changed.Count > 0)
            await Player.SendPacket(new PacketSceneGroupRefreshScNotify(Player, changed));
    }

    public async ValueTask<Retcode?> PickTutorialAvatar(PickRogueAvatarCsReq req)
    {
        if (!IsTutorial(RogueInstance) || RogueInstance!.MapId != 2) return null;
        var scene = Player.SceneInstance;
        if (scene?.FloorId != 80001002 || GetActiveRoomExcel(scene.EntryId)?.RogueRoomID != 201 ||
            !scene.Entities.TryGetValue((int)req.PropEntityId, out var entity) ||
            entity is not EntityProp { GroupId: 4, InstId: 300002 } machine || machine.PropInfo.PropID != 1001 ||
            req.NOEPBNMJFGL.Count != 0 || req.DFPPCAJDBFK.Count != 1 || req.DFPPCAJDBFK[0] != 4000005)
            return Retcode.RetRogueStatusNotMatch;
        if (TutorialMissionFinished(403000207))
            return RogueInstance.BaseAvatarIds.Contains(4000005) ? Retcode.RetSucc : Retcode.RetRogueStatusNotMatch;
        if (machine.State != PropStateEnum.Closed || RogueInstance.BuffSelect?.HasSelection == true ||
            !TutorialMissionFinished(403000209) ||
            Player.MissionManager!.GetSubMissionStatus(403000207) != MissionPhaseEnum.Accept)
            return Retcode.RetRogueStatusNotMatch;

        await machine.SetState(PropStateEnum.Open, false);
        if (!RogueInstance.BaseAvatarIds.Contains(4000005)) RogueInstance.BaseAvatarIds.Add(4000005);
        await Player.MissionManager!.OnPlayerInteractWithProp();
        await RefreshTutorialProps(pickedMachine: machine);
        SaveRun();
        return Retcode.RetSucc;
    }

    private async ValueTask FinishTutorialRoom(RogueInstance inst)
    {
        if (inst.MapId != 2 || !IsTutorialRoomCleared(inst) || inst.GetCurrentRoom() is not { } room ||
            room.Status == RogueRoomStatus.Finish) return;
        room.Status = RogueRoomStatus.Finish;
        await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(inst));
        foreach (var siteId in room.NextSiteIds)
            if (inst.Rooms.TryGetValue(siteId, out var next))
            {
                next.Status = RogueRoomStatus.Unlock;
                await Player.SendPacket(BasePacket.Create(March7thHoney.Kcp.CmdIds.SyncRogueMapRoomScNotify,
                    new SyncRogueMapRoomScNotify { MapId = (uint)inst.MapId, HNEANMNJHDB = BuildRoom(next, next.Status) }));
            }
    }

    public async ValueTask ResumeTutorialVictoryNotification()
    {
        if (RogueInstance is not { Status: RogueStatus.Doing, IsWin: true, ExploreWin: true } inst ||
            !HasPendingTutorialSettlement(inst) || Player.SceneInstance is not { } scene ||
            GetActiveRoomExcel(scene.EntryId) == null || inst.GetCurrentRoom()?.Status != RogueRoomStatus.Finish) return;
        await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(inst));
        await Player.SendPacket(new PacketSyncRogueExploreWinScNotify());
        inst.ExploreWin = false;
    }
}
