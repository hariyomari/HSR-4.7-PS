using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.Proto;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

// Story checkpoints: the browsable story catalog and the provisioner that builds a save standing at a chosen mission.
public static partial class AdminRoutes
{
    private static void MapStoryRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/api/story/catalog", GetStoryCatalog);
        app.MapPost("/admin/api/accounts/provision-story", ProvisionStoryAccount);
        app.MapPost("/admin/api/accounts/{uid:int}/provision-story", ProvisionStoryExisting);
    }

    private static IResult GetStoryCatalog(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;

        var config = GameData.WarpStoryGatesConfig;
        var chapterOf = new Dictionary<int, (int Id, string Name)>();
        foreach (var gate in config.ChapterGates)
        foreach (var mainId in gate.MainMissions)
            chapterOf.TryAdd(mainId, (gate.ChapterId, gate.Chapter));

        var versions = new List<StoryVersionView>();
        foreach (var gate in config.Gates)
        {
            var missions = new List<StoryMainMissionView>();
            foreach (var mainId in gate.MainMissions)
            {
                if (!GameData.MainMissionData.TryGetValue(mainId, out var main)) continue;
                var (chapterId, chapterName) = chapterOf.TryGetValue(mainId, out var chapter)
                    ? chapter
                    : (0, gate.Chapters.Count == 1 ? gate.Chapters[0] : "");
                var subs = main.MissionInfo.SubMissionList
                    .Select(sub => new StorySubMissionView(sub.ID, sub.FinishType.ToString(), sub.WayPointFloorID,
                        sub.WayPointGroupID))
                    .ToList();
                missions.Add(new StoryMainMissionView(mainId, chapterId, chapterName, gate.Version,
                    main.Type.ToString(), MissionFloor(main), subs));
            }

            versions.Add(new StoryVersionView(gate.Version, gate.Chapters, missions));
        }

        var presets = StoryCheckpointProvisioner.Presets()
            .Select(preset => new StoryPresetView(preset.Id, preset.MainMissionId, preset.SubMissionId,
                StopModeText(preset.StopMode)))
            .ToList();
        return Success(new StoryCatalogData(versions, presets));
    }

    // Where the mission starts: its first start sub that names a floor, else the first sub that does.
    private static int MissionFloor(Data.Excel.MainMissionExcel main)
    {
        foreach (var subId in main.MissionInfo.StartSubMissionList)
            if (GameData.SubMissionInfoData.GetValueOrDefault(subId)?.SubMissionInfo is { LevelFloorID: > 0 } start)
                return start.LevelFloorID;
        return main.MissionInfo.SubMissionList.Select(sub => sub.LevelFloorID).FirstOrDefault(floor => floor > 0);
    }

    private static async Task<IResult> ProvisionStoryAccount(HttpContext ctx, AdminProvisionStoryRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (DatabaseHelper.Instance == null) return Error("Database is not initialized");

        request ??= new AdminProvisionStoryRequest();
        if (!TryReadStoryRequest(request, out var story, out var error)) return Error(error);

        AccountData account;
        string? password;
        try
        {
            if (request.keep_save == true) return Error("keep_save needs an existing account");
            account = CreateTestAccount(request, $"test_{story.TargetMainMissionId}", out password);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        try
        {
            var report = await StoryCheckpointProvisioner.ProvisionAsync(account.Uid, story);
            return Success(new StoryProvisionData(ToAccountSummary(account), password, ToReportView(report)));
        }
        catch (Exception ex)
        {
            await DiscardTestAccount(account);
            return Error(ex.Message);
        }
    }

    private static async Task<IResult> ProvisionStoryExisting(HttpContext ctx, int uid,
        AdminProvisionStoryRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (DatabaseHelper.Instance == null) return Error("Database is not initialized");

        var account = AccountData.GetAccountByUid(uid);
        if (account == null) return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        request ??= new AdminProvisionStoryRequest();
        if (!TryReadStoryRequest(request, out var story, out var error)) return Error(error);

        try
        {
            var report = await StoryCheckpointProvisioner.ProvisionAsync(uid, story);
            return Success(new StoryProvisionData(ToAccountSummary(account), null, ToReportView(report)));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static bool TryReadStoryRequest(AdminProvisionStoryRequest request, out StoryProvisionRequest story,
        out string error)
    {
        story = new StoryProvisionRequest();
        error = "";

        var main = request.target_main_mission_id ?? 0;
        var sub = request.target_sub_mission_id ?? 0;
        var stopText = request.stop_mode;
        if (!string.IsNullOrWhiteSpace(request.preset))
        {
            var preset = StoryCheckpointProvisioner.Presets().FirstOrDefault(item => item.Id == request.preset);
            if (preset == null)
            {
                error = $"Unknown story preset {request.preset}";
                return false;
            }

            main = preset.MainMissionId;
            sub = preset.SubMissionId;
            stopText ??= StopModeText(preset.StopMode);
        }

        if (!TryParseOption(stopText, "before", out var stop,
                ("before", StoryStopModeEnum.Before), ("after", StoryStopModeEnum.After)) ||
            !TryParseOption(request.trailblazer_gender, "unset", out var gender,
                ("unset", Gender.None), ("man", Gender.Man), ("woman", Gender.Woman)) ||
            !TryParseOption(request.progression_preset, "test_ready", out var testReady,
                ("test_ready", true), ("live_like", false)) ||
            !TryParseOption(request.tutorials_mode, "finish_prior", out var tutorials,
                ("finish_prior", StoryTutorialsModeEnum.FinishPrior), ("keep_open", StoryTutorialsModeEnum.KeepOpen),
                ("finish_all", StoryTutorialsModeEnum.FinishAll)) ||
            !TryParseOption(request.teleports_mode, "reached_planes", out var teleports,
                ("none", StoryTeleportsModeEnum.None), ("reached_planes", StoryTeleportsModeEnum.ReachedPlanes),
                ("all", StoryTeleportsModeEnum.All)))
        {
            error = "Invalid stop_mode, trailblazer_gender, progression_preset, tutorials_mode or teleports_mode";
            return false;
        }

        story = new StoryProvisionRequest
        {
            TargetMainMissionId = main,
            TargetSubMissionId = sub,
            StopMode = stop,
            TrailblazerGender = gender,
            TrailblazerName = string.IsNullOrWhiteSpace(request.trailblazer_name) ? null : request.trailblazer_name,
            TestReady = testReady,
            Tutorials = tutorials,
            Teleports = teleports,
            IncludeSideMissions = request.include_side_missions ?? false,
            GrantTestCurrency = request.grant_test_currency ?? false,
            KeepSave = request.keep_save ?? false
        };

        var invalid = StoryCheckpointProvisioner.Validate(story);
        if (invalid == null) return true;
        error = invalid;
        return false;
    }

    private static bool TryParseOption<T>(string? text, string fallback, out T value, params (string Name, T Value)[] options)
    {
        var key = string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
        foreach (var option in options)
            if (string.Equals(option.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                value = option.Value;
                return true;
            }

        value = default!;
        return false;
    }

    private static string StopModeText(StoryStopModeEnum mode) => mode == StoryStopModeEnum.After ? "after" : "before";

    private static StoryProvisionReportView ToReportView(StoryProvisionReport report)
    {
        return new StoryProvisionReportView(report.TargetMainMissionId, report.TargetSubMissionId,
            report.TrackingMainMissionId, report.PrerequisiteCount, report.MissionsFinished,
            report.SubMissionsFinished, report.TeleportsUnlocked, report.TutorialsMarkedFinished,
            report.PropStatesRestored, report.SpawnPlaneId, report.SpawnFloorId, report.SpawnEntryId,
            report.GenderSet, report.PlayerLevel, report.WorldLevel, report.Lineup, report.BypassedGates,
            report.StalledMissions, report.Warnings, report.WasOnline, report.ElapsedMs);
    }

    public class AdminProvisionStoryRequest : AdminTestAccountRequest
    {
        public string? preset { get; set; }
        public int? target_main_mission_id { get; set; }
        public int? target_sub_mission_id { get; set; }
        public string? stop_mode { get; set; }
        public string? trailblazer_gender { get; set; }
        public string? trailblazer_name { get; set; }
        public string? progression_preset { get; set; }
        public string? tutorials_mode { get; set; }
        public string? teleports_mode { get; set; }
        public bool? include_side_missions { get; set; }
        public bool? grant_test_currency { get; set; }

        /// <summary>Existing account only: advance its save to the point instead of wiping it.</summary>
        public bool? keep_save { get; set; }
    }
}
