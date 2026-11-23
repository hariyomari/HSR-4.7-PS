using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Shop;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Shop;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Shop;

public readonly record struct ShopBuyResult(Retcode Retcode, List<ItemData> Items, uint GoodsBuyTimes);

public class ShopManager(PlayerInstance player) : BasePlayerManager<ShopData>(player)
{
    /// <summary>Upper bound on one purchase of an item without a pile limit.</summary>
    private const int DefaultMaxBuyCount = 9999;

    // Fallback for older Resource.bin caches built before ShopGoodsConfigExcel serialized RefreshType.
    private static readonly HashSet<int> MonthlyResetShopIds = [101, 102];

    public static bool IsMonthlyResetShop(int shopId) => MonthlyResetShopIds.Contains(shopId);

    public static int GetWeeklyKey(int dayKey)
    {
        var date = new DateTime(dayKey / 10000, dayKey / 100 % 100, dayKey % 100);
        var daysSinceMonday = ((int)date.DayOfWeek + 6) % 7;
        var monday = date.AddDays(-daysSinceMonday);
        return monday.Year * 10000 + monday.Month * 100 + monday.Day;
    }

    public static (long BeginTime, long EndTime) GetDailyWindowBounds(int dayKey, int utcOffsetHours, int resetHour)
    {
        var offset = TimeSpan.FromHours(utcOffsetHours);
        var start = new DateTimeOffset(dayKey / 10000, dayKey / 100 % 100, dayKey % 100, resetHour, 0, 0, offset);
        var end = new DateTimeOffset(start.DateTime.AddDays(1), offset);
        return (start.ToUnixTimeSeconds(), end.ToUnixTimeSeconds());
    }

    public static (long BeginTime, long EndTime) GetWeeklyWindowBounds(int mondayKey, int utcOffsetHours, int resetHour)
    {
        var offset = TimeSpan.FromHours(utcOffsetHours);
        var start = new DateTimeOffset(mondayKey / 10000, mondayKey / 100 % 100, mondayKey % 100, resetHour, 0, 0, offset);
        var end = new DateTimeOffset(start.DateTime.AddDays(7), offset);
        return (start.ToUnixTimeSeconds(), end.ToUnixTimeSeconds());
    }

    public static (long BeginTime, long EndTime) GetMonthlyWindowBounds(int monthKey, int utcOffsetHours, int resetHour)
    {
        var year = monthKey / 100;
        var month = monthKey % 100;
        var offset = TimeSpan.FromHours(utcOffsetHours);
        var start = new DateTimeOffset(year, month, 1, resetHour, 0, 0, offset);
        var end = new DateTimeOffset(start.DateTime.AddMonths(1), offset);
        return (start.ToUnixTimeSeconds(), end.ToUnixTimeSeconds());
    }

    private static string ResolveRefreshType(int shopId, ShopGoodsConfigExcel goods)
    {
        if (!string.IsNullOrEmpty(goods.RefreshType))
            return goods.RefreshType.ToUpperInvariant();
        return MonthlyResetShopIds.Contains(shopId) ? "MONTH" : "";
    }

    public void EnsureShopWindows()
    {
        var today = DailyResetClock.Today(Player.Data);
        var currentWeek = GetWeeklyKey(today);
        var currentMonth = today / 100;

        var dailyChanged = Data.DailyResetKey != today;
        var weeklyChanged = Data.WeeklyResetKey != currentWeek;
        var monthlyChanged = Data.MonthlyResetKey != currentMonth;
        if (!dailyChanged && !weeklyChanged && !monthlyChanged) return;

        var clearDaily = dailyChanged && Data.DailyResetKey != 0;
        var clearWeekly = weeklyChanged && Data.WeeklyResetKey != 0;
        var clearMonthly = monthlyChanged && Data.MonthlyResetKey != 0;

        if (clearDaily || clearWeekly || clearMonthly)
        {
            foreach (var shop in GameData.ShopConfigData.Values)
            foreach (var goods in shop.Goods)
            {
                var refresh = ResolveRefreshType(shop.ShopID, goods);
                var shouldClear = refresh switch
                {
                    "DAILY" => clearDaily,
                    "WEEK" => clearWeekly,
                    "MONTH" => clearMonthly,
                    "CYCLE" => clearMonthly && MonthlyResetShopIds.Contains(shop.ShopID),
                    _ => false
                };
                if (shouldClear) Data.GoodsBuyTimes.Remove(goods.GoodsID);
            }
        }

        Data.DailyResetKey = today;
        Data.WeeklyResetKey = currentWeek;
        Data.MonthlyResetKey = currentMonth;
        MarkDirty();
    }

    public (long BeginTime, long EndTime) GetGoodsWindow(int shopId, ShopGoodsConfigExcel goods)
    {
        var today = DailyResetClock.Today(Player.Data);
        var offset = DailyResetClock.UtcOffsetHours;
        var resetHour = DailyResetClock.ResetHour;
        var refresh = ResolveRefreshType(shopId, goods);
        return refresh switch
        {
            "DAILY" => GetDailyWindowBounds(today, offset, resetHour),
            "WEEK" => GetWeeklyWindowBounds(GetWeeklyKey(today), offset, resetHour),
            "MONTH" => GetMonthlyWindowBounds(today / 100, offset, resetHour),
            "CYCLE" when MonthlyResetShopIds.Contains(shopId) => GetMonthlyWindowBounds(today / 100, offset, resetHour),
            _ => (0, uint.MaxValue)
        };
    }

    public uint GetGoodsBuyTimes(int goodsId)
    {
        EnsureShopWindows();
        return (uint)Math.Max(0, Data.GoodsBuyTimes.GetValueOrDefault(goodsId));
    }

