using March7thHoney.Data.Custom;
using March7thHoney.Enums.Rogue;

namespace March7thHoney.Data.Rogue;

/// <summary>
///     Builds the per-block room pools of the Simulated Universe DLC chessboards (<see cref="GameData.ChessRogueRoomData" />)
///     from Config/Custom/ChessRogueRoomGen.json.
/// </summary>
public static class ChessRogueRoomPools
{
    // The file assigns each room one base block type; the Swarm and Nous variants of a block, and the blocks that have
    // no rooms of their own, draw from the same rooms. Adventure is not an event block but uses the event rooms.
    private static readonly Dictionary<RogueDLCBlockTypeEnum, RogueDLCBlockTypeEnum[]> SharedPools = new()
    {
        [RogueDLCBlockTypeEnum.MonsterNormal] = [RogueDLCBlockTypeEnum.MonsterNormal, RogueDLCBlockTypeEnum.MonsterSwarm],
        [RogueDLCBlockTypeEnum.MonsterBoss] =
        [
            RogueDLCBlockTypeEnum.MonsterBoss, RogueDLCBlockTypeEnum.MonsterNousBoss,
            RogueDLCBlockTypeEnum.MonsterSwarmBoss
        ],
        [RogueDLCBlockTypeEnum.Event] =
        [
            RogueDLCBlockTypeEnum.Event, RogueDLCBlockTypeEnum.Reward, RogueDLCBlockTypeEnum.Adventure,
            RogueDLCBlockTypeEnum.NousSpecialEvent, RogueDLCBlockTypeEnum.SwarmEvent, RogueDLCBlockTypeEnum.NousEvent
        ],
        [RogueDLCBlockTypeEnum.Trade] = [RogueDLCBlockTypeEnum.Trade, RogueDLCBlockTypeEnum.BlackMarket]
    };

    /// <summary>
    ///     Replaces every pool with <paramref name="rooms" />. The file is also overlaid after a cache load, so the
    ///     pools are rebuilt rather than appended to.
    /// </summary>
    /// <returns>The number of pool entries, a room counted once per pool it joins.</returns>
    public static int Rebuild(IEnumerable<ChessRogueRoomConfig> rooms)
    {
        GameData.ChessRogueRoomData.Clear();
        var count = 0;
        foreach (var room in rooms)
        foreach (var blockType in SharedPools.GetValueOrDefault(room.BlockType) ?? [room.BlockType])
        {
            if (GameData.ChessRogueRoomData.TryGetValue(blockType, out var pool))
                pool.Add(room);
            else
                GameData.ChessRogueRoomData.Add(blockType, [room]);
            count++;
        }

        return count;
    }
}
