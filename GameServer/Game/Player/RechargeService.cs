using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Player;
using March7thHoney.Database.Shop;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Player;

public enum RechargeResult
{
    Granted,
    Disabled,
    UnsupportedGiftType,
    MonthCardLimit
}

/// <summary>
///     Grants a product bought in the recharge shop (the RechargeConfig / QueryProductInfo catalogue the SDK pay
///     flow sells, not the in-game Shop). The SDK routes resolve the product and the player, this applies it.
/// </summary>
public static class RechargeService
{
    private static readonly Logger Logger = new("Recharge");

    public static ValueTask<RechargeResult> Purchase(PlayerInstance player, RechargeConfigExcel product)
    {
        return Purchase(player, product, ServerTimeProvider.GetServerUnixSec(player.Data));
    }

    /// <summary>
    ///     Same as <see cref="Purchase(PlayerInstance, RechargeConfigExcel)" />, decided at <paramref name="now" />
    ///     (Unix seconds).
    /// </summary>
    public static ValueTask<RechargeResult> Purchase(PlayerInstance player, RechargeConfigExcel product, long now)
    {
        return (ProductGiftType)product.GiftType switch
        {
            ProductGiftType.ProductGiftCoin => GrantCoin(player, product),
            ProductGiftType.ProductGiftMonthCard => GrantMonthCard(player, product, now),
            _ => ValueTask.FromResult(RechargeResult.UnsupportedGiftType)
        };
    }

    /// <summary>
    ///     Returns the yyyyMMdd reset boundary that opened the annual 2x-bonus cycle containing <paramref name="dayKey" />.
    /// </summary>
    public static int GetDoubleBonusResetKey(int dayKey, int resetMonth, int resetDay)
    {
        var year = dayKey / 10000;
        var thisYearBoundary = year * 10000 + resetMonth * 100 + Math.Min(resetDay, DateTime.DaysInMonth(year, resetMonth));
        if (dayKey >= thisYearBoundary) return thisYearBoundary;
        var prevYear = year - 1;
        return prevYear * 10000 + resetMonth * 100 + Math.Min(resetDay, DateTime.DaysInMonth(prevYear, resetMonth));
    }

    /// <summary>
    ///     Rolls a player's Oneiric Pouch 2x bonus history when the configured annual reset date has passed.
    /// </summary>
    public static void EnsureDoubleBonusWindow(ShopData shopData, PlayerData? playerData, int uid)
    {
        if (!ConfigManager.Config.ServerOption.TryGetRechargeDoubleBonusResetDate(out var resetMonth, out var resetDay))
            return;

        var periodKey = GetDoubleBonusResetKey(DailyResetClock.Today(playerData), resetMonth, resetDay);
        if (shopData.RechargeDoubleBonusResetKey == periodKey) return;

        if (shopData.RechargeDoubleBonusResetKey != 0)
            shopData.RechargeProductBuyTimes.Clear();

        shopData.RechargeDoubleBonusResetKey = periodKey;
        DatabaseHelper.MarkDirty(uid);
    }

    private static async ValueTask<RechargeResult> GrantCoin(PlayerInstance player, RechargeConfigExcel product)
    {
        if (!ConfigManager.Config.ServerOption.EnableFakeRecharge) return RechargeResult.Disabled;

        var shopData = player.ShopManager?.Data ?? DatabaseHelper.Instance?.GetInstanceOrCreateNew<ShopData>(player.Uid);
        if (shopData != null) EnsureDoubleBonusWindow(shopData, player.Data, player.Uid);
        var buyTimes = shopData?.RechargeProductBuyTimes.GetValueOrDefault(product.ProductID) ?? 0;
        var isFirstPurchase = buyTimes == 0;
        var bonus = isFirstPurchase ? product.FirstCharge : product.NormalCharge;

        List<ItemData> rewards = [new() { ItemId = (int)VirtualItemEnum.Mcoin, Count = product.FirstCharge }];
        if (bonus > 0)
            rewards.Add(new ItemData { ItemId = (int)VirtualItemEnum.Mcoin, Count = bonus });

        var total = rewards.Sum(x => x.Count);
        if (shopData != null)
        {
            shopData.RechargeProductBuyTimes[product.ProductID] = buyTimes + 1;
            DatabaseHelper.MarkDirty(player.Uid);
        }

        await player.InventoryManager!.AddItem((int)VirtualItemEnum.Mcoin, total, notify: false, sync: false);
        await player.SendPacket(new PacketPlayerSyncScNotify(player.ToProto()));
        await player.SendPacket(new PacketRechargeSuccNotify(product.ProductID, rewards,
            MonthCardService.GetMonthCardOutDateTime(player.Data)));

        Logger.Debug($"Recharge granted. uid={player.Uid} product={product.ProductID} shards={total} first={isFirstPurchase}");
        return RechargeResult.Granted;
    }

    // Express Supply Pass: 300 Oneiric Shards right away, then 90 Stellar Jade a day while the pass runs. The day of
    // purchase counts, so its reward is handed out here instead of waiting for the next login. One instant decides
    // both, so a purchase at a daily reset cannot start the pass in one window and pay the next one's reward.
    private static async ValueTask<RechargeResult> GrantMonthCard(PlayerInstance player, RechargeConfigExcel product,
        long now)
    {
        if (!ConfigManager.Config.ServerOption.EnableMonthCard) return RechargeResult.Disabled;
        if (!MonthCardService.TryExtend(player.Data, now, out var outDateTime))
        {
            var daysLeft = MonthCardService.CountRemainingDays(MonthCardService.GetMonthCardOutDateTime(player.Data), now);
            Logger.Debug($"Supply pass purchase refused, {daysLeft} day(s) left. " +
                        $"uid={player.Uid} product={product.ProductID}");
            return RechargeResult.MonthCardLimit;
        }

        List<ItemData> rewards =
            [new() { ItemId = MonthCardService.PurchaseRewardItemId, Count = MonthCardService.PurchaseRewardAmount }];
        await player.InventoryManager!.AddItem(MonthCardService.PurchaseRewardItemId,
            MonthCardService.PurchaseRewardAmount, notify: false, sync: false);
        await player.SendPacket(new PacketPlayerSyncScNotify(player.ToProto()));
        await player.SendPacket(new PacketRechargeSuccNotify(product.ProductID, rewards, outDateTime));

        if (MonthCardService.TryClaimDailyReward(player.Data, now, out var daily))
        {
            await player.InventoryManager.AddItem(daily.ItemId, daily.Count, false);
            await player.SendPacket(new PacketMonthCardRewardNotify([daily]));
        }

        Logger.Debug($"Supply pass granted. uid={player.Uid} product={product.ProductID} " +
                    $"until={DateTimeOffset.FromUnixTimeSeconds(outDateTime):u}");
        return RechargeResult.Granted;
    }
}
