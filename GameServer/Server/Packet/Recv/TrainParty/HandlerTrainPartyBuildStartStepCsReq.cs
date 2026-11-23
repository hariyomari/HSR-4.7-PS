using March7thHoney.GameServer.Server.Packet.Send.TrainParty;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.TrainParty;

[Opcode(CmdIds.TrainPartyBuildStartStepCsReq)]
public class HandlerTrainPartyBuildStartStepCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TrainPartyBuildStartStepCsReq.Parser.ParseFrom(data);
        var manager = connection.Player!.TrainPartyManager!;
        var (rsp, notify) = manager.BuildStartStep(req);

        await connection.SendPacket(new PacketTrainPartyBuildStartStepScRsp(rsp));
        if (notify == null) return;

        await connection.SendPacket(new PacketTrainPartyBuildingUpdateNotify(notify));
        await manager.SyncFloorSavedValuesAsync();
    }
}
