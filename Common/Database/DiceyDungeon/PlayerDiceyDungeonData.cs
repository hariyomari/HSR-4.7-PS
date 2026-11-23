using MemoryPack;

namespace March7thHoney.Database.DiceyDungeon;

/// <summary>
///     Containment Starts With a Roll (DiceyDungeon) progress: per-level records and the run in progress.
///     New table: an existing database gets it created empty on first start, so old players start with no records.
/// </summary>
[DbTable("PlayerDiceyDungeon")]
public class PlayerDiceyDungeonData : BaseDatabaseDataHelper
{
    public Dictionary<int, DiceyDungeonLevelRecord> Levels { get; set; } = [];

    /// <summary>The level being played. <see cref="DiceyDungeonRunData.LevelId" /> 0 means no run.</summary>
    public DiceyDungeonRunData Run { get; set; } = new();

    /// <summary>Every node ever won; the reroll and recover-HP options unlock on a configured node.</summary>
    public List<int> ClearedNodeIds { get; set; } = [];

    /// <summary>Battle serial, only increases. Each node entry takes the next one.</summary>
    public uint NextSerial { get; set; }
}

[MemoryPackable]
public partial class DiceyDungeonLevelRecord
{
    public int LevelId { get; set; }
    public bool IsFinished { get; set; }
    public int Star { get; set; }
    public int FarthestNodeId { get; set; }
}

[MemoryPackable]
public partial class DiceyDungeonRunData
{
    public int LevelId { get; set; }
    public int AvatarId { get; set; }
    public uint SerialNumber { get; set; }
    public int CurrentNodeId { get; set; }
    public int CurrentHp { get; set; }
    public List<int> FinishedNodeIds { get; set; } = [];
    public List<int> SkillIds { get; set; } = [];
    public List<int> MiracleIds { get; set; } = [];
    public DiceyDungeonPendingReward PendingSkill { get; set; } = new();
    public DiceyDungeonPendingReward PendingMiracle { get; set; } = new();
}

/// <summary>A reward choice; it is pending while <see cref="RewardIds" /> is not empty.</summary>
[MemoryPackable]
public partial class DiceyDungeonPendingReward
{
    public List<int> RewardIds { get; set; } = [];
    public bool CanRecoverHp { get; set; }
    public int LeftRerollTime { get; set; }
}
