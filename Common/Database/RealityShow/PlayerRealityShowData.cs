using MemoryPack;

namespace March7thHoney.Database.RealityShow;

/// <summary>
///     Interastral Peace Gala: Road to Fame (RealityShow) progress, one record per challenge.
///     New table: an existing database gets it created empty on first start, so old players start with no records.
/// </summary>
[DbTable("PlayerRealityShow")]
public class PlayerRealityShowData : BaseDatabaseDataHelper
{
    public Dictionary<int, RealityShowChallengeRecord> Challenges { get; set; } = [];
}

[MemoryPackable]
public partial class RealityShowChallengeRecord
{
    public int ChallengeId { get; set; }

    /// <summary>The warm-up stage was fought; its result stays until the player resets it.</summary>
    public bool IsFirstStageFinished { get; set; }

    /// <summary>Special shields broken in the last warm-up.</summary>
    public int ShieldBreakCount { get; set; }

    /// <summary>Final-stage buff tiers earned by the last warm-up.</summary>
    public int BuffLevel { get; set; }

    /// <summary>Best final stage: most HP bars broken, ties won by less action value consumed.</summary>
    public int MaxHpBarBreakCount { get; set; }

    public double ConsumedActionValue { get; set; }

    /// <summary>Base avatar ids of the team that set the best final stage (shown on the friend ranking).</summary>
    public List<int> BestAvatarIds { get; set; } = [];

    /// <summary>The team last sent to battle, echoed back so the team editor reopens with it.</summary>
    public List<RealityShowLineupAvatar> Lineup { get; set; } = [];
}

[MemoryPackable]
public partial class RealityShowLineupAvatar
{
    /// <summary>Proto AvatarType: trial avatars carry their SpecialAvatar id, own avatars their base id.</summary>
    public int AvatarType { get; set; }

    public int Id { get; set; }
}
