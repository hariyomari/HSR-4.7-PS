using MemoryPack;

namespace March7thHoney.Database.ActivityBangboo;

/// <summary>
///     Planarcadia Reverie (ActivityBangboo) progress. New table: an existing database gets it created empty on
///     first start, so old players start with nothing cleared.
/// </summary>
[DbTable("PlayerActivityBangboo")]
public class PlayerActivityBangbooData : BaseDatabaseDataHelper
{
    /// <summary>Every stage won at least once, Story and Infinity alike, in clear order.</summary>
    public List<int> FinishedLevelIds { get; set; } = [];

    /// <summary>ActivityBangbooPerformance groups (anecdotes) watched to the end.</summary>
    public List<int> ReadPerformanceGroupIds { get; set; } = [];

    public Dictionary<int, ActivityBangbooPostState> Posts { get; set; } = [];

    /// <summary>Infinity hole id → the most waves cleared in one run.</summary>
    public Dictionary<int, int> InfinityMaxWave { get; set; } = [];

    /// <summary>The Infinity run that can be continued. <see cref="ActivityBangbooInfinityRun.HoleId" /> 0 means none.</summary>
    public ActivityBangbooInfinityRun InfinityRun { get; set; } = new();

    /// <summary>The stage started and not yet settled. <see cref="ActivityBangbooGame.LevelId" /> 0 means none.</summary>
    public ActivityBangbooGame CurrentGame { get; set; } = new();

    /// <summary>Only increases; each start takes the next one for its GameTransNo.</summary>
    public uint NextGameSerial { get; set; }
}

[MemoryPackable]
public partial class ActivityBangbooPostState
{
    public bool IsRead { get; set; }
    public bool IsLiked { get; set; }
}

[MemoryPackable]
public partial class ActivityBangbooInfinityRun
{
    public int HoleId { get; set; }

    /// <summary>The wave to play next (1-based); stage <c>LevelIds[Wave - 1]</c> of the hole.</summary>
    public int Wave { get; set; }

    /// <summary>The client's board archive (JDCCDGMJMII) saved after the last cleared wave; empty before wave 2.</summary>
    public byte[] Archive { get; set; } = [];
}

[MemoryPackable]
public partial class ActivityBangbooGame
{
    public int LevelId { get; set; }
    public string GameTransNo { get; set; } = "";
}
