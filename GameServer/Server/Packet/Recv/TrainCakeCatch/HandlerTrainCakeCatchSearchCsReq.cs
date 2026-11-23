using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.TrainCakeCatch;

[Opcode(CmdIds.TrainCakeCatchSearchCsReq)]
public class HandlerTrainCakeCatchSearchCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TrainCakeCatchSearchCsReq.Parser.ParseFrom(data);
        var packet = BasePacket.Create(CmdIds.TrainCakeCatchSearchScRsp, connection.Player!.TrainCakeCatchManager!.Search(req.PCPDHEIEJFO));
        await connection.SendPacket(packet);
    }
}
