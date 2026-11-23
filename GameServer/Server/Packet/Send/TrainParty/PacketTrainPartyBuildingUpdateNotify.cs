using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.TrainParty;

public class PacketTrainPartyBuildingUpdateNotify : BasePacket
{
    public PacketTrainPartyBuildingUpdateNotify(TrainPartyBuildingUpdateNotify notify) : base(
        CmdIds.TrainPartyBuildingUpdateNotify)
    {
        SetData(notify);
    }
}
