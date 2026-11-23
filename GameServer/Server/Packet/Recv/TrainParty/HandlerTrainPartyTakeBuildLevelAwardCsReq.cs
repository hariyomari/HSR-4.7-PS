using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.TrainParty;

[Opcode(CmdIds.TrainPartyTakeBuildLevelAwardCsReq)]
public class HandlerTrainPartyTakeBuildLevelAwardCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        // The request carries no level: every build level reached but not yet taken is handed out.
        var rsp = await connection.Player!.TrainPartyManager!.TakeBuildLevelAward();

        var packet = BasePacket.Create(CmdIds.TrainPartyTakeBuildLevelAwardScRsp, rsp);
        await connection.SendPacket(packet);
    }
}
