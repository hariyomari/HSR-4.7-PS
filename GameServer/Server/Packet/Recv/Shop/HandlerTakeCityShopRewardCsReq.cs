using March7thHoney.GameServer.Server.Packet.Send.Shop;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Shop;

[Opcode(CmdIds.TakeCityShopRewardCsReq)]
public class HandlerTakeCityShopRewardCsReq : Handler<TakeCityShopRewardCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, TakeCityShopRewardCsReq req)
    {
        var (retcode, rewards) = req.ShopId > int.MaxValue || req.Level > int.MaxValue
            ? (Retcode.RetFail, [])
            : await player.ShopManager!.TakeCityShopReward((int)req.ShopId, (int)req.Level);

        await connection.SendPacket(new PacketTakeCityShopRewardScRsp(req.ShopId, req.Level, retcode, rewards));
    }
}
