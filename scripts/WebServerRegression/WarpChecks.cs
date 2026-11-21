using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Enums;
using March7thHoney.GameServer.Game.Gacha;
using March7thHoney.Util;

internal static class WarpChecks
{
    private const long Day = 86400;

    // Synthetic history: three phases. 2903 runs through 9.0-1 and 9.0-2, so it is in 9.0-2's Overlap lineup but
    // not its Started one; 2905 features an item the game data lacks and must stay out of the pool. All windows lie
    // in the past, like the real history file.
    private const long Phase1 = 1_700_000_000;
    private const long Phase2 = Phase1 + 21 * Day;
    private const long Phase3 = Phase2 + 21 * Day;

    private static readonly int[] SyntheticItems =
        [1901, 1902, 1903, 1904, 1911, 1912, 1913, 23901, 23902, 21911, 21912, 21913];

    // Uses only in-process game data, the scratch Config.json and the loopback HTTP server. `client` carries
    // the admin session; `address` is used for an unauthenticated client.
    public static async Task Run(HttpClient client, string address, Action<string, bool> check)
    {
        var config = ConfigManager.Config;
        var originalBanners = GameData.BannersConfig;
        var originalHistory = GameData.BannerHistoryConfig;
        var originalNames = GameData.WarpItemNamesConfig;
        var addedItems = new HashSet<int>();
        try
        {
            foreach (var id in SyntheticItems)
                if (GameData.ItemConfigData.TryAdd(id, new ItemConfigExcel { ID = id }))
                    addedItems.Add(id);

            var now = WarpSchedule.Now();
            config.ServerOption.Warps = new WarpScheduleOption();
            GameData.BannersConfig = new BannersConfig
            {
                Banners =
                [
                    new BannerConfig { GachaId = 1001, GachaType = GachaTypeEnum.Normal },
                    Banner(2139, GachaTypeEnum.AvatarUp, now - Day, now + 30 * Day, 1903, [1911, 1912, 1913])
                ]
            };
            GameData.BannerHistoryConfig = new BannersConfig
            {
                Banners =
                [
                    History(2901, GachaTypeEnum.AvatarUp, "9.0", 1, Phase1, Phase2 - 60, 1901, [1911, 1912, 1913]),
                    History(3901, GachaTypeEnum.WeaponUp, "9.0", 1, Phase1, Phase2 - 60, 23901, [21911, 21912, 21913]),
                    History(2903, GachaTypeEnum.AvatarUp, "9.0", 1, Phase1, Phase3 - 60, 1903, [1911, 1912, 1913]),
                    History(2902, GachaTypeEnum.AvatarUp, "9.0", 2, Phase2, Phase3 - 60, 1902, [1911, 1912, 1913]),
                    History(3902, GachaTypeEnum.WeaponUp, "9.0", 2, Phase2, Phase3 - 60, 23902, [21911, 21912, 21913]),
                    History(2904, GachaTypeEnum.AvatarUp, "9.1", 1, Phase3, Phase3 + 21 * Day, 1904, [1911, 1912, 1913]),
                    History(2905, GachaTypeEnum.AvatarUp, "9.1", 1, Phase3, Phase3 + 21 * Day, 1999, [1911, 1912, 1913])
                ]
            };

            await RunSynthetic(client, address, check, now);
            await RunRealHistory(client, check, addedItems);
        }
        finally
        {
            config.ServerOption.Warps = new WarpScheduleOption();
            ConfigManager.SaveConfig();
            GameData.BannersConfig = originalBanners;
            GameData.BannerHistoryConfig = originalHistory;
            GameData.WarpItemNamesConfig = originalNames;
            foreach (var id in addedItems) GameData.ItemConfigData.Remove(id);
        }
    }

