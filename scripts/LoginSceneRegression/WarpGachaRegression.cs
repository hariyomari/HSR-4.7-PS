using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Enums;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Gacha;
using March7thHoney.GameServer.Game.Player;
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
            option.PhaseLineup, option.CollabBanners, option.Story);
        try
        {
            await Scenarios(check);
            await StoryScenarios(check);
        }
        finally
        {
            (option.Mode, option.RotationDays, option.RotationStart, option.RotationStartPhase, option.Manual,
                option.PhaseLineup, option.CollabBanners, option.Story) = saved;
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
