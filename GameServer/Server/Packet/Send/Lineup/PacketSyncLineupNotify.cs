using March7thHoney.Kcp;
using March7thHoney.Proto;
using LineupInfo = March7thHoney.Database.Lineup.LineupInfo;

namespace March7thHoney.GameServer.Server.Packet.Send.Lineup;

public class PacketSyncLineupNotify : BasePacket
{
    public PacketSyncLineupNotify(LineupInfo info, SyncLineupReason reason = SyncLineupReason.SyncReasonNone) : base(
        CmdIds.SyncLineupNotify)
    {
        var lineupProto = info.ToProto();
        // MaxMp is the default 5 plus the ExtraMpCount that LineupManager maintains; Mp is capped to it.
        var extra = info.LineupData?.ExtraMpCount ?? 0;
        lineupProto.MaxMp = (uint)(5 + extra);
        if (lineupProto.Mp > lineupProto.MaxMp)
            lineupProto.Mp = lineupProto.MaxMp;

        var proto = new SyncLineupNotify
        {
            Lineup = lineupProto
        };

        if (reason != SyncLineupReason.SyncReasonNone) proto.ReasonList.Add(reason);

        SetData(proto);
    }
}
