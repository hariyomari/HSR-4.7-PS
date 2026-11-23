using March7thHoney.GameServer.Server.Packet.Send.TrainParty;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.TrainParty;

[Opcode(CmdIds.TrainPartyBuildDiyCsReq)]
public class HandlerTrainPartyBuildDiyCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TrainPartyBuildDiyCsReq.Parser.ParseFrom(data);
        var rsp = connection.Player!.TrainPartyManager!.BuildDiy(req);
        await connection.SendPacket(new PacketTrainPartyBuildDiyScRsp(rsp));
    }
}
