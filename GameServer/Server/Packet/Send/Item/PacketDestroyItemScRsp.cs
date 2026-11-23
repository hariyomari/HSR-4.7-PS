using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Item;

public class PacketDestroyItemScRsp : BasePacket
{
    public PacketDestroyItemScRsp(Retcode retcode) : base(CmdIds.DestroyItemScRsp)
    {
        SetData(new DestroyItemScRsp { Retcode = (uint)retcode });
    }
}
