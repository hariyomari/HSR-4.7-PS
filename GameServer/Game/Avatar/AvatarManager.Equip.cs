using March7thHoney.GameServer.Game.Sync;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;

namespace March7thHoney.GameServer.Game.Avatar;

/// <summary>Dressing an avatar's light cone and relics.</summary>
public partial class AvatarManager
{
    public async ValueTask EquipAvatar(int avatarId, int equipmentUniqueId)
    {
        var inventory = Player.InventoryManager!.Data;
        var itemData = inventory.EquipmentItems.Find(x => x.UniqueId == equipmentUniqueId);
        var avatarData = GetFormalAvatar(avatarId);
        if (itemData == null || avatarData == null) return;
        var oldItem = inventory.EquipmentItems.Find(x => x.UniqueId == avatarData.PathInfos[avatarId].EquipId);
        if (itemData.EquipAvatar > 0) // already be dressed
        {
            var equipAvatarId = itemData.EquipAvatar;
            var equipAvatar = GetFormalAvatar(equipAvatarId);
            if (equipAvatar != null && oldItem != null)
            {
                // switch
                equipAvatar.PathInfos[equipAvatarId].EquipId = oldItem.UniqueId;
                oldItem.EquipAvatar = equipAvatar.AvatarId;
                await Player.SendPacket(new PacketPlayerSyncScNotify(new AvatarSyncData(equipAvatar),
                    new ItemSyncData(oldItem)));
            }
            else if (equipAvatar != null && oldItem == null)
            {
                equipAvatar.PathInfos[equipAvatarId].EquipId = 0;
                await Player.SendPacket(new PacketPlayerSyncScNotify(equipAvatar));
            }
        }
        else
        {
            if (oldItem != null)
            {
                oldItem.EquipAvatar = 0;
                await Player.SendPacket(new PacketPlayerSyncScNotify(oldItem));
            }
        }

        itemData.EquipAvatar = avatarData.AvatarId;
        avatarData.PathInfos[avatarId].EquipId = itemData.UniqueId;
        await Player.SendPacket(new PacketPlayerSyncScNotify(new AvatarSyncData(avatarData),
            new ItemSyncData(itemData)));
    }

    public async ValueTask EquipRelic(int avatarId, int relicUniqueId, int slot)
    {
        var inventory = Player.InventoryManager!.Data;
        var itemData = inventory.RelicItems.Find(x => x.UniqueId == relicUniqueId);
        var avatarData = GetFormalAvatar(avatarId);
        if (itemData == null || avatarData == null) return;
        avatarData.PathInfos[avatarId].Relic.TryGetValue(slot, out var id);
        var oldItem = inventory.RelicItems.Find(x => x.UniqueId == id);

        if (itemData.EquipAvatar > 0) // already be dressed
        {
            var equipAvatarId = itemData.EquipAvatar;
            var equipAvatar = GetFormalAvatar(equipAvatarId);
            if (equipAvatar != null && oldItem != null)
            {
                // switch
                equipAvatar.PathInfos[equipAvatarId].Relic[slot] = oldItem.UniqueId;
                oldItem.EquipAvatar = equipAvatar.AvatarId;
                await Player.SendPacket(new PacketPlayerSyncScNotify(new AvatarSyncData(equipAvatar),
                    new ItemSyncData(oldItem)));
            }
            else if (equipAvatar != null && oldItem == null)
            {
                equipAvatar.PathInfos[equipAvatarId].Relic[slot] = 0;
                await Player.SendPacket(new PacketPlayerSyncScNotify(equipAvatar));
            }
        }
        else
        {
            if (oldItem != null)
            {
                oldItem.EquipAvatar = 0;
                await Player.SendPacket(new PacketPlayerSyncScNotify(oldItem));
            }
        }

        itemData.EquipAvatar = avatarData.AvatarId;
        avatarData.PathInfos[avatarId].Relic[slot] = itemData.UniqueId;
        // save
        await Player.SendPacket(new PacketPlayerSyncScNotify(new AvatarSyncData(avatarData),
            new ItemSyncData(itemData)));
    }

    public async ValueTask UnequipRelic(int avatarId, int slot)
    {
        var avatarData = GetFormalAvatar(avatarId);
        if (avatarData == null) return;
        var pathInfo = avatarData.PathInfos[avatarId];
        pathInfo.Relic.TryGetValue(slot, out var uniqueId);
        var itemData = Player.InventoryManager!.Data.RelicItems.Find(x => x.UniqueId == uniqueId);
        if (itemData == null) return;
        pathInfo.Relic.Remove(slot);
        itemData.EquipAvatar = 0;
        await Player.SendPacket(new PacketPlayerSyncScNotify(new AvatarSyncData(avatarData),
            new ItemSyncData(itemData)));
    }

    public async ValueTask UnequipEquipment(int avatarId)
    {
        var avatarData = GetFormalAvatar(avatarId);
        if (avatarData == null) return;
        var pathInfo = avatarData.PathInfos[avatarId];
        var itemData = Player.InventoryManager!.Data.EquipmentItems.Find(x => x.UniqueId == pathInfo.EquipId);
        if (itemData == null) return;
        itemData.EquipAvatar = 0;
        pathInfo.EquipId = 0;
        await Player.SendPacket(new PacketPlayerSyncScNotify(new AvatarSyncData(avatarData),
            new ItemSyncData(itemData)));
    }
}
