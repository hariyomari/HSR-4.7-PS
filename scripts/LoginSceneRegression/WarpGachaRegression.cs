using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Enums;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Gacha;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Tutorial;
using March7thHoney.Proto;
using March7thHoney.Util;

// Warp scheduling (ServerOption.Warps) against the real history file and the real item table: the banner list a
// player is sent and the pools DoGacha accepts must be the same scheduled set, in every mode.
internal static class WarpGachaRegression
{
    public static async Task Run(Action<string, bool> check, string repository, string scratch)
    {
        foreach (var name in new[] { "Banners.history.json", "WarpStoryGates.json" })
            File.Copy(Path.Combine(repository, "Config/Custom", name), Path.Combine(scratch, "Custom", name), true);
        ResourceManager.ReloadBanners();

        var option = ConfigManager.Config.ServerOption.Warps;
        var saved = (option.Mode, option.RotationDays, option.RotationStart, option.RotationStartPhase, option.Manual,
            option.PhaseLineup, option.CollabBanners, option.Story, option.StoryByChapter);
        try
        {
            await Scenarios(check);
            await StoryScenarios(check);
            await ChapterScenarios(check);
            await PermanentScenarios(check);
        }
        finally
        {
            (option.Mode, option.RotationDays, option.RotationStart, option.RotationStartPhase, option.Manual,
                option.PhaseLineup, option.CollabBanners, option.Story, option.StoryByChapter) = saved;
        }
    }

    private static async Task Scenarios(Action<string, bool> check)
    {
        var option = ConfigManager.Config.ServerOption.Warps;
        var pool = WarpSchedule.Pool;
        check("every 1.0-4.7 history banner features items the resource cache has",
            GameData.BannerHistoryConfig.Banners.Count == 300 && pool.IneligibleIds.Count == 0);
        if (pool.IneligibleIds.Count > 0)
            Console.WriteLine($"  ineligible history banners: {string.Join(", ", pool.IneligibleIds)}");

        var player = new PlayerInstance(60041);
        await player.InitializeAsync();
        await player.InventoryManager!.AddItem(102, 50, false, sync: false);
        var now = ServerTimeProvider.GetServerUnixSec(player.Data);

        // Rotation from 4.7 phase 2 with the live lineup (Overlap): Aha continues, plus three reruns.
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Overlap);
        check("rotation restarts from a known history phase", WarpSchedule.RestartRotation("4.7-2", WarpSchedule.Now()) == null);
        var expected = WarpSchedule.GetScheduledBanners(WarpSchedule.Now()).Select(b => b.GachaId).ToHashSet();
        var info = player.GachaManager!.ToProto();
        var limited = LimitedIds(info);
        check("rotation sends exactly the scheduled limited banners with Overlap",
            limited.SetEquals(expected) && limited.IsSupersetOf([2142, 2146, 2147, 2148, 3142, 3146, 3147, 3148]));
        check("rotation still sends the permanent standard banner", info.GachaInfoList.Any(g => g.GachaId == 1001));
        check("rotation windows contain the player's time",
            info.GachaInfoList.Where(g => limited.Contains((int)g.GachaId)).All(g => g.BeginTime <= now && now <= g.EndTime));
        check("Banners.json limited pools are not served in rotation", !limited.Contains(2139));
        check("rotation sends all eight collab banners, outside the phase lineup",
            CollabIds(info).SetEquals(Collab) && !expected.Overlaps(Collab) &&
            info.GachaInfoList.Where(g => Collab.Contains((int)g.GachaId)).All(g => g.BeginTime <= now && now <= g.EndTime));

