using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Pet;

public class PacketRecallPetScRsp : BasePacket
{
    public PacketRecallPetScRsp() : base(CmdIds.RecallPetScRsp)
    {
        // The official server's reply to a recall carries an empty cur_pet.
        var proto = new RecallPetScRsp
        {
            PLKBLNMOOPH = new EHDNPMEKPCA()
        };

        SetData(proto);
    }
}
