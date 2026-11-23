using March7thHoney.GameServer.Game.Activity.RealityShow;
using March7thHoney.GameServer.Server.Packet.Send.RealityShow;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.RealityShow;

// Interastral Peace Gala: Road to Fame requests, sent by the client's RealityShow network service (LDFEDNEMOCD in the
// 4.7.51 dump): the data query at login, StartChallenge, ResetFirstStage and the friend ranking query.

/// <summary>GetData (CKMHMAJPJEP, 10786, empty). Unanswered, the client keeps its loading spinner up at login.</summary>
[Opcode(CmdIds.CKMHMAJPJEP)]
public sealed class HandlerRealityShowGetDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketRealityShowGetDataScRsp(player.GetComponent<RealityShowManager>()));
    }
}

/// <summary>
///     StartChallenge (OHFEIJJDJIE, 10798): challenge_id, LAJAGHBKIJH = IsFirstStage (the warm-up rather than the
///     final stage), avatar_list = the team (trial avatars by SpecialAvatar id, own avatars by base id).
/// </summary>
[Opcode(CmdIds.OHFEIJJDJIE)]
public sealed class HandlerRealityShowStartChallengeCsReq : Handler<OHFEIJJDJIE>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, OHFEIJJDJIE req)
    {
        var manager = player.GetComponent<RealityShowManager>();
        var avatars = req.AvatarList.Select(x => (x.AvatarType, (int)x.Id)).ToList();
        var (retcode, battle) = manager.StartChallenge((int)req.ChallengeId, req.LAJAGHBKIJH, avatars);
        await connection.SendPacket(new PacketRealityShowStartChallengeScRsp(retcode, req, manager, battle));
    }
}

/// <summary>ResetFirstStage (IOOHJLNKKKD, 10794): challenge_id. Sent by RealityShowChallengeViewModel._OnResetFirstStage.</summary>
[Opcode(CmdIds.IOOHJLNKKKD)]
public sealed class HandlerRealityShowResetFirstStageCsReq : Handler<IOOHJLNKKKD>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, IOOHJLNKKKD req)
    {
        var manager = player.GetComponent<RealityShowManager>();
        var retcode = manager.ResetFirstStage((int)req.ChallengeId);
        await connection.SendPacket(new PacketRealityShowResetFirstStageScRsp(retcode, req.ChallengeId, manager));
    }
}

/// <summary>Friend ranking (NKEMNFGCBLO, 10797): challenge_id. The client sends it without waiting for a reply.</summary>
[Opcode(CmdIds.NKEMNFGCBLO)]
public sealed class HandlerRealityShowFriendRankCsReq : Handler<NKEMNFGCBLO>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, NKEMNFGCBLO req)
    {
        await connection.SendPacket(
            new PacketRealityShowFriendRankScRsp(req.ChallengeId, player.GetComponent<RealityShowManager>()));
    }
}
