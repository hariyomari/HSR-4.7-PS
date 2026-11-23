using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Player;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.Util;
using March7thHoney.WebServer;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

// Story checkpoint provisioning against the real mission graph: the prologue before and after CreateCharacter, the
// warp tutorial point after 1000301, and a sweep of chapter entries from 1.0 to 4.7. Every check reloads the save
// from the database cache the provisioner wrote, the way a login would.
internal static class StoryCheckpointRegression
{
    public static async Task Run(Action<string, bool> check)
    {
        await Prologue(check);
        await WarpTutorial(check);
        await ChapterSweep(check);
        await Http(check);
    }

    private static async Task<(StoryProvisionReport Report, PlayerInstance Player)> Provision(int uid,
        StoryProvisionRequest request)
    {
        var report = await StoryCheckpointProvisioner.ProvisionAsync(uid, request);
        var player = new PlayerInstance(DatabaseHelper.Instance!.GetInstance<PlayerData>(uid)!);
        await player.InitializeAsync();
        Console.WriteLine($"  provisioned {request.TargetMainMissionId}/{request.TargetSubMissionId} " +
                          $"({request.StopMode}): {report.MissionsFinished} mains, {report.SubMissionsFinished} subs, " +
                          $"{report.TeleportsUnlocked} teleports, {report.TutorialsMarkedFinished} tutorials, " +
                          $"floor {report.SpawnFloorId}, lineup [{string.Join(", ", report.Lineup)}], " +
                          $"stalled [{string.Join(", ", report.StalledMissions)}], " +
                          $"{report.BypassedGates.Count} bypassed, {report.ElapsedMs} ms");
        foreach (var warning in report.Warnings.Take(8)) Console.WriteLine($"    warning: {warning}");
        foreach (var gate in report.BypassedGates.Take(8)) Console.WriteLine($"    bypassed: {gate}");
        return (report, player);
    }

    private static List<int> Lineup(PlayerInstance player) =>
        player.LineupManager!.GetCurLineup()?.BaseAvatars?
            .Select(slot => slot.SpecialAvatarId > 0 ? slot.SpecialAvatarId : slot.BaseAvatarId).ToList() ?? [];

    private static async Task Prologue(Action<string, bool> check)
    {
        var (kafka, start) = await Provision(60400, new StoryProvisionRequest
            { TargetMainMissionId = 1000101, TargetSubMissionId = 100010102 });
        check("prologue Kafka start: no gender, trial Kafka in the lineup, 100010102 running on floor 20001001",
            !start.Data.IsGenderSet && !kafka.GenderSet &&
            Lineup(start).Contains(StoryCheckpointProvisioner.KafkaTrial) &&
            start.MissionManager!.GetSubMissionStatus(100010102) == MissionPhaseEnum.Accept &&
            start.Data.FloorId == 20001001 && kafka.TrackingMainMissionId == 1000101);

        var (_, silverWolf) = await Provision(60401, new StoryProvisionRequest
            { TargetMainMissionId = 1000101, TargetSubMissionId = 100010112 });
        check("prologue between 100010110 and 100010123: Silver Wolf's trial has joined Kafka",
            !silverWolf.Data.IsGenderSet &&
            Lineup(silverWolf).Contains(StoryCheckpointProvisioner.KafkaTrial) &&
            Lineup(silverWolf).Contains(StoryCheckpointProvisioner.SilverWolfTrial) &&
            silverWolf.MissionManager!.GetSubMissionStatus(100010112) == MissionPhaseEnum.Accept);

        var (receptacleReport, receptacle) = await Provision(60402, new StoryProvisionRequest
            { TargetMainMissionId = 1000101, TargetSubMissionId = StoryCheckpointProvisioner.CreateCharacterSubMission });
        var missions = receptacle.MissionManager!;
        check("prologue receptacle: CreateCharacter 100010134 running, no gender, Kafka still the trial lead",
            !receptacle.Data.IsGenderSet && !receptacleReport.GenderSet &&
            missions.GetSubMissionStatus(StoryCheckpointProvisioner.CreateCharacterSubMission) == MissionPhaseEnum.Accept &&
            missions.GetSubMissionStatus(100010133) == MissionPhaseEnum.Finish &&
            Lineup(receptacle).Contains(StoryCheckpointProvisioner.KafkaTrial) &&
            !Lineup(receptacle).Contains(StoryCheckpointProvisioner.SilverWolfTrial) &&
            !Lineup(receptacle).Any(id => id is 8001 or 8002) && receptacle.Data.FloorId == 20001001);

        // Group 5 consoles the fast-forwarded PropState subs opened (100010105/107/111/126) and the one 100010124
        // waits on in Closed.
        PropStateEnum? Saved(int prop) => receptacle.GetScenePropData(20001001, 5, prop)?.State;
        check("prologue receptacle: the Group 5 door consoles of the skipped PropState subs are saved unlocked",
            Saved(300002) == PropStateEnum.Open && Saved(300004) == PropStateEnum.Open &&
            Saved(300006) == PropStateEnum.Open && Saved(300018) == PropStateEnum.Open &&
            Saved(300029) == PropStateEnum.Closed);

        GameData.GetFloorInfo(20001, 20001001, out var floor);
        var anchor = floor?.GetAnchorInfo(14, 9);
        check("prologue receptacle: spawned at the receptacle console anchor (group 14 anchor 9), not the hallway start",
            anchor != null && receptacle.Data.Pos is { } pos && pos.X == anchor.ToPositionProto().X &&
            pos.Z == anchor.ToPositionProto().Z);
    }