    private static async Task RunSynthetic(HttpClient client, string address, Action<string, bool> check, long now)
    {
        var option = ConfigManager.Config.ServerOption.Warps;

        using (var anonymous = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) })
        {
            using var denied = await anonymous.GetAsync("/admin/api/warps");
            check("warps requires authentication", denied.StatusCode == HttpStatusCode.Unauthorized);
            using var deniedMode = await anonymous.PostAsJsonAsync("/admin/api/warps/mode", new { mode = "Rotation" });
            check("warp mode change requires authentication",
                deniedMode.StatusCode == HttpStatusCode.Unauthorized && option.Mode == "Static");
        }

        // --- Static: Banners.json as written ---
        using (var warps = await GetData(client, "/admin/api/warps"))
        {
            var data = warps.RootElement.GetProperty("data");
            check("warps static mode serves Banners.json limited banners",
                data.GetProperty("mode").GetString() == "Static" &&
                Ids(data, "scheduled").SequenceEqual([2139]) && Ids(data, "permanent").SequenceEqual([1001]));
            check("warps reports the eligible pool size and the ineligible banner",
                data.GetProperty("pool_size").GetInt32() == 6 && data.GetProperty("phase_count").GetInt32() == 3 &&
                IntList(data.GetProperty("ineligible")).SequenceEqual([2905]));
        }

        check("static mode serves Banners.json limited and permanent banners",
            WarpSchedule.GetActiveBanners(now, now).Select(b => b.GachaId).Order().SequenceEqual([1001, 2139]));

        // --- history pool ---
        using (var pool = await GetData(client, "/admin/api/warps/pool"))
        {
            var data = pool.RootElement.GetProperty("data");
            var phases = data.GetProperty("phases").EnumerateArray()
                .ToDictionary(p => p.GetProperty("key").GetString()!, p => IntList(p.GetProperty("banner_ids")));
            check("warp pool groups phases in order",
                phases.Keys.SequenceEqual(["9.0-1", "9.0-2", "9.1-1"]));
            check("warp pool phases with PhaseLineup Started hold only the banners opened in them",
                phases["9.0-1"].SequenceEqual([2901, 2903, 3901]) && phases["9.0-2"].SequenceEqual([2902, 3902]));
            check("warp pool leaves out a banner whose item is missing",
                phases["9.1-1"].SequenceEqual([2904]) && !Ids(data, "banners").Contains(2905));
            var first = data.GetProperty("banners").EnumerateArray().Single(b => b.GetProperty("id").GetInt32() == 2901);
            check("warp pool rows carry history windows and the default light cone pair",
                first.GetProperty("begin_time").GetInt64() == Phase1 && !first.GetProperty("running").GetBoolean() &&
                first.GetProperty("default_pair_id").GetInt32() == 3901 &&
                first.GetProperty("version").GetString() == "9.0" && first.GetProperty("phase").GetInt32() == 1);
        }

        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Overlap);
        using (var pool = await GetData(client, "/admin/api/warps/pool"))
        {
            var phases = pool.RootElement.GetProperty("data").GetProperty("phases").EnumerateArray()
                .ToDictionary(p => p.GetProperty("key").GetString()!, p => IntList(p.GetProperty("banner_ids")));
            check("warp pool phases with PhaseLineup Overlap list a spanning banner in every phase it overlapped",
                phases["9.0-1"].SequenceEqual([2901, 2903, 3901]) && phases["9.0-2"].SequenceEqual([2902, 2903, 3902]));
        }
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);

        // --- Rotation ---
        using (var response = await client.PostAsJsonAsync("/admin/api/warps/mode", new { mode = "Bogus" }))
            check("warp mode rejects an unknown mode",
                response.StatusCode == HttpStatusCode.BadRequest && option.Mode == "Static");

        using (var rotation = await SendData(client, HttpMethod.Post, "/admin/api/warps/mode", new { mode = "Rotation" }))
        {
            var data = rotation.RootElement.GetProperty("data");
            var state = data.GetProperty("rotation");
            check("rotation mode starts today from the oldest phase",
                data.GetProperty("mode").GetString() == "Rotation" &&
                state.GetProperty("start").GetString() == LocalDate(now) &&
                state.GetProperty("current_phase").GetString() == "9.0-1" &&
                state.GetProperty("next_phase").GetString() == "9.0-2");
            check("rotation serves the phase lineup in the current period",
                Ids(data, "scheduled").SequenceEqual([2901, 2903, 3901]) &&
                InPeriod(data, data.GetProperty("scheduled")));
        }

        var rotationActive = WarpSchedule.GetActiveBanners(now, now).Select(b => b.GachaId).ToList();
        check("rotation does not serve Banners.json limited banners",
            !rotationActive.Contains(2139) && rotationActive.Contains(1001) && rotationActive.Contains(2901));

        using (var response = await client.PostAsJsonAsync("/admin/api/warps/rotation", new { phase = "0.0-9" }))
            check("rotation restart rejects an unknown phase",
                response.StatusCode == HttpStatusCode.BadRequest && option.RotationStartPhase == "");

        using (var restart = await SendData(client, HttpMethod.Post, "/admin/api/warps/rotation", new { phase = "9.0-2" }))
        {
            var data = restart.RootElement.GetProperty("data");
            var state = data.GetProperty("rotation");
            check("rotation restart switches the current phase",
                state.GetProperty("current_phase").GetString() == "9.0-2" &&
                state.GetProperty("start_phase").GetString() == "9.0-2" &&
                Ids(data, "scheduled").SequenceEqual([2902, 3902]) &&
                InPeriod(data, data.GetProperty("scheduled")));
        }

        // --- settings registry ---
        using (var settings = await SendData(client, HttpMethod.Put, "/admin/api/settings", new
               {
                   values = new Dictionary<string, string>
                   {
                       ["Warps.RotationStartPhase"] = "0.0-9", ["Warps.RotationStart"] = "not-a-date",
                       ["Warps.Mode"] = "Bogus", ["Warps.RotationDays"] = "0"
                   }
               }))
        {
            var data = settings.RootElement.GetProperty("data");
            check("settings reject invalid warp values",
                data.GetProperty("applied").GetInt32() == 0 && data.GetProperty("errors").GetArrayLength() == 4 &&
                option.RotationStartPhase == "9.0-2" && option.Mode == "Rotation" && option.RotationDays == 7);
            check("settings list the warps group",
                data.GetProperty("fields").EnumerateArray().Count(f => f.GetProperty("group").GetString() == "warps") == 7);
        }

        using (var settings = await SendData(client, HttpMethod.Put, "/admin/api/settings",
                   new { values = new Dictionary<string, string> { ["Warps.RotationStartPhase"] = "9.1-1" } }))
            check("settings accept a known rotation start phase",
                settings.RootElement.GetProperty("data").GetProperty("applied").GetInt32() == 1 &&
                WarpSchedule.GetRotation(now).Phase?.Key == "9.1-1");
        // Back to 9.0-2 so Manual is seeded from that lineup below.
        (await SendData(client, HttpMethod.Put, "/admin/api/settings",
            new { values = new Dictionary<string, string> { ["Warps.RotationStartPhase"] = "9.0-2" } })).Dispose();

        // Story filter and phase lineup keys: round-trip, then back to the defaults.
        using (var settings = await SendData(client, HttpMethod.Put, "/admin/api/settings", new
               {
                   values = new Dictionary<string, string>
                   {
                       ["Warps.Story"] = "true", ["Warps.PhaseLineup"] = "overlap", ["Warps.CollabOwnPhaseOnly"] = "false"
                   }
               }))
        {
            var data = settings.RootElement.GetProperty("data");
            var fields = SettingValues(data);
            check("settings round-trip Warps.Story, Warps.PhaseLineup and Warps.CollabOwnPhaseOnly",
                data.GetProperty("applied").GetInt32() == 3 && data.GetProperty("errors").GetArrayLength() == 0 &&
                option.Story && option.PhaseLineup == "Overlap" && !option.CollabOwnPhaseOnly &&
                fields.GetValueOrDefault("Warps.Story") == "true" &&
                fields.GetValueOrDefault("Warps.PhaseLineup") == "Overlap" &&
                fields.GetValueOrDefault("Warps.CollabOwnPhaseOnly") == "false");
        }

        using (var warps = await GetData(client, "/admin/api/warps"))
        {
            var data = warps.RootElement.GetProperty("data");
            check("warps reports the story filter and phase lineup settings",
                data.GetProperty("story").GetBoolean() && data.GetProperty("story_active").GetBoolean() &&
                data.GetProperty("phase_lineup").GetString() == "Overlap" &&
                !data.GetProperty("collab_own_phase_only").GetBoolean());
        }

        using (var settings = await SendData(client, HttpMethod.Put, "/admin/api/settings",
                   new { values = new Dictionary<string, string> { ["Warps.PhaseLineup"] = "Sideways" } }))
        {
            var data = settings.RootElement.GetProperty("data");
            check("settings reject an unknown Warps.PhaseLineup",
                data.GetProperty("applied").GetInt32() == 0 && data.GetProperty("errors").GetArrayLength() == 1 &&
                option.PhaseLineup == "Overlap");
        }

        using (var settings = await SendData(client, HttpMethod.Put, "/admin/api/settings", new
               {
                   values = new Dictionary<string, string>
                   {
                       ["Warps.Story"] = "false", ["Warps.PhaseLineup"] = "Started", ["Warps.CollabOwnPhaseOnly"] = "true"
                   }
               }))
            check("settings restore the story filter and phase lineup defaults",
                settings.RootElement.GetProperty("data").GetProperty("applied").GetInt32() == 3 &&
                !option.Story && option.PhaseLineup == "Started" && option.CollabOwnPhaseOnly);

        // --- Manual ---
        // Seeded from the Overlap lineup of 9.0-2, which carries 2903 without its light cone banner (pair 0).
        option.PhaseLineup = "Overlap";
        long periodBegin, periodEnd;
        using (var manual = await SendData(client, HttpMethod.Post, "/admin/api/warps/mode", new { mode = "Manual" }))
        {
            var data = manual.RootElement.GetProperty("data");
            var entries = data.GetProperty("manual").EnumerateArray().ToList();
            var state = data.GetProperty("rotation");
            periodBegin = state.GetProperty("period_begin").GetInt64();
            periodEnd = state.GetProperty("period_end").GetInt64();
            check("manual mode is seeded from the running lineup",
                data.GetProperty("mode").GetString() == "Manual" &&
                entries.Select(e => e.GetProperty("gacha_id").GetInt32()).SequenceEqual([2902, 2903, 3902]) &&
                entries.All(e => e.GetProperty("begin_time").GetInt64() == 0 && e.GetProperty("end_time").GetInt64() == 0 &&
                                 e.GetProperty("rate_up5").ValueKind == JsonValueKind.Null) &&
                entries[0].GetProperty("paired_gacha_id").GetInt32() == 3902 &&
                entries[1].GetProperty("paired_gacha_id").GetInt32() == 0);
            check("seeded manual banners keep running in the rolling period",
                Ids(data, "scheduled").SequenceEqual([2902, 2903, 3902]) && InPeriod(data, data.GetProperty("scheduled")));
        }

        option.PhaseLineup = "Started";

        var invalidBodies = new (string Name, object Body)[]
        {
            ("unknown banner", new { entries = new object[] { new { gacha_id = 9999 } } }),
            ("ineligible banner", new { entries = new object[] { new { gacha_id = 2905 } } }),
            ("duplicate banner", new { entries = new object[] { new { gacha_id = 2901 }, new { gacha_id = 2901 } } }),
            ("unknown item", new { entries = new object[] { new { gacha_id = 2901, rate_up5 = new[] { 777777 } } } }),
            ("empty featured 5-star", new { entries = new object[] { new { gacha_id = 2901, rate_up5 = Array.Empty<int>() } } }),
            ("begin after end", new { entries = new object[] { new { gacha_id = 2901, begin_time = now + Day, end_time = now } } }),
            ("bad light cone pair", new { entries = new object[] { new { gacha_id = 2901, paired_gacha_id = 2902 } } }),
            ("missing entries", new { })
        };
        var seeded = option.Manual;
        foreach (var (name, body) in invalidBodies)
        {
            using var response = await client.PutAsJsonAsync("/admin/api/warps/manual", body);
            check($"manual warps reject {name} without changing the config",
                response.StatusCode == HttpStatusCode.BadRequest && ReferenceEquals(option.Manual, seeded) &&
                option.Mode == "Manual");
        }

        var customBegin = now - 3600;
        var customEnd = now + Day;
        using (var manual = await SendData(client, HttpMethod.Put, "/admin/api/warps/manual", new
               {
                   entries = new object[]
                   {
                       new { gacha_id = 2901, rate_up5 = new[] { 1902 }, begin_time = customBegin, end_time = customEnd, paired_gacha_id = 3901 },
                       new { gacha_id = 3901 },
                       new { gacha_id = 2904, begin_time = now + 10 * Day, end_time = now + 20 * Day }
                   }
               }))
        {
            var data = manual.RootElement.GetProperty("data");
            var scheduled = data.GetProperty("scheduled").EnumerateArray()
                .ToDictionary(b => b.GetProperty("id").GetInt32());
            check("manual warps store the new list",
                !ReferenceEquals(option.Manual, seeded) && option.Manual.Select(e => e.GachaId).SequenceEqual([2901, 3901, 2904]) &&
                option.Manual[0].RateUpItems5 is [1902] && option.Manual[0].RateUpItems4 == null &&
                option.Manual[0].PairedGachaId == 3901 && data.GetProperty("manual").GetArrayLength() == 3);
            check("manual override and explicit window reach the scheduled banner",
                IntList(scheduled[2901].GetProperty("rate_up5")).SequenceEqual([1902]) &&
                IntList(scheduled[2901].GetProperty("rate_up4")).SequenceEqual([1911, 1912, 1913]) &&
                scheduled[2901].GetProperty("begin_time").GetInt64() == customBegin &&
                scheduled[2901].GetProperty("end_time").GetInt64() == customEnd &&
                scheduled[2901].GetProperty("running").GetBoolean());
            check("manual entry without a window rolls with the period",
                scheduled[3901].GetProperty("begin_time").GetInt64() == periodBegin &&
                scheduled[3901].GetProperty("end_time").GetInt64() == periodEnd &&
                scheduled[3901].GetProperty("running").GetBoolean());
            check("manual entry not open yet is listed but not running",
                !scheduled[2904].GetProperty("running").GetBoolean());
        }

        var playerNow = now - 200 * Day;
        var shifted = WarpSchedule.GetActiveBanners(now, playerNow);
        var shiftedLimited = shifted.Where(b => WarpSchedule.IsLimited(b.GachaType)).ToList();
        check("fake server time moves served windows onto the player's clock",
            shiftedLimited.Select(b => b.GachaId).Order().SequenceEqual([2901, 3901]) &&
            shiftedLimited.All(b => b.IsActiveAt(playerNow) && !b.IsActiveAt(now)) &&
            shiftedLimited.Single(b => b.GachaId == 2901).RateUpItems5 is [1902]);
        check("fake server time keeps the permanent pool and drops Banners.json limited banners",
            shifted.Any(b => b.GachaId == 1001) && shifted.All(b => b.GachaId != 2139));

        using (var cleared = await SendData(client, HttpMethod.Put, "/admin/api/warps/manual", new { entries = Array.Empty<object>() }))
        {
            var data = cleared.RootElement.GetProperty("data");
            check("manual warps accept an empty list",
                data.GetProperty("mode").GetString() == "Manual" && data.GetProperty("manual").GetArrayLength() == 0 &&
                data.GetProperty("scheduled").GetArrayLength() == 0 &&
                WarpSchedule.GetActiveBanners(now, now).Select(b => b.GachaId).SequenceEqual([1001]));
        }

        using (var back = await SendData(client, HttpMethod.Post, "/admin/api/warps/mode", new { mode = "Static" }))
            check("static mode restores Banners.json limited banners",
                Ids(back.RootElement.GetProperty("data"), "scheduled").SequenceEqual([2139]));
    }

    // Repo history file: every banner becomes eligible once its items exist; counts were read from the data.
    private static async Task RunRealHistory(HttpClient client, Action<string, bool> check, HashSet<int> addedItems)
    {
        var history = ResourceManager.LoadBannerHistory();
        check("repo banner history loads 300 banners", history.Banners.Count == 300);
        foreach (var id in history.Banners.SelectMany(b => b.RateUpItems5.Concat(b.RateUpItems4).Concat(b.PrizeItems)))
            if (GameData.ItemConfigData.TryAdd(id, new ItemConfigExcel { ID = id }))
                addedItems.Add(id);
        GameData.BannerHistoryConfig = history;

        var pool = WarpSchedule.Pool;
        check("repo banner history forms 65 phases with every banner eligible",
            pool.Phases.Count == 65 && pool.Banners.Count == 300 && pool.IneligibleIds.Count == 0);
        var overlapping = pool.FindPhase("4.7-2")?.Overlapping.Select(b => b.GachaId).ToHashSet() ?? [];
        check("repo phase 4.7-2 Overlap lineup includes its own and the carried-over banners",
            new[] { 2142, 3142, 2146, 2147, 2148, 3146, 3147, 3148 }.All(overlapping.Contains));

        var names = ResourceManager.LoadCustomFile<WarpItemNamesConfig>("Warp item names", "WarpItemNames");
        GameData.WarpItemNamesConfig = names ?? new WarpItemNamesConfig();

        // Pool API phases follow the PhaseLineup settings; option values are put back afterwards.
        var option = ConfigManager.Config.ServerOption.Warps;
        var (lineupSetting, collabSetting) = (option.PhaseLineup, option.CollabOwnPhaseOnly);
        try
        {
            option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);
            using (var response = await GetData(client, "/admin/api/warps/pool"))
            {
                var data = response.RootElement.GetProperty("data");
                check("warp pool endpoint serves the repo history",
                    data.GetProperty("phases").GetArrayLength() == 65 &&
                    data.GetProperty("banners").GetArrayLength() == 300);
                check("warp pool endpoint phase 4.7-2 with PhaseLineup Started is exactly the banners opened in it",
                    PhaseBannerIds(data, "4.7-2").Order().SequenceEqual([2146, 2147, 2148, 3146, 3147, 3148]));
                var poolNames = data.GetProperty("names");
                check("warp pool endpoint serves the repo item names (en 1306 Sparkle, 1501 Sparxie)",
                    ItemName(poolNames, "en", 1306) == "Sparkle" && ItemName(poolNames, "en", 1501) == "Sparxie" &&
                    !string.IsNullOrEmpty(ItemName(poolNames, "zh-CN", 1306)));
            }

            option.PhaseLineup = nameof(WarpPhaseLineupEnum.Overlap);
            option.CollabOwnPhaseOnly = false;
            using (var response = await GetData(client, "/admin/api/warps/pool"))
            {
                var ids = PhaseBannerIds(response.RootElement.GetProperty("data"), "4.7-2");
                check("warp pool endpoint phase 4.7-2 with Overlap includes carried-over 2142 and collab 5001",
                    ids.Contains(2142) && ids.Contains(5001));
            }

            option.CollabOwnPhaseOnly = true;
            using (var response = await GetData(client, "/admin/api/warps/pool"))
            {
                var ids = PhaseBannerIds(response.RootElement.GetProperty("data"), "4.7-2");
                check("warp pool endpoint phase 4.7-2 with Overlap + CollabOwnPhaseOnly keeps 2142 and drops collab 5001",
                    ids.Contains(2142) && !ids.Contains(5001));
            }
        }
        finally
        {
            option.PhaseLineup = lineupSetting;
            option.CollabOwnPhaseOnly = collabSetting;
        }
    }

    private static List<int> PhaseBannerIds(JsonElement poolData, string key)
    {
        var phase = poolData.GetProperty("phases").EnumerateArray()
            .FirstOrDefault(p => p.GetProperty("key").GetString() == key);
        return phase.ValueKind == JsonValueKind.Object ? IntList(phase.GetProperty("banner_ids")) : [];
    }

    private static string? ItemName(JsonElement names, string lang, int id)
    {
        return names.TryGetProperty(lang, out var table) && table.TryGetProperty(id.ToString(), out var name)
            ? name.GetString()
            : null;
    }

    private static Dictionary<string, string?> SettingValues(JsonElement settingsData)
    {
        return settingsData.GetProperty("fields").EnumerateArray().ToDictionary(
            f => f.GetProperty("key").GetString() ?? "", f => f.GetProperty("value").GetString());
    }

    private static BannerConfig Banner(int id, GachaTypeEnum type, long begin, long end, int rate5, List<int> rate4)
    {
        return new BannerConfig
        {
            GachaId = id, GachaType = type, BeginTime = begin, EndTime = end, RateUpItems5 = [rate5],
            RateUpItems4 = rate4, PrizeItems = [..rate4, rate5]
        };
    }

    private static BannerConfig History(int id, GachaTypeEnum type, string version, int phase, long begin, long end,
        int rate5, List<int> rate4)
    {
        var banner = Banner(id, type, begin, end, rate5, rate4);
        banner.Version = version;
        banner.Phase = phase;
        return banner;
    }

    private static bool InPeriod(JsonElement data, JsonElement banners)
    {
        var rotation = data.GetProperty("rotation");
        var begin = rotation.GetProperty("period_begin").GetInt64();
        var end = rotation.GetProperty("period_end").GetInt64();
        return banners.GetArrayLength() > 0 && banners.EnumerateArray().All(b =>
            b.GetProperty("begin_time").GetInt64() == begin && b.GetProperty("end_time").GetInt64() == end &&
            b.GetProperty("running").GetBoolean());
    }

    private static string LocalDate(long unixSeconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd");
    }

    private static List<int> Ids(JsonElement data, string property)
    {
        return data.GetProperty(property).EnumerateArray().Select(b => b.GetProperty("id").GetInt32()).ToList();
    }

    private static List<int> IntList(JsonElement array)
    {
        return array.EnumerateArray().Select(e => e.GetInt32()).ToList();
    }

    private static async Task<JsonDocument> GetData(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> SendData(HttpClient client, HttpMethod method, string path, object body)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{method} {path} -> {(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text);
    }
}
