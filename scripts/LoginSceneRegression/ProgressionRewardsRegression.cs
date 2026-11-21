using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Fight;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Progression;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.Proto;
using March7thHoney.Util;

// Progression bonus (ServerOption.ProgressionRewards) against the real mission and item tables. Mission checks run the
// same flow on a player with the bonus off and one with it on, so the official rewards cancel out of the difference.
internal static class ProgressionRewardsRegression
{
    private static int _nextUid = 60100;

    public static async Task Run(Action<string, bool> check)
    {
        var option = ConfigManager.Config.ServerOption;
        var saved = option.ProgressionRewards;
        try
        {
            option.ProgressionRewards = new ProgressionRewardsOption();
            await Battles(check, option.ProgressionRewards);
            option.ProgressionRewards = new ProgressionRewardsOption();
            await Missions(check, option.ProgressionRewards);
        }
        finally
        {
            option.ProgressionRewards = saved;
        }
    }

    private static async Task Battles(Action<string, bool> check, ProgressionRewardsOption rewards)
    {
        var player = new PlayerInstance(_nextUid++);
        await player.InitializeAsync();
        await player.TryEnterScene(1000001, 0, false);

        check("a plain fight on a town map is classified as story",
            ProgressionRewards.Classify(player, NewBattle(player)) == ProgressionBattleKindEnum.Story);
        var cocoon = NewBattle(player);
        cocoon.CocoonId = 1;
        check("a calyx fight is classified as a domain",
            ProgressionRewards.Classify(player, cocoon) == ProgressionBattleKindEnum.Domain);
        var rogue = NewBattle(player);
        rogue.IsTournRogue = true;
        check("a Divergent Universe fight is classified as rogue",
            ProgressionRewards.Classify(player, rogue) == ProgressionBattleKindEnum.Rogue);

        var (jade, _, _, drops) = await Fight(player, NewBattle(player), BattleEndStatus.BattleEndWin);
        check("the battle bonus pays nothing while the feature is off", jade == 0 && drops == 0);

        rewards.Enabled = true;
        var (winJade, winCredit, _, winDrops) = await Fight(player, NewBattle(player), BattleEndStatus.BattleEndWin);
        check("a won story fight pays the default battle bonus and lists it with the drops",
            winJade == 20 && winCredit == 500 && winDrops == 2);
        var (loseJade, _, _, _) = await Fight(player, NewBattle(player), BattleEndStatus.BattleEndLose);
        check("a lost story fight pays nothing", loseJade == 0);

        var (domainJade, _, _, _) = await Fight(player, Domain(player), BattleEndStatus.BattleEndWin);
        check("a won domain fight pays nothing by default", domainJade == 0);
        rewards.BattleKinds.Domain = true;
        rewards.BattleWin = [new ProgressionRewardItem { ItemId = 1, Count = 7 }];
        var (paidDomainJade, paidDomainCredit, _, _) = await Fight(player, Domain(player), BattleEndStatus.BattleEndWin);
        check("an enabled kind pays the edited battle bonus", paidDomainJade == 7 && paidDomainCredit == 0);
    }

