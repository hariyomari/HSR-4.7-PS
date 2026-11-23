using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Multiplayer;

// The 4.4 request (IHGLPGLPDFF) is empty. No multiplayer fight room is ever created, so there is nothing to leave and
// the request is simply acknowledged.
[Opcode(CmdIds.MultiplayerFightGiveUpCsReq)]
public class HandlerMultiplayerFightGiveUpCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var rsp = new BasePacket(CmdIds.MultiplayerFightGiveUpScRsp);
        rsp.SetData(new JJAGFPLDCAF { Retcode = 0 });
        await connection.SendPacket(rsp);
    }
}
