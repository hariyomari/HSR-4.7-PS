using March7thHoney.Data;
using March7thHoney.Data.Avatar;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Pet;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager(PlayerInstance player) : BasePlayerManager<InventoryData>(player)
{
    public int GetAvailableGrantCount(int itemId, int requested)
    {
        if (!AvatarExRankMaterials.IsMaterial(itemId)) return requested;
        var owned = Math.Max(0, Data.MaterialItems.Find(item => item.ItemId == itemId)?.Count ?? 0);
        return Math.Min(Math.Max(requested, 0), Math.Max(0, GameData.ItemConfigData[itemId].PileLimit - owned));
    }

    public async ValueTask AddItem(ItemData itemData, bool notify = true)
    {
        var grantedCount = GetAvailableGrantCount(itemData.ItemId, itemData.Count);
        if (AvatarExRankMaterials.IsMaterial(itemData.ItemId) && grantedCount == 0) return;
        var storedItem = await PutItem(itemData.ItemId, grantedCount,
            itemData.Rank, itemData.Promotion,
            itemData.Level, itemData.Exp, itemData.TotalExp,
            itemData.MainAffix, itemData.SubAffixes,
            itemData.ReforgeSubAffixes, itemData.UniqueId);
        itemData.Locked = storedItem.Locked;
        if (AvatarExRankMaterials.IsMaterial(itemData.ItemId)) itemData.Count = storedItem.Count;

        await Player.SendPacket(new PacketPlayerSyncScNotify(itemData));
        if (notify) await Player.SendPacket(new PacketScenePlaneEventScNotify(itemData));
    }

    public async ValueTask AddItems(List<ItemData> items, bool notify = true)
    {
        var syncItems = new List<ItemData>();
        var notifiedItems = new List<ItemData>();
        foreach (var item in items)
        {
            var grantedCount = GetAvailableGrantCount(item.ItemId, item.Count);
            var i = await AddItem(item.ItemId, grantedCount, false, sync: false, returnRaw: true);
            if (i != null)
            {
                syncItems.Add(i);
                var granted = item.Clone();
                if (AvatarExRankMaterials.IsMaterial(item.ItemId)) granted.Count = grantedCount;
                notifiedItems.Add(granted);
            }
        }

        if (syncItems.Count == 0) return;
        await Player.SendPacket(new PacketPlayerSyncScNotify(syncItems));
        if (notify) await Player.SendPacket(new PacketScenePlaneEventScNotify(notifiedItems));
    }

    public async ValueTask<ItemData?> AddItem(int itemId, int count, bool notify = true, int rank = 1, int level = 1,
        int promotion = 0, bool sync = true, bool returnRaw = false)
    {
        GameData.ItemConfigData.TryGetValue(itemId, out var itemConfig);
        if (itemConfig == null) return null;
        count = GetAvailableGrantCount(itemId, count);
        if (AvatarExRankMaterials.IsMaterial(itemId) && count == 0) return null;

        ItemData? itemData = null;

        switch (itemConfig.ItemMainType)
        {
            case ItemMainTypeEnum.Equipment:
                itemData = await AddEquipmentItem(itemId, rank, level, promotion, itemConfig);
                break;
            case ItemMainTypeEnum.Usable:
                itemData = await AddUsableItem(itemId, count, itemConfig);
                break;
            case ItemMainTypeEnum.Relic:
                itemData = await AddRelicItem(itemId, level, sync);
                break;
            case ItemMainTypeEnum.Virtual:
                itemData = await AddVirtualItem(itemId, count, itemConfig);
                break;
            case ItemMainTypeEnum.AvatarCard:
                itemData = await AddAvatarCardItem(itemId, sync, notify);
                break;
            case ItemMainTypeEnum.Mission:
                itemData = await PutItem(itemId, count);
                break;
            default:
                itemData = await PutItem(itemId, Math.Min(count, itemConfig.PileLimit));
                break;
        }

        ItemData? clone = null;
        if (itemData == null) return returnRaw ? itemData : clone ?? itemData;

        clone = itemData.Clone();
        if (sync && itemConfig.ItemMainType != ItemMainTypeEnum.Virtual)
            await Player.SendPacket(new PacketPlayerSyncScNotify(itemData));
        clone.Count = count;
        if (notify) await Player.SendPacket(new PacketScenePlaneEventScNotify(clone));

        Player.MissionManager?.HandleFinishType(MissionFinishTypeEnum.GetItem, itemData.ToProto());

        return returnRaw ? itemData : clone;
    }

    public async ValueTask<ItemData> PutItem(int itemId, int count, int rank = 0, int promotion = 0, int level = 0,
        int exp = 0, int totalExp = 0, int mainAffix = 0, List<ItemSubAffix>? subAffixes = null,
        List<ItemSubAffix>? regorgeSubAffixes = null, int uniqueId = 0)
    {
        count = GetAvailableGrantCount(itemId, count);
        if (AvatarExRankMaterials.IsMaterial(itemId) && count == 0)
            return Data.MaterialItems.Find(item => item.ItemId == itemId) ?? new ItemData { ItemId = itemId, Count = 0 };
        if (promotion == 0 && level > 10) promotion = GameData.GetMinPromotionForLevel(level);
        var item = new ItemData
        {
            ItemId = itemId,
            Count = count,
            Rank = rank,
            Promotion = promotion,
            Level = level,
            Exp = exp,
            TotalExp = totalExp,
            MainAffix = mainAffix,
            SubAffixes = subAffixes ?? [],
            ReforgeSubAffixes = regorgeSubAffixes ?? []
        };

        if (uniqueId > 0) item.UniqueId = uniqueId;

        switch (GameData.ItemConfigData[itemId].ItemMainType)
        {
            case ItemMainTypeEnum.Material:
            case ItemMainTypeEnum.Pet:
            case ItemMainTypeEnum.Virtual:
            case ItemMainTypeEnum.Usable:
            case ItemMainTypeEnum.Mission:
                var oldItem = Data.MaterialItems.Find(x => x.ItemId == itemId);
                if (oldItem != null)
                {
                    oldItem.Count += count;
                    item = oldItem;
                    break;
                }

                Data.MaterialItems.Add(item);
                break;
            case ItemMainTypeEnum.Equipment:
                if (Data.EquipmentItems.Count + 1 > GameConstants.INVENTORY_MAX_EQUIPMENT)
                {
                    await Player.SendPacket(new PacketRetcodeNotify(Retcode.RetEquipmentExceedLimit));
                    return item;
                }

                Data.EquipmentItems.Add(item);
                break;
            case ItemMainTypeEnum.Relic:
                if (Data.RelicItems.Count + 1 > GameConstants.INVENTORY_MAX_RELIC)
                {
                    await Player.SendPacket(new PacketRetcodeNotify(Retcode.RetRelicExceedLimit));
                    return item;
                }

                Data.RelicItems.Add(item);
                break;
        }

        MarkDirty();
        if (GameData.ItemConfigData[itemId].ItemMainType == ItemMainTypeEnum.Pet)
            await NotifyPetItemsChanged();
        return item;
    }

    /// <summary>
    ///     After pet materials change, has the pet instances reconciled and PetDataChangeScNotify pushed for the new
    ///     instances (a no-op before the component is registered).
    /// </summary>
    private async ValueTask NotifyPetItemsChanged()
    {
        var pets = Player.GetComponentOrDefault<PetManager>();
        if (pets != null) await pets.OnPetItemsChanged();
    }

    public async ValueTask<List<ItemData>> RemoveItems(List<(int itemId, int count, int uniqueId)> items,
        bool sync = true)
    {
        List<ItemData> removedItems = [];
        foreach (var item in items)
        {
            var removedItem = await RemoveItem(item.itemId, item.count, item.uniqueId, false);
            if (removedItem != null) removedItems.Add(removedItem);
        }

        if (sync && removedItems.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(removedItems));
        return removedItems;
    }

    /// <summary>
    ///     How many of a stackable item or removable currency the player holds; 0 for anything RemoveItem cannot take
    ///     by count (equipment, relics, unknown items, non-removable virtual items).
    /// </summary>
    public long GetRemovableCount(int itemId)
    {
        GameData.ItemConfigData.TryGetValue(itemId, out var itemConfig);
        if (itemConfig == null) return 0;
        long owned = itemConfig.ItemMainType switch
        {
            ItemMainTypeEnum.Material or ItemMainTypeEnum.Pet or ItemMainTypeEnum.Mission or ItemMainTypeEnum.Usable =>
                Data.MaterialItems.Find(x => x.ItemId == itemId)?.Count ?? 0,
            ItemMainTypeEnum.Virtual => (VirtualItemEnum)itemConfig.ID switch
            {
                VirtualItemEnum.Hcoin => Player.Data.Hcoin,
                VirtualItemEnum.Scoin => Player.Data.Scoin,
                VirtualItemEnum.Mcoin => Player.Data.Mcoin,
                VirtualItemEnum.TalentPoint => Player.Data.TalentPoints,
                _ => 0
            },
            _ => 0
        };
        return Math.Max(0, owned);
    }

    /// <summary>
    ///     Merges the costs per item and checks every one of them: null when any amount is not positive, a total does
    ///     not fit an int, or the player holds less than the total.
    /// </summary>
    private Dictionary<int, int>? ResolveAffordableCosts(IEnumerable<(int itemId, long count)> costs)
    {
        var totals = new Dictionary<int, long>();
        foreach (var (itemId, count) in costs)
        {
            if (count <= 0 || count > int.MaxValue) return null;
            var total = totals.GetValueOrDefault(itemId) + count;
            if (total > int.MaxValue) return null;
            totals[itemId] = total;
        }

        if (totals.Any(cost => GetRemovableCount(cost.Key) < cost.Value)) return null;
        return totals.ToDictionary(cost => cost.Key, cost => (int)cost.Value);
    }

    public bool CanAfford(IEnumerable<(int itemId, long count)> costs)
    {
        return ResolveAffordableCosts(costs) != null;
    }

    /// <summary>
    ///     All-or-nothing removal of stackable items and currencies: nothing is removed unless every cost is positive
    ///     and affordable. Returns false when the costs were rejected.
    /// </summary>
    public async ValueTask<bool> TryRemoveItems(IEnumerable<(int itemId, long count)> costs, bool sync = true)
    {
        var resolved = ResolveAffordableCosts(costs);
        if (resolved == null) return false;

        List<ItemData> removedItems = [];
        var currencyChanged = false;
        foreach (var (itemId, count) in resolved)
        {
            var removed = await RemoveItem(itemId, count, sync: false);
            if (removed == null) continue;
            if (GameData.ItemConfigData[itemId].ItemMainType == ItemMainTypeEnum.Virtual) currencyChanged = true;
            else removedItems.Add(removed);
        }

        if (!sync) return true;
        if (removedItems.Count > 0) await Player.SendPacket(new PacketPlayerSyncScNotify(removedItems));
        if (currencyChanged) await Player.SendPacket(new PacketPlayerSyncScNotify(Player.ToProto()));
        return true;
    }

    /// <summary>
    ///     Removes up to <paramref name="count" /> of a stackable item (the stack is emptied when it holds less), the
    ///     exact amount of a currency, or the equipment/relic with <paramref name="uniqueId" />. Returns null and changes
    ///     nothing for a non-positive count, a missing item or an unaffordable currency amount. Use TryRemoveItems for
    ///     costs that must be paid in full.
    /// </summary>
    public async ValueTask<ItemData?> RemoveItem(int itemId, int count, int uniqueId = 0, bool sync = true)
    {
        GameData.ItemConfigData.TryGetValue(itemId, out var itemConfig);
        if (itemConfig == null) return null;
        if (count <= 0 && itemConfig.ItemMainType is not (ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic))
            return null;

        ItemData? itemData = null;

        switch (itemConfig.ItemMainType)
        {
            case ItemMainTypeEnum.Material:
            case ItemMainTypeEnum.Pet:
            case ItemMainTypeEnum.Mission:
            case ItemMainTypeEnum.Usable:
                var item = Data.MaterialItems.Find(x => x.ItemId == itemId);
                if (item == null) return null;
                item.Count -= count;
                if (item.Count <= 0)
                {
                    Data.MaterialItems.Remove(item);
                    item.Count = 0;
                }

                itemData = item;
                if (itemConfig.ItemMainType == ItemMainTypeEnum.Pet)
                    Player.GetComponentOrDefault<PetManager>()?.Reconcile();
                break;
            case ItemMainTypeEnum.Virtual:
                if (GetRemovableCount(itemId) < count) return null;
                switch ((VirtualItemEnum)itemConfig.ID)
                {
                    case VirtualItemEnum.Hcoin:
                        Player.Data.Hcoin -= count;
                        itemData = new ItemData { ItemId = itemId, Count = count };
                        break;
                    case VirtualItemEnum.Scoin:
                        Player.Data.Scoin -= count;
                        itemData = new ItemData { ItemId = itemId, Count = count };
                        break;
                    case VirtualItemEnum.Mcoin:
                        Player.Data.Mcoin -= count;
                        itemData = new ItemData { ItemId = itemId, Count = count };
                        break;
                    case VirtualItemEnum.TalentPoint:
                        Player.Data.TalentPoints -= count;
                        itemData = new ItemData { ItemId = itemId, Count = count };
                        break;
                }

                if (sync && itemData != null) await Player.SendPacket(new PacketPlayerSyncScNotify(Player.ToProto()));
                break;
            case ItemMainTypeEnum.Equipment:
                var equipment = Data.EquipmentItems.Find(x => x.UniqueId == uniqueId);
                if (equipment == null) return null;
                Data.EquipmentItems.Remove(equipment);
                equipment.Count = 0;
                itemData = equipment;
                break;
            case ItemMainTypeEnum.Relic:
                var relic = Data.RelicItems.Find(x => x.UniqueId == uniqueId);
                if (relic == null) return null;
                Data.RelicItems.Remove(relic);
                relic.Count = 0;
                itemData = relic;
                break;
        }

        if (itemData != null && sync && itemConfig.ItemMainType != ItemMainTypeEnum.Virtual)
            await Player.SendPacket(new PacketPlayerSyncScNotify(itemData));

        Player.MissionManager?.HandleFinishType(MissionFinishTypeEnum.UseItem, new ItemData
        {
            ItemId = itemId,
            Count = count
        });

        MarkDirty();
        return itemData;
    }

    /// <summary>
    ///     Get item by itemId and uniqueId, if uniqueId provided, itemId will be ignored
    /// </summary>
    /// <param name="itemId"></param>
    /// <param name="uniqueId"></param>
    /// <returns></returns>
    public ItemData? GetItem(int itemId, int uniqueId = 0, ItemMainTypeEnum mainType = ItemMainTypeEnum.Unknown)
    {
        GameData.ItemConfigData.TryGetValue(itemId, out var itemConfig);
        if (itemConfig == null && mainType == ItemMainTypeEnum.Unknown) return null;
        if (itemConfig != null)
            mainType = itemConfig.ItemMainType;
        switch (mainType)
        {
            case ItemMainTypeEnum.Material:
            case ItemMainTypeEnum.Pet:
            case ItemMainTypeEnum.Usable:
                return Data.MaterialItems.Find(x => x.ItemId == itemId);
            case ItemMainTypeEnum.Equipment:
                return uniqueId > 0
                    ? Data.EquipmentItems.Find(x => x.UniqueId == uniqueId)
                    : Data.EquipmentItems.Find(x => x.ItemId == itemId);
            case ItemMainTypeEnum.Relic:
                return uniqueId > 0
                    ? Data.RelicItems.Find(x => x.UniqueId == uniqueId)
                    : Data.RelicItems.Find(x => x.ItemId == itemId);
            case ItemMainTypeEnum.Virtual:
                var balance = GetVirtualItemCount(itemConfig?.ID ?? 0);
                return balance == null ? null : new ItemData { ItemId = itemId, Count = balance.Value };
        }

        return null;
    }
}