    private static async Task Missions(Action<string, bool> check, ProgressionRewardsOption rewards)
    {
        var main = GameData.MainMissionData.Values.OrderBy(m => m.MainMissionID)
            .First(m => m.Type == MainMissionTypeEnum.Main && m.MissionInfo?.StartSubMissionList.Count > 0);
        var daily = GameData.MainMissionData.Values.OrderBy(m => m.MainMissionID)
            .First(m => m.Type == MainMissionTypeEnum.Daily);

        async Task<bool> FinishFirstSub(PlayerInstance player)
        {
            await player.MissionManager!.AcceptMainMission(main.MainMissionID, false);
            var sub = player.MissionManager.GetRunningSubMissionIdList()
                .FirstOrDefault(id => GameData.SubMissionInfoData[id].MainMissionId == main.MainMissionID);
            if (sub == 0) return false;
            await player.MissionManager.FinishSubMission(sub);
            await player.MissionManager.FinishSubMission(sub); // already finished: must not pay twice
            return player.MissionManager.GetSubMissionStatus(sub) == MissionPhaseEnum.Finish;
        }

        async Task<bool> FinishMain(PlayerInstance player, int missionId)
        {
            await player.MissionManager!.AcceptMainMission(missionId, false);
            if (player.MissionManager.GetMainMissionStatus(missionId) != MissionPhaseEnum.Accept) return false;
            await player.MissionManager.FinishMainMission(missionId);
            await player.MissionManager.FinishMainMission(missionId);
            return player.MissionManager.GetMainMissionStatus(missionId) == MissionPhaseEnum.Finish;
        }

        async Task<bool> FinishMainByCommand(PlayerInstance player)
        {
            using var scope = ProgressionRewards.EnterCommand();
            return await FinishMain(player, main.MainMissionID);
        }

        var sub = await Bonus(rewards, FinishFirstSub);
        check($"finishing a sub-mission of main {main.MainMissionID} pays the sub bonus once",
            sub is { Reached: true, Jade: 60, Credit: 2000, Pass: 0 });

        var mainBonus = await Bonus(rewards, player => FinishMain(player, main.MainMissionID));
        check($"finishing main mission {main.MainMissionID} pays the main bonus once, standard pass included",
            mainBonus is { Reached: true, Jade: 180, Credit: 10000, Pass: 1 });

        var dailyBonus = await Bonus(rewards, player => FinishMain(player, daily.MainMissionID));
        check($"daily main mission {daily.MainMissionID} pays nothing by default",
            dailyBonus is { Reached: true, Jade: 0, Credit: 0, Pass: 0 });

        // Rows without sub-missions have nothing to play (the server closes such Gap/Branch rows itself). The 4.7
        // cache ships none, so this uses an in-process row.
        rewards.Enabled = true;
        var emptyPlayer = new PlayerInstance(_nextUid++);
        await emptyPlayer.InitializeAsync();
        var emptyRow = new MainMissionExcel { MainMissionID = 99900001, Type = MainMissionTypeEnum.Branch };
        var jadeBefore = Counts(emptyPlayer).Jade;
        var emptyPaid = await ProgressionRewards.GrantMainMission(emptyPlayer, emptyRow);
        check("a main mission without sub-missions pays nothing",
            emptyPaid.Count == 0 && Counts(emptyPlayer).Jade == jadeBefore);

        var commandBonus = await Bonus(rewards, FinishMainByCommand);
        check("a mission finished by a command pays nothing by default",
            commandBonus is { Reached: true, Jade: 0, Credit: 0, Pass: 0 });

        rewards.IncludeCommandFinishes = true;
        var allowedCommandBonus = await Bonus(rewards, FinishMainByCommand);
        check("IncludeCommandFinishes lets command finishes pay", allowedCommandBonus is { Reached: true, Jade: 180 });
    }

    // Runs the flow with the bonus off and on and returns the difference in Jade, Credits and Star Rail Passes.
    private static async Task<(bool Reached, long Jade, long Credit, long Pass)> Bonus(ProgressionRewardsOption rewards,
        Func<PlayerInstance, Task<bool>> flow)
    {
        rewards.Enabled = false;
        var off = await Delta(flow);
        rewards.Enabled = true;
        var on = await Delta(flow);
        return (off.Reached && on.Reached, on.Jade - off.Jade, on.Credit - off.Credit, on.Pass - off.Pass);
    }

    private static async Task<(bool Reached, long Jade, long Credit, long Pass)> Delta(Func<PlayerInstance, Task<bool>> flow)
    {
        var player = new PlayerInstance(_nextUid++);
        await player.InitializeAsync();
        player.MissionManager!.Data.EnableMissionOverride = true;
        var before = Counts(player);
        var reached = await flow(player);
        var after = Counts(player);
        return (reached, after.Jade - before.Jade, after.Credit - before.Credit, after.Pass - before.Pass);
    }

    private static async Task<(long Jade, long Credit, long Pass, int Drops)> Fight(PlayerInstance player,
        BattleInstance battle, BattleEndStatus status)
    {
        var before = Counts(player);
        player.BattleInstance = battle;
        await player.BattleManager!.EndBattle(new PVEBattleResultCsReq { EndStatus = status, Stt = new() });
        var after = Counts(player);
        return (after.Jade - before.Jade, after.Credit - before.Credit, after.Pass - before.Pass,
            battle.MonsterDropItems.Count(item => item.ItemId is 1 or 2));
    }

    private static (long Jade, long Credit, long Pass) Counts(PlayerInstance player)
    {
        var inventory = player.InventoryManager!;
        return (inventory.GetItem(1)?.Count ?? 0, inventory.GetItem(2)?.Count ?? 0, inventory.GetItem(101)?.Count ?? 0);
    }

    private static BattleInstance NewBattle(PlayerInstance player)
    {
        return new BattleInstance(player, player.LineupManager!.GetCurLineup()!, new List<EntityMonster>());
    }

    private static BattleInstance Domain(PlayerInstance player)
    {
        var battle = NewBattle(player);
        battle.CocoonId = 1;
        return battle;
    }
}
