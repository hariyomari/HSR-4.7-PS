using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Item;

// CmdId 528 is named AddEquipmentScRsp in the 4.7 proto, though it is a server-push notify
// (no CsReq, no retcode) carrying the Light Cone's base equipment ID to trigger the reveal screen.
public class PacketAddEquipmentScNotify : BasePacket
{
    public PacketAddEquipmentScNotify(int baseEquipmentId) : base(CmdIds.AddEquipmentScRsp)
    {
        SetData(new AddEquipmentScRsp
        {
            BaseEquipmentId = (uint)baseEquipmentId
        });
    }
}
