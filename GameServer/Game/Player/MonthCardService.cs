using March7thHoney.Database;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Player;
using March7thHoney.Enums.Item;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Player;

/// <summary>
///     Express Supply Pass state. The pass is no longer switched on by the server: it only runs while a purchase made
///     through the recharge shop (RechargeConfig gift type PRODUCT_GIFT_MONTH_CARD, see <see cref="RechargeService" />)
///     covers the current time. Each purchase adds <see cref="ActiveDaysPerPurchase" /> days on top of what is left.
/// </summary>
public static class MonthCardService
{
    public const int DailyRewardItemId = (int)VirtualItemEnum.Hcoin;
    public const int DailyRewardAmount = 90;
    public const int PurchaseRewardItemId = (int)VirtualItemEnum.Mcoin;
    public const int PurchaseRewardAmount = 300;
    public const int ActiveDaysPerPurchase = 30;

    // The shop refuses another pass once the remaining days would go past 180, i.e. above 150 days left.
    public const int MaxRemainingDays = 180;

    private const long SecondsPerDay = 24 * 60 * 60;

    // Purchases arrive from the SDK web routes while login runs on the game connection; both read-modify-write the
    // same two fields, so the check and the update are taken under one lock.
    private static readonly object StateLock = new();

    /// <summary>
    ///     The time the pass runs out, as the client expects it in QueryProductInfoScRsp / RechargeSuccNotify.
    ///     Zero when the pass is disabled or was never bought; a past time when it has expired.
    /// </summary>
    public static long GetMonthCardOutDateTime(PlayerData data)
    {
        if (!ConfigManager.Config.ServerOption.EnableMonthCard) return 0;
        return Math.Max(data.MonthCardExpireTime, 0);
    }

    public static bool IsActive(PlayerData data)
    {
        var outDate = GetMonthCardOutDateTime(data);
        return outDate > ServerTimeProvider.GetServerUnixSec(data);
    }

    /// <summary>Whole days left on the pass, counting a started day as a full one (0 when expired).</summary>
    public static int GetRemainingDays(PlayerData data)
    {
        var left = GetMonthCardOutDateTime(data) - ServerTimeProvider.GetServerUnixSec(data);
        return left <= 0 ? 0 : (int)((left + SecondsPerDay - 1) / SecondsPerDay);
    }

    /// <summary>
    ///     Adds one purchase worth of days. Fails, leaving the pass untouched, when the pass is disabled or the new
    ///     total would exceed <see cref="MaxRemainingDays" />.
    /// </summary>
    public static bool TryExtend(PlayerData data, out long newOutDateTime)
    {
        lock (StateLock)
        {
            newOutDateTime = GetMonthCardOutDateTime(data);
            if (!ConfigManager.Config.ServerOption.EnableMonthCard) return false;
            if (GetRemainingDays(data) + ActiveDaysPerPurchase > MaxRemainingDays) return false;

            var now = ServerTimeProvider.GetServerUnixSec(data);
            var from = Math.Max(data.MonthCardExpireTime, now);
            data.MonthCardExpireTime = from + ActiveDaysPerPurchase * SecondsPerDay;
            newOutDateTime = data.MonthCardExpireTime;
        }

        DatabaseHelper.MarkDirty(data.Uid);
        return true;
    }

    /// <summary>Claims today's Stellar Jade at most once per local day while the pass is active.</summary>
    public static bool TryClaimDailyReward(PlayerData data, out ItemData reward)
    {
        reward = null!;
        lock (StateLock)
        {
            if (!IsActive(data)) return false;

            var today = GetLocalDayStamp(ServerTimeProvider.GetServerUnixSec(data));
            if (data.LastMonthCardRewardDate == today) return false;
            data.LastMonthCardRewardDate = today;
        }

        DatabaseHelper.MarkDirty(data.Uid);
        reward = new ItemData
        {
            ItemId = DailyRewardItemId,
            Count = DailyRewardAmount
        };
        return true;
    }

    private static int GetLocalDayStamp(long unixSeconds)
    {
        var localDate = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().Date;
        return localDate.Year * 10000 + localDate.Month * 100 + localDate.Day;
    }
}
