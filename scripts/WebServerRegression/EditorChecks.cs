using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Generated;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Util;
using Microsoft.Data.Sqlite;

// The player editor through the admin API without game resources: version checks, the offline-edit lease, a
// failing edit that puts the save back, raw table edits that replace only their table, the one-step undo, and
// delete-on-logout test accounts. Edits that load the save into a player (profile, characters, items, missions)
// need resources and are covered by LoginSceneRegression.
internal static class EditorChecks
{
    private const int Uid = 20501;
    private const int ThrowawayUid = 20502;

    public static async Task Run(HttpClient client, Action<string, bool> check)
    {
        var database = DatabaseHelper.Instance!;
        AccountHelper.CreateAccount("editor-target", "editor-target@example.invalid", "editor-password-1", Uid, null);
        var player = database.GetInstanceOrCreateNew<PlayerData>(Uid);
        player.Name = "Editor";
        player.Level = 10;
        player.Scoin = 5;
        var mission = database.GetInstanceOrCreateNew<MissionData>(Uid);
        mission.SetMainMissionStatus(1010002, MissionPhaseEnum.Accept);
        DatabaseHelper.MarkDirty(Uid);
        database.SaveUidData(Uid);
        var basePath = $"/admin/api/players/{Uid}/editor";

        using (var response = await client.GetAsync(basePath))
        {
            var data = (await Data(response))!;
            check("editor summary reports an offline save with no undo point",
                response.StatusCode == HttpStatusCode.OK && data["exists"]!.GetValue<bool>() &&
                !data["online"]!.GetValue<bool>() && data["undo"] == null && !data["delete_on_logout"]!.GetValue<bool>());
        }

        // --- profile: stale version, a failing load, lease -----------------------------------------------------
        var version = (await Data(await client.GetAsync(basePath + "/profile")))!["version"]!.GetValue<string>();
        player.Signature = "changed meanwhile";
        using (var response = await client.PutAsJsonAsync(basePath + "/profile", Profile("Stale", 30, 1, version)))
            check("an edit naming an old version is a 409 and changes nothing",
                response.StatusCode == HttpStatusCode.Conflict && database.GetInstance<PlayerData>(Uid) is { Name: "Editor", Level: 10 });

        // Without resources the save cannot be loaded into a player, which exercises the rollback of a failed edit.
        var fresh = (await Data(await client.GetAsync(basePath + "/profile")))!["version"]!.GetValue<string>();
        using (var response = await client.PutAsJsonAsync(basePath + "/profile", Profile("Edited", 20, 777, fresh)))
            check("an edit that fails puts the save back and keeps no undo point",
                response.StatusCode == HttpStatusCode.InternalServerError &&
                database.GetInstance<PlayerData>(Uid) is { Name: "Editor", Level: 10, Scoin: 5 } &&
                database.GetInstance<MissionData>(Uid)?.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept &&
                AccountSnapshotService.GetUndoPoint(Uid) == null);

        // --- raw tables -------------------------------------------------------------------------------------
        using (var response = await client.GetAsync(basePath + "/raw"))
        {
            var tables = (await Data(response))!["tables"]!.AsArray();
            check("raw lists the save's tables and marks the ones it holds",
                tables.Any(table => table!["table"]!.GetValue<string>() == "Player" && table["present"]!.GetValue<bool>()) &&
                tables.Any(table => table!["table"]!.GetValue<string>() == "Mission" && table["present"]!.GetValue<bool>()) &&
                tables.All(table => table!["table"]!.GetValue<string>() != "Account"));
        }

        var raw = (await Data(await client.GetAsync(basePath + "/raw/Player")))!;
        check("the Player table reads back from its JSON exactly, so it is editable", raw["editable"]!.GetValue<bool>());
        var document = JsonNode.Parse(raw["json"]!.GetValue<string>())!;
        document["Scoin"] = 4242;
        document["Uid"] = 1; // the row always lands on the edited uid
        var missionBefore = database.GetInstance<MissionData>(Uid);
        using (var response = await client.PutAsJsonAsync(basePath + "/raw/Player",
                   new { json = document.ToJsonString(), version = raw["version"]!.GetValue<string>() }))
            check("a raw edit replaces only its table",
                response.StatusCode == HttpStatusCode.OK && database.GetInstance<PlayerData>(Uid) is { Scoin: 4242, Name: "Editor" } &&
                database.GetInstance<PlayerData>(1) == null &&
                database.GetInstance<MissionData>(Uid)?.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept &&
                !ReferenceEquals(database.GetInstance<MissionData>(Uid), missionBefore) && StoredPlayer(Uid)?.Scoin == 4242);
        using (var response = await client.PutAsJsonAsync(basePath + "/raw/Player",
                   new { json = document.ToJsonString(), version = raw["version"]!.GetValue<string>() }))
            check("a raw edit on an old version is a 409", response.StatusCode == HttpStatusCode.Conflict);
        var current = (await Data(await client.GetAsync(basePath + "/raw/Player")))!["version"]!.GetValue<string>();
        using (var response = await client.PutAsJsonAsync(basePath + "/raw/Player", new { json = "{ \"Scoin\": ", version = current }))
            check("malformed raw JSON is a 400 and changes nothing",
                response.StatusCode == HttpStatusCode.BadRequest && database.GetInstance<PlayerData>(Uid)!.Scoin == 4242);
        using (var response = await client.GetAsync(basePath + "/raw/NoSuchTable"))
            check("an unknown raw table is a 404", response.StatusCode == HttpStatusCode.NotFound);

        // --- undo ------------------------------------------------------------------------------------------
        using (var response = await client.GetAsync(basePath))
            check("the save from before the raw edit is the undo point",
                (await Data(response))!["undo"]?["label"]?.GetValue<string>() == "raw edit of Player");
        using (var response = await client.PostAsync(basePath + "/undo", null))
            check("undo puts the save from before the last edit back",
                response.StatusCode == HttpStatusCode.OK && database.GetInstance<PlayerData>(Uid) is { Scoin: 5, Name: "Editor" } &&
                StoredPlayer(Uid)?.Scoin == 5 &&
                database.GetInstance<MissionData>(Uid)?.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept);
        using (var response = await client.PostAsync(basePath + "/undo", null))
            check("undoing an undo redoes the edit",
                response.StatusCode == HttpStatusCode.OK && database.GetInstance<PlayerData>(Uid)!.Scoin == 4242);

        // --- lease and unknown targets ------------------------------------------------------------------------
        using (OfflinePlayerSession.Acquire(Uid))
        {
            var latest = (await Data(await client.GetAsync(basePath + "/profile")))!["version"]!.GetValue<string>();
            using var response = await client.PutAsJsonAsync(basePath + "/profile", Profile("Busy", 20, 1, latest));
            using var undo = await client.PostAsync(basePath + "/undo", null);
            check("an edit or undo while another tool holds the save is a 409",
                response.StatusCode == HttpStatusCode.Conflict && undo.StatusCode == HttpStatusCode.Conflict &&
                database.GetInstance<PlayerData>(Uid) is { Name: "Editor", Scoin: 4242 });
        }

        using (var response = await client.PutAsJsonAsync("/admin/api/players/99999/editor/profile", Profile("x", 1, 0, null)))
            check("editing an unknown account is a 404", response.StatusCode == HttpStatusCode.NotFound);
        using (var response = await client.PostAsync("/admin/api/players/99999/editor/undo", null))
            check("undo without an undo point is a 404", response.StatusCode == HttpStatusCode.NotFound);

        // --- delete-on-logout test accounts -----------------------------------------------------------------
        var throwaway = AccountHelper.CreateAccount("editor-throwaway", "editor-throwaway@example.invalid", "throwaway-password-1",
            ThrowawayUid, null);
        database.GetInstanceOrCreateNew<PlayerData>(ThrowawayUid).Name = "Throwaway";
        DatabaseHelper.MarkDirty(ThrowawayUid);
        database.SaveUidData(ThrowawayUid);
        using (var response = await client.PutAsJsonAsync($"/admin/api/accounts/{ThrowawayUid}/delete-on-logout", new { enabled = true }))
        {
            var data = (await Data(response))!;
            check("an account can be marked delete-on-logout",
                response.StatusCode == HttpStatusCode.OK && data["account"]!["delete_on_logout"]!.GetValue<bool>() &&
                throwaway.DeleteOnLogout);
        }

        AccountDeletion.OnSessionEnded(ThrowawayUid, true);
        check("a session replaced by a new login does not delete the account", !DatabaseHelper.IsUidDeleted(ThrowawayUid));
        using (OfflinePlayerSession.Acquire(ThrowawayUid))
            AccountDeletion.OnSessionEnded(ThrowawayUid, false);
        check("a session a tool stopped does not delete the account", !DatabaseHelper.IsUidDeleted(ThrowawayUid));
        AccountDeletion.OnSessionEnded(Uid, false);
        check("an ordinary account survives its logoff", !DatabaseHelper.IsUidDeleted(Uid));
        AccountDeletion.OnSessionEnded(ThrowawayUid, false);
        check("a delete-on-logout account is deleted with its save on logoff",
            DatabaseHelper.IsUidDeleted(ThrowawayUid) && AccountData.GetAccountByUid(ThrowawayUid) == null &&
            database.GetInstance<PlayerData>(ThrowawayUid) == null && StoredPlayer(ThrowawayUid) == null);

        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{Uid}/clone",
                   new { username = "editor-clone", password = "clone-password-2", delete_on_logout = true }))
        {
            var data = (await Data(response))!;
            var cloneUid = data["account"]!["uid"]!.GetValue<int>();
            check("a test account can be created as delete-on-logout",
                response.StatusCode == HttpStatusCode.OK && AccountData.GetAccountByUid(cloneUid)?.DeleteOnLogout == true &&
                StoredAccount(cloneUid)?.DeleteOnLogout == true);
        }
    }

    private static object Profile(string name, int level, int scoin, string? version) => new
    {
        name, signature = "", head_icon = 208002, level, exp = 0, world_level = 2, scoin, hcoin = 0, mcoin = 0,
        stamina = 240, stamina_reserve = 0.0, month_card_expire = 0, version
    };

    private static async Task<JsonNode?> Data(HttpResponseMessage response)
    {
        using (response)
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())?["data"];
    }

    private static PlayerData? StoredPlayer(int uid) => Load<PlayerData>().FirstOrDefault(row => row.Uid == uid);

    private static AccountData? StoredAccount(int uid) => Load<AccountData>().FirstOrDefault(row => row.Uid == uid);

    // Reads a table straight from the SQLite file, bypassing the in-memory cache.
    private static List<T> Load<T>() where T : BaseDatabaseDataHelper
    {
        var config = ConfigManager.Config;
        var path = Path.GetFullPath(Path.Combine(config.Path.DatabasePath, config.Database.DatabaseName));
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        return EntityStores.Map[typeof(T)].LoadAll(connection).Cast<T>().ToList();
    }
}
