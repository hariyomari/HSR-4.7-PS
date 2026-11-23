using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Avatar;

[Opcode(CmdIds.DressAvatarSkinCsReq)]
public class HandlerDressAvatarSkinCsReq : Handler<DressAvatarSkinCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, DressAvatarSkinCsReq req)
    {
        var retcode = await player.AvatarManager!.ChangeSkin((int)req.AvatarId, (int)req.SkinId);
        await connection.SendPacket(new PacketDressAvatarSkinScRsp(retcode));
    }
}
