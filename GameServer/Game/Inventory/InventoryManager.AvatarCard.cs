using March7thHoney.Database.Inventory;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager
{
    private async ValueTask<ItemData?> AddAvatarCardItem(int itemId, bool sync, bool notify)
    {
        // add avatar
        var avatar = Player.AvatarManager?.GetFormalAvatar(itemId);
        if (avatar != null)
        {
            var rankUpItem = Player.InventoryManager!.GetItem(itemId + 10000);
            var currentRank = avatar.PathInfos.TryGetValue(itemId, out var pathInfo) ? pathInfo.Rank : 0;
            if (currentRank + (rankUpItem?.Count ?? 0) <= 5)
                return await PutItem(itemId + 10000, 1);
            return null;
        }

        await Player.AvatarManager!.AddAvatar(itemId, sync, notify);
        await AddItem(itemId + 200000, 1, false);
        return null;
    }
}
