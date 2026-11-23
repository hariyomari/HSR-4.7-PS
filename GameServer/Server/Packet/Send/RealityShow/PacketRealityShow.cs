using March7thHoney.GameServer.Game.Activity.RealityShow;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.RealityShow;

// Interastral Peace Gala: Road to Fame replies. The 4.8 dump left the command group (10785-10799) unnamed;
// docs/development/reality-show.md maps each message and field to the client code that reads it.

/// <summary>GetData reply (PAMDEKPLLEL, 10799): OGDEMOAKJOK = challenge records, retcode.</summary>
public class PacketRealityShowGetDataScRsp : BasePacket
{
    public PacketRealityShowGetDataScRsp(RealityShowManager manager) : base(CmdIds.PAMDEKPLLEL)
    {
        var proto = new PAMDEKPLLEL();
        proto.OGDEMOAKJOK.AddRange(manager.ToChallengeListProto());
        SetData(proto);
    }
}

/// <summary>
///     StartChallenge reply (IFMNCFKBEGL, 10792): challenge id, retcode, the challenge (NEMJKLMIJOL) and the team
///     (KHBJOAHCCCE) the client stores, and the battle the client enters (AdventureModule.CreateBattleServerCheckResult).
/// </summary>
public class PacketRealityShowStartChallengeScRsp : BasePacket
{
    public PacketRealityShowStartChallengeScRsp(Retcode retcode, OHFEIJJDJIE req, RealityShowManager manager,
        BattleInstance? battle) : base(CmdIds.IFMNCFKBEGL)
    {
        var proto = new IFMNCFKBEGL
        {
            Retcode = (uint)retcode,
            ChallengeId = req.ChallengeId
        };
        if (retcode == Retcode.RetSucc && battle != null)
        {
            proto.NEMJKLMIJOL = manager.ToChallengeProto((int)req.ChallengeId);
            proto.KHBJOAHCCCE.AddRange(req.AvatarList);
            proto.BattleInfo = battle.ToProto();
        }

        SetData(proto);
    }
}

/// <summary>ResetFirstStage reply (FKHNFMFIKGL, 10795): retcode and the challenge after the reset.</summary>
public class PacketRealityShowResetFirstStageScRsp : BasePacket
{
    public PacketRealityShowResetFirstStageScRsp(Retcode retcode, uint challengeId, RealityShowManager manager)
        : base(CmdIds.FKHNFMFIKGL)
    {
        var proto = new FKHNFMFIKGL { Retcode = (uint)retcode };
        if (retcode == Retcode.RetSucc) proto.NEMJKLMIJOL = manager.ToChallengeProto((int)challengeId);
        SetData(proto);
    }
}

/// <summary>Friend ranking reply (JBPOKKNFOKM, 10796), which the client receives as a push keyed by challenge id.</summary>
public class PacketRealityShowFriendRankScRsp : BasePacket
{
    public PacketRealityShowFriendRankScRsp(uint challengeId, RealityShowManager manager) : base(CmdIds.JBPOKKNFOKM)
    {
        var proto = new JBPOKKNFOKM { ChallengeId = challengeId };
        proto.MJCCFIINAJC.AddRange(manager.ToFriendRankProto((int)challengeId));
        SetData(proto);
    }
}

/// <summary>
///     Stage settlement (NOEGOMIJDAH, 10785), sent before PVEBattleResultScRsp: BCLOOPGAIKJ for the warm-up (buff
///     level, shields broken), OEFFDKDCIMC for the final stage (HP bars broken, action value consumed).
/// </summary>
public class PacketRealityShowSettleScNotify : BasePacket
{
    public PacketRealityShowSettleScNotify(NOEGOMIJDAH notify) : base(CmdIds.NOEGOMIJDAH)
    {
        SetData(notify);
    }
}
