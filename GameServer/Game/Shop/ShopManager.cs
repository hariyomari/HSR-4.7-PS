using March7thHoney.Data;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Shop;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Shop;

public readonly record struct ShopBuyResult(Retcode Retcode, List<ItemData> Items, uint GoodsBuyTimes);

public class ShopManager(PlayerInstance player) : BasePlayerManager<ShopData>(player)
{
    /// <summary>Upper bound on one purchase of an item without a pile limit.</summary>
    private const int DefaultMaxBuyCount = 9999;

    // Live HSR: Starlight Exchange (101) and Embers Exchange (102) reset monthly on the 1st at server reset hour.
    private static readonly HashSet<int> MonthlyResetShopIds = [101, 102];

    public static bool IsMonthlyResetShop(int shopId) => MonthlyResetShopIds.Contains(shopId);

    public static (long BeginTime, long EndTime) GetMonthlyWindowBounds(int monthKey, int utcOffsetHours, int resetHour)
    {
        var year = monthKey / 100;
        var month = monthKey % 100;
        var offset = TimeSpan.FromHours(utcOffsetHours);
        var start = new DateTimeOffset(year, month, 1, resetHour, 0, 0, offset);
        var end = new DateTimeOffset(start.DateTime.AddMonths(1), offset);
        return (start.ToUnixTimeSeconds(), end.ToUnixTimeSeconds());
    }

    public void EnsureMonthlyWindow()
    {
        var currentMonth = DailyResetClock.Today(Player.Data) / 100;
        if (Data.MonthlyResetKey == currentMonth) return;

        if (Data.MonthlyResetKey != 0)
        {
            foreach (var shopId in MonthlyResetShopIds)
                if (GameData.ShopConfigData.TryGetValue(shopId, out var shop))
                    foreach (var goods in shop.Goods)
                        Data.GoodsBuyTimes.Remove(goods.GoodsID);
        }

        Data.MonthlyResetKey = currentMonth;
        MarkDirty();
    }

    public (long BeginTime, long EndTime) GetShopWindow(int shopId)
    {
        if (!IsMonthlyResetShop(shopId)) return (0, uint.MaxValue);
        var monthKey = DailyResetClock.Today(Player.Data) / 100;
        return GetMonthlyWindowBounds(monthKey, DailyResetClock.UtcOffsetHours, DailyResetClock.ResetHour);
    }

    public uint GetGoodsBuyTimes(int goodsId)
    {
        EnsureMonthlyWindow();
        return (uint)Math.Max(0, Data.GoodsBuyTimes.GetValueOrDefault(goodsId));
    }

    public List<Proto.Shop> BuildShopList(uint shopType)
    {
        EnsureMonthlyWindow();

        var shops = new List<Proto.Shop>();
        foreach (var shop in GameData.ShopConfigData.Values)
        {
            if (shop.ShopType != shopType || shop.Goods.Count == 0) continue;

            var (beginTime, endTime) = GetShopWindow(shop.ShopID);
            var shopProto = new Proto.Shop
            {
                ShopId = (uint)shop.ShopID,
                CityLevel = 1,
                BeginTime = beginTime,
                EndTime = endTime
            };

            foreach (var goods in shop.Goods)
            {
                if (goods.ItemID == 0) continue;
                var goodsProto = goods.ToProto();
                goodsProto.BuyTimes = (uint)Math.Max(0, Data.GoodsBuyTimes.GetValueOrDefault(goods.GoodsID));
                goodsProto.BeginTime = beginTime;
                goodsProto.EndTime = endTime;
                shopProto.GoodsList.Add(goodsProto);
            }

            shops.Add(shopProto);
        }

        return shops;
    }