    private static async Task WarpTutorial(Action<string, bool> check)
    {
        var (report, player) = await Provision(60410, new StoryProvisionRequest
        {
            TargetMainMissionId = 1000301, StopMode = StoryStopModeEnum.After, TrailblazerGender = Gender.Man,
            TrailblazerName = "Caelus"
        });
        check("warp tutorial point: 1000301 finished, tutorial 3002 untouched, Trailblazer created and in the lineup",
            player.MissionManager!.GetMainMissionStatus(1000301) == MissionPhaseEnum.Finish &&
            player.TutorialData?.Tutorials.GetValueOrDefault(3002) != TutorialStatus.TutorialFinish &&
            player.Data is { IsGenderSet: true, CurrentGender: Gender.Man, Name: "Caelus" } && report.GenderSet &&
            Lineup(player).Contains(8001) && !Lineup(player).Contains(StoryCheckpointProvisioner.KafkaTrial));
        check("warp tutorial point: earlier tutorials are finished, the next main is tracked",
            report.TutorialsMarkedFinished > 0 &&
            player.TutorialData?.Tutorials.GetValueOrDefault(1002) == TutorialStatus.TutorialFinish &&
            player.MissionManager.Data.TrackingMainMissionId != 1000301 &&
            player.MissionManager.GetMainMissionStatus(player.MissionManager.Data.TrackingMainMissionId) ==
            MissionPhaseEnum.Accept);
    }

