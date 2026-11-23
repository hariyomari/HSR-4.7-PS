using March7thHoney.Database.Inventory;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Shop;

public class PacketBuyGoodsScRsp : BasePacket
{
    public PacketBuyGoodsScRsp(BuyGoodsCsReq req, List<ItemData> items, Retcode retcode = Retcode.RetSucc)
        : base(CmdIds.BuyGoodsScRsp)
    {
        var proto = new BuyGoodsScRsp
        {
            Retcode = (uint)retcode,
            ShopId = req.ShopId,
            GoodsBuyTimes = retcode == Retcode.RetSucc ? req.GoodsNum : 0,
            GoodsId = req.GoodsId,
            ReturnItemList = new ItemList
            {
                ItemList_ = { items.Select(item => item.ToProto()) }
            }
        };

        SetData(proto);
    }
}
