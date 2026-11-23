using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using March7thHoney.Data;
using March7thHoney.Data.Config;
using MissionInfo = March7thHoney.Data.Config.MissionInfo;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Player;
using March7thHoney.Enums.Mission;

// Story checkpoint API over a two-mission synthetic story (this runner loads no resource cache): catalog shape,
// request validation, and both provision routes end to end. The real story graph is covered by LoginSceneRegression.
internal static class StoryChecks
{
    private const int First = 9900001;
    private const int Second = 9900002;

    public static async Task Run(HttpClient client, string address, Action<string, bool> check)
    {
        var savedGates = GameData.WarpStoryGatesConfig;
        GameData.WarpStoryGatesConfig = new WarpStoryGatesConfig
        {
            Gates = [new WarpStoryGate { Version = "9.9", Chapters = ["Synthetic"], MainMissions = [First, Second] }]
        };
        AddMain(First, [new MissionParam { Type = MissionTakeTypeEnum.Auto }], 20001001);
        AddMain(Second, [new MissionParam { Type = MissionTakeTypeEnum.MultiSequence, Value = First }], 20002001);
        try
        {
            await Scenarios(client, address, check);
        }
        finally
        {
            GameData.WarpStoryGatesConfig = savedGates;
            foreach (var id in new[] { First, Second })
            {
                GameData.MainMissionData.Remove(id);
                foreach (var sub in new[] { id * 100 + 1, id * 100 + 2 }) GameData.SubMissionInfoData.TryRemove(sub, out _);
            }
        }
    }

    // Two talk subs in sequence; the second closes the main.
    private static void AddMain(int id, List<MissionParam> take, int floor)
    {
        var main = new MainMissionExcel
            { MainMissionID = id, Type = MainMissionTypeEnum.Main, TakeParam = take, BeginParam = [] };
        main.SetMissionInfo(new MissionInfo
        {
            MainMissionID = id,
            StartSubMissionList = [id * 100 + 1],
            FinishSubMissionList = [id * 100 + 2],
            SubMissionList =
            [
                new SubMissionInfo
                {
                    ID = id * 100 + 1, MainMissionID = id, TakeType = SubMissionTakeTypeEnum.Auto,
                    FinishType = MissionFinishTypeEnum.Talk, LevelFloorID = floor
                },
                new SubMissionInfo
                {
                    ID = id * 100 + 2, MainMissionID = id, TakeType = SubMissionTakeTypeEnum.AnySequence,
                    TakeParamIntList = [id * 100 + 1], FinishType = MissionFinishTypeEnum.Talk, LevelFloorID = floor,
                    WayPointFloorID = floor, WayPointGroupID = 3
                }
            ]
        });
        GameData.MainMissionData[id] = main;
    }

    private static async Task Scenarios(HttpClient client, string address, Action<string, bool> check)
    {
        using (var anonymous = new HttpClient { BaseAddress = new Uri(address) })
        {
            using var catalog = await anonymous.GetAsync("/admin/api/story/catalog");
            using var provision = await anonymous.PostAsJsonAsync("/admin/api/accounts/provision-story",
                new { target_main_mission_id = First });
            check("story catalog and provisioning require authentication",
                catalog.StatusCode == HttpStatusCode.Unauthorized && provision.StatusCode == HttpStatusCode.Unauthorized);
        }

        using (var response = await client.GetAsync("/admin/api/story/catalog"))
        {
            using var json = await Json(response);
            var data = json.RootElement.GetProperty("data");
            var version = data.GetProperty("versions").EnumerateArray().Single();
            var missions = version.GetProperty("main_missions").EnumerateArray().ToList();
            var first = missions.First();
            check("story catalog lists versions, chapters and main missions in gate order with their sub-missions",
                response.StatusCode == HttpStatusCode.OK && version.GetProperty("version").GetString() == "9.9" &&
                missions.Select(m => m.GetProperty("id").GetInt32()).SequenceEqual([First, Second]) &&
                first.GetProperty("chapter_name").GetString() == "Synthetic" &&
                first.GetProperty("floor_id").GetInt32() == 20001001 &&
                first.GetProperty("type").GetString() == "Main" &&
                first.GetProperty("sub_missions").EnumerateArray().Select(s => s.GetProperty("id").GetInt32())
                    .SequenceEqual([First * 100 + 1, First * 100 + 2]) &&
                first.GetProperty("sub_missions")[1].GetProperty("way_point_group_id").GetInt32() == 3);
            check("story catalog offers only presets whose missions exist",
                data.GetProperty("presets").GetArrayLength() == 0);
        }

        var accounts = AccountCount();
        foreach (var (name, body) in new (string, object)[]
                 {
                     ("an unknown main mission", new { target_main_mission_id = 123 }),
                     ("a sub-mission of another main", new { target_main_mission_id = First, target_sub_mission_id = Second * 100 + 1 }),
                     ("an unknown stop mode", new { target_main_mission_id = First, stop_mode = "during" }),
                     ("an unknown gender", new { target_main_mission_id = First, trailblazer_gender = "robot" }),
                     ("an unknown preset", new { preset = "no-such-preset" })
                 })
        {
            using var response = await client.PostAsJsonAsync("/admin/api/accounts/provision-story", body);
            check($"provisioning with {name} is a 400 that creates no account",
                response.StatusCode == HttpStatusCode.BadRequest && AccountCount() == accounts);
        }

        // This runner has no resource cache, so a fresh player cannot be bootstrapped and the replay fails: the
        // account made for the request must be retired again, not left behind with a half-built save. The
        // successful replay over HTTP runs in LoginSceneRegression against the real story.
        using (var response = await client.PostAsJsonAsync("/admin/api/accounts/provision-story",
                   new { target_main_mission_id = Second, username = "story-synthetic" }))
            check("a provision that fails leaves no account behind",
                response.StatusCode == HttpStatusCode.BadRequest && AccountCount() == accounts &&
                AccountData.GetAccountByUserName("story-synthetic") == null);

        // The same failure on an existing account puts its previous save back (SnapshotChecks' source account).
        using (var response = await client.PostAsJsonAsync("/admin/api/accounts/20001/provision-story",
                   new { target_main_mission_id = First }))
            check("a provision that fails on an existing account restores its previous save",
                response.StatusCode == HttpStatusCode.BadRequest &&
                DatabaseHelper.Instance!.GetInstance<PlayerData>(20001) is { Scoin: 1234, Level: 42 });

        using (var response = await client.PostAsJsonAsync("/admin/api/accounts/99999/provision-story",
                   new { target_main_mission_id = First }))
            check("provision-story on a missing account is a 404", response.StatusCode == HttpStatusCode.NotFound);
    }

    private static async Task<JsonDocument> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static int AccountCount() => DatabaseHelper.GetAllInstance<AccountData>()!.Count;
}