    private static async Task ChapterSweep(Action<string, bool> check)
    {
        var gates = GameData.WarpStoryGatesConfig.Gates;
        int Entry(string version) =>
            MissionManager.StoryEntryMission(gates.First(gate => gate.Version == version).MainMissions);
        (string Name, int Main)[] chapters =
        [
            ("Jarilo-VI", 1010002), ("Xianzhou Luofu", 1020101), ("Penacony (2.0)", Entry("2.0")),
            ("Amphoreus (3.0)", Entry("3.0")), ("4.7", Entry("4.7"))
        ];

        var uid = 60420;
        foreach (var (name, main) in chapters)
        {
            var (report, player) = await Provision(uid++, new StoryProvisionRequest { TargetMainMissionId = main });
            var missions = player.MissionManager!;
            GameData.MapEntranceData.TryGetValue(report.SpawnEntryId, out var entrance);
            // A finished raid's own mains are reset when the run is left (as on the official server).
            var raidMains = GameData.RaidConfigData.Values.SelectMany(raid => raid.MainMissionIDList).ToHashSet();
            var unfinished = MissionManager.StoryPrerequisites(main, false)
                .Where(id => !raidMains.Contains(id) && missions.GetMainMissionStatus(id) != MissionPhaseEnum.Finish)
                .ToList();
            var planeType = entrance == null ? PlaneTypeEnum.Unknown
                : GameData.MazePlaneData.GetValueOrDefault(entrance.PlaneID)?.PlaneType ?? PlaneTypeEnum.Unknown;
            Console.WriteLine($"    status {missions.GetMainMissionStatus(main)}, tracking " +
                              $"{missions.Data.TrackingMainMissionId}, unfinished prerequisites " +
                              $"[{string.Join(", ", unfinished.Take(10))}], entry {report.SpawnEntryId} ({planeType}), " +
                              $"raid {player.RaidManager!.Data.CurRaidId}, gender set {player.Data.IsGenderSet}");
            check($"chapter {name} ({main}): accepted and tracked with every prerequisite finished",
                main != 0 && missions.GetMainMissionStatus(main) == MissionPhaseEnum.Accept &&
                missions.Data.TrackingMainMissionId == main && unfinished.Count == 0);
            check($"chapter {name} ({main}): spawns on a real floor with teleports unlocked and no raid open",
                entrance != null && entrance.FloorID == report.SpawnFloorId && planeType != PlaneTypeEnum.Raid &&
                report.TeleportsUnlocked > 0 && player.RaidManager.Data.CurRaidId == 0 && player.Data.IsGenderSet);
            check($"chapter {name} ({main}): no mission closed by force and no story hook threw",
                report.StalledMissions.Count == 0 && !report.Warnings.Any(warning => warning.Contains(" threw ")));
            // 1000204 (Dan Heng joins) branches off the chain; left running, tutorial 3003 (opens on GetAvatar 1002,
            // closes on FinishMainMission 1000204) popped at login and held input for good.
            var leftovers = missions.Data.RunningMainMissionIds.Where(id => id != main &&
                GameData.MainMissionData.GetValueOrDefault(id)?.Type == MainMissionTypeEnum.Main &&
                MissionManager.StoryDepth(id) < MissionManager.StoryDepth(main)).ToList();
            // General form of the lock: a free-play tutorial the server could not close once open must not be able
            // to open at login. Each one still open has to wait on something not yet reached (a mission not done, a
            // level not hit) or on a client task unlock; ids are not special-cased.
            bool Pending(March7thHoney.Data.Excel.TutorialTriggerParam trigger)
            {
                if (trigger.TriggerType == "TaskUnlock") return true;
                if (!int.TryParse(trigger.TriggerParam, out var value)) return false;
                return trigger.TriggerType switch
                {
                    "FinishMainMission" => missions.GetMainMissionStatus(value) != MissionPhaseEnum.Finish,
                    "TakeMainMission" => missions.GetMainMissionStatus(value) == MissionPhaseEnum.None,
                    "FinishSubMission" => missions.GetSubMissionStatus(value) != MissionPhaseEnum.Finish,
                    "TakeSubMission" => missions.GetSubMissionStatus(value) == MissionPhaseEnum.None,
                    "PlayerLevel" => player.Data.Level < value,
                    "AnyAvatarToLevel" => player.AvatarManager!.Data.FormalAvatars.All(avatar => avatar.Level < value),
                    _ => false
                };
            }

            var trapping = GameData.TutorialDataData.Values
                .Where(tutorial => tutorial.TriggerParams.Any(trigger => trigger.TriggerType == "OnMazeMainPage") &&
                                   player.TutorialData?.Tutorials.GetValueOrDefault(tutorial.TutorialID) !=
                                   TutorialStatus.TutorialFinish &&
                                   !March7thHoney.GameServer.Game.Tutorial.TutorialHelper.CanClose(player,
                                       tutorial.TutorialID) &&
                                   !tutorial.TriggerParams.Any(Pending))
                .Select(tutorial => tutorial.TutorialID).ToList();
            check($"chapter {name} ({main}): no free-play tutorial that cannot close is ready to open at login " +
                  $"[{string.Join(", ", trapping)}]", trapping.Count == 0);
            check($"chapter {name} ({main}): no Main row behind the target left running (1000204 finished)",
                leftovers.Count == 0 && missions.GetMainMissionStatus(1000204) == MissionPhaseEnum.Finish);
            if (main == 1020101)
            {
                check("test_ready raises every level gate on the way to Luofu instead of bypassing it",
                    player.Data.Level >= 13 && !report.BypassedGates.Any(gate => gate.Contains("Level")));
                check("a dialogue choice on the way (1011501) is answered through the custom-value path and recorded",
                    missions.Data.MainMissionCustomValues.TryGetValue(1011501, out var answers) &&
                    answers.Any(answer => answer.Key == "OldValue_0"));
            }
        }

        // live_like leaves the level to the replayed rewards: 1010502 (begin PlayerLevel 13) comes up before the
        // account gets there, is accepted anyway and is reported.
        var (liveLike, _) = await Provision(uid, new StoryProvisionRequest
            { TargetMainMissionId = 1020101, TestReady = false, Teleports = StoryTeleportsModeEnum.None });
        check("live_like accepts level-gated missions anyway, reports each bypassed gate and unlocks no teleport",
            liveLike.BypassedGates.Contains("1010502: begin=PlayerLevel 13 unmet") &&
            liveLike.TeleportsUnlocked == 0);

        // A target sub that is itself one answer of a dialogue choice: that answer is taken, not its first sibling.
        var (_, branch) = await Provision(uid + 1, new StoryProvisionRequest
            { TargetMainMissionId = 1011501, TargetSubMissionId = 101150103 });
        check("a dialogue-branch target sub is reached through its own answer",
            branch.MissionManager!.GetSubMissionStatus(101150103) == MissionPhaseEnum.Accept &&
            branch.MissionManager.GetSubMissionStatus(101150102) == MissionPhaseEnum.None &&
            branch.MissionManager.GetSubMissionStatus(101150104) == MissionPhaseEnum.None);
    }

