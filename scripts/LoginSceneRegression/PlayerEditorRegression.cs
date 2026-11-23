using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.GameServer.Game.Avatar;
using March7thHoney.Util;
using March7thHoney.WebServer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

// The admin player editor against real resources, through its HTTP API: profile, characters, items, teams, mission
// tools, position and raw tables on a provisioned save, the keep-save story advance, and undo. Every change is
// checked on the stored save and once more on a fresh load of it, the way the next login reads it.
internal static class PlayerEditorRegression
{
    private const int Uid = 60500;

    public static async Task Run(Action<string, bool> check)
    {
        AccountHelper.CreateAccount("editor-regression", "editor-regression@example.invalid", "editor-password-1", Uid, null);
        await StoryCheckpointProvisioner.ProvisionAsync(Uid, new StoryProvisionRequest { TargetMainMissionId = 1000301, StopMode = StoryStopModeEnum.After });

        await using var app = WebProgram.BuildWebApp([], 0, "http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
        var login = (await Data(await client.PostAsJsonAsync("/admin/api/login", new { admin_key = ConfigManager.Config.MuipServer.AdminKey })))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login["token"]!.GetValue<string>());
        var basePath = $"/admin/api/players/{Uid}/editor";
        var database = DatabaseHelper.Instance!;

        // --- profile ---------------------------------------------------------------------------------------------
        var profile = (await Data(await client.GetAsync(basePath + "/profile")))!;
        var version = profile["version"]!.GetValue<string>();
        using (var response = await client.PutAsJsonAsync(basePath + "/profile", Profile("Edited", 40, 123456, 3, version)))
        {
            var loaded = await Load();
            check("editor: a profile edit reaches the stored save and the next load",
                response.StatusCode == HttpStatusCode.OK &&
                database.GetInstance<PlayerData>(Uid) is { Name: "Edited", Level: 40, Scoin: 123456, WorldLevel: 3 } &&
                loaded.Data is { Name: "Edited", Level: 40, Scoin: 123456, WorldLevel: 3 });
        }

        var fresh = (await Data(await client.GetAsync(basePath + "/profile")))!["version"]!.GetValue<string>();
        using (var stale = await client.PutAsJsonAsync(basePath + "/profile", Profile("Stale", 41, 1, 3, version)))
        using (var level = await client.PutAsJsonAsync(basePath + "/profile", Profile("Edited", 0, 1, 3, fresh)))
        using (var name = await client.PutAsJsonAsync(basePath + "/profile", Profile(new string('n', 33), 40, 1, 3, fresh)))
            check("editor: a stale profile is a 409 and an invalid one a 400, both changing nothing",
                stale.StatusCode == HttpStatusCode.Conflict && level.StatusCode == HttpStatusCode.BadRequest &&
                name.StatusCode == HttpStatusCode.BadRequest && database.GetInstance<PlayerData>(Uid) is { Name: "Edited", Scoin: 123456 });

        // Teams hold the Trailblazer's base id 8001 whatever the gender; only the hero's path id is paired.
        foreach (var (gender, expected, parity) in new[] { ("man", Gender.Man, 1), ("woman", Gender.Woman, 0) })
        {
            var genderVersion = (await Data(await client.GetAsync(basePath + "/profile")))!["version"]!.GetValue<string>();
            using var response = await client.PutAsJsonAsync(basePath + "/profile", Profile("Edited", 40, 123456, 3, genderVersion, gender));
            var loaded = await Load();
            var heroSlots = loaded.LineupManager!.Data.Lineups.Values.SelectMany(lineup => lineup.BaseAvatars ?? [])
                .Where(slot => slot.SpecialAvatarId == 0 && TrailblazerAvatar.IsPath(slot.BaseAvatarId))
                .Select(slot => slot.BaseAvatarId).ToList();
            var teamView = (await Data(await client.GetAsync(basePath + "/lineups")))!["lineups"]!.AsArray()
                .SelectMany(lineup => lineup!["slots"]!.AsArray()).Select(slot => slot!["base_avatar_id"]!.GetValue<int>()).ToList();
            check($"editor: switching the Trailblazer to {gender} pairs the hero path and leaves team slots on 8001",
                response.StatusCode == HttpStatusCode.OK && loaded.Data.CurrentGender == expected &&
                loaded.Data.CurBasicType % 2 == parity && loaded.AvatarManager!.GetHero()?.AvatarId == loaded.Data.CurBasicType &&
                heroSlots.Count > 0 && heroSlots.All(id => id == 8001) && teamView.Contains(8001) &&
                !loaded.AvatarManager.IsHeroGenderPending);
        }

        // --- characters ------------------------------------------------------------------------------------------
        var catalog = (await Data(await client.GetAsync("/admin/api/editor/catalog")))!;
        var owned = database.GetInstance<AvatarData>(Uid)!.FormalAvatars.SelectMany(avatar => avatar.PathInfos.Keys).ToHashSet();
        var added = catalog["avatars"]!.AsArray()
            .Select(avatar => (Id: avatar!["id"]!.GetValue<int>(), Base: avatar["base_id"]!.GetValue<int>(), Rarity: avatar["rarity"]!.GetValue<int>()))
            .First(avatar => avatar.Id == avatar.Base && avatar.Rarity == 5 && avatar.Id < 8000 && !owned.Contains(avatar.Id)).Id;
        using (var response = await client.PostAsJsonAsync(basePath + "/avatars", new { avatar_id = added }))
            check($"editor: character {added} is added",
                response.StatusCode == HttpStatusCode.OK && database.GetInstance<AvatarData>(Uid)!.FindByAnyId(added) is { Level: 1 });
        using (var response = await client.PostAsJsonAsync(basePath + "/avatars", new { avatar_id = added }))
            check("editor: adding an owned character is a 400", response.StatusCode == HttpStatusCode.BadRequest);

        var avatars = (await Data(await client.GetAsync(basePath + "/avatars")))!;
        using (var response = await client.PutAsJsonAsync(basePath + $"/avatars/{added}",
                   new { level = 80, rank = 6, max_traces = true, version = avatars["version"]!.GetValue<string>() }))
        {
            var avatar = (await Load()).AvatarManager!.GetFormalAvatar(added);
            var view = (await Data(await client.GetAsync(basePath + "/avatars")))!["avatars"]!.AsArray()
                .First(item => item!["id"]!.GetValue<int>() == added)!;
            check("editor: a character edit sets level, ascension, eidolons and traces",
                response.StatusCode == HttpStatusCode.OK && avatar is { Level: 80, Promotion: 6 } &&
                avatar.PathInfos.Values.All(path => path.Rank == 6) && view["traces_maxed"]!.GetValue<bool>());
        }

        var avatarsVersion = (await Data(await client.GetAsync(basePath + "/avatars")))!["version"]!.GetValue<string>();
        using (var promotion = await client.PutAsJsonAsync(basePath + $"/avatars/{added}", new { level = 50, promotion = 1, rank = 0, version = avatarsVersion }))
        using (var trailblazer = await client.DeleteAsync(basePath + "/avatars/8001"))
            check("editor: an ascension the level does not allow and removing the Trailblazer are 400s",
                promotion.StatusCode == HttpStatusCode.BadRequest && trailblazer.StatusCode == HttpStatusCode.BadRequest &&
                database.GetInstance<AvatarData>(Uid)!.FindByAnyId(added) is { Level: 80 });

        // --- teams -----------------------------------------------------------------------------------------------
        var lineups = (await Data(await client.GetAsync(basePath + "/lineups")))!;
        var hero = database.GetInstance<AvatarData>(Uid)!.FindByAnyId(8001)!.BaseAvatarId;
        using (var response = await client.PutAsJsonAsync(basePath + "/lineups/1", new
               {
                   slots = new[] { new { base_avatar_id = added, special_avatar_id = 0 }, new { base_avatar_id = hero, special_avatar_id = 0 } },
                   make_current = true, version = lineups["version"]!.GetValue<string>()
               }))
        {
            var lineup = database.GetInstance<LineupData>(Uid)!;
            check("editor: a team edit sets the slots, leader and current team",
                response.StatusCode == HttpStatusCode.OK && lineup.CurLineup == 1 &&
                lineup.Lineups[1].BaseAvatars!.Select(slot => slot.BaseAvatarId).SequenceEqual([added, hero]) &&
                lineup.Lineups[1].LeaderAvatarId == added);
        }

        var lineupsVersion = (await Data(await client.GetAsync(basePath + "/lineups")))!["version"]!.GetValue<string>();
        using (var empty = await client.PutAsJsonAsync(basePath + "/lineups/1", new { slots = Array.Empty<object>(), version = lineupsVersion }))
        using (var twice = await client.PutAsJsonAsync(basePath + "/lineups/2", new
               {
                   slots = new[] { new { base_avatar_id = added, special_avatar_id = 0 }, new { base_avatar_id = added, special_avatar_id = 0 } },
                   version = lineupsVersion
               }))
            check("editor: emptying the current team and a character twice in a team are 400s",
                empty.StatusCode == HttpStatusCode.BadRequest && twice.StatusCode == HttpStatusCode.BadRequest);

        // --- inventory -------------------------------------------------------------------------------------------
        var cone = catalog["light_cones"]!.AsArray().Select(id => id!.GetValue<int>())
            .First(id => GameData.EquipmentConfigData.ContainsKey(id));
        var before = database.GetInstance<InventoryData>(Uid)!.EquipmentItems.Count;
        using (var response = await client.PostAsJsonAsync(basePath + "/inventory/grant", new { item_id = cone, count = 2, level = 80, rank = 5 }))
        {
            var grant = (await Data(response))!;
            var copies = database.GetInstance<InventoryData>(Uid)!.EquipmentItems.Where(item => item.ItemId == cone).ToList();
            check("editor: granting light cones adds the copies at the level and superimposition asked",
                grant["count"]!.GetValue<int>() == 2 && database.GetInstance<InventoryData>(Uid)!.EquipmentItems.Count == before + 2 &&
                copies.Count >= 2 && copies.TakeLast(2).All(item => item is { Level: 80, Rank: 5, Promotion: 6 }));
        }

        var relic = catalog["relics"]!.AsArray().Select(id => id!.GetValue<int>())
            .First(id => GameData.RelicConfigData.TryGetValue(id, out var config) && config.MaxLevel == 15);
        using (var response = await client.PostAsJsonAsync(basePath + "/inventory/grant", new { item_id = relic, count = 1, level = 15 }))
            check("editor: granting a relic adds it with rolled stats at the level asked",
                response.StatusCode == HttpStatusCode.OK &&
                database.GetInstance<InventoryData>(Uid)!.RelicItems.LastOrDefault() is { Level: 15, MainAffix: > 0 } last && last.ItemId == relic);

        var material = catalog["items"]!.AsArray().Select(id => id!.GetValue<int>())
            .First(id => GameData.ItemConfigData[id].ItemMainType == ItemMainTypeEnum.Material && GameData.ItemConfigData[id].PileLimit > 100);
        using (var set = await client.PutAsJsonAsync(basePath + $"/inventory/items/{material}", new { count = 77 }))
        {
            var count = database.GetInstance<InventoryData>(Uid)!.MaterialItems.Find(item => item.ItemId == material)?.Count;
            using var clear = await client.PutAsJsonAsync(basePath + $"/inventory/items/{material}", new { count = 0 });
            check("editor: a material count is set exactly, and 0 removes it",
                set.StatusCode == HttpStatusCode.OK && count == 77 && clear.StatusCode == HttpStatusCode.OK &&
                database.GetInstance<InventoryData>(Uid)!.MaterialItems.All(item => item.ItemId != material));
        }

        using (var response = await client.PostAsJsonAsync(basePath + "/inventory/grant", new { item_id = cone, count = 101 }))
            check("editor: granting more than 100 light cone copies is a 400", response.StatusCode == HttpStatusCode.BadRequest);

        var worn = database.GetInstance<InventoryData>(Uid)!.EquipmentItems.Last(item => item.ItemId == cone);
        using (var response = await client.PutAsJsonAsync(basePath + $"/inventory/light-cones/{worn.UniqueId}", new { level = 50, rank = 1 }))
            check("editor: a light cone edit sets level, the ascension it needs and superimposition",
                response.StatusCode == HttpStatusCode.OK &&
                database.GetInstance<InventoryData>(Uid)!.EquipmentItems.Find(item => item.UniqueId == worn.UniqueId) is { Level: 50, Promotion: 3, Rank: 1 });

        // Dress it and a relic through the gear route, then remove the light cone from the inventory.
        var dressRelic = database.GetInstance<InventoryData>(Uid)!.RelicItems.Last(item => item.ItemId == relic);
        var relicSlot = (int)GameData.RelicConfigData[relic].Type;
        var gearPath = $"/avatars/{added}/gear";
        using (var wrong = await client.PutAsJsonAsync(basePath + gearPath,
                   new { relics = new Dictionary<string, int> { [(relicSlot % 6 + 1).ToString()] = dressRelic.UniqueId } }))
        using (var response = await client.PutAsJsonAsync(basePath + gearPath,
                   new { light_cone_unique_id = worn.UniqueId, relics = new Dictionary<string, int> { [relicSlot.ToString()] = dressRelic.UniqueId } }))
        {
            var path = database.GetInstance<AvatarData>(Uid)!.FindByAnyId(added)!.GetCurPathInfo();
            var view = (await Data(await client.GetAsync(basePath + "/avatars")))!["avatars"]!.AsArray()
                .First(item => item!["id"]!.GetValue<int>() == added)!;
            check("editor: the gear route dresses a light cone and a relic in its own slot; a wrong slot is a 400",
                wrong.StatusCode == HttpStatusCode.BadRequest && response.StatusCode == HttpStatusCode.OK &&
                path.EquipId == worn.UniqueId && path.Relic.GetValueOrDefault(relicSlot) == dressRelic.UniqueId &&
                database.GetInstance<InventoryData>(Uid)!.EquipmentItems.Find(item => item.UniqueId == worn.UniqueId)!.EquipAvatar != 0 &&
                view["light_cone_unique_id"]!.GetValue<int>() == worn.UniqueId &&
                view["relics"]!.AsArray().Any(item => item!["unique_id"]!.GetValue<int>() == dressRelic.UniqueId));
        }

        using (var response = await client.PostAsJsonAsync(basePath + "/inventory/remove", new { unique_ids = new[] { worn.UniqueId } }))
            check("editor: removing a worn light cone deletes it and takes it off its wearer",
                response.StatusCode == HttpStatusCode.OK &&
                database.GetInstance<InventoryData>(Uid)!.EquipmentItems.All(item => item.UniqueId != worn.UniqueId) &&
                database.GetInstance<AvatarData>(Uid)!.FindByAnyId(added)!.PathInfos.Values.All(path => path.EquipId != worn.UniqueId));

        using (var response = await client.DeleteAsync(basePath + $"/avatars/{added}"))
        {
            var lineup = database.GetInstance<LineupData>(Uid)!.Lineups[1];
            check("editor: removing a character takes it out of its team and takes its relics off",
                response.StatusCode == HttpStatusCode.OK && database.GetInstance<AvatarData>(Uid)!.FindByAnyId(added) == null &&
                lineup.BaseAvatars!.All(slot => slot.BaseAvatarId != added) && lineup.LeaderAvatarId == hero &&
                database.GetInstance<InventoryData>(Uid)!.RelicItems.Find(item => item.UniqueId == dressRelic.UniqueId)?.EquipAvatar == 0);
        }

        // --- story, world and position ---------------------------------------------------------------------------
        var story = (await Data(await client.GetAsync(basePath + "/story")))!;
        var running = story["running"]!.AsArray().SelectMany(main => main!["sub_ids"]!.AsArray().Select(id => id!.GetValue<int>())).First();
        using (var response = await client.PostAsJsonAsync(basePath + "/story/missions", new { action = "finish_sub", mission_id = running }))
        {
            var result = (await Data(response))!;
            check($"editor: finishing running sub-mission {running} runs its hooks on the stored save",
                response.StatusCode == HttpStatusCode.OK && database.GetInstance<MissionData>(Uid)!.GetSubMissionStatus(running) == MissionPhaseEnum.Finish &&
                result["log"]!.AsArray().Count == 1 && result["story"]!["finished_subs"]!.GetValue<int>() > story["finished_subs"]!.GetValue<int>());
        }

        using (var advance = await client.PostAsJsonAsync(basePath + "/story/missions", new { action = "advance", count = 2 }))
        using (var unknown = await client.PostAsJsonAsync(basePath + "/story/missions", new { action = "explode" }))
        using (var missing = await client.PostAsJsonAsync(basePath + "/story/missions", new { action = "finish_main", mission_id = 1 }))
            check("editor: the story can be advanced; unknown actions and missions are 400s",
                advance.StatusCode == HttpStatusCode.OK && unknown.StatusCode == HttpStatusCode.BadRequest &&
                missing.StatusCode == HttpStatusCode.BadRequest);

        using (var response = await client.PostAsJsonAsync(basePath + "/story/teleports", new { mode = "all" }))
            check("editor: unlocking every teleport records them",
                response.StatusCode == HttpStatusCode.OK && (await Data(response))!["count"]!.GetValue<int>() > 0);
        using (var response = await client.PostAsync(basePath + "/story/tutorials", null))
        {
            var after = (await Data(await client.GetAsync(basePath + "/story")))!;
            check("editor: finishing tutorials marks every tutorial finished",
                response.StatusCode == HttpStatusCode.OK &&
                after["tutorials_finished"]!.GetValue<int>() == after["tutorials_total"]!.GetValue<int>());
        }

        using (var response = await client.PutAsJsonAsync(basePath + "/position", new { entry_id = PlayerInstance.DefaultSpawnEntryId }))
        using (var bad = await client.PutAsJsonAsync(basePath + "/position", new { entry_id = 1 }))
            check("editor: the player can be moved to a map entrance; an unknown one is a 400",
                response.StatusCode == HttpStatusCode.OK && database.GetInstance<PlayerData>(Uid)!.EntryId == PlayerInstance.DefaultSpawnEntryId &&
                bad.StatusCode == HttpStatusCode.BadRequest);

        // --- raw tables ------------------------------------------------------------------------------------------
        var tables = (await Data(await client.GetAsync(basePath + "/raw")))!["tables"]!.AsArray()
            .Where(table => table!["present"]!.GetValue<bool>()).Select(table => table!["table"]!.GetValue<string>()).ToList();
        var readOnly = new List<string>();
        foreach (var table in tables)
            if ((await Data(await client.GetAsync(basePath + $"/raw/{table}")))?["editable"]?.GetValue<bool>() != true)
                readOnly.Add(table);
        Console.WriteLine($"  editor raw tables: {tables.Count} present, read-only [{string.Join(", ", readOnly)}]");
        check("editor: the core save tables round-trip through raw JSON",
            new[] { "Player", "Mission", "InventoryData", "Avatar", "Lineup" }.All(table => tables.Contains(table) && !readOnly.Contains(table)));

        // --- keep-save story advance and undo ---------------------------------------------------------------------
        var granted = database.GetInstance<InventoryData>(Uid)!.EquipmentItems.Count(item => item.ItemId == cone);
        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{Uid}/provision-story", new { preset = "jarilo-vi", keep_save = true }))
        {
            var loaded = await Load();
            check("editor: advancing the save to Jarilo-VI keeps its name, items and finished missions",
                response.StatusCode == HttpStatusCode.OK &&
                loaded.MissionManager!.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept &&
                loaded.MissionManager.GetMainMissionStatus(1000301) == MissionPhaseEnum.Finish &&
                loaded.Data.Name == "Edited" &&
                loaded.InventoryManager!.Data.EquipmentItems.Count(item => item.ItemId == cone) == granted);
        }

        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{Uid}/provision-story", new { preset = "warp-tutorial", keep_save = true }))
            check("editor: advancing to a point the save is past is refused and changes nothing",
                response.StatusCode == HttpStatusCode.BadRequest &&
                database.GetInstance<MissionData>(Uid)!.GetMainMissionStatus(1010002) == MissionPhaseEnum.Accept);
        using (var response = await client.PostAsJsonAsync("/admin/api/accounts/provision-story", new { preset = "jarilo-vi", keep_save = true }))
            check("editor: keep_save without an account is a 400", response.StatusCode == HttpStatusCode.BadRequest);

        using (var response = await client.PostAsync(basePath + "/undo", null))
        {
            var loaded = await Load();
            check("editor: undo puts the save from before the story advance back",
                response.StatusCode == HttpStatusCode.OK &&
                loaded.MissionManager!.GetMainMissionStatus(1010002) != MissionPhaseEnum.Accept &&
                loaded.MissionManager.GetMainMissionStatus(1000301) == MissionPhaseEnum.Finish && loaded.Data.Name == "Edited");
        }

        await app.StopAsync();
    }

    private static object Profile(string name, int level, int scoin, int worldLevel, string? version, string gender = "keep") => new
    {
        name, signature = "editor", head_icon = 208002, level, exp = 0, world_level = worldLevel, scoin, hcoin = 10,
        mcoin = 0, stamina = 240, stamina_reserve = 0.0, month_card_expire = 0, gender, version
    };

    // A fresh load of the stored save, as the next login would see it.
    private static async Task<PlayerInstance> Load()
    {
        var player = new PlayerInstance(DatabaseHelper.Instance!.GetInstance<PlayerData>(Uid)!);
        await player.InitializeAsync();
        return player;
    }

    private static async Task<JsonNode?> Data(HttpResponseMessage response)
    {
        using (response)
            return JsonNode.Parse(await response.Content.ReadAsStringAsync())?["data"];
    }
}
