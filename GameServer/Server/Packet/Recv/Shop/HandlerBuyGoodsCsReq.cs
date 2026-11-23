using March7thHoney.GameServer.Game.Shop;
using March7thHoney.GameServer.Server.Packet.Send.Shop;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Shop;

[Opcode(CmdIds.BuyGoodsCsReq)]
public class HandlerBuyGoodsCsReq : Handler<BuyGoodsCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, BuyGoodsCsReq req)
    {
        var result = req.ShopId > int.MaxValue || req.GoodsId > int.MaxValue
            ? new ShopBuyResult(Retcode.RetFail, [], 0)
            : await player.ShopManager!.BuyItem((int)req.ShopId, (int)req.GoodsId, req.GoodsNum);

        await connection.SendPacket(new PacketBuyGoodsScRsp(req, result));
    }
}
