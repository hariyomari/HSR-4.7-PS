namespace March7thHoney.Enums.Item;

/// <summary>
///     Item IDs of the virtual items (ItemMainType Virtual) whose balance is a player counter rather than an
///     inventory stack. The names follow the PlayerData fields that hold the balances.
/// </summary>
public enum VirtualItemEnum
{
    /// <summary>Stellar Jade.</summary>
    Hcoin = 1,

    /// <summary>Credit.</summary>
    Scoin = 2,

    /// <summary>Oneiric Shard.</summary>
    Mcoin = 3,

    /// <summary>Trailblaze Power.</summary>
    Stamina = 11,

    /// <summary>Trailblaze EXP.</summary>
    PlayerExp = 22,

    /// <summary>Daily training activity points; the balance lives in the daily active record.</summary>
    DailyActivePoint = 23,

    /// <summary>Simulated Universe talent points.</summary>
    TalentPoint = 32
}
