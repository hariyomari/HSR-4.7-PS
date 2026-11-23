using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Multiplayer;

public class PacketMultiplayerGetFightGateScRsp : BasePacket
{
    public PacketMultiplayerGetFightGateScRsp(Retcode code) : base(CmdIds.MultiplayerGetFightGateScRsp)
    {
        var proto = new HPGIGMPBIBO
        {
            Retcode = (uint)code
        };

        SetData(proto);
    }
}
