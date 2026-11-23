using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Avatar;

[Opcode(CmdIds.TakeOffAvatarSkinCsReq)]
public class HandlerTakeOffAvatarSkinCsReq : Handler<TakeOffAvatarSkinCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, TakeOffAvatarSkinCsReq req)
    {
        var retcode = await player.AvatarManager!.ChangeSkin((int)req.AvatarId, 0);
        await connection.SendPacket(new PacketTakeOffAvatarSkinScRsp(retcode));
    }
}
