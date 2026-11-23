using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Chat;

public class PacketSendMsgScRsp : BasePacket
{
    public PacketSendMsgScRsp(Retcode retcode) : base(CmdIds.SendMsgScRsp)
    {
        SetData(new SendMsgScRsp { Retcode = (uint)retcode });
    }
}
