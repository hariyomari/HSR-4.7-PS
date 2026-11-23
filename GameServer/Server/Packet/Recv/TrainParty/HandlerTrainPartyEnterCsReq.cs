using March7thHoney.GameServer.Server.Packet.Send.TrainParty;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.TrainParty;

[Opcode(CmdIds.TrainPartyEnterCsReq)]
public class HandlerTrainPartyEnterCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var rsp = BasePacket.Create(CmdIds.TrainPartyEnterScRsp, new FDDJJCFNFOJ { Retcode = 0 });
        await connection.SendPacket(rsp);

        // Refresh the build room state for the entering client.
        await connection.SendPacket(new PacketTrainPartyBuildRoomScNotify(connection.Player!));
    }
}
