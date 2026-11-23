namespace March7thHoney.Database.Shop;

/// <summary>
///     Per-player shop purchase counts and Oneiric Pouch tier history.
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

    /// <summary>Cumulative purchase count keyed by <c>GoodsID</c>.</summary>
    public Dictionary<int, int> GoodsBuyTimes { get; set; } = [];

    /// <summary>
    ///     Annual reset period key (yyyyMMdd of the reset boundary that opened the current cycle) for
    ///     <see cref="RechargeProductBuyTimes" />. 0 = not yet initialized.
    /// </summary>
    public int RechargeDoubleBonusResetKey { get; set; }

    /// <summary>Cumulative Oneiric Pouch purchase count keyed by <c>ProductID</c>.</summary>
    public Dictionary<string, int> RechargeProductBuyTimes { get; set; } = [];
}
