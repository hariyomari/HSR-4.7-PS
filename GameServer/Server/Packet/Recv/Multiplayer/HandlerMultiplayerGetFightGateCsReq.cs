using March7thHoney.GameServer.Server.Packet.Send.Multiplayer;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Multiplayer;

// No multiplayer fight room is ever created (the 4.4 fight protocol is not restored), so there is never a gate to
// hand out.
[Opcode(CmdIds.MultiplayerGetFightGateCsReq)]
public class HandlerMultiplayerGetFightGateCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await connection.SendPacket(new PacketMultiplayerGetFightGateScRsp(Retcode.RetFightRoomNotExist));
    }
}
