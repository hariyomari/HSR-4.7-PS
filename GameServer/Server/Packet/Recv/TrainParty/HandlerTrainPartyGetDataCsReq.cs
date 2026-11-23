using March7thHoney.GameServer.Server.Packet.Send.TrainParty;
using March7thHoney.Kcp;

namespace March7thHoney.GameServer.Server.Packet.Recv.TrainParty;

[Opcode(CmdIds.TrainPartyGetDataCsReq)]
public class HandlerTrainPartyGetDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var player = connection.Player!;
        await connection.SendPacket(new PacketTrainPartyGetDataScRsp(player));
        // The party-car floors read the build through floor saved values; bring them in line with the build state.
        await player.TrainPartyManager!.SyncFloorSavedValuesAsync();
    }
}
