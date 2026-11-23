using March7thHoney.Database;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Quests;
using March7thHoney.Database.Generated;
using March7thHoney.Database.Player;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Util;
using Microsoft.Data.Sqlite;

// Clone isolation on a real, resource-backed save: a clone loads like any saved player, and every later change to it
// (inventory, missions, avatars) stays out of the source, in memory and in SQLite.
internal static class AccountSnapshotRegression
{
    public static async Task Run(Action<string, bool> check)
    {
        const int sourceUid = 60300;
        const int cloneUid = 60301;
        var database = DatabaseHelper.Instance!;

        var source = new PlayerInstance(sourceUid) { PendingMissionEnabledOverride = true };
        await source.InitializeAsync();
        await source.InventoryManager!.AddItem(2, 5000, false, sync: false);
        DatabaseHelper.MarkDirty(sourceUid);
        database.SaveUidData(sourceUid);
        var credits = source.Data.Scoin;
        var avatars = source.AvatarManager!.Data.FormalAvatars.Select(avatar => avatar.BaseAvatarId).Order().ToList();
        var running = source.MissionManager!.Data.RunningMainMissionIds.Order().ToList();
        var lineup = source.LineupManager!.GetCurLineup()!.BaseAvatars!.Select(slot => slot.SpecialAvatarId).ToList();

        await AccountSnapshotService.CloneGameplayAsync(sourceUid, cloneUid, "Clone");
        var clone = new PlayerInstance(database.GetInstance<PlayerData>(cloneUid)!);
        await clone.InitializeAsync();
        check("a cloned save loads as an existing player with the source's progress",
            !clone.IsNewPlayer && clone.Data.Name == "Clone" && clone.Data.Scoin == credits &&
            clone.AvatarManager!.Data.FormalAvatars.Select(avatar => avatar.BaseAvatarId).Order().SequenceEqual(avatars) &&
            clone.MissionManager!.Data.RunningMainMissionIds.Order().SequenceEqual(running) &&
            clone.LineupManager!.GetCurLineup()!.BaseAvatars!.Select(slot => slot.SpecialAvatarId).SequenceEqual(lineup));

        await clone.InventoryManager!.AddItem(2, 777, false, sync: false);
        await clone.AvatarManager!.AddAvatar(1002, false, false);
        clone.MissionManager.Data.SetMainMissionStatus(1000101, MissionPhaseEnum.Finish);
        clone.MissionManager.Data.SetMainMissionStatus(1000201, MissionPhaseEnum.Accept);
        clone.LineupManager.GetCurLineup()!.BaseAvatars!.Clear();
        DatabaseHelper.MarkDirty(cloneUid);
        database.SaveUidData(cloneUid);

        check("changing the clone's inventory, roster, missions and lineup leaves the source unchanged in memory",
            source.Data.Scoin == credits && clone.Data.Scoin == credits + 777 &&
            source.AvatarManager.GetFormalAvatar(1002) == null &&
            source.MissionManager.Data.GetMainMissionStatus(1000201) == MissionPhaseEnum.None &&
            source.LineupManager.GetCurLineup()!.BaseAvatars!.Select(slot => slot.SpecialAvatarId).SequenceEqual(lineup));

        var config = ConfigManager.Config;
        using var connection = new SqliteConnection(
            $"Data Source={Path.GetFullPath(Path.Combine(config.Path.DatabasePath, config.Database.DatabaseName))}");
        connection.Open();
        T Stored<T>(int uid) where T : BaseDatabaseDataHelper =>
            EntityStores.Map[typeof(T)].LoadAll(connection).Cast<T>().Single(row => row.Uid == uid);
        check("after a reload from SQLite the source keeps its credits, roster and missions; the clone has its own",
            Stored<PlayerData>(sourceUid).Scoin == credits && Stored<PlayerData>(cloneUid).Scoin == credits + 777 &&
            Stored<AvatarData>(sourceUid).FormalAvatars.All(avatar => avatar.BaseAvatarId != 1002) &&
            Stored<AvatarData>(cloneUid).FormalAvatars.Any(avatar => avatar.BaseAvatarId == 1002) &&
            Stored<MissionData>(sourceUid).GetMainMissionStatus(1000201) == MissionPhaseEnum.None &&
            Stored<MissionData>(cloneUid).GetMainMissionStatus(1000201) == MissionPhaseEnum.Accept);
    }
}
