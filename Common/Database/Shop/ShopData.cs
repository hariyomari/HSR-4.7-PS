using MemoryPack;

namespace March7thHoney.Database.Shop;

/// <summary>
///     Per-player shop purchase counts, city-shop progression, and Oneiric Pouch tier history.
///     New table: existing databases create it empty on startup.
/// </summary>
[DbTable("Shop")]
public class ShopData : BaseDatabaseDataHelper
{
    /// <summary>
    ///     Monthly reset window (yyyyMM, shifted by the daily reset hour) for which monthly shop
    ///     purchase counts in <see cref="GoodsBuyTimes" /> were recorded. 0 = not yet initialized.
    /// </summary>
    public int MonthlyResetKey { get; set; }

    /// <summary>Daily reset window (yyyyMMdd) for DAILY shop goods. 0 = not yet initialized.</summary>
    public int DailyResetKey { get; set; }

    /// <summary>Weekly reset window (yyyyMMdd of Monday) for WEEK shop goods. 0 = not yet initialized.</summary>
    public int WeeklyResetKey { get; set; }

    /// <summary>Cumulative purchase count keyed by <c>GoodsID</c>.</summary>
    public Dictionary<int, int> GoodsBuyTimes { get; set; } = [];

    /// <summary>
    ///     Annual reset period key (yyyyMMdd of the reset boundary that opened the current cycle) for
    ///     <see cref="RechargeProductBuyTimes" />. 0 = not yet initialized.
    /// </summary>
    public int RechargeDoubleBonusResetKey { get; set; }

    /// <summary>Cumulative Oneiric Pouch purchase count keyed by <c>ProductID</c>.</summary>
    public Dictionary<string, int> RechargeProductBuyTimes { get; set; } = [];

    /// <summary>Per-shop world/city shop level progression keyed by <c>ShopID</c>.</summary>
    public Dictionary<int, CityShopProgress> CityShops { get; set; } = [];
}

[MemoryPackable]
public partial class CityShopProgress
{
    public int Level { get; set; } = 1;
    public int Exp { get; set; }
    public List<int> TakenRewardLevels { get; set; } = [];

    /// <summary>
    ///     Encodes claimed city-shop levels into the wire bitmask (<c>1UL &lt;&lt; level</c>, matching
    ///     <c>ChallengeGroupReward.TakenStarsCountReward</c>). Verify on a live client whether level N checks
    ///     bit N or N-1.
    /// </summary>
    public ulong ToTakenLevelRewardMask()
    {
        ulong mask = 0;
        foreach (var level in TakenRewardLevels)
            if (level is >= 0 and < 64)
                mask |= 1UL << level;
        return mask;
    }
}