    public List<Proto.Shop> BuildShopList(uint shopType)
    {
        EnsureShopWindows();

        var shops = new List<Proto.Shop>();
        foreach (var shop in GameData.ShopConfigData.Values)
        {
            if (shop.ShopType != shopType || shop.Goods.Count == 0) continue;

            var cityProgress = Data.CityShops.GetValueOrDefault(shop.ShopID);
            long shopBegin = 0;
            long shopEnd = uint.MaxValue;

            var shopProto = new Proto.Shop
            {
                ShopId = (uint)shop.ShopID,
                CityLevel = (uint)Math.Max(cityProgress?.Level ?? 1, 1),
                CityExp = (uint)Math.Max(cityProgress?.Exp ?? 0, 0),
                CityTakenLevelReward = cityProgress?.ToTakenLevelRewardMask() ?? 0
            };

            foreach (var goods in shop.Goods)
            {
                if (goods.ItemID == 0) continue;
                var (goodsBegin, goodsEnd) = GetGoodsWindow(shop.ShopID, goods);
                if (goodsEnd < shopEnd)
                {
                    shopBegin = goodsBegin;
                    shopEnd = goodsEnd;
                }

                var goodsProto = goods.ToProto();
                goodsProto.BuyTimes = (uint)Math.Max(0, Data.GoodsBuyTimes.GetValueOrDefault(goods.GoodsID));
                goodsProto.BeginTime = goodsBegin;
                goodsProto.EndTime = goodsEnd;
                shopProto.GoodsList.Add(goodsProto);
            }

            shopProto.BeginTime = shopBegin;
            shopProto.EndTime = shopEnd;
            shops.Add(shopProto);
        }

        return shops;
    }

    public async ValueTask<ShopBuyResult> BuyItem(int shopId, int goodsId, long count)
    {
        EnsureShopWindows();

        var existingBuyTimes = (uint)Math.Max(0, Data.GoodsBuyTimes.GetValueOrDefault(goodsId));
        if (count < 1 || count > int.MaxValue) return new ShopBuyResult(Retcode.RetFail, [], existingBuyTimes);

        GameData.ShopConfigData.TryGetValue(shopId, out var shopConfig);
        if (shopConfig == null) return new ShopBuyResult(Retcode.RetFail, [], existingBuyTimes);
        var goods = shopConfig.Goods.Find(g => g.GoodsID == goodsId);
        if (goods == null) return new ShopBuyResult(Retcode.RetFail, [], existingBuyTimes);
        if (goods.LimitTimes > 0 && existingBuyTimes + count > goods.LimitTimes)
            return new ShopBuyResult(Retcode.RetBuyTimesLimit, [], existingBuyTimes);

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

        if (GameData.CityShopConfigData.TryGetValue(shopId, out var cityShop) &&
            goods.CostList.TryGetValue(cityShop.ItemID, out var unitCurrencyCost) && unitCurrencyCost > 0)
            await AdvanceCityShopExp(cityShop, (long)unitCurrencyCost * count);

        if (Player.MissionManager != null)
            await Player.MissionManager.HandleFinishType(MissionFinishTypeEnum.BuyShopGoods, goods);

        return new ShopBuyResult(Retcode.RetSucc, items, (uint)updatedBuyTimes);
    }

    public async ValueTask<(Retcode Retcode, List<ItemData> Rewards)> TakeCityShopReward(int shopId, int level)
    {
        if (level < 1) return (Retcode.RetFail, []);
        if (!GameData.CityShopConfigData.TryGetValue(shopId, out var cityShop))
            return (Retcode.RetFail, []);
        if (!GameData.CityShopRewardListData.TryGetValue(cityShop.RewardListGroupID, out var rewardLevels) ||
            !rewardLevels.TryGetValue(level, out var rewardRow) || rewardRow.RewardID <= 0)
            return (Retcode.RetFail, []);

        var progress = Data.CityShops.GetValueOrDefault(shopId);
        if (progress == null || progress.Level < level)
            return (Retcode.RetCityLevelNotMeet, []);
        if (progress.TakenRewardLevels.Contains(level))
            return (Retcode.RetCityLevelRewardTaken, []);

        // Commit the claim before granting so a repeated request cannot pay twice.
        progress.TakenRewardLevels.Add(level);
        MarkDirty();

        var rewards = await Player.InventoryManager!.HandleReward(rewardRow.RewardID);
        await Player.SendPacket(new PacketCityShopInfoScNotify((uint)shopId, progress));
        return (Retcode.RetSucc, rewards);
    }

    private async ValueTask AdvanceCityShopExp(CityShopConfigExcel cityShop, long gainedExp)
    {
        if (gainedExp <= 0) return;
        if (!Data.CityShops.TryGetValue(cityShop.ShopID, out var progress))
        {
            progress = new CityShopProgress();
            Data.CityShops[cityShop.ShopID] = progress;
        }

        var maxLevel = Math.Max(cityShop.MaxLevel, 1);
        GameData.CityShopRewardListData.TryGetValue(cityShop.RewardListGroupID, out var rewardLevels);

        var exp = Math.Min((long)progress.Exp + gainedExp, int.MaxValue);
        while (progress.Level < maxLevel && rewardLevels != null &&
               rewardLevels.TryGetValue(progress.Level, out var currentRow) &&
               currentRow.ItemNeed > 0 && exp >= currentRow.ItemNeed)
        {
            exp -= currentRow.ItemNeed;
            progress.Level++;
        }

        progress.Exp = (int)Math.Min(exp, int.MaxValue);
        MarkDirty();
        await Player.SendPacket(new PacketCityShopInfoScNotify((uint)cityShop.ShopID, progress));
    }
}
