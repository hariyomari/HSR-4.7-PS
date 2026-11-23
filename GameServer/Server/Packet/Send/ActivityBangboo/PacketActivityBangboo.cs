using March7thHoney.GameServer.Game.Activity.ActivityBangboo;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.ActivityBangboo;

// Planarcadia Reverie replies and the change notify. The 4.8 dump left the command group (MFNJLCNMBON, 10602-10640)
// unnamed; docs/development/activity-bangboo.md maps each message and field to the client code that reads it.
// JKCOAIGJMMM is the retcode in every reply.

/// <summary>GetData reply (BGCOEFMFEHA, 10627): FJCAKOIDONG = ABCMHMIHJBO.</summary>
public class PacketActivityBangbooGetDataScRsp : BasePacket
{
    public PacketActivityBangbooGetDataScRsp(ActivityBangbooManager manager) : base(CmdIds.BGCOEFMFEHA)
    {
        SetData(new BGCOEFMFEHA { FJCAKOIDONG = manager.ToDataProto() });
    }
}

/// <summary>ReadPost reply (MHJNBHCDPHK, 10632): AMFEFIDDMDA the post; the client drops a reply for another post.</summary>
public class PacketActivityBangbooReadPostScRsp : BasePacket
{
    public PacketActivityBangbooReadPostScRsp(Retcode retcode, IHNJGNPKKOA? post) : base(CmdIds.MHJNBHCDPHK)
    {
        SetData(new MHJNBHCDPHK { JKCOAIGJMMM = (uint)retcode, AMFEFIDDMDA = post });
    }
}

/// <summary>SetLike reply (NIIFNPFEKIG, 10637): AMFEFIDDMDA the post.</summary>
public class PacketActivityBangbooSetLikeScRsp : BasePacket
{
    public PacketActivityBangbooSetLikeScRsp(Retcode retcode, IHNJGNPKKOA? post) : base(CmdIds.NIIFNPFEKIG)
    {
        SetData(new NIIFNPFEKIG { JKCOAIGJMMM = (uint)retcode, AMFEFIDDMDA = post });
    }
}

/// <summary>StartGame reply (BOBEICIPCNK, 10640): IJAPHCOHMIN stage, OJHNAJCAKNB GameTransNo, IFDOJGKJEBN Infinity run.</summary>
public class PacketActivityBangbooStartGameScRsp : BasePacket
{
    public PacketActivityBangbooStartGameScRsp(ActivityBangbooManager.StartResult result) : base(CmdIds.BOBEICIPCNK)
    {
        SetData(new BOBEICIPCNK
        {
            JKCOAIGJMMM = (uint)result.Retcode,
            IJAPHCOHMIN = (uint)result.LevelId,
            OJHNAJCAKNB = result.GameTransNo,
            IFDOJGKJEBN = result.Run
        });
    }
}

/// <summary>FinishPerformance reply (PPHGPJDEHDA, 10602): CIGGAEJNEFM echoes the group.</summary>
public class PacketActivityBangbooFinishPerformanceScRsp : BasePacket
{
    public PacketActivityBangbooFinishPerformanceScRsp(Retcode retcode, uint groupId) : base(CmdIds.PPHGPJDEHDA)
    {
        SetData(new PPHGPJDEHDA { JKCOAIGJMMM = (uint)retcode, CIGGAEJNEFM = groupId });
    }
}

/// <summary>
///     Settle reply (EBHKOMEILOJ, 10633): IJAPHCOHMIN echoes the stage (needed for the settlement screen),
///     ENGHMIAMPLG the first-clear reward, HPKEGPGPPOK the hole's best wave (EndlessScore.HistoricalMaxWave).
/// </summary>
public class PacketActivityBangbooSettleScRsp : BasePacket
{
    public PacketActivityBangbooSettleScRsp(ActivityBangbooManager.SettleResult result) : base(CmdIds.EBHKOMEILOJ)
    {
        var proto = new EBHKOMEILOJ
        {
            JKCOAIGJMMM = (uint)result.Retcode,
            IJAPHCOHMIN = (uint)result.LevelId,
            HPKEGPGPPOK = (uint)result.HistoricalMaxWave,
            ENGHMIAMPLG = new ItemList()
        };
        foreach (var item in result.Rewards) proto.ENGHMIAMPLG.ItemList_.Add(item.ToProto());
        SetData(proto);
    }
}

/// <summary>SettleInfinityRun reply (EEJOPBDAHNJ, 10611): retcode only.</summary>
public class PacketActivityBangbooSettleInfinityRunScRsp : BasePacket
{
    public PacketActivityBangbooSettleInfinityRunScRsp(Retcode retcode) : base(CmdIds.EEJOPBDAHNJ)
    {
        SetData(new EEJOPBDAHNJ { JKCOAIGJMMM = (uint)retcode });
    }
}

/// <summary>
///     Change notify (LIEHHGLAEHG, 10622): DGNGMJMPFGK lists CAGDONKMOGB entries, one oneof arm each:
///     CEMIOADEMHA newly unlocked stages, FADGOOJBAKE newly unlocked Bangboo, GILOPNKGNAA the Infinity holes opened,
///     BPJABGNNHDO a post. CBAPNPOMOII marks the unlocks new and then always refreshes (GetData).
/// </summary>
public class PacketActivityBangbooChangeScNotify : BasePacket
{
    public PacketActivityBangbooChangeScNotify(ActivityBangbooManager.SettleResult result) : base(CmdIds.LIEHHGLAEHG)
    {
        var proto = new LIEHHGLAEHG();
        if (result.NewLevelIds is { Count: > 0 } levels)
        {
            var arm = new NPFMFIKIDAC();
            arm.LNMPJKPEHAE.AddRange(levels.Select(x => (uint)x));
            proto.DGNGMJMPFGK.Add(new CAGDONKMOGB { CEMIOADEMHA = arm });
        }

        if (result.NewBangbooIds is { Count: > 0 } bangboos)
        {
            var arm = new FKDJGPFCDJK();
            arm.LNMPJKPEHAE.AddRange(bangboos.Select(x => (uint)x));
            proto.DGNGMJMPFGK.Add(new CAGDONKMOGB { FADGOOJBAKE = arm });
        }

        if (result.InfinityNewlyUnlocked) proto.DGNGMJMPFGK.Add(new CAGDONKMOGB { GILOPNKGNAA = true });
        SetData(proto);
    }
}
