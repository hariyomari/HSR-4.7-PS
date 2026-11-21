using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Enums;
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
        File.Copy(Path.Combine(repository, "Config/Custom/Banners.history.json"),
            Path.Combine(scratch, "Custom/Banners.history.json"), true);
        ResourceManager.ReloadBanners();

        var option = ConfigManager.Config.ServerOption.Warps;
        var saved = (option.Mode, option.RotationDays, option.RotationStart, option.RotationStartPhase, option.Manual);
        try
        {
            await Scenarios(check);
        }
        finally
        {
            (option.Mode, option.RotationDays, option.RotationStart, option.RotationStartPhase, option.Manual) = saved;
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

        // Rotation from 4.7 phase 2: Aha continues, three reruns, plus the long-running collab pools.
        check("rotation restarts from a known history phase", WarpSchedule.RestartRotation("4.7-2", WarpSchedule.Now()) == null);
        var expected = WarpSchedule.GetScheduledBanners(WarpSchedule.Now()).Select(b => b.GachaId).ToHashSet();
        var info = player.GachaManager!.ToProto();
        var limited = LimitedIds(info);
        check("rotation sends exactly the scheduled limited banners, collab pools included",
            limited.SetEquals(expected) && limited.IsSupersetOf([2142, 2146, 2147, 2148, 3142, 3146, 3147, 3148, 5001]));
        check("rotation still sends the permanent standard banner", info.GachaInfoList.Any(g => g.GachaId == 1001));
        check("rotation windows contain the player's time",
            info.GachaInfoList.Where(g => limited.Contains((int)g.GachaId)).All(g => g.BeginTime <= now && now <= g.EndTime));
        check("Banners.json limited pools are not served in rotation", !limited.Contains(2139));

        var pull = await player.GachaManager.DoGacha(2146, 1);
        check("a scheduled rerun banner pays out a real item",
            pull is { Retcode: 0, GachaItemList.Count: 1 } &&
            GameData.ItemConfigData.ContainsKey((int)pull.GachaItemList[0].GachaItem_.ItemId));
        var off = await player.GachaManager.DoGacha(2139, 1);
        check("a limited banner outside the schedule is refused",
            off?.Retcode == (uint)Retcode.RetGachaIdNotExist);

        // Manual: one old character banner with a featured override; everything else is gone.
        option.Manual = [new() { GachaId = 2003, RateUpItems5 = [1102] }];
        option.Mode = nameof(WarpScheduleModeEnum.Manual);
        info = player.GachaManager.ToProto();
        check("manual mode sends only the picked limited banner",
            LimitedIds(info).SetEquals([2003]) && info.GachaInfoList.Any(g => g.GachaId == 1001));
        check("manual mode refuses the banner rotation served a moment ago",
            (await player.GachaManager.DoGacha(2146, 1))?.Retcode == (uint)Retcode.RetGachaIdNotExist);
        check("manual mode pulls on the picked 1.0 banner",
            (await player.GachaManager.DoGacha(2003, 1))?.Retcode == 0);

        // Static: Banners.json as written again.
        option.Mode = nameof(WarpScheduleModeEnum.Static);
        check("static mode serves Banners.json again",
            LimitedIds(player.GachaManager.ToProto()).SetEquals(GameData.BannersConfig.Banners
                .Where(b => WarpSchedule.IsLimited(b.GachaType) && b.IsActiveAt(WarpSchedule.Now()))
                .Select(b => b.GachaId)));
    }

    private static HashSet<int> LimitedIds(GetGachaInfoScRsp info)
    {
        return info.GachaInfoList.Select(g => (int)g.GachaId)
            .Where(id => GameData.BannersConfig.Banners.Concat(GameData.BannerHistoryConfig.Banners)
                .Any(b => b.GachaId == id && WarpSchedule.IsLimited(b.GachaType)))
            .ToHashSet();
    }
}
