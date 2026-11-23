using March7thHoney.Data;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Sync;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager
{
    private readonly SemaphoreSlim _equipmentQuickUpLock = new(1, 1);

    public async ValueTask<(Retcode Retcode, List<ItemData> ReturnItems)> QuickLevelUpEquipment(EquipmentQuickUpCsReq req)
    {
        await _equipmentQuickUpLock.WaitAsync();
        try
        {
            return await ApplyEquipmentQuickUp(req);
        }
        finally
        {
            _equipmentQuickUpLock.Release();
        }
    }

    /// <summary>
    ///     Reads a quick-up cost list into item ID -> count, merging repeated IDs. False when an entry lacks an item ID
    ///     or a count, or a count does not fit an int; a missing list is an empty one.
    /// </summary>
    internal static bool ReadQuickUpCosts(ItemCostData? data, out Dictionary<int, long> costs)
    {
        costs = [];
        if (data == null) return true;
        foreach (var cost in data.ItemList)
        {
            if (cost.PileItem is not { ItemId: > 0, ItemNum: > 0 } pile || pile.ItemId > int.MaxValue ||
                pile.ItemNum > int.MaxValue)
                return false;
            var id = (int)pile.ItemId;
            costs[id] = costs.GetValueOrDefault(id) + pile.ItemNum;
            if (costs[id] > int.MaxValue) return false;
        }
        return true;
    }

    private async ValueTask<(Retcode, List<ItemData>)> ApplyEquipmentQuickUp(EquipmentQuickUpCsReq req)
    {
        var equipment = Data.EquipmentItems.Find(item => item.UniqueId == req.EquipmentUniqueId);
        if (equipment == null)
        {
            await SyncStaleEquipmentIds([req.EquipmentUniqueId]);
            return (Retcode.RetEquipmentNotExist, []);
        }
        if (!GameData.EquipmentConfigData.TryGetValue(equipment.ItemId, out var config))
            return (Retcode.RetItemConfigNotExist, []);

        // 4.6 EOBJKAMNNPI (563, mislabeled PromoteEquipmentCsReq) and 4.7 (513)
        // share these property identities: level, promotion, EXP costs and promotion costs.
        var level = req.IGOOJKMPMDC;
        var promotion = req.KLMIBJIKIBB;
        if (equipment.Level < 1 || equipment.Exp < 0 || level is < 1 or > 80 || level < equipment.Level ||
            promotion < equipment.Promotion || promotion > config.MaxPromotion)
            return (Retcode.RetReqParaInvalid, []);
        if (level == equipment.Level && promotion == equipment.Promotion)
            return (Retcode.RetEquipmentLevelReachMax, []);
        if (!GameData.EquipmentPromotionConfigData.TryGetValue(equipment.ItemId * 10 + (int)promotion, out var targetConfig))
            return (Retcode.RetItemConfigNotExist, []);
        if (level > targetConfig.MaxLevel) return (Retcode.RetEquipmentLevelReachMax, []);

        var promotionCosts = new Dictionary<int, long>();
        for (var p = equipment.Promotion; p < promotion; p++)
        {
            if (!GameData.EquipmentPromotionConfigData.TryGetValue(equipment.ItemId * 10 + p, out var step))
                return (Retcode.RetItemConfigNotExist, []);
            if (level < step.MaxLevel || Player.Data.WorldLevel < step.WorldLevelRequire)
                return (Retcode.RetEquipmentLevelNotMeet, []);
            foreach (var cost in step.PromotionCostList)
                promotionCosts[cost.ItemID] = promotionCosts.GetValueOrDefault(cost.ItemID) + cost.ItemNum;
        }
        if (!ReadQuickUpCosts(req.IDGEEENIMMK, out var submittedPromotion) ||
            submittedPromotion.Count != promotionCosts.Count ||
            promotionCosts.Any(cost => submittedPromotion.GetValueOrDefault(cost.Key) != cost.Value))
            return (Retcode.RetEquipmentItemNotInCostList, []);

        var costs = new Dictionary<int, long>();
        var fodder = new List<ItemData>();
        var fodderIds = new HashSet<uint>();
        long providedExp = 0;
        foreach (var cost in req.BOBOKJBAMNB?.ItemList ?? [])
        {
            if (cost.PileItem is { } pile)
            {
                if (pile.ItemId is 0 or > int.MaxValue || pile.ItemNum is 0 or > int.MaxValue)
                    return (Retcode.RetReqParaInvalid, []);
                var id = (int)pile.ItemId;
                if (!GameData.EquipmentExpItemConfigData.TryGetValue(id, out var expItem) || expItem.ExpProvide <= 0 ||
                    !GameData.ItemConfigData.TryGetValue(id, out var item) || item.ItemMainType != ItemMainTypeEnum.Material)
                    return (Retcode.RetEquipmentItemNotInCostList, []);
                costs[id] = costs.GetValueOrDefault(id) + pile.ItemNum;
                providedExp += (long)expItem.ExpProvide * pile.ItemNum;
            }
            else if (cost.EquipmentUniqueId != 0)
            {
                if (cost.EquipmentUniqueId == req.EquipmentUniqueId) return (Retcode.RetEquipmentConsumeSelf, []);
                if (!fodderIds.Add(cost.EquipmentUniqueId)) return (Retcode.RetReqParaInvalid, []);
                var consumed = Data.EquipmentItems.Find(item => item.UniqueId == cost.EquipmentUniqueId);
                if (consumed == null)
                {
                    await SyncStaleEquipmentIds([cost.EquipmentUniqueId]);
                    return (Retcode.RetEquipmentNotExist, []);
                }
                if (consumed.Locked) return (Retcode.RetEquipmentLocked, []);
                if (consumed.EquipAvatar != 0) return (Retcode.RetEquipmentAlreadyDressed, []);
                if (!GameData.EquipmentConfigData.TryGetValue(consumed.ItemId, out var consumedConfig) ||
                    consumed.Level is < 1 or > 80 || consumed.Exp < 0)
                    return (Retcode.RetItemConfigNotExist, []);
                long fodderExp = consumedConfig.ExpProvide + (long)consumed.Exp;
                for (var current = 1; current < consumed.Level; current++)
                {
                    var exp = GameData.GetEquipmentExpRequired(consumedConfig.ExpType, current);
                    if (exp <= 0) return (Retcode.RetItemConfigNotExist, []);
                    fodderExp += exp;
                }
                providedExp += fodderExp;
                fodder.Add(consumed);
            }
            else return (Retcode.RetReqParaInvalid, []);
            if (providedExp > int.MaxValue) return (Retcode.RetReqParaInvalid, []);
        }
        long requiredExp = -(long)equipment.Exp;
        for (var current = equipment.Level; current < level; current++)
        {
            var exp = GameData.GetEquipmentExpRequired(config.ExpType, current);
            if (exp <= 0) return (Retcode.RetItemConfigNotExist, []);
            requiredExp += exp;
        }
        requiredExp = Math.Max(0, requiredExp);
        if (providedExp < requiredExp) return (Retcode.RetItemNotEnough, []);
        foreach (var (id, count) in promotionCosts) costs[id] = costs.GetValueOrDefault(id) + count;
        costs[(int)VirtualItemEnum.Scoin] = costs.GetValueOrDefault((int)VirtualItemEnum.Scoin) + providedExp / 2;
        foreach (var (id, count) in costs)
        {
            if (count > int.MaxValue || !GameData.ItemConfigData.ContainsKey(id)) return (Retcode.RetReqParaInvalid, []);
            if ((GetItem(id)?.Count ?? 0) < count)
                return (id == (int)VirtualItemEnum.Scoin ? Retcode.RetScoinNotEnough : Retcode.RetItemNotEnough, []);
        }

        // All costs, caps and fodder protections are validated before the first inventory change.
        var changedMaterials = new Dictionary<int, ItemData>();
        foreach (var (id, count) in costs.Where(cost => cost.Value > 0))
        {
            var removed = await RemoveItem(id, (int)count, sync: false);
            if (id != (int)VirtualItemEnum.Scoin && removed != null) changedMaterials[id] = removed;
        }
        foreach (var consumed in fodder) await RemoveItem(consumed.ItemId, 1, consumed.UniqueId, false);
        var returns = new List<ItemData>();
        var leftover = providedExp - requiredExp;
        foreach (var expItem in GameData.EquipmentExpItemConfigData.Values.Where(item => item.ExpProvide > 0)
                     .OrderByDescending(item => item.ExpProvide).ThenBy(item => item.ItemID))
        {
            if (!GameData.ItemConfigData.TryGetValue(expItem.ItemID, out var item) || item.ItemMainType != ItemMainTypeEnum.Material) continue;
            var count = (int)(leftover / expItem.ExpProvide);
            if (count == 0) continue;
            changedMaterials[expItem.ItemID] = await PutItem(expItem.ItemID, count);
            returns.Add(new ItemData { ItemId = expItem.ItemID, Count = count });
            leftover %= expItem.ExpProvide;
        }
        var leveled = level > equipment.Level;
        equipment.Level = (int)level;
        equipment.Promotion = (int)promotion;
        if (leveled) equipment.Exp = 0;
        MarkDirty();
        var sync = new List<BaseSyncData> { new ItemSyncData(equipment), new BasicInfoSyncData(Player.ToProto()) };
        sync.AddRange(changedMaterials.Values.Select(item => (BaseSyncData)new ItemSyncData(item)));
        sync.AddRange(fodder.Select(item => (BaseSyncData)new ItemSyncData(item)));
        await Player.SendPacket(new PacketPlayerSyncScNotify(sync));
        if (leveled) await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.AnyEquipmentLevelUp);
        return (Retcode.RetSucc, returns);
    }
}
