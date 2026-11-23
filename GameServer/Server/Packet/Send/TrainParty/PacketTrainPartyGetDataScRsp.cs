using March7thHoney.GameServer.Game.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.TrainParty;

public class PacketTrainPartyGetDataScRsp : BasePacket
{
    // TrainPartyModule._OnTrainPartyGetDataScRsp hands train_party_data to TrainPartyGameInstance.Sync, which needs
    // it and its train_party_info present; TrainPartyManager.ToProto builds both from the player's build state.
    public PacketTrainPartyGetDataScRsp(PlayerInstance player) : base(CmdIds.TrainPartyGetDataScRsp)
    {
        SetData(new TrainPartyGetDataScRsp
        {
            TrainPartyData = player.TrainPartyManager!.ToProto()
        });
    }
}
