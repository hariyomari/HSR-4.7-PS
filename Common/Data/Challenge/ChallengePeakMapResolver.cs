using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Custom;

namespace March7thHoney.Data.Challenge;

/// <summary>
///     Works out where a Challenge Peak (异相仲裁) season is fought when Config/Custom/ChallengePeak.json does not say.
///     The peak tables carry no map at all, but every challenge arena floor marks where the server should put the
///     fight: "Contents" groups on a 301xx challenge plane holding one placeholder monster (config id 200001, the
///     dummy event 99999011). A season gets the newest such floor that Memory of Chaos / Pure Fiction / Apocalyptic
///     Shadow and the other peak seasons do not already use, and its groups are taken from the first set of
///     placeholders that stand on the same spot: the first group hosts the knight stages, the second the king.
///     Hiding seasons 9-12 from the 4.7.51 resources and deriving them again reproduces the hand-written entries of
///     seasons 9, 10 and 11 exactly, which is the evidence for the rule. It cannot tell apart floors a season shares
///     with Memory of Chaos (seasons 1-8 did), so those stay pinned in the JSON.
/// </summary>
public static class ChallengePeakMapResolver
{
    public const int PlaceholderMonsterConfigId = 200001;
    public const int PlaceholderEventId = 99999011;

    // Floors of the challenge arena planes, P30100..P30199.
    private const int ChallengePlanePrefix = 301;

    public sealed record Derived(int GroupId, int MapEntranceId, int FloorId, int KnightMazeGroupId, int KingMazeGroupId);

    /// <summary>
    ///     Derives every season of ChallengePeakGroupConfig that <paramref name="pinned" /> leaves out, newest season
    ///     first onto the newest free floor. Seasons for which no free floor is left are not returned.
    /// </summary>
    public static List<Derived> Derive(IEnumerable<ChallengePeakGroupOverride> pinned)
    {
        var pinnedList = pinned.ToList();
        var pinnedGroups = pinnedList.Select(x => x.GroupId).ToHashSet();

        var usedFloors = new HashSet<int>();
        foreach (var challenge in GameData.ChallengeConfigData.Values)
        {
            AddFloor(usedFloors, challenge.MapEntranceID);
            AddFloor(usedFloors, challenge.MapEntranceID2);
        }

        foreach (var stage in pinnedList.SelectMany(x => x.Stages.Values))
            AddFloor(usedFloors, stage.MapEntranceId);

        var candidates = GameData.FloorInfoData.Values
            .Where(floor => floor.FloorID / 100000 == ChallengePlanePrefix && !usedFloors.Contains(floor.FloorID))
            .Select(floor => (Floor: floor, Entrance: EntranceOf(floor.FloorID), Groups: PlaceholderSpot(floor)))
            .Where(x => x.Entrance > 0 && x.Groups.Count > 0)
            .OrderByDescending(x => x.Floor.FloorID)
            .ToList();

        var missing = GameData.ChallengePeakGroupConfigData.Keys
            .Where(id => !pinnedGroups.Contains(id))
            .OrderByDescending(id => id)
            .ToList();

        return missing.Zip(candidates, (groupId, c) => new Derived(groupId, c.Entrance, c.Floor.FloorID,
            c.Groups[0], c.Groups.Count > 1 ? c.Groups[1] : c.Groups[0])).ToList();
    }

    public static ChallengePeakGroupOverride ToOverride(Derived derived)
    {
        var result = new ChallengePeakGroupOverride { GroupId = derived.GroupId };
        if (!GameData.ChallengePeakGroupConfigData.TryGetValue(derived.GroupId, out var group)) return result;

        // NPC monster 0: ChallengePeakConfigExcel.RebuildChallengeMonsters takes it from the stage's monster template.
        foreach (var levelId in group.PreLevelIDList)
            result.Stages[levelId.ToString()] = new ChallengePeakStageOverride
                { MapEntranceId = derived.MapEntranceId, MazeGroupId = derived.KnightMazeGroupId };
        if (group.BossLevelID > 0)
            result.Stages[group.BossLevelID.ToString()] = new ChallengePeakStageOverride
                { MapEntranceId = derived.MapEntranceId, MazeGroupId = derived.KingMazeGroupId };
        return result;
    }

    /// <summary>
    ///     The placeholder groups of the floor's first spot, in group order: placeholders are grouped by where the
    ///     monster stands, and the spot holding the lowest group id is the one the season uses.
    /// </summary>
    public static List<int> PlaceholderSpot(FloorInfo floor)
    {
        var placeholders = floor.Groups.Values
            .Select(group => (Group: group, Monster: group.MonsterList.FirstOrDefault(IsPlaceholder)))
            .Where(x => x.Monster != null)
            .OrderBy(x => x.Group.Id)
            .ToList();
        if (placeholders.Count == 0) return [];

        var first = placeholders[0].Monster!;
        return placeholders.Where(x => SameSpot(x.Monster!, first)).Select(x => x.Group.Id).ToList();
    }

    private static bool IsPlaceholder(MonsterInfo monster)
    {
        return monster.ID == PlaceholderMonsterConfigId && monster.EventID == PlaceholderEventId && !monster.IsDelete;
    }

    private static bool SameSpot(PositionInfo a, PositionInfo b)
    {
        const float tolerance = 0.5f;
        return Math.Abs(a.PosX - b.PosX) <= tolerance && Math.Abs(a.PosY - b.PosY) <= tolerance &&
               Math.Abs(a.PosZ - b.PosZ) <= tolerance;
    }

    private static int EntranceOf(int floorId)
    {
        return GameData.MapEntranceData.Values.Where(x => x.FloorID == floorId).Select(x => x.ID)
            .DefaultIfEmpty(0).Min();
    }

    private static void AddFloor(HashSet<int> floors, int entranceId)
    {
        if (entranceId > 0 && GameData.MapEntranceData.TryGetValue(entranceId, out var entrance))
            floors.Add(entrance.FloorID);
    }
}
