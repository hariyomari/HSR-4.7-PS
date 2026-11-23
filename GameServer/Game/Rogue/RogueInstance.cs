using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Rogue;

[MemoryPackable]
public partial class RogueInstance
{
    public int AreaId { get; set; }
    public int MapId { get; set; }
    public int AeonId { get; set; }

    public int CurrentRoomProgress { get; set; }
    public int CurrentSiteId { get; set; }
    public int StartSiteId { get; set; }
    public Dictionary<int, RogueRoomData> Rooms { get; set; } = [];

    public List<int> BaseAvatarIds { get; set; } = [];

    // Roster holds trial/story avatar ids; the finish record renders them as trial avatars.
    public bool Trial { get; set; }
    public Dictionary<int, RogueBuffData> Buffs { get; set; } = [];
    public Dictionary<int, RogueMiracleData> Miracles { get; set; } = [];

    public int Coin { get; set; }
    public int RoomScore { get; set; }
    public int EarnedTalentCoin { get; set; }
    public bool IsWin { get; set; }
    public RogueStatus Status { get; set; }

    // Pending-action queue position: the start coin action is 1, each draft advances by 2.
    public int ActionSeq { get; set; }

    // Not persisted: a draft left unanswered across a relog is dropped rather than soft-locking the run.
    [MemoryPackIgnore] public RogueBuffSelectMenu? BuffSelect { get; set; }

    // One-shot flag for SyncRogueExploreWinScNotify on the run-final battle win.
    [MemoryPackIgnore] public bool ExploreWin { get; set; }

    public RogueRoomData? GetCurrentRoom()
    {
        return Rooms.GetValueOrDefault(CurrentSiteId);
    }

    public void InitRooms()
    {
        if (Rooms.Count > 0 || !GameData.RogueMapData.TryGetValue(MapId, out var sites)) return;

        foreach (var site in sites.Values.OrderBy(x => x.SiteID))
        {
            // Core maps key rooms as mapId*100+siteId; the tutorial map only has room mapId*100.
            var room = GameData.RogueRoomData.GetValueOrDefault(MapId * 100 + site.SiteID) ??
                       GameData.RogueRoomData.GetValueOrDefault(MapId * 100);
            // Later Rogue maps no longer encode their room pool in the map id. Keep the direct lookup
            // first for the tutorial/core maps, then select a valid room from the site's configured pool.
            if (room == null && GameData.RogueMapGenData.TryGetValue(site.SiteID, out var roomPool))
            {
                var availableRoomIds = roomPool.Where(GameData.RogueRoomData.ContainsKey).ToList();
                if (availableRoomIds.Count > 0)
                    room = GameData.RogueRoomData[availableRoomIds.RandomElement()];
            }
            if (room == null) continue;

            Rooms[site.SiteID] = new RogueRoomData
            {
                SiteId = site.SiteID,
                RoomId = room.RogueRoomID,
                Status = RogueRoomStatus.None,
                NextSiteIds = [.. site.NextSiteIDList],
                RoomType = room.RogueRoomType
            };
            if (site.IsStart) StartSiteId = site.SiteID;
        }
    }

    public RogueRoomData? EnterRoom(int siteId)
    {
        var prev = GetCurrentRoom();
        if (prev != null && !prev.NextSiteIds.Contains(siteId)) return null;
        if (!Rooms.TryGetValue(siteId, out var next)) return null;

        if (prev != null) prev.Status = RogueRoomStatus.Finish;
        CurrentRoomProgress++;
        CurrentSiteId = next.SiteId;
        next.Status = RogueRoomStatus.Play;
        return next;
    }

    public void AddBuff(BaseRogueBuffExcel buff)
    {
        if (!Buffs.TryGetValue(buff.MazeBuffID, out var owned))
        {
            Buffs[buff.MazeBuffID] = new RogueBuffData { BuffId = buff.MazeBuffID, Level = buff.MazeBuffLevel };
            return;
        }

        // Enhance from the level currently held, not the drafted row's level.
        if (GameData.RogueBuffData.TryGetValue(buff.MazeBuffID * 100 + owned.Level + 1, out var higher))
            owned.Level = higher.MazeBuffLevel;
    }
}

[MemoryPackable]
public partial class RogueRoomData
{
    public int SiteId { get; set; }
    public int RoomId { get; set; }
    public RogueRoomStatus Status { get; set; }
    public List<int> NextSiteIds { get; set; } = [];
    public int RoomType { get; set; }
}

[MemoryPackable]
public partial class RogueBuffData
{
    public int BuffId { get; set; }
    public int Level { get; set; }
}

[MemoryPackable]
public partial class RogueMiracleData
{
    public int MiracleId { get; set; }
    public int Level { get; set; }
}
