using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.RogueCommon;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.Scene;

namespace March7thHoney.GameServer.Game.Rogue;

public class RogueEntityLoader(SceneInstance scene, PlayerInstance player) : SceneEntityLoader(scene)
{
    // Room props belong to one run: a portal opened or a chest used in an earlier run must not carry over.
    public override bool PersistsPropState => false;

    private bool BelongsToActiveRoom(int groupId)
    {
        var room = player.RogueManager?.GetActiveRoomExcel(Scene.EntryId);
        return room == null || groupId == room.GroupID || room.GroupWithContent.ContainsKey(groupId);
    }

    public override ValueTask<List<BaseGameEntity>?> LoadGroup(GroupInfo info, bool forceLoad = false)
    {
        // One floor contains multiple room templates. RogueRoom selects both the main prop group and
        // its content groups; loading every floor group stacks other rooms' Closed barriers on ours.
        // Apply this even to forced loads and mission reconciliation, which also walk the whole floor.
        return BelongsToActiveRoom(info.Id)
            ? base.LoadGroup(info, forceLoad)
            : ValueTask.FromResult<List<BaseGameEntity>?>(null);
    }

    public override async ValueTask LoadEntity()
    {
        var firstLoad = !Scene.IsLoaded;
        var roomExcel = player.RogueManager?.GetActiveRoomExcel(Scene.EntryId);
        if (firstLoad && roomExcel != null)
            Scene.LeaveEntryId = RogueLobby.EntranceId;

        await base.LoadEntity();

        // A room that spawned no monster has no battle that could ever clear it, so its exit starts open
        // instead of leaving the run at a dead end. Not verified against an official capture.
        if (firstLoad && roomExcel != null && !Scene.Entities.Values.OfType<EntityMonster>().Any())
            foreach (var door in Scene.GetEntitiesInGroup<EntityProp>(roomExcel.GroupID)
                         .Where(door => door.RogueDoorTarget != null))
                door.State = PropStateEnum.Open;
    }

    public override async ValueTask<EntityMonster?> LoadMonster(MonsterInfo info, GroupInfo group,
        bool sendPacket = false)
    {
        var manager = player.RogueManager;
        var roomExcel = manager?.GetActiveRoomExcel(Scene.EntryId);
        if (manager == null || roomExcel == null) return await base.LoadMonster(info, group, sendPacket);

        if (manager.RogueInstance is { IsWin: true } run && run.GetCurrentRoom()?.Status == Proto.RogueRoomStatus.Finish)
            return null; // A completed room awaiting settlement must not respawn its encounters on relogin.

        // In a live room only the room's own groups spawn, each swapped to a drawn RogueMonster.
        if (info.IsClientOnly || info.IsDelete) return null;
        if (manager.IsDefeatedTutorialMonster(Scene.FloorId, group.Id, info.ID)) return null;
        var content = roomExcel.GroupWithContent.GetValueOrDefault(group.Id);
        if (content == 0) return null;

        var rogueMonsterId = RogueManager.ResolveRogueMonsterId(content);
        if (!GameData.RogueMonsterData.TryGetValue(rogueMonsterId, out var rogueMonster)) return null;
        if (!GameData.NpcMonsterDataData.TryGetValue(rogueMonster.NpcMonsterID, out var npcMonster)) return null;

        var entity = new EntityMonster(Scene, info.ToPositionProto(), info.ToRotationProto(), group.Id, info.ID,
            npcMonster, info)
        {
            EventId = rogueMonster.EventID,
            CustomStageId = rogueMonster.EventID,
            RogueMonsterId = rogueMonsterId,
            CustomLevel = manager.GetCurrentMonsterLevel()
        };

        await Scene.AddEntity(entity, sendPacket);
        return entity;
    }

    public override async ValueTask<EntityProp?> LoadProp(PropInfo info, GroupInfo group, bool sendPacket = false)
    {
        if (!BelongsToActiveRoom(group.Id)) return null;
        // Apply run-specific state before the first entity packet, including late group loads/relogin.
        var prop = await base.LoadProp(info, group, false);
        if (prop == null) return null;
        var manager = player.RogueManager;
        var roomExcel = manager?.GetActiveRoomExcel(Scene.EntryId);
        if (manager != null && roomExcel != null)
        {
            if (prop.Excel.PropType == PropTypeEnum.PROP_ROGUE_DOOR && group.Id == roomExcel.GroupID)
            {
                // The official room ships its exit portal Closed with the destination attached
                // (rogue_game_info{room_id, site}); OpenRoomExit opens it once the room is cleared.
                prop.State = PropStateEnum.Closed;
                prop.RogueDoorTarget = ResolveDoorTarget(manager.GetRoomExitTargets(), group, info.ID);
            }
            if (manager.GetTutorialPropState(Scene.FloorId, group.Id, info) is { } state)
                prop.State = state;
        }
        if (sendPacket) await player.SendPacket(new PacketSceneGroupRefreshScNotify(player, prop));
        return prop;
    }

    public override ValueTask<EntityNpc?> LoadNpc(NpcInfo info, GroupInfo group, bool sendPacket = false) =>
        BelongsToActiveRoom(group.Id)
            ? base.LoadNpc(info, group, sendPacket)
            : ValueTask.FromResult<EntityNpc?>(null);

    // Doors are paired with next sites in ascending inst id / site id order; a surplus door leads to the last
    // site. Official captures only cover single-door rooms, so the pairing itself is unverified.
    private static RogueDoorTarget? ResolveDoorTarget(List<RogueDoorTarget> targets, GroupInfo group, int instId)
    {
        if (targets.Count == 0) return null;

        var doors = group.PropList
            .Where(prop => !prop.IsDelete && GameData.MazePropData.TryGetValue(prop.PropID, out var excel) &&
                           excel.PropType == PropTypeEnum.PROP_ROGUE_DOOR)
            .Select(prop => prop.ID)
            .Order()
            .ToList();
        var index = doors.IndexOf(instId);
        return index < 0 ? null : targets[Math.Min(index, targets.Count - 1)];
    }
}
