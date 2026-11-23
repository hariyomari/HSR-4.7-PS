using Newtonsoft.Json;

namespace March7thHoney.Data.Custom;

/// <summary>
///     Planarcadia Reverie (client module ActivityBangboo, activity panel 50147, modules 5014701/5014702). The
///     Bangboo lane battles run on the client; the server keeps which stages are unlocked and cleared, the Infinity
///     run (wave and the board archive the client saves between waves), the posts read or liked and the anecdote
///     groups watched. The client reads its tables from ExcelOutput assets whose paths are not in the community name
///     list (BangbooLevel, BangbooHole, BangbooConfig, ActivityBangbooPerformance), so the resource export skips them;
///     Config/Custom/ActivityBangboo.json carries the columns the server needs, decoded from the OSBETAWin4.7.51
///     DesignData by row type (see docs/development/activity-bangboo.md). Loaded from Config/Custom on every boot and
///     not stored in Resource.bin.
/// </summary>
public class ActivityBangbooConfig
{
    [JsonProperty("activityModuleIds")] public List<int> ActivityModuleIds { get; set; } = [];

    /// <summary>
    ///     Also require each stage's UnlockSubMissionID (a BangbooLevel column the client never reads) to be finished.
    ///     Off by default: the activity's quest line (main mission 8048117) does not run on this server, so the
    ///     stages unlock in story order only.
    /// </summary>
    [JsonProperty("requireUnlockSubMission")]
    public bool RequireUnlockSubMission { get; set; }

    /// <summary>The stage whose first clear unlocks the Infinity holes (0 = unlocked from the start).</summary>
    [JsonProperty("infinityUnlockLevelId")]
    public int InfinityUnlockLevelId { get; set; }

    [JsonProperty("holes")] public List<ActivityBangbooHoleConfig> Holes { get; set; } = [];
    [JsonProperty("levels")] public List<ActivityBangbooLevelConfig> Levels { get; set; } = [];
    [JsonProperty("bangboos")] public List<ActivityBangbooUnitConfig> Bangboos { get; set; } = [];

    /// <summary>ActivityBangbooPerformance GroupIDs (the anecdotes watched with FinishPerformance).</summary>
    [JsonProperty("performanceGroupIds")]
    public List<int> PerformanceGroupIds { get; set; } = [];

    /// <summary>
    ///     Post ids accepted by read/like. BangbooPost is empty in the 4.7.51 build; the ids are the ones the
    ///     BangbooReadPost quests list. An empty list accepts any non-zero id.
    /// </summary>
    [JsonProperty("postIds")]
    public List<int> PostIds { get; set; } = [];

    public ActivityBangbooLevelConfig? GetLevel(int id) => Levels.FirstOrDefault(x => x.Id == id);
    public ActivityBangbooHoleConfig? GetHole(int id) => Holes.FirstOrDefault(x => x.Id == id);
    public ActivityBangbooUnitConfig? GetBangboo(int id) => Bangboos.FirstOrDefault(x => x.Id == id);

    /// <summary>The hole that lists the stage, or null for a stage outside every hole (tutorial, boss phases).</summary>
    public ActivityBangbooHoleConfig? GetHoleOfLevel(int levelId) =>
        Holes.FirstOrDefault(x => x.LevelIds.Contains(levelId));

    /// <summary>Story stages in play order: the Story holes by id, each hole's stages in their listed order.</summary>
    public List<int> StoryChain() =>
        Holes.Where(x => !x.IsInfinity).OrderBy(x => x.Id).SelectMany(x => x.LevelIds).ToList();
}

public class ActivityBangbooHoleConfig
{
    [JsonProperty("id")] public int Id { get; set; }

    /// <summary>Story or Infinity (RPG.GameCore.BangbooHoleType).</summary>
    [JsonProperty("type")] public string Type { get; set; } = "";

    /// <summary>The hole's stages; in an Infinity hole stage n is wave n.</summary>
    [JsonProperty("levelIds")] public List<int> LevelIds { get; set; } = [];

    [JsonIgnore] public bool IsInfinity => Type == "Infinity";
}

public class ActivityBangbooLevelConfig
{
    [JsonProperty("id")] public int Id { get; set; }

    /// <summary>RewardData granted on the first clear (BangbooLevel column ENGHMIAMPLG).</summary>
    [JsonProperty("rewardId")] public int RewardId { get; set; }

    /// <summary>BangbooLevel column BDGPDCKLFNF; only used with <see cref="ActivityBangbooConfig.RequireUnlockSubMission" />.</summary>
    [JsonProperty("unlockSubMissionId")]
    public int UnlockSubMissionId { get; set; }
}

public class ActivityBangbooUnitConfig
{
    [JsonProperty("id")] public int Id { get; set; }

    /// <summary>Fight or Tactics (RPG.GameCore.BangbooType).</summary>
    [JsonProperty("type")] public string Type { get; set; } = "";

    /// <summary>The stage whose first clear unlocks this Bangboo (0 = available from the start).</summary>
    [JsonProperty("unlockLevelId")]
    public int UnlockLevelId { get; set; }

    /// <summary>RPG.GameCore.BangbooType value (Fight = 1, Tactics = 2), as BangbooDeployCount's ParamInt1 uses it.</summary>
    [JsonIgnore]
    public int TypeValue => Type switch { "Fight" => 1, "Tactics" => 2, _ => 0 };
}
