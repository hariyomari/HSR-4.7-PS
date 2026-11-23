using MemoryPack;

namespace March7thHoney.Database.TrainParty;

[DbTable("TrainParty")]
public class TrainData : BaseDatabaseDataHelper
{
    public int Fund { get; set; }
    public int RecordId { get; set; }
    public uint WorldId { get; set; }
    public long RefreshTime { get; set; }
    public bool BadgeAutoFill { get; set; }
    public List<uint> UnlockedPassengerIds { get; set; } = [];
    public List<uint> PassengerRecordMarks { get; set; } = [];
    public List<TrainCardInfo> Cards { get; set; } = [];
    public List<TrainPendingPassengerInfo> GameplayPassengers { get; set; } = [];
    public List<TrainSkillInfo> GameplaySkills { get; set; } = [];
    public List<TrainMoveInfo> MoveHistory { get; set; } = [];
    public int GameplayRound { get; set; }
    public int GameplayType { get; set; }
    public int GameplayQueuePosition { get; set; }
    public int LastUsedCardId { get; set; }

    // Unlocked build areas, keyed by TrainPartyAreaConfig ID.
    public Dictionary<int, TrainAreaInfo> Areas { get; set; } = [];

    // Exhibit-room display wall (TrainPartyBuildSelfDisplayAreaDiy): photos, placed props and badges.
    public List<TrainSelfDisplayEntry> SelfDisplay { get; set; } = [];

    // Owned DIY props (TrainPartyDynamicConfig IDs) and the time each was acquired.
    public Dictionary<int, long> PropTimes { get; set; } = [];

    // No longer read: held the captured display-wall badge timers before the wall was persisted per player.
    public Dictionary<int, long> TimedDynamicPropTimes { get; set; } = [];

    // Version of the Areas layout; below TrainPartyManager.AreaDataVersion the step lists still hold the captured
    // static prop IDs written by the old defaults and are converted to real step IDs on load.
    public int AreaDataVersion { get; set; }

    // Current gameplay turn: a TrainPartyProgress ID, which the client looks up and throws on when absent.
    public int TurnId { get; set; }

    public Dictionary<int, int> SelfDisplayDynamics { get; set; } = [];
    public List<TrainSelfDisplayBadge> SelfDisplayBadges { get; set; } = [];

    // Highest TrainPartyRewardConfig level whose reward has been taken.
    public int TakenBuildLevel { get; set; }
}

[MemoryPackable]
public partial class TrainAreaInfo
{
    public int AreaId { get; set; }

    // Finished TrainPartyStepConfig IDs of the area's goal.
    public List<int> StepList { get; set; } = [];

    // DIY slot ID -> placed TrainPartyDynamicConfig ID.
    public Dictionary<int, int> DynamicInfo { get; set; } = [];
}

[MemoryPackable]
public partial class TrainSelfDisplayEntry
{
    public uint Slot { get; set; }
    public uint Id { get; set; }
    public uint Type { get; set; }
}

[MemoryPackable]
public partial class TrainSelfDisplayBadge
{
    public uint Slot { get; set; }
    public uint BadgeId { get; set; }
    public long ExpireTime { get; set; }
}

[MemoryPackable]
public partial class TrainCardInfo
{
    public uint CardId { get; set; }
    public uint UniqueId { get; set; }
    public uint CurIndex { get; set; }
}

[MemoryPackable]
public partial class TrainPendingPassengerInfo
{
    public uint PassengerId { get; set; }
    public uint Hp { get; set; }
    public uint Atk { get; set; }
}

[MemoryPackable]
public partial class TrainSkillInfo
{
    public uint SkillId { get; set; }
    public uint SkillLevel { get; set; }
    public uint Count { get; set; }
}

[MemoryPackable]
public partial class TrainMoveInfo
{
    public uint CardId { get; set; }
    public uint UniqueId { get; set; }
    public uint DisplayValue { get; set; }
    public uint BoardIndex { get; set; }
}
