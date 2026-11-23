using March7thHoney.Database.Shop;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Shop;

public class PacketCityShopInfoScNotify : BasePacket
{
    public PacketCityShopInfoScNotify(uint shopId, CityShopProgress progress) : base(CmdIds.CityShopInfoScNotify)
    {
        SetData(new CityShopInfoScNotify
        {
            ShopId = shopId,
            Level = (uint)Math.Max(progress.Level, 1),
            Exp = (uint)Math.Max(progress.Exp, 0),
            TakenLevelReward = progress.ToTakenLevelRewardMask()
        });
    }
}
