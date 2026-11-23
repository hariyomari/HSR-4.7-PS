using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Shop;

public class PacketGetShopListScRsp : BasePacket
{
    public PacketGetShopListScRsp(uint shopType, IEnumerable<Proto.Shop> shops) : base(CmdIds.GetShopListScRsp)
    {
        var proto = new GetShopListScRsp
        {
            ShopType = shopType,
            ShopList = { shops }
        };

        SetData(proto);
    }
}
