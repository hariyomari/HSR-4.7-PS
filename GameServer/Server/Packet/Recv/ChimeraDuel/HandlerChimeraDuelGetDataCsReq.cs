using March7thHoney.GameServer.Server.Packet.Send.MiscModule;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.ChimeraDuel;

// ChimeraDuelModule._OnChimeraDuelGetDataScRsp passes COFPFIDNONN to ChimeraDuelPlayerData.Sync, which dereferences
// it; the empty-body reply left it null. The official capture ships CFDIKLBINHK and COFPFIDNONN present.
[Opcode(CmdIds.ChimeraDuelGetDataCsReq)]
public class HandlerChimeraDuelGetDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketPresentEmptyScRsp(CmdIds.ChimeraDuelGetDataScRsp,
            new ChimeraDuelGetDataScRsp()));
    }
}