    // The admin routes end to end against the real story: presets resolve to the documented checkpoints, a new
    // account lands in the receptacle room, and the per-account route moves it past CreateCharacter.
    private static async Task Http(Action<string, bool> check)
    {
        await using var app = WebProgram.BuildWebApp([], 0, "http://127.0.0.1:0");
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMinutes(2) };
        using (var login = await client.PostAsJsonAsync("/admin/api/login",
                   new { admin_key = ConfigManager.Config.MuipServer.AdminKey }))
        {
            using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                json.RootElement.GetProperty("data").GetProperty("token").GetString());
        }

        using (var response = await client.GetAsync("/admin/api/story/catalog"))
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data");
            var presets = data.GetProperty("presets").EnumerateArray().ToDictionary(
                preset => preset.GetProperty("id").GetString()!,
                preset => (Main: preset.GetProperty("main_mission_id").GetInt32(),
                    Sub: preset.GetProperty("sub_mission_id").GetInt32(),
                    Stop: preset.GetProperty("stop_mode").GetString()));
            var versions = data.GetProperty("versions").EnumerateArray()
                .Select(version => version.GetProperty("version").GetString()).ToList();
            check("story catalog serves 1.0-4.7 and every preset at its checkpoint",
                versions.FirstOrDefault() == "1.0" && versions.LastOrDefault() == "4.7" && presets.Count == 10 &&
                presets["prologue-kafka-start"] == (1000101, 100010102, "before") &&
                presets["prologue-receptacle"] == (1000101, 100010134, "before") &&
                presets["post-prologue-wakeup"] == (1000201, 0, "before") &&
                presets["warp-tutorial"] == (1000301, 0, "after") &&
                presets["jarilo-vi"].Main == 1010002 && presets["xianzhou-luofu"].Main == 1020101 &&
                presets["penacony"].Main == 1030101 && presets["amphoreus"].Main == 1040101 &&
                presets["version-4-7"].Main == 1054704);
        }

        int uid;
        using (var response = await client.PostAsJsonAsync("/admin/api/accounts/provision-story",
                   new { preset = "prologue-receptacle", username = "story-receptacle" }))
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data");
            var report = data.GetProperty("report");
            uid = data.GetProperty("account").GetProperty("uid").GetInt32();
            check("provision-story creates an account standing in the receptacle room with a generated login",
                response.IsSuccessStatusCode && AccountData.GetAccountByUid(uid)?.Username == "story-receptacle" &&
                AccountData.GetAccountByUid(uid)!.VerifyPassword(data.GetProperty("generated_password").GetString()!) &&
                report.GetProperty("target_sub_mission_id").GetInt32() == 100010134 &&
                !report.GetProperty("gender_set").GetBoolean() &&
                report.GetProperty("spawn_floor_id").GetInt32() == 20001001 &&
                report.GetProperty("lineup").EnumerateArray().Any(id => id.GetInt32() == StoryCheckpointProvisioner.KafkaTrial));
        }

        using (var response = await client.PostAsJsonAsync($"/admin/api/accounts/{uid}/provision-story",
                   new { preset = "warp-tutorial", trailblazer_gender = "man" }))
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var report = json.RootElement.GetProperty("data").GetProperty("report");
            var player = DatabaseHelper.Instance!.GetInstance<PlayerData>(uid);
            check("provision-story on the same account moves it past CreateCharacter to the warp tutorial",
                response.IsSuccessStatusCode && report.GetProperty("gender_set").GetBoolean() &&
                player is { IsGenderSet: true, CurrentGender: Gender.Man } &&
                report.GetProperty("lineup").EnumerateArray().Any(id => id.GetInt32() == 8001) &&
                AccountData.GetAccountByUid(uid)?.Username == "story-receptacle");
        }

        await app.StopAsync();
    }
}
