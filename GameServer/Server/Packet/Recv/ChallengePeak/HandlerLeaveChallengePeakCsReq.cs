using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Server.Packet.Send.ChallengePeak;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.Packet.Recv.ChallengePeak;

[Opcode(CmdIds.LeaveChallengePeakCsReq)]
public class HandlerLeaveChallengePeakCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        int? peakGroupId = null;
        // Only run the leave cleanup (heal team, teleport) when actually inside a challenge-type
        // scene — otherwise a client could spam this for a free full heal + teleport.
        if (player.SceneInstance is not null && player.SceneInstance.Excel.PlaneType == PlaneTypeEnum.Challenge)
        {
            // Remember the peak group being left, so the overview's lineup slots can be refreshed after leaving.
            if (player.ChallengeManager!.ChallengeInstance?.Data.Peak is { } peak)
                peakGroupId = (int)peak.CurrentPeakGroupId;

            player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupChallenge, []);


            player.ChallengeManager!.ChallengeInstance = null;
            player.ChallengeManager!.ClearInstance();

            // Leave scene
            player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone, []);
            // Heal avatars (temproary solution)
            foreach (var avatar in player.LineupManager.GetCurLineup()!.AvatarData!.FormalAvatars)
                avatar.CurrentHp = 10000;

            var leaveEntryId = GameConstants.CHALLENGE_PEAK_ENTRANCE;
            if (player.SceneInstance.LeaveEntryId != 0) leaveEntryId = player.SceneInstance.LeaveEntryId;
            await player.EnterScene(leaveEntryId, 0, true);
        }

        await connection.SendPacket(CmdIds.LeaveChallengePeakScRsp);

        // Leaving returns to the overview. Send the group data once more so the client refills the lineup slots with
        // the last team and buff; the difficulty is the one remembered for the group.
        if (peakGroupId is { } gid)
            await player.SendPacket(new PacketChallengePeakGroupDataUpdateScNotify(
                player.ChallengePeakManager!.GetChallengePeakInfo(gid)));
    }
}
