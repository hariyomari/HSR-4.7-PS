using March7thHoney.Database.Inventory;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Item;

public class PacketComposeItemScRsp : BasePacket
{
    public PacketComposeItemScRsp(Retcode retcode = Retcode.RetFail) : base(CmdIds.ComposeItemScRsp)
    {
        var proto = new ComposeItemScRsp
        {
            Retcode = (uint)retcode
        };

        SetData(proto);
    }

    public PacketComposeItemScRsp(uint composeId, uint count, ItemData item) : base(CmdIds.ComposeItemScRsp)
    {
        var proto = new ComposeItemScRsp
        {
            ReturnItemList = new ItemList
            {
                ItemList_ = { item.ToProto() }
            },
            ComposeId = composeId,
            Count = count
        };

        SetData(proto);
    }
}
