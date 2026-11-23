using March7thHoney.Data;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Item;

public class PacketGetBagScRsp : BasePacket
{
    public PacketGetBagScRsp(PlayerInstance player) : base(CmdIds.GetBagScRsp)
    {
        var proto = new GetBagScRsp();

        player.InventoryManager!.Data.MaterialItems.ForEach(item =>
        {
            // Pet items are sent only as instances in GetPetDataScRsp; see ItemSyncData.
            if (GameData.ItemConfigData.TryGetValue(item.ItemId, out var config) &&
                config.ItemMainType == ItemMainTypeEnum.Pet) return;
            proto.MaterialList.Add(item.ToMaterialProto());
        });

        player.InventoryManager.Data.RelicItems.ForEach(item => { proto.RelicList.Add(item.ToRelicProto()); });

        player.InventoryManager.Data.EquipmentItems.ForEach(item =>
        {
            proto.EquipmentList.Add(item.ToEquipmentProto());
        });

        SetData(proto);
    }
}
