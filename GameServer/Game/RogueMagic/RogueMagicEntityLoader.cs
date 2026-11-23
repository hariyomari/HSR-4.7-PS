using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Excel;
using March7thHoney.Database.RogueMagic;
using March7thHoney.Enums.RogueMagic;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.RogueCommon;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.RogueMagic;

/// <summary>
///     Loads the current magic room: the room's RogueMagicRoomGen groups plus the floor's Normal groups, room monsters
///     drawn from the rogue monster pools, and doors that advertise the next room type.
/// </summary>
public class RogueMagicEntityLoader(SceneInstance scene, RogueMagicRunData run, RogueMagicRoomData room)
    : SceneEntityLoader(scene)
{
    // Boss pools by layer index (legacy): third layer, second layer, first layer.
    private const int FinalLayerBossGroup = 500401;
    private const int SecondLayerBossGroup = 500301;
    private const int FirstLayerBossGroup = 400711;
    private const int FinalLayerIndex = 3;

    private const int CenterDoorInstId = 300002;
    private const int ExitDoorPropId = 1053;
    private const int CombatDoorPropId = 1054;
    private const int NonCombatDoorPropId = 1055;
    private const int GambleMachinePropId = 1049;

    private static readonly HashSet<int> DoorPropIds = [1033, 1034, 1035, 1036, 1037, 1000, 1053, 1054, 1055, 1056, 1057];

    // Legacy door weights. Adventure (60) is left out: 4.5 carries no RogueCommon adventure-room opcodes to run it.
    private static readonly (RogueMagicRoomTypeEnum Type, int Weight)[] DoorRoomWeights =
    [
        (RogueMagicRoomTypeEnum.Battle, 15),
        (RogueMagicRoomTypeEnum.Wealth, 4),
        (RogueMagicRoomTypeEnum.Shop, 4),
        (RogueMagicRoomTypeEnum.Event, 7),
        (RogueMagicRoomTypeEnum.Reward, 5),
        (RogueMagicRoomTypeEnum.Elite, 1)
    ];

    private readonly HashSet<RogueMagicRoomTypeEnum> _doorTypes = [];
    private readonly HashSet<int> _roomGroups = room.LoadGroups.ToHashSet();

    public override async ValueTask LoadEntity()
    {
        if (Scene.IsLoaded || Scene.FloorInfo == null) return;

        Scene.LeaveEntryId = RogueLobby.EntranceId;
        LoadGroups.AddRange(_roomGroups);
        foreach (var group in Scene.FloorInfo.Groups.Values.OrderBy(group => group.Id))
        {
            var isRoomGroup = _roomGroups.Contains(group.Id);
            if (isRoomGroup || group.Category == GroupCategoryEnum.Normal) await LoadGroup(group, isRoomGroup);
        }

        Scene.IsLoaded = true;
    }

    // Mission reconciliation walks every floor group; letting it run would re-add the other rooms' groups.
    public override ValueTask SyncEntity()
    {
        return ValueTask.CompletedTask;
    }

    public override async ValueTask<EntityMonster?> LoadMonster(MonsterInfo info, GroupInfo group,
        bool sendPacket = false)
    {
        if (info.IsClientOnly || info.IsDelete) return null;

        var roomType = (RogueMagicRoomTypeEnum)room.RoomType;
        var rogueMonster = roomType == RogueMagicRoomTypeEnum.Boss
            ? DrawBossMonster()
            : DrawRoomMonster(roomType == RogueMagicRoomTypeEnum.Elite
                ? [MonsterRankEnum.Elite]
                : [MonsterRankEnum.Minion, MonsterRankEnum.MinionLv2]);
        if (rogueMonster == null || !GameData.NpcMonsterDataData.TryGetValue(rogueMonster.NpcMonsterID, out var excel))
            return null;

        var entity = new EntityMonster(Scene, info.ToPositionProto(), info.ToRotationProto(), group.Id, info.ID, excel,
            info)
        {
            EventId = rogueMonster.EventID,
            CustomStageId = rogueMonster.EventID,
            RogueMonsterId = rogueMonster.RogueMonsterID
        };

        await Scene.AddEntity(entity, sendPacket);
        return entity;
    }

    private RogueMonsterExcel? DrawBossMonster()
    {
        var groupId = run.CurLayer()?.LayerIndex switch
        {
            FinalLayerIndex => FinalLayerBossGroup,
            2 => SecondLayerBossGroup,
            _ => FirstLayerBossGroup
        };
        if (!GameData.RogueMonsterGroupData.TryGetValue(groupId, out var monsterGroup)) return null;

        var weighted = new RandomList<int>();
        foreach (var (monsterId, weight) in monsterGroup.RogueMonsterListAndWeight)
            if (int.TryParse(monsterId, out var id) && GameData.RogueMonsterData.ContainsKey(id))
                weighted.Add(id, (int)weight);

        return weighted.GetCount() == 0 ? null : GameData.RogueMonsterData[weighted.GetRandom()];
    }

    private static RogueMonsterExcel? DrawRoomMonster(List<MonsterRankEnum> ranks)
    {
        var pool = GameData.RogueMonsterData.Values
            .Where(monster => GameData.NpcMonsterDataData.TryGetValue(monster.NpcMonsterID, out var npc) &&
                              ranks.Contains(npc.Rank))
            .ToList();
        return pool.Count == 0 ? null : pool.RandomElement();
    }

    public override async ValueTask<EntityProp?> LoadProp(PropInfo info, GroupInfo group, bool sendPacket = false)
    {
        if (info.PropID == GambleMachinePropId) return null;
        if (!DoorPropIds.Contains(info.PropID)) return await base.LoadProp(info, group, sendPacket);
        if (info.IsDelete || !GameData.MazePropData.TryGetValue(info.PropID, out var excel)) return null;

        var door = CreateDoor(excel, group, info);
        if (door == null) return null;

        door.State = PropStateEnum.Open;
        await Scene.AddEntity(door, sendPacket);
        return door;
    }

    private RogueMagicDoorProp? CreateDoor(MazePropExcel excel, GroupInfo group, PropInfo info)
    {
        var layer = run.CurLayer();
        if (layer == null) return null;

        var isCenter = info.ID == CenterDoorInstId;
        var isLastRoom = room.RoomIndex == layer.Rooms.Count;
        var door = new RogueMagicDoorProp(Scene, excel, group, info);

        if (isLastRoom && run.CurLayerIndex == run.Layers.Count)
        {
            if (!isCenter) return null;
            door.CustomPropId = ExitDoorPropId;
            return door;
        }

        RogueMagicRoomTypeEnum nextType;
        if (room.RoomIndex == layer.Rooms.Count - 1)
        {
            if (!isCenter) return null;
            nextType = RogueMagicRoomTypeEnum.Boss;
        }
        else if (room.RoomIndex == layer.Rooms.Count - 2 && layer.LayerIndex == FinalLayerIndex)
        {
            if (!isCenter) return null;
            nextType = RogueMagicRoomTypeEnum.Reforge;
        }
        else
        {
            // The next room lives on the next layer's entrance when this room closes the layer.
            var targetEntrance = isLastRoom ? run.Layers[run.CurLayerIndex].EntranceId : layer.EntranceId;
            var available = RogueMagicManager.RoomTypesAt(targetEntrance);
            var weighted = new RandomList<RogueMagicRoomTypeEnum>();
            foreach (var (type, weight) in DoorRoomWeights)
                if (available.Contains(type) && !_doorTypes.Contains(type))
                    weighted.Add(type, weight);
            if (weighted.GetCount() == 0) return null;

            nextType = weighted.GetRandom();
            _doorTypes.Add(nextType);
        }

        door.CustomPropId = nextType is RogueMagicRoomTypeEnum.Event or RogueMagicRoomTypeEnum.Wealth
            or RogueMagicRoomTypeEnum.Shop or RogueMagicRoomTypeEnum.Reward
            ? NonCombatDoorPropId
            : CombatDoorPropId;
        door.DoorInfo = new PGLLFCMEFKG
        {
            LHLKJIDFLIN = (uint)nextType,
            OEEDPJHLPHL = isLastRoom
        };
        return door;
    }
}

public class RogueMagicDoorProp(SceneInstance scene, MazePropExcel excel, GroupInfo group, PropInfo prop)
    : EntityProp(scene, excel, group, prop)
{
    public int CustomPropId { get; set; }

    // Next room type + enter-next-layer; the client echoes the type back in RogueMagicEnterRoom/EnterLayer.
    public PGLLFCMEFKG? DoorInfo { get; set; }

    public override SceneEntityInfo ToProto()
    {
        var proto = base.ToProto();
        if (DoorInfo != null) proto.Prop.ExtraInfo = new PropExtraInfo { IDMAELPELLA = DoorInfo };
        if (CustomPropId != 0) proto.Prop.PropId = (uint)CustomPropId;
        return proto;
    }
}
