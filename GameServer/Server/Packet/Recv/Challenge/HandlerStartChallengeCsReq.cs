using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Challenge;

[Opcode(CmdIds.StartChallengeCsReq)]
public class HandlerStartChallengeCsReq : Handler<StartChallengeCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, StartChallengeCsReq req)
    {
        ChallengeStoryBuffInfo? storyBuffInfo = null;
        if (req.StageInfo is { StoryInfo: not null })
            storyBuffInfo = req.StageInfo.StoryInfo;

        ChallengeBossBuffInfo? bossBuffInfo = null;
        if (req.StageInfo != null && req.StageInfo.BossInfo != null) bossBuffInfo = req.StageInfo.BossInfo;

        // The client sends each half of the team as a list of AvatarIdentifier; older clients sent packed
        // uint32 lists in first_lineup / second_lineup, which are still read as a fallback.
        var firstLineup = req.AvatarLineupFirst.Count > 0
            ? req.AvatarLineupFirst.Select(x => (int)x.Id).Where(x => x > 0).ToList()
            : req.FirstLineup.Select(x => (int)x).Where(x => x > 0).ToList();
        if (firstLineup.Count > 0)
            await player.LineupManager!.ReplaceLineup(0, firstLineup, ExtraLineupType.LineupChallenge);

        var secondLineup = req.AvatarLineupSecond.Count > 0
            ? req.AvatarLineupSecond.Select(x => (int)x.Id).Where(x => x > 0).ToList()
            : req.SecondLineup.Select(x => (int)x).Where(x => x > 0).ToList();
        if (secondLineup.Count > 0)
            await player.LineupManager!.ReplaceLineup(0, secondLineup, ExtraLineupType.LineupChallenge2);

        await player.ChallengeManager!.StartChallenge((int)req.ChallengeId, storyBuffInfo, bossBuffInfo);
    }
}
