using March7thHoney.Data;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager
{
    private async ValueTask<ItemData?> AddRelicItem(int itemId, int level, bool sync)
    {
        //I dont think one player can have more than max count of relic until i see a player get 50000 relic and the client crashed :(
        if (Data.RelicItems.Count + 1 > GameConstants.INVENTORY_MAX_RELIC) // get the max relic
        {
            await Player.SendPacket(new PacketRetcodeNotify(Retcode.RetRelicExceedLimit));
            return null;
        }

        var (_, itemData) = await HandleRelic(itemId, ++Data.NextUniqueId, level, sync: sync);
        return itemData;
    }

    public async ValueTask<(int, ItemData?)> HandleRelic(
        int relicId, int uniqueId, int level, int mainAffixId = 0, List<(int, int)>? subAffixes = null,
        bool sync = true)
    {
        // Excel
        GameData.RelicConfigData.TryGetValue(relicId, out var itemConfig);
        GameData.ItemConfigData.TryGetValue(relicId, out var itemConfigExcel);
        if (itemConfig == null || itemConfigExcel == null)
            return (1, null);

        GameData.RelicSubAffixData.TryGetValue(itemConfig.SubAffixGroup, out var subAffixConfig);
        GameData.RelicMainAffixData.TryGetValue(itemConfig.MainAffixGroup, out var mainAffixConfig);
        if (subAffixConfig == null || mainAffixConfig == null)
            return (1, null);

        var relic = new ItemData
        {
            ItemId = relicId,
            Level = Math.Max(Math.Min(level, 9999), 0),
            UniqueId = uniqueId,
            Count = 1
        };

        // MainAffixId
        if (mainAffixId == 0 || !mainAffixConfig.TryGetValue(mainAffixId, out _))
            relic.AddRandomRelicMainAffix();
        else
            relic.MainAffix = mainAffixId;

        // SubAffixes
        subAffixes ??= [];
        if (subAffixes.Count > 4) return (3, null);
        relic.AddRelicSubAffix(subAffixes); // Add from input

        var initSubCnt = new Random().Next(3, 5);
        relic.AddRandomRelicSubAffix(initSubCnt - subAffixes.Count);
        if (initSubCnt == 3 && level / 3 > 0) relic.AddRandomRelicSubAffix(); // Random add init subAffixes

        var remainUpCnt = level / 3 - (4 - initSubCnt) - subAffixes.Sum(x => x.Item2);
        relic.IncreaseRandomRelicSubAffix(remainUpCnt); // Level up

        if (sync)
        {
            await Player.InventoryManager!.AddItem(relic, false);
        }
        else
        {
            await PutItem(relic.ItemId, relic.Count, relic.Rank, relic.Promotion, relic.Level,
                relic.Exp, relic.TotalExp, relic.MainAffix, relic.SubAffixes,
                relic.ReforgeSubAffixes, relic.UniqueId);
        }
        return (0, relic);
    }

    /// <summary>Wishful Resin: designates up to two sub stats during relic synthesis.</summary>
    private const int WishfulResinItemId = 237;

    private const int MaxDesignatedSubAffixes = 2;
    private const int DefaultRelicComposeMaxCount = 20;

    public async ValueTask<(Retcode retcode, List<ItemData> items)> ComposeRelic(ComposeSelectedRelicCsReq req)
    {
        GameData.ItemComposeConfigData.TryGetValue((int)req.ComposeId, out var composeConfig);
        if (composeConfig == null) return (Retcode.RetItemConfigNotExist, []);

        var count = req.Count == 0 ? 1L : req.Count;
        if (count > GetComposeMaxCount(composeConfig, DefaultRelicComposeMaxCount)) return (Retcode.RetFail, []);
        if (Data.RelicItems.Count + count > GameConstants.INVENTORY_MAX_RELIC) return (Retcode.RetRelicExceedLimit, []);

        // The relic and its designated stats must belong to the recipe.
        var relicId = (int)req.ComposeRelicId;
        if (req.ComposeRelicId > int.MaxValue || !composeConfig.RelicList.Contains(relicId) ||
            !GameData.RelicConfigData.TryGetValue(relicId, out var relicConfig) ||
            !GameData.ItemConfigData.ContainsKey(relicId) ||
            !GameData.RelicMainAffixData.TryGetValue(relicConfig.MainAffixGroup, out var mainAffixConfig) ||
            !GameData.RelicSubAffixData.TryGetValue(relicConfig.SubAffixGroup, out var subAffixConfig))
            return (Retcode.RetItemConfigNotExist, []);

        var mainAffix = (int)req.MainAffixId;
        if (req.MainAffixId > int.MaxValue || (mainAffix != 0 && !mainAffixConfig.ContainsKey(mainAffix)))
            return (Retcode.RetFail, []);

        var subAffixIds = req.SubAffixIdList.Select(id => (long)id).ToList();
        if (subAffixIds.Count > MaxDesignatedSubAffixes || subAffixIds.Distinct().Count() != subAffixIds.Count ||
            (subAffixIds.Count > 0 && mainAffix == 0) ||
            subAffixIds.Any(id => id > int.MaxValue || !subAffixConfig.ContainsKey((int)id) ||
                                  subAffixConfig[(int)id].Property == mainAffixConfig[mainAffix].Property))
            return (Retcode.RetFail, []);

        var costs = GetComposeCosts(composeConfig, (int)count, req.ComposeItemList?.ItemList ?? []);
        if (costs == null) return (Retcode.RetItemNotInCostList, []);
        if (subAffixIds.Count > 0)
        {
            // Designating sub stats costs extra resin on top of the recipe: at least one per relic.
            var resin = (req.WrItemList?.ItemList ?? [])
                .Where(cost => cost.PileItem is { ItemNum: > 0 } pile &&
                               ((int)pile.ItemId == WishfulResinItemId ||
                                composeConfig.SpecialMaterialCost.Contains((int)pile.ItemId)))
                .Select(cost => ((int)cost.PileItem.ItemId, (long)cost.PileItem.ItemNum))
                .ToList();
            if (resin.Sum(cost => cost.Item2) < count) return (Retcode.RetItemNotEnough, []);
            costs.AddRange(resin);
        }

        if (!await TryRemoveItems(costs)) return (Retcode.RetItemNotEnough, []);

        List<ItemData> items = [];
        for (var i = 0; i < count; i++)
        {
            var itemData = new ItemData
            {
                ItemId = relicId,
                Level = 0,
                UniqueId = ++Data.NextUniqueId,
                MainAffix = mainAffix,
                SubAffixes = subAffixIds.Select(subId => new ItemSubAffix(subAffixConfig[(int)subId], 1)).ToList(),
                Count = 1
            };
            if (mainAffix == 0) itemData.AddRandomRelicMainAffix();
            itemData.AddRandomRelicSubAffix(3 - itemData.SubAffixes.Count + itemData.LuckyRelicSubAffixCount());
            await AddItem(itemData, false);
            items.Add(itemData);
        }

        if (Player.MissionManager != null)
            await Player.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.AnyItemComposeCount);
        return (Retcode.RetSucc, items);
    }
}
