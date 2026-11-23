using March7thHoney.Data;
using March7thHoney.Data.Config;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Database.ChessRogue;
using March7thHoney.Enums.Rogue;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.ChessRogue;

public static class ChessRogueResources
{
    public const int EventDraftGroupId = 100004;
    public const int DiceSlotCount = 6;
    private const int DraftOfferCount = 3;

    public static Dictionary<int, int> DefaultSurfaces(int branchId)
    {
        if (!GameData.RogueNousDiceBranchData.TryGetValue(branchId, out var branch)) return [];
        return branch.GetDefaultSurfaceList()
            .Select((surfaceId, index) => (surfaceId, slot: index + 1))
            .ToDictionary(x => x.slot, x => x.surfaceId);
    }

    public static int ResolveBranchId(int branchId)
    {
        // The 4.5 start request omits the dice branch when the player has not edited a preset.
        // Treat only that omitted value as the first complete default; an explicit invalid id
        // must still be rejected by StartRun.
        if (branchId != 0) return branchId;
        return GameData.RogueNousDiceBranchData.Keys.OrderBy(id => id)
            .FirstOrDefault(id => DefaultSurfaces(id).Count == DiceSlotCount);
    }

    public static RogueDLCChessBoardExcel? PickBoard(int subMode, int level)
    {
        var boards = subMode == (int)RogueSubModeEnum.ChessRogueNous
            ? GameData.RogueNousChessBoardData
            : GameData.RogueSwarmChessBoardData;
        if (!boards.TryGetValue(level, out var list)) return null;

        var usable = list.Where(board => board.MapInfo is { RogueChestGridItemMap.Count: > 0 }).ToList();
        return usable.Count == 0 ? null : usable.RandomElement();
    }

    public static RogueDLCBlockTypeEnum RollBlockType(RogueChestGridItem item)
    {
        if (item.BlockTypeList.Count > 0) return item.BlockTypeList.RandomElement();

        var list = new RandomList<RogueDLCBlockTypeEnum>();
        list.Add(RogueDLCBlockTypeEnum.MonsterNormal, 8);
        list.Add(RogueDLCBlockTypeEnum.Reward, 4);
        list.Add(RogueDLCBlockTypeEnum.Event, 6);
        list.Add(RogueDLCBlockTypeEnum.NousSpecialEvent, 4);
        list.Add(RogueDLCBlockTypeEnum.NousEvent, 2);
        return list.GetRandom();
    }

    // Boss candidates per cell, as the legacy board: layer-1 boss, later-layer boss, and the final boss pair.
    public static void InitCellMonsters(ChessRogueRunData run, ChessRogueCellData cell, int layer)
    {
        switch ((RogueDLCBlockTypeEnum)cell.BlockType)
        {
            case RogueDLCBlockTypeEnum.MonsterBoss when layer == 1:
            {
                var decays = Enumerable.Range(101, 7).Where(i => i != 103).ToList();
                var first = decays.RandomElement();
                decays.Remove(first);
                cell.Monsters.Add(new ChessRogueCellMonsterData
                    { BossDecayId = first, MonsterId = Random.Shared.Next(221001, 221018) });
                cell.Monsters.Add(new ChessRogueCellMonsterData
                    { BossDecayId = decays.RandomElement(), MonsterId = Random.Shared.Next(221001, 221018) });
                break;
            }
            case RogueDLCBlockTypeEnum.MonsterBoss:
            {
                var decays = Enumerable.Range(2, 3).Select(i => i * 10 + 101).ToList();
                var first = decays.RandomElement();
                decays.Remove(first);
                cell.Monsters.Add(new ChessRogueCellMonsterData
                    { BossDecayId = first, MonsterId = Random.Shared.Next(222001, 222007) });
                cell.Monsters.Add(new ChessRogueCellMonsterData
                    { BossDecayId = decays.RandomElement(), MonsterId = Random.Shared.Next(222001, 222017) });
                break;
            }
            case RogueDLCBlockTypeEnum.MonsterNousBoss:
            case RogueDLCBlockTypeEnum.MonsterSwarmBoss:
                cell.Monsters.Add(new ChessRogueCellMonsterData { BossDecayId = 114, MonsterId = 223003 });
                cell.Monsters.Add(new ChessRogueCellMonsterData
                    { BossDecayId = 102 + run.BossAeonId * 10, MonsterId = Random.Shared.Next(223001, 223003) });
                cell.SelectedDecayIds.AddRange(run.BossDecayIds);
                break;
        }
    }

    /// <summary>
    ///     Rolls the room config for a cell on the layer's entrance. The first normal battle of a layer takes a room
    ///     with a sub-monster group. Returns false when the entrance has no room of the cell's block type.
    /// </summary>
    public static bool RollRoom(ChessRogueRunData run, ChessRogueCellData cell)
    {
        if (cell.EntranceId != 0) return true;

        var blockType = (RogueDLCBlockTypeEnum)cell.BlockType;
        if (!GameData.ChessRogueRoomData.TryGetValue(blockType, out var rooms)) return false;

        var pool = rooms.Where(room => room.EntranceId == run.LayerEntranceId &&
                                       GameData.MapEntranceData.ContainsKey(room.EntranceId)).ToList();
        if (pool.Count == 0) return false;

        if (run.FirstEnterBattle && blockType == RogueDLCBlockTypeEnum.MonsterNormal)
        {
            var withSubMonsters = pool.Where(room => room.SubMonsterGroup.Count > 0).ToList();
            if (withSubMonsters.Count > 0) pool = withSubMonsters;
            run.FirstEnterBattle = false;
        }

        var config = pool.RandomElement();
        cell.EntranceId = config.EntranceId;
        cell.AnchorGroup = config.AnchorGroup;
        cell.AnchorId = config.AnchorId;
        cell.RoomId = config.RoomPrefix * 10000 + cell.BlockType * 100 + Random.Shared.Next(1, 10);

        cell.LoadGroups = [.. config.DefaultLoadBasicGroup];
        cell.LoadGroups.AddRange((RogueCellMarkTypeEnum)cell.MarkType switch
        {
            RogueCellMarkTypeEnum.Choice => config.SelectEventLoadGroup,
            RogueCellMarkTypeEnum.Double => config.DoubleEventLoadGroup,
            _ => config.DefaultLoadGroup
        });
        cell.LoadGroups.AddRange(config.SubMonsterGroup);
        return true;
    }

    public static List<BaseRogueBuffExcel> DraftOffer(int groupId, ChessRogueRunData run)
    {
        if (!GameData.RogueBuffGroupData.TryGetValue(groupId, out var group)) return [];

        return group.BuffList
            .Where(buff => buff is RogueBuffExcel && !run.Buffs.ContainsKey(buff.MazeBuffID))
            .OrderBy(_ => Random.Shared.Next())
            .Take(DraftOfferCount)
            .OrderBy(buff => buff.MazeBuffID)
            .ToList();
    }

    public static ChessRogueDiceSurfaceContentEffect? SurfaceEffect(int surfaceId)
    {
        return GameData.ChessRogueDiceSurfaceEffectData.GetValueOrDefault(surfaceId)?.ContentEffects.FirstOrDefault();
    }
}
