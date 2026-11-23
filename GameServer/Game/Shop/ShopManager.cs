using March7thHoney.Data;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Shop;

public class ShopManager(PlayerInstance player) : BasePlayerManager(player)
{
    /// <summary>Upper bound on one purchase of an item without a pile limit.</summary>
    private const int DefaultMaxBuyCount = 9999;

    public async ValueTask<(Retcode retcode, List<ItemData> items)> BuyItem(int shopId, int goodsId, long count)
    {
        GameData.ShopConfigData.TryGetValue(shopId, out var shopConfig);
        if (shopConfig == null) return (Retcode.RetFail, []);
        var goods = shopConfig.Goods.Find(g => g.GoodsID == goodsId);
        if (goods == null) return (Retcode.RetFail, []);
        GameData.ItemConfigData.TryGetValue(goods.ItemID, out var itemConfig);
        if (itemConfig == null) return (Retcode.RetItemConfigNotExist, []);

        var inventory = Player.InventoryManager!;
        var (maxCount, limitRetcode) = itemConfig.ItemMainType switch
        {
            ItemMainTypeEnum.Equipment => (GameConstants.INVENTORY_MAX_EQUIPMENT - inventory.Data.EquipmentItems.Count,
                Retcode.RetEquipmentExceedLimit),
            ItemMainTypeEnum.Relic => (GameConstants.INVENTORY_MAX_RELIC - inventory.Data.RelicItems.Count,
                Retcode.RetRelicExceedLimit),
            _ => (itemConfig.PileLimit > 0 ? itemConfig.PileLimit : DefaultMaxBuyCount, Retcode.RetFail)
        };
        if (count < 1) return (Retcode.RetFail, []);
        if (count > maxCount) return (limitRetcode, []);

        var costs = goods.CostList.Where(cost => cost.Value != 0).Select(cost => (cost.Key, (long)cost.Value * count));
        if (!await inventory.TryRemoveItems(costs)) return (Retcode.RetItemNotEnough, []);

        var items = new List<ItemData>();
        if (itemConfig.ItemMainType is ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic)
        {
            for (var i = 0; i < count; i++)
            {
                var item = await Player.InventoryManager!.AddItem(itemConfig.ID, 1, false);
                if (item != null) items.Add(item);
            }
        }
        else
        {
            var item = await Player.InventoryManager!.AddItem(itemConfig.ID, (int)count, false);
            if (item != null)
            {
                if (GameData.ItemUseDataData.TryGetValue(item.ItemId, out var useData) && useData.IsAutoUse)
                {
                    var res = await Player.InventoryManager!.UseItem(item.ItemId);
                    if (res.returnItems != null) items.AddRange(res.returnItems);
                }
                else
                {
                    items.Add(item);
                }
            }
        }

        if (Player.MissionManager != null)
            await Player.MissionManager.HandleFinishType(MissionFinishTypeEnum.BuyShopGoods, goods);

        return (Retcode.RetSucc, items);
    }
}
