using System.Collections.Frozen;
using March7thHoney.Data;
using March7thHoney.Data.Avatar;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Sync;
using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;
using Google.Protobuf.Collections;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager
{
    #region Levelup

    private async ValueTask SyncStaleEquipmentIds(IEnumerable<int> equipmentIds)
    {
        var ids = equipmentIds.Where(id => id > 0).Select(id => (uint)id).Distinct().ToList();
        if (ids.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(ids));
    }

    private async ValueTask SyncStaleEquipmentIds(IEnumerable<uint> equipmentIds)
    {
        var ids = equipmentIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(ids));
    }

    public async ValueTask<(Retcode retcode, List<ItemData> returnItems)> LevelUpEquipment(int equipmentUniqueId,
        ItemCostData item)
    {
        var itemData = Data.EquipmentItems.Find(x => x.UniqueId == equipmentUniqueId);
        if (itemData == null)
        {
            await SyncStaleEquipmentIds([equipmentUniqueId]);
            return (Retcode.RetEquipmentNotExist, []);
        }

        GameData.EquipmentPromotionConfigData.TryGetValue(itemData.ItemId * 10 + itemData.Promotion,
            out var equipmentPromotionConfig);
        GameData.EquipmentConfigData.TryGetValue(itemData.ItemId, out var equipmentConfig);
        if (equipmentConfig == null || equipmentPromotionConfig == null) return (Retcode.RetItemConfigNotExist, []);
        if (itemData.Level >= equipmentPromotionConfig.MaxLevel) return (Retcode.RetEquipmentLevelReachMax, []);

        var exp = 0;
        List<(int itemId, int count, int uniqueId)> costItems = [];
        HashSet<uint> costEquipmentIds = [];
        List<uint> staleEquipmentIds = [];

        foreach (var cost in item.ItemList)
            if (cost.EquipmentUniqueId != 0)
            {
                if (cost.EquipmentUniqueId == equipmentUniqueId)
                    return (Retcode.RetEquipmentConsumeSelf, []);
                if (!costEquipmentIds.Add(cost.EquipmentUniqueId)) continue;

                var costItem = Data.EquipmentItems.Find(x => x.UniqueId == cost.EquipmentUniqueId);
                if (costItem == null)
                {
                    staleEquipmentIds.Add(cost.EquipmentUniqueId);
                    continue;
                }

                if (costItem.Locked) return (Retcode.RetEquipmentLocked, []);

                exp += costItem.CalcTotalEquipmentExpGained();
                costItems.Add((costItem.ItemId, 1, (int)cost.EquipmentUniqueId));
            }
            else if (cost.PileItem != null)
            {
                GameData.EquipmentExpItemConfigData.TryGetValue((int)cost.PileItem.ItemId, out var itemConfig);
                if (itemConfig == null) continue;
                exp += itemConfig.ExpProvide * (int)cost.PileItem.ItemNum;
                costItems.Add(((int)cost.PileItem.ItemId, (int)cost.PileItem.ItemNum, 0));
            }

        // payment
        if (staleEquipmentIds.Count > 0)
        {
            await SyncStaleEquipmentIds(staleEquipmentIds);
            return (Retcode.RetEquipmentNotExist, []);
        }

        if (exp <= 0 || costItems.Count == 0) return (Retcode.RetItemNoCost, []);

        var costScoin = exp / 2;
        if (Player.Data.Scoin < costScoin) return (Retcode.RetScoinNotEnough, []);
        await RemoveItems(costItems);

        await RemoveItem((int)VirtualItemEnum.Scoin, costScoin);

        var maxLevel = equipmentPromotionConfig.MaxLevel;
        var curExp = itemData.Exp;
        var curLevel = itemData.Level;
        var startLevel = curLevel;
        var nextLevelExp = GameData.GetEquipmentExpRequired(equipmentConfig.ExpType, itemData.Level);
        do
        {
            int toGain;
            if (curExp + exp >= nextLevelExp)
                toGain = nextLevelExp - curExp;
            else
                toGain = exp;
            curExp += toGain;
            exp -= toGain;
            // level up
            if (curExp >= nextLevelExp)
            {
                curExp = 0;
                curLevel++;
                nextLevelExp = GameData.GetEquipmentExpRequired(equipmentConfig.ExpType, curLevel);
            }
        } while (exp > 0 && nextLevelExp > 0 && curLevel < maxLevel);

        itemData.Level = curLevel;
        itemData.Exp = curExp;
        MarkDirty();
        // leftover
        Dictionary<int, ItemData> list = [];
        var leftover = exp;
        while (leftover > 0)
        {
            var gain = false;
            foreach (var expItem in GameData.EquipmentExpItemConfigData.Values.Reverse())
                if (leftover >= expItem.ExpProvide)
                {
                    // add
                    await PutItem(expItem.ItemID, 1);
                    if (list.TryGetValue(expItem.ItemID, out var i))
                    {
                        i.Count++;
                    }
                    else
                    {
                        i = new ItemData
                        {
                            ItemId = expItem.ItemID,
                            Count = 1
                        };
                        list[expItem.ItemID] = i;
                    }

                    leftover -= expItem.ExpProvide;
                    gain = true;
                    break;
                }

            if (!gain) break; // no more item
        }

        if (list.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(list.Values.ToList()));
        await Player.SendPacket(new PacketPlayerSyncScNotify(itemData));
        if (curLevel > startLevel)
            await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.AnyEquipmentLevelUp);
        return (Retcode.RetSucc, [.. list.Values]);
    }

    public async ValueTask<bool> PromoteEquipment(int equipmentUniqueId)
    {
        var equipmentData =
            Player.InventoryManager!.Data.EquipmentItems.FirstOrDefault(x => x.UniqueId == equipmentUniqueId);
        if (equipmentData == null ||
            equipmentData.Promotion >= GameData.EquipmentConfigData[equipmentData.ItemId].MaxPromotion) return false;

        var promotionConfig = GameData.EquipmentPromotionConfigData.Values
            .FirstOrDefault(x => x.EquipmentID == equipmentData.ItemId && x.Promotion == equipmentData.Promotion);

        if (promotionConfig == null || equipmentData.Level < promotionConfig.MaxLevel ||
            Player.Data.WorldLevel < promotionConfig.WorldLevelRequire) return false;

        foreach (var cost in promotionConfig.PromotionCostList)
            await Player.InventoryManager!.RemoveItem(cost.ItemID, cost.ItemNum);

        equipmentData.Promotion++;
        await Player.SendPacket(new PacketPlayerSyncScNotify(equipmentData));

        return true;
    }

    public async ValueTask<List<ItemData>> LevelUpRelic(int uniqueId, ItemCostData costData)
    {
        var relicItem = Data.RelicItems.Find(x => x.UniqueId == uniqueId);
        if (relicItem == null) return [];

        var exp = 0;
        var money = 0;
        foreach (var cost in costData.ItemList)
            if (cost.PileItem != null)
            {
                GameData.RelicExpItemData.TryGetValue((int)cost.PileItem.ItemId, out var excel);
                if (excel != null)
                {
                    exp += excel.ExpProvide * (int)cost.PileItem.ItemNum;
                    money += excel.CoinCost * (int)cost.PileItem.ItemNum;
                }

                await RemoveItem((int)cost.PileItem.ItemId, (int)cost.PileItem.ItemNum);
            }
            else if (cost.RelicUniqueId != 0)
            {
                var costItem = Data.RelicItems.Find(x => x.UniqueId == cost.RelicUniqueId);
                if (costItem != null)
                {
                    GameData.RelicConfigData.TryGetValue(costItem.ItemId, out var costExcel);
                    if (costExcel == null) continue;

                    if (costItem.Level > 0)
                        foreach (var level in Enumerable.Range(0, costItem.Level))
                        {
                            GameData.RelicExpTypeData.TryGetValue(costExcel.ExpType * 100 + level, out var typeExcel);
                            if (typeExcel != null)
                                exp += typeExcel.Exp;
                        }
                    else
                        exp += costExcel.ExpProvide;

                    exp += costItem.Exp;
                    money += costExcel.CoinCost;

                    await RemoveItem(costItem.ItemId, 1, (int)cost.RelicUniqueId);
                }
            }

        // credit
        await RemoveItem((int)VirtualItemEnum.Scoin, money);

        // level up
        GameData.RelicConfigData.TryGetValue(relicItem.ItemId, out var relicExcel);
        if (relicExcel == null) return [];

        var startLevel = relicItem.Level;
        GameData.RelicExpTypeData.TryGetValue(relicExcel.ExpType * 100 + relicItem.Level, out var relicType);
        do
        {
            if (relicType == null) break;
            int toGain;
            if (relicItem.Exp + exp >= relicType.Exp)
                toGain = relicType.Exp - relicItem.Exp;
            else
                toGain = exp;
            relicItem.Exp += toGain;
            exp -= toGain;

            // level up
            if (relicItem.Exp >= relicType.Exp)
            {
                relicItem.Exp = 0;
                relicItem.Level++;
                GameData.RelicExpTypeData.TryGetValue(relicExcel.ExpType * 100 + relicItem.Level, out relicType);
                // relic attribute
                if (relicItem.Level % 3 == 0)
                {
                    if (relicItem.SubAffixes.Count >= 4)
                        relicItem.IncreaseRandomRelicSubAffix();
                    else
                        relicItem.AddRandomRelicSubAffix();
                }
            }
        } while (exp > 0 && relicType?.Exp > 0 && relicItem.Level < relicExcel.MaxLevel);

        // leftover
        Dictionary<int, ItemData> list = [];
        var leftover = exp;
        while (leftover > 0)
        {
            var gain = false;
            foreach (var expItem in GameData.RelicExpItemData.Values.Reverse())
                if (leftover >= expItem.ExpProvide)
                {
                    // add
                    await PutItem(expItem.ItemID, 1);
                    if (list.TryGetValue(expItem.ItemID, out var i))
                    {
                        i.Count++;
                    }
                    else
                    {
                        i = new ItemData
                        {
                            ItemId = expItem.ItemID,
                            Count = 1
                        };
                        list[expItem.ItemID] = i;
                    }

                    leftover -= expItem.ExpProvide;
                    gain = true;
                    break;
                }

            if (!gain) break; // no more item
        }

        if (list.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(list.Values.ToList()));

        // sync
        await Player.SendPacket(new PacketPlayerSyncScNotify(relicItem));
        if (relicItem.Level > startLevel)
            await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.AnyRelicLevelUp);

        return [.. list.Values];
    }

    public async ValueTask<Retcode> RankUpEquipment(int equipmentUniqueId, ItemCostData costData)
    {
        var itemData = Data.EquipmentItems.Find(x => x.UniqueId == equipmentUniqueId);
        if (itemData == null)
        {
            await SyncStaleEquipmentIds([equipmentUniqueId]);
            return Retcode.RetEquipmentNotExist;
        }

        GameData.EquipmentConfigData.TryGetValue(itemData.ItemId, out var equipmentConfig);
        if (equipmentConfig == null) return Retcode.RetItemConfigNotExist;

        var maxRank = Math.Max(1, equipmentConfig.MaxRank);
        if (itemData.Rank >= maxRank) return Retcode.RetEquipmentRankUpReachMax;

        var rank = 0;
        List<(int itemId, int count, int uniqueId)> costItems = [];
        HashSet<uint> costEquipmentIds = [];
        List<uint> staleEquipmentIds = [];
        foreach (var cost in costData.ItemList)
        {
            if (cost.EquipmentUniqueId == 0) continue;
            if (cost.EquipmentUniqueId == equipmentUniqueId) return Retcode.RetEquipmentConsumeSelf;
            if (!costEquipmentIds.Add(cost.EquipmentUniqueId)) continue;

            var costItem = Data.EquipmentItems.Find(x => x.UniqueId == cost.EquipmentUniqueId);
            if (costItem == null)
            {
                staleEquipmentIds.Add(cost.EquipmentUniqueId);
                continue;
            }

            if (costItem.Locked) return Retcode.RetEquipmentLocked;
            if (costItem.ItemId != itemData.ItemId) return Retcode.RetEquipmentRankUpMustConsumeSameTid;

            costItems.Add((costItem.ItemId, 1, (int)cost.EquipmentUniqueId));
            rank += Math.Max(1, costItem.Rank);

            if (itemData.Rank + rank >= maxRank) break;
        }

        if (staleEquipmentIds.Count > 0)
        {
            await SyncStaleEquipmentIds(staleEquipmentIds);
            return Retcode.RetEquipmentNotExist;
        }

        if (rank == 0 || costItems.Count == 0) return Retcode.RetItemNoCost;

        await RemoveItems(costItems, false);
        itemData.Rank = Math.Min(itemData.Rank + rank, maxRank);
        MarkDirty();
        await Player.SendPacket(new PacketPlayerSyncScNotify(new ItemSyncData(itemData),
            new DelEquipmentSyncData(costItems.Select(item => (uint)item.uniqueId))));
        return Retcode.RetSucc;
    }

    #endregion
}
