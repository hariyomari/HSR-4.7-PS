using March7thHoney.GameServer.Server.Packet.Send.MiscModule;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Fate;

// FateModule._OnFateQueryScRsp passes LFLCDNGALME to FateAreaInfo.Sync, which dereferences it; the empty-body reply
// left it null. The official capture ships LFLCDNGALME and OEIBHLMDJKA present.
[Opcode(CmdIds.FateQueryCsReq)]
public class HandlerFateQueryCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketPresentEmptyScRsp(CmdIds.FateQueryScRsp, new FateQueryScRsp()));
    }
}
