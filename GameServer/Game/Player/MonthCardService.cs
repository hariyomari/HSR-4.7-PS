using March7thHoney.Database;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Player;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Player;

/// <summary>
///     Express Supply Pass state. The pass is no longer switched on by the server: it only runs while a purchase made
///     through the recharge shop (RechargeConfig gift type PRODUCT_GIFT_MONTH_CARD, see <see cref="RechargeService" />)
///     covers the current time. Days are the client's daily windows (<see cref="DailyResetClock" />): each purchase
///     adds <see cref="ActiveDaysPerPurchase" /> windows to a running pass, or starts a new or lapsed pass at the
///     current window, the day of purchase included. Days left are whole days until the expiry, rounded down, which
///     is what the shop shows: a pass bought today shows 29, and a pass that ends on a reset loses one at every reset.
/// </summary>
public static class MonthCardService
{
    public const int DailyRewardItemId = (int)VirtualItemEnum.Hcoin;
    public const int DailyRewardAmount = 90;
    public const int PurchaseRewardItemId = (int)VirtualItemEnum.Mcoin;
    public const int PurchaseRewardAmount = 300;
    public const int ActiveDaysPerPurchase = 30;

    // The client's pass rules (4.7 TextMap 14763198289797068476): another pass can be bought only while fewer than
    // 179 days are left, so the longest pass runs 178 + 30 = 208 days past today.
    public const int PurchaseBelowRemainingDays = 179;

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
        return IsActive(data, ServerTimeProvider.GetServerUnixSec(data));
    }

    /// <summary>The pass runs until its expiry: still active a second before it, over at the expiry itself.</summary>
    public static bool IsActive(PlayerData data, long now)
    {
        return GetMonthCardOutDateTime(data) > now;
    }

    /// <summary>Whole days until the pass ends, rounded down as the shop shows them (0 on its last day).</summary>
    public static int GetRemainingDays(PlayerData data)
    {
        return CountRemainingDays(GetMonthCardOutDateTime(data), ServerTimeProvider.GetServerUnixSec(data));
    }

    public static int CountRemainingDays(long outDateTime, long now)
    {
        if (outDateTime <= now) return 0;
        return (int)Math.Min((outDateTime - now) / SecondsPerDay, int.MaxValue);
    }

    /// <summary>
    ///     Adds one purchase worth of days at the account's server time. Fails, leaving the pass untouched, when the
    ///     pass is disabled or <see cref="PurchaseBelowRemainingDays" /> or more days remain.
    /// </summary>
    public static bool TryExtend(PlayerData data, out long newOutDateTime)
    {
        return Extend(data, null, out newOutDateTime);
    }

    /// <summary>
    ///     Same as <see cref="TryExtend(PlayerData, out long)" />, decided at <paramref name="now" /> (Unix seconds).
    /// </summary>
    public static bool TryExtend(PlayerData data, long now, out long newOutDateTime)
    {
        return Extend(data, now, out newOutDateTime);
    }

    /// <summary>
    ///     Claims the Stellar Jade of the current daily window at the account's server time, at most once per window
    ///     while the pass is active.
    /// </summary>
    public static bool TryClaimDailyReward(PlayerData data, out ItemData reward)
    {
        return ClaimDailyReward(data, null, out reward);
    }

    /// <summary>
    ///     Same as <see cref="TryClaimDailyReward(PlayerData, out ItemData)" />, decided at <paramref name="now" />
    ///     (Unix seconds).
    /// </summary>
    public static bool TryClaimDailyReward(PlayerData data, long now, out ItemData reward)
    {
        return ClaimDailyReward(data, now, out reward);
    }

    // Without an explicit time the clock is read under the lock, so concurrent orders and logins decide in lock order.
    private static bool Extend(PlayerData data, long? at, out long newOutDateTime)
    {
        lock (StateLock)
        {
            newOutDateTime = GetMonthCardOutDateTime(data);
            if (!ConfigManager.Config.ServerOption.EnableMonthCard) return false;

            // One instant decides the limit, whether the pass still runs and the window a new pass starts in.
            var now = at ?? ServerTimeProvider.GetServerUnixSec(data);
            var expiry = newOutDateTime;
            if (CountRemainingDays(expiry, now) >= PurchaseBelowRemainingDays) return false;

            // A running pass goes on from its exact expiry, also a legacy one that does not end on a reset. A new or
            // lapsed pass starts at the current window, so the day of purchase is one of its days and the expiry falls
            // on a daily reset; that includes a pass that ran out earlier in this same window.
            var from = expiry > now
                ? expiry
                : DailyResetClock.WindowStart(now, DailyResetClock.UtcOffsetHours, DailyResetClock.ResetHour);
            data.MonthCardExpireTime = from + ActiveDaysPerPurchase * SecondsPerDay;
            newOutDateTime = data.MonthCardExpireTime;
        }

        DatabaseHelper.MarkDirty(data.Uid);
        return true;
    }

    private static bool ClaimDailyReward(PlayerData data, long? at, out ItemData reward)
    {
        reward = null!;
        lock (StateLock)
        {
            // One instant decides whether the pass still runs and which window is paid.
            var now = at ?? ServerTimeProvider.GetServerUnixSec(data);
            if (!IsActive(data, now)) return false;

            var window = DailyResetClock.DayKey(now, DailyResetClock.UtcOffsetHours, DailyResetClock.ResetHour);
            if (data.LastMonthCardRewardDate == window) return false;
            data.LastMonthCardRewardDate = window;
        }

        DatabaseHelper.MarkDirty(data.Uid);
        reward = new ItemData
        {
            ItemId = DailyRewardItemId,
            Count = DailyRewardAmount
        };
        return true;
    }
}
