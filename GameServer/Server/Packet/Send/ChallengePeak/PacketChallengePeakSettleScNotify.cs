using March7thHoney.Data;
using March7thHoney.GameServer.Game.Challenge.Instances;
using March7thHoney.GameServer.Game.ChallengePeak;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.ChallengePeak;

public class PacketChallengePeakSettleScNotify : BasePacket
{
    public PacketChallengePeakSettleScNotify(ChallengePeakInstance inst, List<uint> targetIdList) : base(
        CmdIds.ChallengePeakSettleScNotify)
    {
        var peak = inst.Data.Peak!;
        var isBoss = inst.Config.BossExcel != null;

        var proto = new ChallengePeakSettleScNotify
        {
            IsWin = inst.IsWin,
            PeakId = peak.CurrentPeakLevelId,
            CyclesUsed = peak.RoundCnt
        };

        if (inst.IsWin && !isBoss)
            proto.IsUnlockEasyBoss = inst.Player.ChallengePeakManager!.WillUnlockEasyBoss(
                (int)peak.CurrentPeakGroupId, (int)peak.CurrentPeakLevelId);

        if (inst.IsWin && isBoss && peak.IsHard)
        {
            // King boss (王棋) cleared in hard mode (绝境): the client renders the diamond and three stars from the
            // red hard-mode template, so the normal target list is not sent.
            proto.HardModeHasPassed = ChallengePeakMedal.IsColorMedalClear(true, peak.RoundCnt);
        }
        else
        {
            proto.FinishedTargetList.AddRange(targetIdList);
            if (inst.IsWin && isBoss)
            {
                // King boss on normal: only a clear in 0 cycles meets the color target and shows the diamond; any
                // other clear shows bronze / silver / gold by the number of finished targets. turnLeft is the
                // largest cycle limit among the normal targets minus the cycles used.
                var maxParam = inst.Config.NormalTargetList
                    .Select(t => GameData.BattleTargetConfigData.GetValueOrDefault(t)?.TargetParam ?? 0)
                    .DefaultIfEmpty(0).Max();
                var isDiamond = ChallengePeakMedal.IsColorMedalClear(false, peak.RoundCnt);
                proto.TurnLeft = (uint)Math.Max(0, maxParam - (int)peak.RoundCnt);
                proto.IsBossTargetBetter = isDiamond;
                proto.HardModeHasPassed = isDiamond;
            }
        }

        SetData(proto);
    }
}
