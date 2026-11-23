using March7thHoney.GameServer.Game.Challenge.Instances;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Challenge;

public class PacketChallengeTierceSyncNotify : BasePacket
{
    // 4.4: the cmd id ChallengeTierceSyncNotify was renamed to ChallengeTierceSettleNotify (the value is still 8973).
    // The id must not be None: the Tierce stage settlement sync then cannot be sent ("missing cmd id"), and the
    // client shows no settlement screen and drops back to the scene.
    private PacketChallengeTierceSyncNotify(ChallengeTierceSyncNotify proto) : base(CmdIds.ChallengeTierceSyncNotify)
    {
        SetData(proto);
    }

    public static PacketChallengeTierceSyncNotify ForSingleStage(ChallengeTierceInstance inst, ChallengeTierceStageData stageData)
    {
        var single = new ChallengeTierceSyncSingleStage
        {
            IsPassed = true,
            StageData = stageData
        };
        foreach (var t in inst.FinishedTargetList) single.FinishedTargetList.Add(t);

        return new PacketChallengeTierceSyncNotify(new ChallengeTierceSyncNotify
        {
            IANBCDJPDJF = single
        });
    }

    public static PacketChallengeTierceSyncNotify ForAllStage(ChallengeTierceInstance inst)
    {
        var all = new ChallengeTierceSyncAllStage
        {
            ChallengeId = inst.ChallengeId,
            IsPassed = inst.IsPassed,
            EGEHLOPFELK = new ChallengeTierceRecord
            {
                UsedCycleCount = (uint)Math.Max(0, inst.Config.ChallengeCountDown - inst.RoundsLeftPool),
                TotalScore = (uint)inst.GetRecordScore()
            }
        };
        foreach (var t in inst.FinishedTargetList) all.FinishedTargetList.Add(t);
        foreach (var r in inst.ResultList) all.GBMBAHHDONN.Add(r);

        return new PacketChallengeTierceSyncNotify(new ChallengeTierceSyncNotify
        {
            DFNNKENBOEN = all
        });
    }

    public static PacketChallengeTierceSyncNotify ForMedal(uint groupId)
    {
        return new PacketChallengeTierceSyncNotify(new ChallengeTierceSyncNotify
        {
            Medal = new ChallengeTierceMedal
            {
                GroupId = groupId
            }
        });
    }
}
