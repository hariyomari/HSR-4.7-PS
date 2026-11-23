using March7thHoney.Data.Excel;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.DailyActive;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;

namespace March7thHoney.GameServer.Game.Inventory;

public partial class InventoryManager
{
    private async ValueTask<ItemData?> AddVirtualItem(int itemId, int count, ItemConfigExcel itemConfig)
    {
        var levelBefore = Player.Data.Level;
        switch ((VirtualItemEnum)itemConfig.ID)
        {
            case VirtualItemEnum.Hcoin:
                Player.Data.Hcoin += count;
                break;
            case VirtualItemEnum.Scoin:
                Player.Data.Scoin += count;
                break;
            case VirtualItemEnum.Mcoin:
                Player.Data.Mcoin += count;
                break;
            case VirtualItemEnum.Stamina:
                Player.Data.Stamina += count;
                break;
            case VirtualItemEnum.PlayerExp:
                Player.Data.Exp += count;
                Player.OnAddExp();
                break;
            case VirtualItemEnum.TalentPoint:
                Player.Data.TalentPoints += count;
                break;
            case VirtualItemEnum.DailyActivePoint:
                // Daily active points: today's window, not a stored item.
                Player.GetComponentOrDefault<DailyActiveManager>()?.AddPoint(count);
                break;
        }

        // Read before the level-up handling below, which can roll the daily window.
        var actualCount = GetVirtualItemCount(itemConfig.ID) ?? 0;

        if (count != 0)
        {
            await Player.SendPacket(new PacketPlayerSyncScNotify(Player.ToProto()));
            if (Player.Data.Level > levelBefore) await Player.OnLevelUp();
            return new ItemData
            {
                ItemId = itemId,
                Count = actualCount
            };
        }

        return null;
    }

    /// <summary>The balance of a player-counter virtual item, or null when the ID is not one.</summary>
    private int? GetVirtualItemCount(int itemId)
    {
        return (VirtualItemEnum)itemId switch
        {
            VirtualItemEnum.Hcoin => Player.Data.Hcoin,
            VirtualItemEnum.Scoin => Player.Data.Scoin,
            VirtualItemEnum.Mcoin => Player.Data.Mcoin,
            VirtualItemEnum.Stamina => Player.Data.Stamina,
            VirtualItemEnum.PlayerExp => Player.Data.Exp,
            VirtualItemEnum.TalentPoint => Player.Data.TalentPoints,
            VirtualItemEnum.DailyActivePoint => Player.GetComponentOrDefault<DailyActiveManager>()?.Point ?? 0,
            _ => null
        };
    }
}
