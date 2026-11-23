using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.RelicPreset;

public class PacketRelicPresetUpdatePlanScRsp : BasePacket
{
    /// <summary>
    ///     Echoes back the one item the request changed (the name or the relics); the client updates its local plan
    ///     cache from it.
    /// </summary>
    public PacketRelicPresetUpdatePlanScRsp(RelicPresetUpdatePlanCsReq req, Retcode retcode = Retcode.RetSucc)
        : base(CmdIds.RelicPresetUpdatePlanScRsp)
    {
        var proto = new RelicPresetUpdatePlanScRsp
        {
            UniqueId = req.UniqueId,
            Retcode = (uint)retcode
        };

        if (req.RelicList != null) proto.RelicList = req.RelicList;
        else proto.Name = req.Name;

        SetData(proto);
    }
}
