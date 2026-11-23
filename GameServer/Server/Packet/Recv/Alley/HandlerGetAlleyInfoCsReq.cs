using March7thHoney.GameServer.Server.Packet.Send.MiscModule;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Alley;

// AlleyModule._OnGetAlleyInfoScRsp hands NFJKAILEFAD to _SyncLogisticsMapList, which dereferences it; the empty-body
// reply left it null. The official capture ships HAMAOOBGMLO, EEAACLBJODA and NFJKAILEFAD present.
[Opcode(CmdIds.GetAlleyInfoCsReq)]
public class HandlerGetAlleyInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketPresentEmptyScRsp(CmdIds.GetAlleyInfoScRsp, new GetAlleyInfoScRsp()));
    }
}
