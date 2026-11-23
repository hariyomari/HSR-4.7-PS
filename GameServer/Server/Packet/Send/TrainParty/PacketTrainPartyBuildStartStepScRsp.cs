using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.TrainParty;

public class PacketTrainPartyBuildStartStepScRsp : BasePacket
{
    public PacketTrainPartyBuildStartStepScRsp(TrainPartyBuildStartStepScRsp rsp) : base(
        CmdIds.TrainPartyBuildStartStepScRsp)
    {
        SetData(rsp);
    }
}
