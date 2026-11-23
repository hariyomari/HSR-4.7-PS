using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager
{
    /// <summary>Upper bound on one compose request when the recipe has no MaxCount (an older resource cache).</summary>
    private const int DefaultComposeMaxCount = 9999;

    public async ValueTask<(Retcode retcode, ItemData? item)> ComposeItem(int composeId, int count,
        List<ItemCost> costData)
    {
        GameData.ItemComposeConfigData.TryGetValue(composeId, out var composeConfig);
        if (composeConfig == null || composeConfig.IsSelectedRelic ||
            !GameData.ItemConfigData.ContainsKey(composeConfig.ItemID))
            return (Retcode.RetItemConfigNotExist, null);
        if (count < 1 || count > GetComposeMaxCount(composeConfig, DefaultComposeMaxCount))
            return (Retcode.RetFail, null);

        var costs = GetComposeCosts(composeConfig, count, costData);
        if (costs == null) return (Retcode.RetItemNotInCostList, null);
        if (!await TryRemoveItems(costs)) return (Retcode.RetItemNotEnough, null);

        var composed = await AddItem(composeConfig.ItemID, count, false);
        if (composed != null && Player.MissionManager != null)
            await Player.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.AnyItemComposeCount);
        return (Retcode.RetSucc, composed);
    }

    private static int GetComposeMaxCount(ItemComposeConfigExcel composeConfig, int fallback)
    {
        return composeConfig.MaxCount > 0 ? composeConfig.MaxCount : fallback;
    }

    /// <summary>
    ///     The full price of <paramref name="count" /> compose results, taken from the recipe. The client only chooses
    ///     which of the recipe's SpecialMaterialCost items pays the special part; any other entry it sends is ignored.
    ///     Null when the special part is not paid exactly or the recipe has no cost at all.
    /// </summary>
    private static List<(int itemId, long count)>? GetComposeCosts(ItemComposeConfigExcel composeConfig, int count,
        IEnumerable<ItemCost> chosenItems)
    {
        List<(int itemId, long count)> costs =
        [
            .. composeConfig.MaterialCost.Where(cost => cost.ItemNum != 0)
                .Select(cost => (cost.ItemID, (long)cost.ItemNum * count))
        ];
        if (composeConfig.CoinCost != 0) costs.Add(((int)VirtualItemEnum.Scoin, (long)composeConfig.CoinCost * count));

        if (composeConfig.SpecialMaterialCostNumber > 0 && composeConfig.SpecialMaterialCost.Count > 0)
        {
            var required = (long)composeConfig.SpecialMaterialCostNumber * count;
            if (composeConfig.SpecialMaterialCost.Count == 1)
            {
                costs.Add((composeConfig.SpecialMaterialCost[0], required));
            }
            else
            {
                var chosen = chosenItems
                    .Where(cost => cost.PileItem is { ItemNum: > 0 } pile &&
                                   composeConfig.SpecialMaterialCost.Contains((int)pile.ItemId))
                    .Select(cost => ((int)cost.PileItem.ItemId, (long)cost.PileItem.ItemNum))
                    .ToList();
                if (chosen.Sum(cost => cost.Item2) != required) return null;
                costs.AddRange(chosen);
            }
        }

        // A recipe that costs nothing is malformed (or comes from a cache without the special-cost columns).
        return costs.Count == 0 ? null : costs;
    }

    public async ValueTask<List<ItemData>> SellItem(ItemCostData costData, bool toMaterial)
    {
        List<ItemData> items = [];
        Dictionary<int, int> itemMap = [];
        List<(int itemId, int count, int uniqueId)> removeItems = [];

        foreach (var cost in costData.ItemList)
            if (cost.EquipmentUniqueId != 0) // equipment
            {
                var itemData = Data.EquipmentItems.Find(x => x.UniqueId == cost.EquipmentUniqueId);
                if (itemData == null) continue;
                removeItems.Add((itemData.ItemId, 1, (int)cost.EquipmentUniqueId));
                GameData.ItemConfigData.TryGetValue(itemData.ItemId, out var itemConfig);
                if (itemConfig == null) continue;
                foreach (var returnItem in itemConfig.ReturnItemIDList) // return items
                {
                    if (!itemMap.ContainsKey(returnItem.ItemID)) itemMap[returnItem.ItemID] = 0;
                    itemMap[returnItem.ItemID] += returnItem.ItemNum;
                }
            }
            else if (cost.RelicUniqueId != 0) // relic
            {
                var itemData = Data.RelicItems.Find(x => x.UniqueId == cost.RelicUniqueId);
                if (itemData == null) continue;
                removeItems.Add((itemData.ItemId, 1, (int)cost.RelicUniqueId));
                GameData.ItemConfigData.TryGetValue(itemData.ItemId, out var itemConfig);
                if (itemConfig == null) continue;
                if (itemConfig.Rarity != ItemRarityEnum.SuperRare || toMaterial)
                {
                    foreach (var returnItem in itemConfig.ReturnItemIDList) // basic return items
                    {
                        itemMap.TryAdd(returnItem.ItemID, 0);
                        itemMap[returnItem.ItemID] += returnItem.ItemNum;
                    }

                    var expReturned = (int)(itemData.CalcTotalExpGained() * 0.8);

                    var credit = (int)(expReturned * 1.5);
                    if (credit > 0)
                    {
                        itemMap.TryAdd(2, 0);
                        itemMap[2] += (int)(expReturned * 1.5);
                    }

                    var lostGoldFragCnt = expReturned / 500;
                    if (lostGoldFragCnt > 0)
                    {
                        itemMap.TryAdd(232, 0);
                        itemMap[232] += lostGoldFragCnt;
                    }

                    var lostGoldLightdust = expReturned % 500 / 100;
                    if (lostGoldLightdust > 0)
                    {
                        itemMap.TryAdd(231, 0);
                        itemMap[231] += lostGoldLightdust;
                    }
                }
                else
                {
                    var expGained = itemData.CalcTotalExpGained();
                    var remainsCnt = (int)(10 + expGained * 0.005144);
                    if (remainsCnt > 0)
                    {
                        itemMap.TryAdd(235, 0);
                        itemMap[235] += remainsCnt;
                    }
                }
            }
            else
            {
                removeItems.Add(((int)cost.PileItem.ItemId, (int)cost.PileItem.ItemNum, 0));
            }

        var removedItems = RemoveItems(removeItems);
        var resolvedRelics = removeItems.Count(item => item.uniqueId != 0 &&
                                                       GameData.RelicConfigData.ContainsKey(item.itemId));

        foreach (var itemInfo in itemMap)
        {
            var item = await AddItem(itemInfo.Key, itemInfo.Value, false);
            if (item != null) items.Add(item);
        }

        if (resolvedRelics > 0)
            await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.ResolveRelic, resolvedRelics);
        return items;
    }
}
