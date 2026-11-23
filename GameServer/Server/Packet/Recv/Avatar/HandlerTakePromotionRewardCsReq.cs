using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Avatar;

[Opcode(CmdIds.TakePromotionRewardCsReq)]
public class HandlerTakePromotionRewardCsReq : Handler<TakePromotionRewardCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, TakePromotionRewardCsReq req)
    {
        var (retcode, rewards) =
            await player.AvatarManager!.TakePromotionReward((int)req.BaseAvatarId, (int)req.Promotion);
        await connection.SendPacket(new PacketTakePromotionRewardScRsp(retcode, rewards));
    }
}
