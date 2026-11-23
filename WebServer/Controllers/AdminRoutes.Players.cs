using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.Mail;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static void MapPlayerRoutes(IEndpointRouteBuilder api)
    {
        api.MapGet("/sessions", GetSessions);
        api.MapPost("/sessions/kick-all", KickAllSessions);
        api.MapPost("/sessions/{uid:int}/kick", KickSession);
        api.MapGet("/players/{uid:int}", GetPlayerDetail);
    }

    private static IResult GetSessions()
    {
        var sessions = Listener.GetConnections()
            .Select(ToSessionInfo)
            .OrderByDescending(session => session.has_player)
            .ThenBy(session => session.uid)
            .ToList();

        return Success(new SessionsData(sessions, sessions.Count, sessions.Count(session => session.has_player)));
    }

    private static IResult KickAllSessions()
    {
        var kicked = 0;
        foreach (var connection in Listener.GetConnections())
        {
            if (connection.Player == null) continue;
            ConnectionDisconnectHelper.KickByGm(connection);
            kicked++;
        }

        return Success(new KickAllData(kicked));
    }

    private static IResult KickSession(int uid)
    {
        var connection = Listener.GetActiveConnection(uid);
        if (connection == null)
            return Error("Player is not online", statusCode: StatusCodes.Status404NotFound);

        ConnectionDisconnectHelper.KickByGm(connection);
        return Success(new KickAllData(1));
    }

    private static IResult GetPlayerDetail(int uid)
    {
        var database = DatabaseHelper.Instance;
        if (database == null)
            return Error("Database is not initialized");

        var connection = Listener.GetActiveConnection(uid);
        var player = connection?.Player;
        var data = player?.Data ?? database.GetInstance<PlayerData>(uid);
        if (data == null)
            return Success(EmptyPlayerDetail(uid));

        var avatarData = database.GetInstance<AvatarData>(uid);
        var missionData = database.GetInstance<MissionData>(uid);
        var mailData = database.GetInstance<MailData>(uid);
        var inventory = database.GetInstance<InventoryData>(uid);

        var avatars = (avatarData?.FormalAvatars ?? [])
            .Select(avatar => new AvatarBrief(
                avatar.BaseAvatarId != 0 ? avatar.BaseAvatarId : avatar.AvatarId,
                avatar.Level,
                avatar.Promotion,
                avatar.GetPathInfo(avatar.AvatarId)?.Rank ?? 0))
            .OrderBy(avatar => avatar.id)
            .ToList();

        return Success(new PlayerDetailData(
            uid,
            true,
            player != null,
            data.Name ?? "",
            data.Signature ?? "",
            data.Level,
            data.Exp,
            data.WorldLevel,
            data.Scoin,
            data.Hcoin,
            data.Mcoin,
            data.Stamina,
            Math.Round(data.StaminaReserve, 1),
            data.HeadIcon,
            data.PlaneId,
            data.FloorId,
            data.EntryId,
            data.LastActiveTime,
            data.MonthCardExpireTime,
            data.FakeTimeDate,
            avatars.Count,
            avatars,
            GetCurrentLineup(uid),
            missionData?.FinishedMainMissionIds.Count ?? 0,
            missionData?.RunningMainMissionIds.Count ?? 0,
            mailData?.MailList.Count ?? 0,
            inventory?.MaterialItems.Count ?? 0,
            inventory?.EquipmentItems.Count ?? 0,
            inventory?.RelicItems.Count ?? 0,
            player?.BattleInstance != null,
            (player?.RaidManager?.Data.CurRaidId ?? 0) != 0,
            player?.Language ?? ""));
    }

    private static PlayerDetailData EmptyPlayerDetail(int uid)
    {
        return new PlayerDetailData(uid, false, false, "", "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            [], [], 0, 0, 0, 0, 0, 0, false, false, "");
    }

    private static SessionInfo ToSessionInfo(Connection connection)
    {
        var player = connection.Player;
        var data = player?.Data;
        return new SessionInfo(
            player?.Uid ?? 0,
            data?.Name ?? "",
            data?.Level ?? 0,
            data?.WorldLevel ?? 0,
            connection.RemoteEndPoint.ToString(),
            connection.State.ToString(),
            connection.ConversationId ?? 0,
            data?.PlaneId ?? 0,
            data?.FloorId ?? 0,
            data?.EntryId ?? 0,
            player?.BattleInstance != null,
            (player?.RaidManager?.Data.CurRaidId ?? 0) != 0,
            player?.Language ?? "",
            player != null ? GetCurrentLineup(player.Uid) : [],
            data?.LastActiveTime ?? 0,
            player != null);
    }

    private static List<int> GetCurrentLineup(int uid)
    {
        var lineupData = DatabaseHelper.Instance?.GetInstance<LineupData>(uid);
        if (lineupData == null) return [];

        return lineupData.Lineups.TryGetValue(lineupData.GetCurLineupIndex(), out var lineup)
            ? (lineup.BaseAvatars ?? []).Select(avatar => avatar.BaseAvatarId).Where(id => id > 0).ToList()
            : [];
    }
}
