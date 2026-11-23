using March7thHoney.Data.Avatar;
using March7thHoney.Data;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Inventory;
using March7thHoney.Database;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Inventory;
using March7thHoney.GameServer.Game.Sync;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Internationalization;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Avatar;

/// <summary>Avatar progression: level, promotion, quick up, eidolons (rank) and ExRank.</summary>
public partial class AvatarManager
{
    /// <summary>Star Rail Pass, the standard warp ticket (the cost item of the standard banner).</summary>
    public const int PromotionRewardItemId = 101;

    public async ValueTask<List<ItemData>> LevelUpAvatar(int baseAvatarId, ItemCostData item)
    {
        var inventory = Player.InventoryManager!;
        var avatarData = GetFormalAvatar(baseAvatarId);
        if (avatarData == null) return [];
        GameData.AvatarConfigData.TryGetValue(avatarData.AvatarId, out var avatarConfig);
        if (avatarConfig == null) return [];

        GameData.AvatarPromotionConfigData.TryGetValue(avatarData.AvatarId * 10 + avatarData.Promotion,
            out var promotionConfig);
        if (promotionConfig == null) return [];
        var exp = 0;

        foreach (var cost in item.ItemList)
        {
            GameData.ItemConfigData.TryGetValue((int)cost.PileItem.ItemId, out var itemConfig);
            if (itemConfig == null) continue;
            exp += itemConfig.Exp * (int)cost.PileItem.ItemNum;
        }

        // payment
        var costScoin = exp / 10;
        if (Player.Data.Scoin < costScoin) return [];
        foreach (var cost in item.ItemList)
            await inventory.RemoveItem((int)cost.PileItem.ItemId, (int)cost.PileItem.ItemNum);
        await inventory.RemoveItem((int)VirtualItemEnum.Scoin, costScoin);

        var maxLevel = promotionConfig.MaxLevel;
        var curExp = avatarData.Exp;
        var curLevel = avatarData.Level;
        var startLevel = curLevel;
        var nextLevelExp = GameData.GetAvatarExpRequired(avatarConfig.ExpGroup, avatarData.Level);
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
                nextLevelExp = GameData.GetAvatarExpRequired(avatarConfig.ExpGroup, curLevel);
            }
        } while (exp > 0 && nextLevelExp > 0 && curLevel < maxLevel);

        avatarData.Level = curLevel;
        avatarData.Exp = curExp;
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
                    await inventory.PutItem(expItem.ItemID, 1);
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
        await Player.SendPacket(new PacketPlayerSyncScNotify(avatarData));
        if (curLevel > startLevel)
            await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.AnyAvatarLevelUp);
        return [.. list.Values];
    }

    public async ValueTask<bool> PromoteAvatar(int avatarId)
    {
        // Get avatar
        var avatarData = GetFormalAvatar(avatarId);
        if (avatarData == null) return false;

        GameData.AvatarConfigData.TryGetValue(avatarId, out var avatarConfig);
        if (avatarConfig == null ||
            avatarData.Promotion >= avatarConfig.MaxPromotion) return false;

        // Get promotion data
        var promotion =
            GameData.AvatarPromotionConfigData.Values.FirstOrDefault(x =>
                x.AvatarID == avatarId && x.Promotion == avatarData.Promotion)!;

        // Sanity check
        if (avatarData.Level < promotion.MaxLevel ||
            Player.Data.Level < promotion.PlayerLevelRequire ||
            Player.Data.WorldLevel < promotion.WorldLevelRequire) return false;

        // Pay items
        foreach (var cost in promotion.PromotionCostList)
            await Player.InventoryManager!.RemoveItem(cost.ItemID, cost.ItemNum);

        // Promote
        avatarData.Promotion += 1;

        // Send packets
        await Player.SendPacket(new PacketPlayerSyncScNotify(avatarData));
        return true;
    }

    /// <summary>
    ///     Claims the reward of a promotion level the avatar has already reached. Every check runs before the claim is
    ///     recorded; on success the paid items are returned.
    /// </summary>
    public async ValueTask<(Retcode Retcode, List<Proto.Item> Rewards)> TakePromotionReward(int baseAvatarId,
        int promotion)
    {
        var avatar = GetFormalAvatar(baseAvatarId);
        if (avatar == null) return (Retcode.RetAvatarNotExist, []);
        if (promotion is not (1 or 3 or 5)) return (Retcode.RetPromotionRewardConfigNotExist, []);
        if (promotion > avatar.Promotion) return (Retcode.RetAvatarPromotionNotMeet, []);
        if (avatar.HasTakenReward(promotion)) return (Retcode.RetPromotionRewardAlreadyTaken, []);

        avatar.TakeReward(promotion);
        MarkDirty();
        await Player.InventoryManager!.AddItem(PromotionRewardItemId, 1, false);
        await Player.SendPacket(new PacketPlayerSyncScNotify(avatar));
        return (Retcode.RetSucc, [new Proto.Item { ItemId = PromotionRewardItemId, Num = 1 }]);
    }

    private readonly SemaphoreSlim _avatarQuickUpLock = new(1, 1);

    public async ValueTask<(Retcode Retcode, List<ItemData> ReturnItems)> QuickLevelUpAvatar(EKLIDIDKFHC req)
    {
        await _avatarQuickUpLock.WaitAsync();
        try
        {
            return await ApplyAvatarQuickUp(req);
        }
        finally
        {
            _avatarQuickUpLock.Release();
        }
    }

    private async ValueTask<(Retcode, List<ItemData>)> ApplyAvatarQuickUp(EKLIDIDKFHC req)
    {
        var inventory = Player.InventoryManager!;
        var avatar = GetFormalAvatar((int)req.BaseAvatarId);
        if (avatar == null) return (Retcode.RetAvatarNotExist, []);
        if (!GameData.AvatarConfigData.TryGetValue(avatar.AvatarId, out var config))
            return (Retcode.RetItemConfigNotExist, []);

        // Capture: IGOOJKMPMDC=30 and KLMIBJIKIBB=1 produce level 30 / promotion 1.
        // BACAOLDADMC holds EXP books; OAINNDAHIMG holds the separate promotion costs.
        var level = req.IGOOJKMPMDC;
        var promotion = req.KLMIBJIKIBB;
        if (level is < 1 or > 80 || level < avatar.Level || promotion < avatar.Promotion ||
            promotion > config.MaxPromotion)
            return (Retcode.RetReqParaInvalid, []);
        if (level == avatar.Level && promotion == avatar.Promotion)
            return (Retcode.RetAvatarExpReachPromotionLimit, []); // Includes replayed successful requests.
        if (!GameData.AvatarPromotionConfigData.TryGetValue(avatar.AvatarId * 10 + (int)promotion,
                out var targetConfig))
            return (Retcode.RetItemConfigNotExist, []);
        if (level > targetConfig.MaxLevel) return (Retcode.RetAvatarExpReachPromotionLimit, []);

        var promotionCosts = new Dictionary<int, long>();
        for (var p = avatar.Promotion; p < promotion; p++)
        {
            if (!GameData.AvatarPromotionConfigData.TryGetValue(avatar.AvatarId * 10 + p, out var step))
                return (Retcode.RetItemConfigNotExist, []);
            if (level < step.MaxLevel || Player.Data.Level < step.PlayerLevelRequire ||
                Player.Data.WorldLevel < step.WorldLevelRequire)
                return (Retcode.RetAvatarPromotionNotMeet, []);
            foreach (var cost in step.PromotionCostList)
                promotionCosts[cost.ItemID] = promotionCosts.GetValueOrDefault(cost.ItemID) + cost.ItemNum;
        }
        if (!InventoryManager.ReadQuickUpCosts(req.OAINNDAHIMG, out var submittedPromotion) ||
            submittedPromotion.Count != promotionCosts.Count ||
            promotionCosts.Any(cost => submittedPromotion.GetValueOrDefault(cost.Key) != cost.Value))
            return (Retcode.RetItemNotInCostList, []);
        if (!InventoryManager.ReadQuickUpCosts(req.BACAOLDADMC, out var costs)) return (Retcode.RetReqParaInvalid, []);

        long providedExp = 0;
        foreach (var (itemId, count) in costs)
        {
            if (!GameData.AvatarExpItemConfigData.TryGetValue(itemId, out var book) || book.Exp <= 0 ||
                !GameData.ItemConfigData.TryGetValue(itemId, out var item) ||
                item.ItemMainType != ItemMainTypeEnum.Material)
                return (Retcode.RetAvatarExpItemNotExist, []);
            providedExp += book.Exp * count;
            if (providedExp > int.MaxValue) return (Retcode.RetReqParaInvalid, []);
        }
        long requiredExp = -avatar.Exp;
        for (var current = avatar.Level; current < level; current++)
        {
            var exp = GameData.GetAvatarExpRequired(config.ExpGroup, current);
            if (exp <= 0) return (Retcode.RetItemConfigNotExist, []);
            requiredExp += exp;
        }
        requiredExp = Math.Max(0, requiredExp);
        if (providedExp < requiredExp) return (Retcode.RetAvatarResExpNotEnough, []);
        foreach (var (itemId, count) in promotionCosts)
            costs[itemId] = costs.GetValueOrDefault(itemId) + count;
        costs[(int)VirtualItemEnum.Scoin] = costs.GetValueOrDefault((int)VirtualItemEnum.Scoin) + providedExp / 10;
        foreach (var (itemId, count) in costs)
        {
            if (count > int.MaxValue || !GameData.ItemConfigData.ContainsKey(itemId))
                return (Retcode.RetReqParaInvalid, []);
            if ((inventory.GetItem(itemId)?.Count ?? 0) < count)
                return (itemId == (int)VirtualItemEnum.Scoin ? Retcode.RetScoinNotEnough : Retcode.RetItemNotEnough,
                    []);
        }

        // Validate every cost before consuming anything. Publish the final avatar, currency and
        // material balances together before the quick-up response, matching the captured order.
        var changedItems = new Dictionary<int, ItemData>();
        foreach (var (itemId, count) in costs.Where(cost => cost.Value > 0))
        {
            var removed = await inventory.RemoveItem(itemId, (int)count, sync: false);
            if (itemId != (int)VirtualItemEnum.Scoin && removed != null) changedItems[itemId] = removed;
        }
        var returns = new List<ItemData>();
        var leftover = providedExp - requiredExp;
        foreach (var book in GameData.AvatarExpItemConfigData.Values.Where(book => book.Exp > 0)
                     .OrderByDescending(book => book.Exp).ThenBy(book => book.ItemID))
        {
            if (!GameData.ItemConfigData.TryGetValue(book.ItemID, out var item) ||
                item.ItemMainType != ItemMainTypeEnum.Material) continue;
            var count = (int)(leftover / book.Exp);
            if (count == 0) continue;
            changedItems[book.ItemID] = await inventory.PutItem(book.ItemID, count);
            returns.Add(new ItemData { ItemId = book.ItemID, Count = count });
            leftover %= book.Exp;
        }
        var leveled = level > avatar.Level;
        avatar.Level = (int)level;
        avatar.Promotion = (int)promotion;
        // A promotion-only quick up retains existing EXP; level-targeted picks refund surplus books.
        if (leveled) avatar.Exp = 0;
        MarkDirty();
        var sync = new List<BaseSyncData> { new AvatarSyncData(avatar), new BasicInfoSyncData(Player.ToProto()) };
        sync.AddRange(changedItems.Values.Select(item => (BaseSyncData)new ItemSyncData(item)));
        await Player.SendPacket(new PacketPlayerSyncScNotify(sync));
        if (leveled) await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.AnyAvatarLevelUp);
        return (Retcode.RetSucc, returns);
    }

    private readonly object _exRankActivationGate = new();
    private static readonly Logger ExRankLogger = new("AvatarExRank");

    /// <summary>
    /// Client CmdID 400 carries only an ExRank config ID (field 5). Resolve owner and cost server-side.
    /// Normal rank/enhancement and the historical avatar MemoryPack blob are left unchanged.
    /// </summary>
    public async ValueTask<Retcode> ActivateExRank(int exRankId)
    {
        ItemData material;
        FormalAvatarInfo owned;
        lock (_exRankActivationGate)
        {
            if (!AvatarExRankMaterials.Definitions.TryGetValue(exRankId, out var definition))
                return Retcode.RetAvatarRankNotMatch;
            var inventory = Player.InventoryManager!.Data;
            owned = GetFormalAvatar(definition.AvatarId)!;
            if (owned == null || owned.GetPathInfo(definition.AvatarId) == null)
                return Retcode.RetAvatarNotExist;
            var ranks = Data.ExRankIds;
            var hadEntry = ranks.TryGetValue(owned.BaseAvatarId, out var previousIds);
            if (previousIds?.Contains(exRankId) == true) return Retcode.RetAvatarRankReachMax;

            material = inventory.MaterialItems.Find(item => item.ItemId == definition.MaterialId)!;
            if (material == null || material.Count < definition.MaterialCount) return Retcode.RetItemCostNotEnough;
            var previousCount = material.Count;
            var previousIndex = inventory.MaterialItems.IndexOf(material);
            var activated = new List<int>(previousIds ?? []) { exRankId };
            try
            {
                DatabaseHelper.UpdateInstancesAtomically(Player.Uid, () =>
                {
                    material.Count -= definition.MaterialCount;
                    if (material.Count == 0) inventory.MaterialItems.Remove(material);
                    ranks[owned.BaseAvatarId] = activated;
                }, () =>
                {
                    material.Count = previousCount;
                    if (!inventory.MaterialItems.Contains(material))
                        inventory.MaterialItems.Insert(previousIndex, material);
                    if (hadEntry) ranks[owned.BaseAvatarId] = previousIds!;
                    else ranks.Remove(owned.BaseAvatarId);
                }, inventory, Data);
            }
            catch (Exception error)
            {
                ExRankLogger.Error(I18NManager.Translate("Server.Log.ExRankActivateFailed", exRankId.ToString(), Player.Uid.ToString()), error);
                return Retcode.RetSystemBusy;
            }
            // Bind the newly committed sidecar before building PlayerSync's path field 8.
            owned = GetFormalAvatar(definition.AvatarId)!;
        }
        // One notify contains both the consumed material (including zero) and activated avatar path.
        await Player.SendPacket(new PacketPlayerSyncScNotify(new ItemSyncData(material), new AvatarSyncData(owned)));
        return Retcode.RetSucc;
    }

    public async ValueTask RankUpAvatar(int avatarId, ItemCostData costData)
    {
        // ExRank materials must never be consumed through the ordinary star-rank protocol.
        if (costData.ItemList.Any(cost =>
                cost.PileItem != null && AvatarExRankMaterials.IsMaterial((int)cost.PileItem.ItemId)))
            return;
        foreach (var cost in costData.ItemList)
            await Player.InventoryManager!.RemoveItem((int)cost.PileItem.ItemId, (int)cost.PileItem.ItemNum);
        var baseAvatarId = avatarId;
        GameData.MultiplePathAvatarConfigData.TryGetValue(baseAvatarId, out var avatar);
        if (avatar != null) baseAvatarId = avatar.BaseAvatarID;
        var avatarData = GetFormalAvatar(baseAvatarId);
        if (avatarData == null) return;
        avatarData.GetCurPathInfo().Rank++;
        await Player.SendPacket(new PacketPlayerSyncScNotify(avatarData));
    }
}
