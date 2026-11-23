using Newtonsoft.Json;

namespace March7thHoney.Data.Custom;

/// <summary>
///     Containment Starts With a Roll (骰子收容, client module DiceyDungeon, activity panel 50140). The client reads its
///     levels, nodes, avatars and reward pools from ExcelOutput tables whose paths are not in the community name
///     list, so the resource export skips them; Config/Custom/DiceyDungeon.json carries the rows the server needs,
///     decoded from the OSBETAWin4.7.51 DesignData by row type (see docs/development/dicey-dungeon.md).
///     Loaded from Config/Custom on every boot and not stored in Resource.bin.
/// </summary>
public class DiceyDungeonConfig
{
    [JsonProperty("activityModuleId")] public int ActivityModuleId { get; set; }
    [JsonProperty("maxSkillNum")] public int MaxSkillNum { get; set; } = 5;
    [JsonProperty("maxMiracleNumChallenge")] public int MaxMiracleNumChallenge { get; set; } = 3;
    [JsonProperty("maxRerollTime")] public int MaxRerollTime { get; set; } = 1;
    [JsonProperty("recoverUnlockNodeId")] public int RecoverUnlockNodeId { get; set; }
    [JsonProperty("rerollUnlockNodeId")] public int RerollUnlockNodeId { get; set; }
    [JsonProperty("giveUpRecoverHp")] public int GiveUpRecoverHp { get; set; }
    [JsonProperty("challengeStarPercent")] public List<int> ChallengeStarPercent { get; set; } = [];
    [JsonProperty("rewardCandidateCount")] public int RewardCandidateCount { get; set; } = 3;

    [JsonProperty("levels")] public List<DiceyDungeonLevelConfig> Levels { get; set; } = [];
    [JsonProperty("nodes")] public List<DiceyDungeonNodeConfig> Nodes { get; set; } = [];
    [JsonProperty("monsters")] public List<DiceyDungeonMonsterConfig> Monsters { get; set; } = [];
    [JsonProperty("avatars")] public List<DiceyDungeonAvatarConfig> Avatars { get; set; } = [];
    [JsonProperty("skillPool")] public List<DiceyDungeonPoolEntry> SkillPool { get; set; } = [];
    [JsonProperty("miraclePool")] public List<DiceyDungeonPoolEntry> MiraclePool { get; set; } = [];

    public DiceyDungeonLevelConfig? GetLevel(int id) => Levels.FirstOrDefault(x => x.Id == id);
    public DiceyDungeonNodeConfig? GetNode(int id) => Nodes.FirstOrDefault(x => x.Id == id);
    public DiceyDungeonAvatarConfig? GetAvatar(int id) => Avatars.FirstOrDefault(x => x.Id == id);
    public DiceyDungeonMonsterConfig? GetMonster(int id) => Monsters.FirstOrDefault(x => x.Id == id);

    /// <summary>The node that follows <paramref name="nodeId" /> in its level, or null at the last node.</summary>
    public DiceyDungeonNodeConfig? GetNextNode(int nodeId) => Nodes.FirstOrDefault(x => x.PreNodeId == nodeId);

    /// <summary>The first node of a level: the one no other node leads to.</summary>
    public DiceyDungeonNodeConfig? GetFirstNode(int levelId) =>
        Nodes.FirstOrDefault(x => x.LevelId == levelId && x.PreNodeId == 0);
}

public class DiceyDungeonLevelConfig
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("activityModuleId")] public int ActivityModuleId { get; set; }

    /// <summary>Story (fixed avatar, rewards between nodes) or Challenge (one boss node, scored by damage).</summary>
    [JsonProperty("type")] public string Type { get; set; } = "";

    [JsonProperty("avatarId")] public int AvatarId { get; set; }
    [JsonProperty("preLevelId")] public int PreLevelId { get; set; }
    [JsonProperty("challengeAvatarIds")] public List<int> ChallengeAvatarIds { get; set; } = [];

    [JsonIgnore] public bool IsChallenge => Type == "Challenge";
}

public class DiceyDungeonNodeConfig
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("levelId")] public int LevelId { get; set; }
    [JsonProperty("preNodeId")] public int PreNodeId { get; set; }

    /// <summary>Normal, Elite or Boss.</summary>
    [JsonProperty("monsterType")] public string MonsterType { get; set; } = "";

    [JsonProperty("monsterId")] public int MonsterId { get; set; }

    /// <summary>Skill reward offered after this node instead of a pool draw (the tutorial nodes).</summary>
    [JsonProperty("fixedSkillIds")] public List<int> FixedSkillIds { get; set; } = [];

    /// <summary>Miracle reward offered after this node instead of a pool draw.</summary>
    [JsonProperty("fixedMiracleIds")] public List<int> FixedMiracleIds { get; set; } = [];

    [JsonIgnore] public bool IsElite => MonsterType == "Elite";
}

public class DiceyDungeonMonsterConfig
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("hp")] public int Hp { get; set; }
}

public class DiceyDungeonAvatarConfig
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("initialHp")] public int InitialHp { get; set; }
    [JsonProperty("storyInitialSkillIds")] public List<int> StoryInitialSkillIds { get; set; } = [];
    [JsonProperty("challengeInitialSkillIds")] public List<int> ChallengeInitialSkillIds { get; set; } = [];
    [JsonProperty("challengeSkillIds")] public List<int> ChallengeSkillIds { get; set; } = [];
    [JsonProperty("challengeMiracleIds")] public List<int> ChallengeMiracleIds { get; set; } = [];
}

public class DiceyDungeonPoolEntry
{
    [JsonProperty("avatarId")] public int AvatarId { get; set; }
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("weight")] public int Weight { get; set; }
}
