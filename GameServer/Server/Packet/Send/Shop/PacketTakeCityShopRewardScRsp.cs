using March7thHoney.Database.Inventory;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Shop;

public class PacketTakeCityShopRewardScRsp : BasePacket
{
    public PacketTakeCityShopRewardScRsp(uint shopId, uint level, Retcode retcode, List<ItemData> rewards)
        : base(CmdIds.TakeCityShopRewardScRsp)
    {
        SetData(new TakeCityShopRewardScRsp
        {
            Retcode = (uint)retcode,
            ShopId = shopId,
            Level = level,
            Reward = new ItemList
            {
                ItemList_ = { rewards.Select(item => item.ToProto()) }
            }
        });
    }
}