    public async ValueTask<ShopBuyResult> BuyItem(int shopId, int goodsId, long count)
    {
        EnsureMonthlyWindow();

        var existingBuyTimes = (uint)Math.Max(0, Data.GoodsBuyTimes.GetValueOrDefault(goodsId));
        if (count < 1 || count > int.MaxValue) return new ShopBuyResult(Retcode.RetFail, [], existingBuyTimes);

        GameData.ShopConfigData.TryGetValue(shopId, out var shopConfig);
        if (shopConfig == null) return new ShopBuyResult(Retcode.RetFail, [], existingBuyTimes);
        var goods = shopConfig.Goods.Find(g => g.GoodsID == goodsId);
        if (goods == null) return new ShopBuyResult(Retcode.RetFail, [], existingBuyTimes);
        GameData.ItemConfigData.TryGetValue(goods.ItemID, out var itemConfig);
        if (itemConfig == null) return new ShopBuyResult(Retcode.RetItemConfigNotExist, [], existingBuyTimes);

        var itemCountPerBuy = goods.ItemCount > 0 ? goods.ItemCount : 1;
        var totalItemCount = count * itemCountPerBuy;
        if (totalItemCount > int.MaxValue) return new ShopBuyResult(Retcode.RetFail, [], existingBuyTimes);
        var totalCount = (int)totalItemCount;

        var inventory = Player.InventoryManager!;
        var (limitReached, limitRetcode) = itemConfig.ItemMainType switch
        {
            ItemMainTypeEnum.Equipment => (
                totalCount > GameConstants.INVENTORY_MAX_EQUIPMENT - inventory.Data.EquipmentItems.Count,
                Retcode.RetEquipmentExceedLimit),
            ItemMainTypeEnum.Relic => (
                totalCount > GameConstants.INVENTORY_MAX_RELIC - inventory.Data.RelicItems.Count,
                Retcode.RetRelicExceedLimit),
            ItemMainTypeEnum.Virtual => (count > DefaultMaxBuyCount, Retcode.RetFail),
            _ => (
                itemConfig.PileLimit > 0 ? totalCount > itemConfig.PileLimit : count > DefaultMaxBuyCount,
                Retcode.RetFail)
        };
        if (limitReached) return new ShopBuyResult(limitRetcode, [], existingBuyTimes);

        var costs = goods.CostList.Where(cost => cost.Value != 0).Select(cost => (cost.Key, (long)cost.Value * count));
        if (!await inventory.TryRemoveItems(costs))
            return new ShopBuyResult(Retcode.RetItemNotEnough, [], existingBuyTimes);

        var items = new List<ItemData>();
        if (itemConfig.ItemMainType is ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic)
        {
            for (var i = 0; i < totalCount; i++)
            {
                var item = await Player.InventoryManager!.AddItem(itemConfig.ID, 1, false);
                if (item != null) items.Add(item);
            }
        }
        else if (itemConfig.ItemMainType == ItemMainTypeEnum.AvatarCard)
        {
            for (var i = 0; i < totalCount; i++)
            {
                var wasOwned = Player.AvatarManager?.GetFormalAvatar(itemConfig.ID) != null;
                var item = await Player.InventoryManager!.AddItem(itemConfig.ID, 1, notify: !wasOwned);
                if (item != null)
                {
                    items.Add(item);
                }
                else if (!wasOwned && Player.AvatarManager?.GetFormalAvatar(itemConfig.ID) != null)
                {
                    items.Add(new ItemData { ItemId = itemConfig.ID, Count = 1 });
                }
                else if (wasOwned)
                {
                    var starlight = GameData.AvatarConfigData.TryGetValue(itemConfig.ID, out var avatarExcel) &&
                                    avatarExcel.Rarity == RarityEnum.CombatPowerAvatarRarityType5
                        ? 100
                        : 20;
                    var converted = await Player.InventoryManager!.AddItem(252, starlight, false);
                    if (converted != null) items.Add(converted);
                }
            }
        }
        else
        {
            var item = await Player.InventoryManager!.AddItem(itemConfig.ID, totalCount, false);
            if (item != null)
            {
                var useDataId = itemConfig.UseDataID != 0 ? itemConfig.UseDataID : item.ItemId;
                if (GameData.ItemUseDataData.TryGetValue(useDataId, out var useData) && useData.IsAutoUse)
                {
                    var res = await Player.InventoryManager!.UseItem(item.ItemId, totalCount);
                    if (res.returnItems != null) items.AddRange(res.returnItems);
                }
                else
                {
                    items.Add(item);
                }
            }
        }

        var updatedBuyTimes = (int)Math.Min((long)existingBuyTimes + count, int.MaxValue);
        Data.GoodsBuyTimes[goodsId] = updatedBuyTimes;
        MarkDirty();

        if (Player.MissionManager != null)
            await Player.MissionManager.HandleFinishType(MissionFinishTypeEnum.BuyShopGoods, goods);

        return new ShopBuyResult(Retcode.RetSucc, items, (uint)updatedBuyTimes);
    }
}
