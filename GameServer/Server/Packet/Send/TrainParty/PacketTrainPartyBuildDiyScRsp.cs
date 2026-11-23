using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.TrainParty;

public class PacketTrainPartyBuildDiyScRsp : BasePacket
{
    // TrainPartyBuildManager.OnDiyFinish applies each changed slot listed in JKPJFKCBIEM.
    public PacketTrainPartyBuildDiyScRsp(TrainPartyBuildDiyScRsp rsp) : base(CmdIds.TrainPartyBuildDiyScRsp)
    {
        SetData(rsp);
    }
}
