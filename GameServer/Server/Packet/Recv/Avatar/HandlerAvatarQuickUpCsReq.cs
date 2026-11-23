using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Avatar;

// 4.6 registry/capture seq 36813-36829: KELLLHMHFHL is EKLIDIDKFHC, paired with MILFFHPMLMN/PMEMIDKDAFG.
// The obfuscated identities remain the same in 4.7; its active command ids and field numbers differ.
[Opcode(CmdIds.KELLLHMHFHL)]
public class HandlerAvatarQuickUpCsReq : Handler<EKLIDIDKFHC>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, EKLIDIDKFHC req)
    {
        var (retcode, items) = await player.AvatarManager!.QuickLevelUpAvatar(req);
        await connection.SendPacket(new PacketAvatarQuickUpScRsp(retcode, items));
    }
}
