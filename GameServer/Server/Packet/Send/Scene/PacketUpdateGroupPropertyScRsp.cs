using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Scene;

public class PacketUpdateGroupPropertyScRsp : BasePacket
{
    public PacketUpdateGroupPropertyScRsp(Retcode code) : base(CmdIds.UpdateGroupPropertyScRsp)
    {
        var proto = new UpdateGroupPropertyScRsp
        {
            Retcode = (uint)code
        };

        SetData(proto);
    }

    public PacketUpdateGroupPropertyScRsp(GroupPropertyRefreshData data, UpdateGroupPropertyCsReq req) : base(
        CmdIds.UpdateGroupPropertyScRsp)
    {
        var proto = new UpdateGroupPropertyScRsp
        {
            // TODO: the old and new value are not sent. Which fields carry them has not been confirmed since
            // 4.3 renamed them.
            DimensionId = req.DimensionId,
            FloorId = req.FloorId,
            GroupId = (uint)data.GroupId,
            PropertyName = data.PropertyName
        };

        SetData(proto);
    }
}

