using March7thHoney.GameServer.Server.Packet.Send.MiscModule;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.EvolveBuild;

// EvolveBuildModule._OnGetEvolveBuildQueryInfoScRsp walks CFDIKLBINHK in _UpdateGameInfo/_UpdateStages; the
// empty-body reply left it null. The official capture ships CFDIKLBINHK present (JPDOIGJNGNP is a list).
[Opcode(CmdIds.GetEvolveBuildQueryInfoCsReq)]
public class HandlerGetEvolveBuildQueryInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketPresentEmptyScRsp(CmdIds.GetEvolveBuildQueryInfoScRsp,
            new GetEvolveBuildQueryInfoScRsp()));
    }
}
