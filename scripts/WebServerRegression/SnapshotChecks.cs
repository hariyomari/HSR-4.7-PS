using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Generated;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.Enums.Mission;
using March7thHoney.Util;
using Microsoft.Data.Sqlite;

// Account clone and named save snapshots through the admin API: deep-copy isolation in memory and in SQLite,
// snapshot CRUD, spawn/restore, and id validation. Snapshot files live next to the scratch database.
internal static class SnapshotChecks
{
    private const int SourceUid = 20001;
    private const int EmptyUid = 20009;

    public static async Task Run(HttpClient client, Action<string, bool> check)
    {
        var database = DatabaseHelper.Instance!;
        AccountHelper.CreateAccount("snapshot-source", "snapshot-source@example.invalid", "source-password-1", SourceUid, null);
        var sourcePlayer = database.GetInstanceOrCreateNew<PlayerData>(SourceUid);
        sourcePlayer.Name = "Source";
        sourcePlayer.Level = 42;
        sourcePlayer.Scoin = 1234;
        sourcePlayer.FloorId = 20101001;
        var sourceMission = database.GetInstanceOrCreateNew<MissionData>(SourceUid);
        sourceMission.SetMainMissionStatus(1010002, MissionPhaseEnum.Accept);
        sourceMission.TrackingMainMissionId = 1010002;
        database.GetInstanceOrCreateNew<FriendData>(SourceUid).FriendList.Add(SourceUid + 1);
        DatabaseHelper.MarkDirty(SourceUid);
        database.SaveUidData(SourceUid);

        // --- clone ----------------------------------------------------------------------------------------
        int cloneUid;
        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{SourceUid}/clone",
                   new { username = "snapshot-clone", password = "clone-password-1", nickname = "Clone" }))
        {
            using var json = await Json(response);
            var data = json.RootElement.GetProperty("data");
            cloneUid = data.GetProperty("account").GetProperty("uid").GetInt32();
            check("clone creates a new account with the requested credentials and no generated password",
                response.StatusCode == HttpStatusCode.OK && cloneUid != SourceUid &&
                data.GetProperty("account").GetProperty("username").GetString() == "snapshot-clone" &&
                data.GetProperty("generated_password").ValueKind == JsonValueKind.Null &&
                AccountData.GetAccountByUid(cloneUid)?.VerifyPassword("clone-password-1") == true);
        }

        var clonePlayer = database.GetInstance<PlayerData>(cloneUid);
        var cloneMission = database.GetInstance<MissionData>(cloneUid);
        check("clone copies the gameplay save and applies the nickname",
            clonePlayer is { Level: 42, Scoin: 1234, FloorId: 20101001, Name: "Clone" } &&
            cloneMission?.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept &&
            cloneMission.TrackingMainMissionId == 1010002 && sourcePlayer.Name == "Source");
        check("clone shares no object with the source save",
            clonePlayer != null && !ReferenceEquals(clonePlayer, sourcePlayer) &&
            !ReferenceEquals(cloneMission, sourceMission) &&
            !ReferenceEquals(cloneMission!.MainMissionInfo, sourceMission.MainMissionInfo));
        check("clone does not carry friendships (they name other uids)",
            database.GetInstance<FriendData>(cloneUid) is not { FriendList.Count: > 0 });

        clonePlayer!.Scoin = 1;
        cloneMission!.SetMainMissionStatus(1010002, MissionPhaseEnum.Finish);
        cloneMission.SetMainMissionStatus(1020101, MissionPhaseEnum.Accept);
        DatabaseHelper.MarkDirty(cloneUid);
        database.SaveUidData(cloneUid);
        check("mutating the clone leaves the source unchanged in memory",
            sourcePlayer.Scoin == 1234 && sourceMission.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept &&
            sourceMission.GetMainMissionStatus(1020101) == MissionPhaseEnum.None);
        var stored = LoadPlayers();
        check("mutating the clone leaves the source unchanged in SQLite",
            stored.TryGetValue(SourceUid, out var storedSource) && storedSource.Scoin == 1234 &&
            stored.TryGetValue(cloneUid, out var storedClone) && storedClone is { Scoin: 1, Level: 42 });

        var accounts = AccountCount();
        using (var response = await client.PostAsJsonAsync("/admin/api/accounts/99999/clone", new { }))
            check("cloning an unknown account is a 404", response.StatusCode == HttpStatusCode.NotFound);
        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{SourceUid}/clone",
                   new { username = "snapshot-clone" }))
            check("cloning onto a taken username fails without creating an account",
                response.StatusCode == HttpStatusCode.BadRequest && AccountCount() == accounts);
        // An account of its own: the shared fixture account (10002) gains a save from the order checks.
        AccountHelper.CreateAccount("snapshot-empty", "snapshot-empty@example.invalid", "empty-password-1", EmptyUid, null);
        accounts = AccountCount();
        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{EmptyUid}/clone", new { }))
            check("cloning an account without gameplay data is refused",
                response.StatusCode == HttpStatusCode.BadRequest && AccountCount() == accounts);

        // --- snapshots ------------------------------------------------------------------------------------
        string snapshotId;
        using (var response = await client.PostAsJsonAsync("/admin/api/snapshots",
                   new { uid = SourceUid, name = "Jarilo Start!", description = "before Jarilo-VI" }))
        {
            using var json = await Json(response);
            var snapshot = json.RootElement.GetProperty("data").GetProperty("snapshot");
            snapshotId = snapshot.GetProperty("id").GetString()!;
            check("saving a snapshot slugs the name and records the save's metadata",
                response.StatusCode == HttpStatusCode.OK && snapshotId == "jarilo-start" &&
                snapshot.GetProperty("source_uid").GetInt32() == SourceUid &&
                snapshot.GetProperty("player_level").GetInt32() == 42 &&
                snapshot.GetProperty("frontier_main_mission").GetInt32() == 1010002 &&
                snapshot.GetProperty("floor_id").GetInt32() == 20101001 &&
                snapshot.GetProperty("table_count").GetInt32() >= 2 &&
                File.Exists(Path.Combine(ConfigManager.Config.Path.DatabasePath, "Snapshots", "jarilo-start.json")));
        }

        using (var response = await client.PostAsJsonAsync("/admin/api/snapshots",
                   new { uid = SourceUid, name = "Jarilo Start!" }))
        {
            using var json = await Json(response);
            check("a second snapshot with the same name gets its own id",
                json.RootElement.GetProperty("data").GetProperty("snapshot").GetProperty("id").GetString() ==
                "jarilo-start-2");
        }

        using (var response = await client.GetAsync("/admin/api/snapshots"))
        {
            using var json = await Json(response);
            var ids = json.RootElement.GetProperty("data").GetProperty("snapshots").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()).ToList();
            check("the snapshot list holds both snapshots", ids.Contains("jarilo-start") && ids.Contains("jarilo-start-2"));
        }

        int spawnedUid;
        using (var response = await client.PostAsJsonAsync($"/admin/api/snapshots/{snapshotId}/spawn", new { }))
        {
            using var json = await Json(response);
            var data = json.RootElement.GetProperty("data");
            spawnedUid = data.GetProperty("account").GetProperty("uid").GetInt32();
            var password = data.GetProperty("generated_password").GetString();
            var spawned = database.GetInstance<PlayerData>(spawnedUid);
            check("spawning a snapshot creates an account with a generated login and the snapshot's save",
                response.StatusCode == HttpStatusCode.OK && !string.IsNullOrEmpty(password) &&
                AccountData.GetAccountByUid(spawnedUid)?.VerifyPassword(password!) == true &&
                spawned is { Level: 42, Scoin: 1234, Name: "Source" } && !ReferenceEquals(spawned, sourcePlayer) &&
                database.GetInstance<MissionData>(spawnedUid)?.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept);
        }

        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{cloneUid}/snapshot/restore",
                   new { id = snapshotId }))
        {
            var restored = database.GetInstance<PlayerData>(cloneUid);
            var restoredMission = database.GetInstance<MissionData>(cloneUid);
            check("restoring a snapshot replaces the target's save in memory and in SQLite",
                response.StatusCode == HttpStatusCode.OK && restored is { Scoin: 1234, Name: "Source" } &&
                !ReferenceEquals(restored, clonePlayer) &&
                restoredMission?.GetMainMissionStatus(1020101) == MissionPhaseEnum.None &&
                LoadPlayers()[cloneUid].Scoin == 1234 && AccountData.GetAccountByUid(cloneUid)?.Username == "snapshot-clone");
        }

        // --- id validation and damaged files --------------------------------------------------------------
        foreach (var bad in new[] { "..%2F..%2FConfig", "a.b", "%2E%2E", "name%20with%20space" })
        {
            using var spawn = await client.PostAsJsonAsync($"/admin/api/snapshots/{bad}/spawn", new { });
            using var restore = await client.PostAsJsonAsync($"/admin/api/accounts/{cloneUid}/snapshot/restore",
                new { id = Uri.UnescapeDataString(bad) });
            check($"snapshot id {bad} is rejected", spawn.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound &&
                                                    restore.StatusCode == HttpStatusCode.BadRequest);
        }

        using (var response = await client.PostAsJsonAsync("/admin/api/snapshots/no-such-snapshot/spawn", new { }))
            check("spawning a missing snapshot is a 404", response.StatusCode == HttpStatusCode.NotFound);

        var directory = Path.Combine(ConfigManager.Config.Path.DatabasePath, "Snapshots");
        File.WriteAllText(Path.Combine(directory, "damaged.json"), "{ not json");
        accounts = AccountCount();
        using (var response = await client.PostAsJsonAsync("/admin/api/snapshots/damaged/spawn", new { }))
            check("a damaged snapshot file is a 400 and leaves no account behind",
                response.StatusCode == HttpStatusCode.BadRequest && AccountCount() == accounts);

        // A table this build does not know is skipped; an unreadable column payload is a 400, not a crash.
        var original = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            File.ReadAllText(Path.Combine(directory, "jarilo-start.json")))!;
        var tables = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(original["tables"].GetRawText())!;
        tables["NoSuchTable"] = new Dictionary<string, string?> { ["Uid"] = "1" };
        File.WriteAllText(Path.Combine(directory, "extra-table.json"),
            JsonSerializer.Serialize(new { format = 1, meta = original["meta"], tables }));
        using (var response = await client.PostAsJsonAsync("/admin/api/snapshots/extra-table/spawn", new { }))
            check("an unknown table in a snapshot is ignored", response.StatusCode == HttpStatusCode.OK);

        tables.Remove("NoSuchTable");
        tables["Mission"]["MainMissionInfo"] = Convert.ToBase64String([0xff, 0xff, 0xff, 0x7f, 0x01]);
        File.WriteAllText(Path.Combine(directory, "bad-column.json"),
            JsonSerializer.Serialize(new { format = 1, meta = original["meta"], tables }));
        var beforeRestore = database.GetInstance<PlayerData>(spawnedUid);
        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{spawnedUid}/snapshot/restore",
                   new { id = "bad-column" }))
            check("an unreadable column payload is a 400 and keeps the target's save",
                response.StatusCode == HttpStatusCode.BadRequest &&
                ReferenceEquals(database.GetInstance<PlayerData>(spawnedUid), beforeRestore));

        using (var response = await client.DeleteAsync($"/admin/api/snapshots/{snapshotId}"))
            check("deleting a snapshot removes its file",
                response.StatusCode == HttpStatusCode.OK && !File.Exists(Path.Combine(directory, snapshotId + ".json")));
        using (var response = await client.DeleteAsync($"/admin/api/snapshots/{snapshotId}"))
            check("deleting a missing snapshot is a 404", response.StatusCode == HttpStatusCode.NotFound);
        using (var response = await client.DeleteAsync("/admin/api/snapshots/a.b"))
            check("deleting an invalid snapshot id is a 400", response.StatusCode == HttpStatusCode.BadRequest);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static int AccountCount() => DatabaseHelper.GetAllInstance<AccountData>()!.Count;

    // Reads the Player table straight from the SQLite file, bypassing the in-memory cache.
    private static Dictionary<int, PlayerData> LoadPlayers()
    {
        var config = ConfigManager.Config;
        var path = Path.GetFullPath(Path.Combine(config.Path.DatabasePath, config.Database.DatabaseName));
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return EntityStores.Map[typeof(PlayerData)].LoadAll(connection).Cast<PlayerData>()
            .ToDictionary(player => player.Uid);
    }
}
