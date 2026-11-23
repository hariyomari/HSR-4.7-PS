using March7thHoney.Data.Excel;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
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
        return (ProductGiftType)product.GiftType switch
        {
            ProductGiftType.ProductGiftCoin => GrantCoin(player, product),
            ProductGiftType.ProductGiftMonthCard => GrantMonthCard(player, product),
            _ => ValueTask.FromResult(RechargeResult.UnsupportedGiftType)
        };
    }

    private static async ValueTask<RechargeResult> GrantCoin(PlayerInstance player, RechargeConfigExcel product)
    {
        if (!ConfigManager.Config.ServerOption.EnableFakeRecharge) return RechargeResult.Disabled;

        List<ItemData> rewards = [new() { ItemId = (int)VirtualItemEnum.Mcoin, Count = product.FirstCharge }];
        if (product.NormalCharge > 0)
            rewards.Add(new ItemData { ItemId = (int)VirtualItemEnum.Mcoin, Count = product.NormalCharge });

        var total = rewards.Sum(x => x.Count);
        await player.InventoryManager!.AddItem((int)VirtualItemEnum.Mcoin, total, notify: false, sync: false);
        await player.SendPacket(new PacketPlayerSyncScNotify(player.ToProto()));
        await player.SendPacket(new PacketRechargeSuccNotify(product.ProductID, rewards,
            MonthCardService.GetMonthCardOutDateTime(player.Data)));

        Logger.Debug($"Recharge granted. uid={player.Uid} product={product.ProductID} shards={total}");
        return RechargeResult.Granted;
    }

    // Express Supply Pass: 300 Oneiric Shards right away, then 90 Stellar Jade a day while the pass runs. The day of
    // purchase counts, so its reward is handed out here instead of waiting for the next login.
    private static async ValueTask<RechargeResult> GrantMonthCard(PlayerInstance player, RechargeConfigExcel product)
    {
        if (!ConfigManager.Config.ServerOption.EnableMonthCard) return RechargeResult.Disabled;
        if (!MonthCardService.TryExtend(player.Data, out var outDateTime))
        {
            Logger.Debug($"Supply pass purchase refused, {MonthCardService.GetRemainingDays(player.Data)} day(s) left. " +
                        $"uid={player.Uid} product={product.ProductID}");
            return RechargeResult.MonthCardLimit;
        }

        List<ItemData> rewards =
            [new() { ItemId = MonthCardService.PurchaseRewardItemId, Count = MonthCardService.PurchaseRewardAmount }];
        await player.InventoryManager!.AddItem(MonthCardService.PurchaseRewardItemId,
            MonthCardService.PurchaseRewardAmount, notify: false, sync: false);
        await player.SendPacket(new PacketPlayerSyncScNotify(player.ToProto()));
        await player.SendPacket(new PacketRechargeSuccNotify(product.ProductID, rewards, outDateTime));

        if (MonthCardService.TryClaimDailyReward(player.Data, out var daily))
        {
            await player.InventoryManager.AddItem(daily.ItemId, daily.Count, false);
            await player.SendPacket(new PacketMonthCardRewardNotify([daily]));
        }

        Logger.Debug($"Supply pass granted. uid={player.Uid} product={product.ProductID} " +
                    $"until={DateTimeOffset.FromUnixTimeSeconds(outDateTime):u}");
        return RechargeResult.Granted;
    }
}
