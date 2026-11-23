using March7thHoney.GameServer.Game.Shop;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Shop;

public class PacketBuyGoodsScRsp : BasePacket
{
    public PacketBuyGoodsScRsp(BuyGoodsCsReq req, ShopBuyResult result) : base(CmdIds.BuyGoodsScRsp)
    {
        var proto = new BuyGoodsScRsp
        {
            Retcode = (uint)result.Retcode,
            ShopId = req.ShopId,
            GoodsBuyTimes = result.GoodsBuyTimes,
            GoodsId = req.GoodsId,
            ReturnItemList = new ItemList
            {
                ItemList_ = { result.Items.Select(item => item.ToProto()) }
            }
        };

        SetData(proto);
    }
}