        // Default lineup (Started): only the banners that opened in 4.7 phase 2.
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);
        check("the Started lineup sends only the banners that opened in the phase",
            LimitedIds(player.GachaManager.ToProto()).SetEquals([2146, 2147, 2148, 3146, 3147, 3148]));
        var pull = await player.GachaManager.DoGacha(2146, 1);
        check("a scheduled rerun banner pays out a real item",
            pull is { Retcode: 0, GachaItemList.Count: 1 } &&
            GameData.ItemConfigData.ContainsKey((int)pull.GachaItemList[0].GachaItem_.ItemId));
        var off = await player.GachaManager.DoGacha(2139, 1);
        check("a limited banner outside the schedule is refused",
            off?.Retcode == (uint)Retcode.RetGachaIdNotExist);

        // Version lineup: both phases of 4.7 from a phase-2 start; the next period moves on a whole version.
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Version);
        var version47 = pool.Phases.Where(p => p.Version == "4.7").SelectMany(p => p.Started)
            .Select(b => b.GachaId).ToHashSet();
        var byVersion = WarpSchedule.GetRotation(WarpSchedule.Now());
        check("the Version lineup sends both phases of the start phase's version",
            LimitedIds(player.GachaManager.ToProto()).SetEquals(version47) &&
            version47.IsSupersetOf([2142, 3142, 2146, 3146]) && !version47.Overlaps(Collab));
        check("the Version lineup rotates by whole versions (4.7 wraps to 1.0)",
            byVersion.Phase?.Version == "4.7" && byVersion.NextPhase?.Key == "1.0-1");
        // Mid-pool: phase stepping would go 4.6-1 -> 4.6-2; version stepping goes 4.6 -> 4.7.
        WarpSchedule.RestartRotation("4.6-1", WarpSchedule.Now());
        var mid = WarpSchedule.GetRotation(WarpSchedule.Now());
        check("the Version lineup steps from 4.6 to 4.7, not to 4.6-2",
            mid.Phase?.Version == "4.6" && mid.NextPhase?.Version == "4.7" &&
            LimitedIds(player.GachaManager.ToProto()).SetEquals(pool.Phases.Where(p => p.Version == "4.6")
                .SelectMany(p => p.Started).Select(b => b.GachaId)));
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);

        // Manual: one old character banner with a featured override; everything else is gone.
        option.Manual = [new() { GachaId = 2003, RateUpItems5 = [1102] }];
        option.Mode = nameof(WarpScheduleModeEnum.Manual);
        info = player.GachaManager.ToProto();
        check("manual mode sends only the picked limited banner",
            LimitedIds(info).SetEquals([2003]) && info.GachaInfoList.Any(g => g.GachaId == 1001));
        check("manual mode still sends the collab banners", CollabIds(info).SetEquals(Collab));
        check("manual mode refuses the banner rotation served a moment ago",
            (await player.GachaManager.DoGacha(2146, 1))?.Retcode == (uint)Retcode.RetGachaIdNotExist);
        check("manual mode pulls on the picked 1.0 banner",
            (await player.GachaManager.DoGacha(2003, 1))?.Retcode == 0);
        check("a manual list cannot hold a collab banner",
            WarpSchedule.ValidateManual([new() { GachaId = 5001 }]) != null);

        // Static: Banners.json as written again, collab banners from the switch.
        option.Mode = nameof(WarpScheduleModeEnum.Static);
        info = player.GachaManager.ToProto();
        check("static mode serves Banners.json again", LimitedIds(info).SetEquals(StaticLimited()));
        check("static mode sends the collab banners", CollabIds(info).SetEquals(Collab));
        check("a collab banner pays out while the switch is on",
            (await player.GachaManager.DoGacha(5001, 1))?.Retcode == 0);

        option.CollabBanners = false;
        info = player.GachaManager.ToProto();
        check("the collab switch off removes every collab banner",
            CollabIds(info).Count == 0 && LimitedIds(info).SetEquals(StaticLimited()));
        check("a collab banner is refused while the switch is off",
            (await player.GachaManager.DoGacha(5001, 1))?.Retcode == (uint)Retcode.RetGachaIdNotExist);
        option.CollabBanners = true;
    }

    // Story filter: banners unlock with the story chapter of their version (Config/Custom/WarpStoryGates.json).
    private static async Task StoryScenarios(Action<string, bool> check)
    {
        var option = ConfigManager.Config.ServerOption.Warps;
        var history = WarpSchedule.Pool.Banners;
        option.Story = true;
        option.StoryByChapter = false;
        option.Mode = nameof(WarpScheduleModeEnum.Static);
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);

        // A real new account: missions on before initialisation, so the opening missions are bootstrapped.
        var player = new PlayerInstance(60042) { PendingMissionEnabledOverride = true };
        await player.InitializeAsync();
        await player.InventoryManager!.AddItem(102, 50, false, sync: false);

        var progress = WarpSchedule.GetStoryProgress(player);
        check("a new player has reached 1.0 and unlocks up to 1.2",
            progress is { ReachedVersion: "1.0", NextVersion: "1.2" });
        var start = player.GachaManager!.ToProto();
        check("static + story sends every 1.0 and 1.1 banner at once and nothing newer",
            LimitedIds(start).SetEquals(history.Where(b => b.Version is "1.0" or "1.1").Select(b => b.GachaId)) &&
            LimitedIds(start).Count == 8);
        check("static + story still sends every collab banner before their story", CollabIds(start).SetEquals(Collab));
        check("static + story drops the Banners.json limited pools", !LimitedIds(start).Contains(2139));
        check("static + story pulls on an unlocked 1.0 banner", (await player.GachaManager.DoGacha(2003, 1))?.Retcode == 0);
        check("static + story refuses a banner of a version the story has not reached",
            (await player.GachaManager.DoGacha(2007, 1))?.Retcode == (uint)Retcode.RetGachaIdNotExist);

        // Starting Topclouded Towerthrust (1.2) unlocks 1.2.
        player.MissionManager!.Data.SetMainMissionStatus(1021201, MissionPhaseEnum.Accept);
        var luofu = LimitedIds(player.GachaManager.ToProto());
        check("starting the 1.2 chapter unlocks the 1.2 banners and nothing after them",
            luofu.IsSupersetOf([2007, 3007, 2008, 3008]) &&
            luofu.All(id => WarpSchedule.CompareVersions(WarpSchedule.Pool.Find(id)!.Version, "1.3") < 0));

        // 3.8's chapter has lower mission ids than 3.6/3.7; the gate order follows the version.
        player.MissionManager.Data.SetMainMissionStatus(1033800, MissionPhaseEnum.Finish);
        check("a 3.8 mission unlocks through 3.8 although its ids sort before 3.6",
            WarpSchedule.GetStoryProgress(player) is { ReachedVersion: "3.8", NextVersion: "4.0" });

        // Rotation + Story: only unlocked phases rotate; a locked start phase falls back to the oldest.
        player.MissionManager.Data.SetMainMissionStatus(1033800, MissionPhaseEnum.None);
        WarpSchedule.RestartRotation("4.7-2", WarpSchedule.Now());
        var rotated = LimitedIds(player.GachaManager.ToProto());
        check("rotation + story starts at the oldest phase when the start phase is still locked",
            rotated.SetEquals([2003, 3003]));
        WarpSchedule.RestartRotation("1.2-2", WarpSchedule.Now());
        check("rotation + story serves an unlocked start phase", LimitedIds(player.GachaManager.ToProto()).SetEquals([2008, 3008]));
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Version);
        check("rotation + story with the Version lineup serves both unlocked 1.2 phases",
            LimitedIds(player.GachaManager.ToProto()).SetEquals([2007, 3007, 2008, 3008]) &&
            WarpSchedule.GetRotation(WarpSchedule.Now(), WarpSchedule.GetStoryProgress(player)).NextPhase?.Key == "1.0-1");
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);

        // Missions off: no story to read, so Banners.json as written even in Rotation.
        var noMissions = new PlayerInstance(60043);
        await noMissions.InitializeAsync();
        noMissions.MissionManager!.Data.EnableMissionOverride = false;
        check("a player with missions off gets the Banners.json limited pools",
            LimitedIds(noMissions.GachaManager!.ToProto()).SetEquals(StaticLimited()));

        // Manual ignores the filter: the admin's pick is served even though 2146 is 4.7.
        option.Manual = [new() { GachaId = 2146 }];
        option.Mode = nameof(WarpScheduleModeEnum.Manual);
        check("manual mode ignores the story filter", LimitedIds(player.GachaManager.ToProto()).SetEquals([2146]));
    }

    // StoryByChapter: the 1.0 and 1.1 phases also wait for their story chapter (ChapterGates in WarpStoryGates.json):
    // Seele / Silver Wolf for Jarilo-VI (101001), Jing Yuan / Luocha for the first Xianzhou Luofu chapter (102001).
    private static async Task ChapterScenarios(Action<string, bool> check)
    {
        var option = ConfigManager.Config.ServerOption.Warps;
        option.Story = true;
        option.StoryByChapter = false;
        option.Mode = nameof(WarpScheduleModeEnum.Static);
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);
        int[] firstEight = [2003, 3003, 2004, 3004, 2005, 3005, 2006, 3006];

        var chapters = GameData.WarpStoryGatesConfig.ChapterGates;
        check("the chapter gates hold the Jarilo-VI and Xianzhou Luofu missions used below",
            chapters.Any(g => g.ChapterId == 101001 && g.MainMissions.Contains(1010002) &&
                              g.Phases.SequenceEqual(["1.0-1", "1.1-1"])) &&
            chapters.Any(g => g.ChapterId == 102001 && g.MainMissions.Contains(1020101) &&
                              g.Phases.SequenceEqual(["1.0-2", "1.1-2"])));

        var player = new PlayerInstance(60044) { PendingMissionEnabledOverride = true };
        await player.InitializeAsync();
        await player.InventoryManager!.AddItem(102, 50, false, sync: false);
        check("with StoryByChapter off a new player still gets Seele, Jing Yuan, Silver Wolf and Luocha",
            LimitedIds(player.GachaManager!.ToProto()).IsSupersetOf([2003, 2004, 2005, 2006]) &&
            WarpSchedule.GetStoryProgress(player)?.LockedPhases.Count == 0);

        option.StoryByChapter = true;
        check("story by chapter locks the four 1.0 / 1.1 phases of a new player",
            WarpSchedule.GetStoryProgress(player)?.LockedPhases.SetEquals(["1.0-1", "1.1-1", "1.0-2", "1.1-2"]) == true);
        check("static + story by chapter sends a new player no limited banner (none of 2003-2008 / 3003-3008)",
            LimitedIds(player.GachaManager.ToProto()).Count == 0);
        check("static + story by chapter refuses Seele's banner before Jarilo-VI",
            (await player.GachaManager.DoGacha(2003, 1))?.Retcode == (uint)Retcode.RetGachaIdNotExist);

        // Rotation with no phase unlocked: no phase, no limited banner, and no fallback to the whole pool.
        WarpSchedule.RestartRotation("1.0-1", WarpSchedule.Now());
        check("rotation + story by chapter sends a new player no limited banner",
            LimitedIds(player.GachaManager.ToProto()).Count == 0 &&
            WarpSchedule.GetRotation(WarpSchedule.Now(), WarpSchedule.GetStoryProgress(player)).Phase == null);
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Version);
        check("rotation + story by chapter with the Version lineup sends a new player no limited banner",
            LimitedIds(player.GachaManager.ToProto()).Count == 0);
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);
        option.Mode = nameof(WarpScheduleModeEnum.Static);

        // Jarilo-VI: Seele and Silver Wolf; Jing Yuan and Luocha still wait for the Xianzhou Luofu.
        player.MissionManager!.Data.SetMainMissionStatus(1010002, MissionPhaseEnum.Accept);
        check("starting Jarilo-VI unlocks the Seele and Silver Wolf banners only",
            LimitedIds(player.GachaManager.ToProto()).SetEquals([2003, 3003, 2005, 3005]));
        check("story by chapter refuses Jing Yuan's banner before the Xianzhou Luofu",
            (await player.GachaManager.DoGacha(2004, 1))?.Retcode == (uint)Retcode.RetGachaIdNotExist);
        WarpSchedule.RestartRotation("1.0-2", WarpSchedule.Now());
        check("rotation + story by chapter starts at the oldest unlocked phase when the start phase is chapter-locked",
            LimitedIds(player.GachaManager.ToProto()).SetEquals([2003, 3003]));
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Version);
        check("the Version lineup serves 1.0 without its chapter-locked phase",
            LimitedIds(player.GachaManager.ToProto()).SetEquals([2003, 3003]));
        option.PhaseLineup = nameof(WarpPhaseLineupEnum.Started);
        option.Mode = nameof(WarpScheduleModeEnum.Static);

        player.MissionManager.Data.SetMainMissionStatus(1020101, MissionPhaseEnum.Accept);
        check("starting the first Xianzhou Luofu chapter also unlocks Jing Yuan and Luocha",
            LimitedIds(player.GachaManager.ToProto()).SetEquals(firstEight));

        // A later version reached opens every chapter gate of the versions before it.
        var later = new PlayerInstance(60045) { PendingMissionEnabledOverride = true };
        await later.InitializeAsync();
        later.MissionManager!.Data.SetMainMissionStatus(1021201, MissionPhaseEnum.Accept);
        check("a player who reached 1.2 gets all four 1.0 / 1.1 banners with story by chapter",
            LimitedIds(later.GachaManager!.ToProto()).IsSupersetOf(firstEight) &&
            WarpSchedule.GetStoryProgress(later)?.LockedPhases.Count == 0);
    }

    // The live new-account flow on the permanent pools: the warp tutorial (3002) opens once main 1000301 ("A Moment
    // of Peace") is done and its one Stellar Warp pull is Asta (1009); the Departure Warp (4001) takes 50 pulls.
    private static async Task PermanentScenarios(Action<string, bool> check)
    {
        var option = ConfigManager.Config.ServerOption.Warps;
        option.Story = false;
        option.StoryByChapter = false;
        option.Mode = nameof(WarpScheduleModeEnum.Static);

        var player = new PlayerInstance(60046) { PendingMissionEnabledOverride = true };
        await player.InitializeAsync();
        var missions = player.MissionManager!.Data;
        var inventory = player.InventoryManager!;
        int Passes() => inventory.GetItem(101)?.Count ?? 0;
        var normal = player.GachaManager!.Data.GetPityState(GachaPityFamilyEnum.Normal);

        // Right after 1000301 the tutorial's config finish (1000303) is not done yet; the tutorial still opens, also
        // with the input-lock guard on, because its script finishes it.
        var server = ConfigManager.Config.ServerOption;
        var savedGuard = server.TutorialInputGuard;
        missions.SetMainMissionStatus(1000301, MissionPhaseEnum.Finish);
        missions.SetMainMissionStatus(1000303, MissionPhaseEnum.Accept);
        server.TutorialInputGuard = true;
        check("the warp tutorial 3002 opens right after main 1000301, even with the input-lock guard on",
            TutorialHelper.Unlock(player, 3002) == TutorialStatus.TutorialUnlock &&
            player.TutorialData!.Tutorials[3002] == TutorialStatus.TutorialUnlock);
        check("with the input-lock guard on, battle tutorial 2012 is closed on arrival",
            TutorialHelper.Unlock(player, 2012) == TutorialStatus.TutorialFinish);

        // Guard off (the default): every tutorial opens as on live; turning the guard on closes it at the next login.
        server.TutorialInputGuard = false;
        check("with the input-lock guard off, free-play tutorial 3006 opens although 1000511 is not finished",
            TutorialHelper.Unlock(player, 3006) == TutorialStatus.TutorialUnlock &&
            TutorialHelper.MayStayOpen(player, 3006));
        server.TutorialInputGuard = true;
        check("turning the input-lock guard on marks the open tutorial 3006 for closing at login, keeps 3002 open",
            !TutorialHelper.MayStayOpen(player, 3006) && TutorialHelper.MayStayOpen(player, 3002));
        server.TutorialInputGuard = savedGuard;

        // The tutorial's pull, paid with the pass from 1000301's reward.
        var startPasses = Passes();
        await inventory.AddItem(101, 1, false, sync: false);
        var tutorialPull = await player.GachaManager.DoGacha(1001, 1);
        check("the warp tutorial's Stellar Warp pull is Asta, new, paid with the one pass",
            tutorialPull is { Retcode: 0, GachaItemList.Count: 1 } &&
            tutorialPull.GachaItemList[0].GachaItem_.ItemId == 1009 && tutorialPull.GachaItemList[0].IsNew &&
            player.AvatarManager!.GetFormalAvatar(1009) != null && Passes() == startPasses);
        check("the tutorial pull is in the warp records and counts as a Stellar Warp 4-star",
            player.GachaManager.Data.GachaHistory.LastOrDefault() is { GachaId: 1001, ItemId: 1009 } &&
            normal is { TotalPulls: 1, PullsSinceFourStarOrHigher: 0, PullsSinceFiveStar: 1 });

        // The tutorial finishes itself; opening it again cannot arm a second Asta.
        TutorialHelper.RecordTutorial(player, 3002, TutorialStatus.TutorialFinish);
        check("a finished warp tutorial stays finished and is not armed again",
            TutorialHelper.Unlock(player, 3002) == TutorialStatus.TutorialFinish &&
            player.GachaManager.Data is { TutorialPullArmed: false, TutorialPullDone: true });

        // A tutorial re-sent while still open (relog before the click) arms once; a 10-pull starts with Asta.
        var relog = new PlayerInstance(60047) { PendingMissionEnabledOverride = true };
        await relog.InitializeAsync();
        relog.MissionManager!.Data.SetMainMissionStatus(1000301, MissionPhaseEnum.Finish);
        TutorialHelper.Unlock(relog, 3002);
        TutorialHelper.Unlock(relog, 3002);
        await relog.InventoryManager!.AddItem(101, 10, false, sync: false);
        var ten = await relog.GachaManager!.DoGacha(1001, 10);
        check("a Stellar Warp 10-pull after the tutorial opened starts with the one Asta",
            ten is { Retcode: 0, GachaItemList.Count: 10 } && ten.GachaItemList[0].GachaItem_.ItemId == 1009 &&
            relog.GachaManager.Data.GetPityState(GachaPityFamilyEnum.Normal).TotalPulls == 10 &&
            relog.GachaManager.Data is { TutorialPullArmed: false, TutorialPullDone: true });

        // Departure Warp: permanent, 10 pulls only at 8 passes, a standard 5-star within 50, then gone.
        var departure = player.GachaManager.ToProto().GachaInfoList.FirstOrDefault(g => g.GachaId == 4001);
        check("a new account is sent the Departure Warp without a time window, showing the seven standard 5-stars",
            departure is { BeginTime: 0, EndTime: 0 } &&
            departure.PrizeItemList.Order().SequenceEqual([1003u, 1004u, 1101u, 1104u, 1107u, 1209u, 1211u]));
        await inventory.AddItem(101, 48, false, sync: false);
        check("the Departure Warp refuses a single pull and charges nothing",
            (await player.GachaManager.DoGacha(4001, 1))?.Retcode == (uint)Retcode.RetGachaNumInvalid &&
            Passes() == startPasses + 48);
        var results = new List<List<int>>();
        for (var batch = 0; batch < 5; batch++)
        {
            var rsp = await player.GachaManager.DoGacha(4001, 10);
            if (rsp is { Retcode: 0 })
                results.Add(rsp.GachaItemList.Select(g => (int)g.GachaItem_.ItemId).ToList());
        }

        var gold = player.GachaManager.GetGoldAvatars();
        check("five Departure Warp 10-pulls cost 40 passes, each with a 4-star or better, and one standard 5-star",
            results.Count == 5 && Passes() == startPasses + 8 &&
            results.All(batch => batch.Count == 10 && batch.Any(id => player.GachaManager.GetRarity(id) >= 4)) &&
            results.SelectMany(b => b).Any(gold.Contains) &&
            results.SelectMany(b => b).Where(id => player.GachaManager.GetRarity(id) == 5).All(gold.Contains));
        check("the Departure Warp closes after 50 pulls and leaves the Stellar Warp counters alone",
            (await player.GachaManager.DoGacha(4001, 10))?.Retcode == (uint)Retcode.RetGachaNewbieClose &&
            Passes() == startPasses + 8 && player.GachaManager.ToProto().GachaInfoList.All(g => g.GachaId != 4001) &&
            player.GachaManager.ToProto().GachaInfoList.Any(g => g.GachaId == 1001) && normal.TotalPulls == 1);
    }

    private static readonly HashSet<int> Collab = [5001, 5002, 5003, 5004, 6001, 6002, 6003, 6004];

    // Limited banner ids in the response, collab banners left out (they are their own group).
    private static HashSet<int> LimitedIds(GetGachaInfoScRsp info)
    {
        return info.GachaInfoList.Select(g => (int)g.GachaId)
            .Where(id => GameData.BannersConfig.Banners.Concat(GameData.BannerHistoryConfig.Banners)
                .Any(b => b.GachaId == id && WarpSchedule.IsLimited(b.GachaType) && !WarpSchedule.IsCollab(b.GachaType)))
            .ToHashSet();
    }

    private static HashSet<int> CollabIds(GetGachaInfoScRsp info)
    {
        return info.GachaInfoList.Select(g => (int)g.GachaId).Where(Collab.Contains).ToHashSet();
    }

    // Banners.json limited pools a Static player gets: those in their window, collab rows excluded.
    private static IEnumerable<int> StaticLimited()
    {
        return GameData.BannersConfig.Banners
            .Where(b => WarpSchedule.IsLimited(b.GachaType) && !WarpSchedule.IsCollab(b.GachaType) &&
                        b.IsActiveAt(WarpSchedule.Now()))
            .Select(b => b.GachaId);
    }
}
