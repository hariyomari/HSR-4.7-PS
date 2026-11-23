using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Player;
using March7thHoney.Enums.Item;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Challenge;
using March7thHoney.GameServer.Game.Challenge.Instances;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.Proto;
using MemoryPack;

/// <summary>
///     Pure Fiction stage scores, finished/failed-run persistence for Pure Fiction, Memory of Chaos and Apocalyptic
///     Shadow, settlement when the client omits a battle-target list (Apocalyptic Shadow, Anomaly Arbitration), and
///     the Anomaly Arbitration king's limited-time head frame. Battles are not fought: results are fed to the instance
///     directly.
/// </summary>
internal static class ChallengeRegression
{
    public static async Task Run(Action<string, bool> check)
    {
        await PureFiction(check);
        await Memory(check);
        await Boss(check);
        Peak(check);
        await PeakHeadFrame(check);
    }

    private static async Task<PlayerInstance> NewPlayer(int uid)
    {
        var player = new PlayerInstance(uid);
        await player.InitializeAsync();
        var avatars = player.AvatarManager!.Data.FormalAvatars.Select(avatar => avatar.BaseAvatarId).Distinct()
            .Take(8).ToList();
        // The two halves never share an avatar (Apocalyptic Shadow keys its stage info by avatar id).
        var half = avatars.Count / 2;
        player.LineupManager!.SetExtraLineup(ExtraLineupType.LineupChallenge, avatars[..half]);
        player.LineupManager.SetExtraLineup(ExtraLineupType.LineupChallenge2, avatars[half..]);
        await player.LineupManager.SetExtraLineup(ExtraLineupType.LineupNone, false);
        return player;
    }

    private static ChallengeStateData? Saved(PlayerInstance player)
    {
        return player.ChallengeManager!.ChallengeData.ChallengeInstance is { } blob
            ? MemoryPackSerializer.Deserialize<ChallengeStateData>(Convert.FromBase64String(blob))
            : null;
    }

    private static async Task ClearStageMonsters(PlayerInstance player)
    {
        foreach (var monster in player.SceneInstance!.Entities.Values.OfType<EntityMonster>().ToList())
            await player.SceneInstance.RemoveEntity(monster, false);
    }

    private static List<(uint Id, uint Progress)> TargetsOf(BattleInstance battle, int key)
    {
        return battle.BattleTargets.TryGetValue(key, out var list)
            ? list.BattleTargetList_.Select(target => (target.Id, target.Progress)).ToList()
            : [];
    }

    private static PVEBattleResultCsReq Result(BattleEndStatus status, uint score)
    {
        return new PVEBattleResultCsReq { EndStatus = status, Stt = new BattleStatistics { ChallengeScore = score } };
    }

    private static async Task PureFiction(Action<string, bool> check)
    {
        var config = GameData.ChallengeConfigData.Values
            .Where(c => c.IsStory() && c.StageNum >= 2 && c.StoryExcel != null)
            .MaxBy(c => c.ID);
        check("PF cache carries a two-stage story challenge", config != null);
        if (config == null) return;

        var player = await NewPlayer(60401);
        var manager = player.ChallengeManager!;
        await manager.StartChallenge(config.ID, new ChallengeStoryBuffInfo(), null);
        var story = manager.ChallengeInstance as ChallengeStoryInstance;
        check($"PF {config.ID} starts on stage 1", story is { Data.Story.CurrentStage: 1 });
        if (story == null) return;

        var state = story.Data.Story!;
        var startScore = state.ScoreStage1;
        state.ScoreStage1 = 1200;
        var storyBattle = new BattleInstance(player, player.LineupManager!.GetCurLineup()!, new List<EntityMonster>());
        story.OnBattleStart(storyBattle);
        check("PF battle carries the 10003 score container and the stage targets at the run score",
            TargetsOf(storyBattle, 1).SequenceEqual([(10003u, 1200u)]) &&
            TargetsOf(storyBattle, 5).SequenceEqual(config.StoryExcel!.BattleTargetID!.Select(id => ((uint)id, 1200u))));
        state.ScoreStage1 = startScore;

        // A stage-1 win with monsters left keeps the stage open; the client reports the cumulative score.
        await story.OnBattleEnd(null!, Result(BattleEndStatus.BattleEndWin, 30000));
        check("PF stage-1 win with monsters left records the score and stays on stage 1",
            state is { ScoreStage1: 30000, CurrentStage: 1 });

        await story.OnBattleEnd(null!, Result(BattleEndStatus.BattleEndQuit, 0));
        check("PF quit keeps the stage score instead of wrapping a negative delta",
            state is { ScoreStage1: 30000, ScoreStage2: 0, CurrentStage: 1 });

        // The re-fight is seeded with the run total (30000), so 45000 is the stage's cumulative score.
        await story.OnBattleEnd(null!, Result(BattleEndStatus.BattleEndLose, 45000));
        check("PF stage-1 re-fight keeps the stage's earlier score and advances",
            state is { ScoreStage1: 45000, CurrentStage: 2 });

        await story.OnBattleEnd(null!, Result(BattleEndStatus.BattleEndLose, 65000));
        check("PF stage 2 subtracts only the stage-1 score",
            state is { ScoreStage1: 45000, ScoreStage2: 20000 } && story.GetTotalScore() == 65000);

        var saved = Saved(player)?.Story;
        check("PF final settle persists the finished state and the stage-2 score",
            saved is { CurStatus: (uint)ChallengeStatus.ChallengeFinish, CurrentStage: 2, ScoreStage2: 20000 });
        check("PF final settle records the split scores in history",
            manager.ChallengeData.History.TryGetValue(config.ID, out var history) &&
            history is { Score: 65000, ScoreOne: 45000, ScoreTwo: 20000 });
    }

    private static async Task Memory(Action<string, bool> check)
    {
        var config = GameData.ChallengeConfigData.Values
            .Where(c => !c.IsStory() && !c.IsBoss() && c.StageNum >= 2)
            .MaxBy(c => c.ID);
        check("MoC cache carries a two-stage memory challenge", config != null);
        if (config == null) return;

        var player = await NewPlayer(60403);
        var manager = player.ChallengeManager!;
        await manager.StartChallenge(config.ID, null, null);
        var memory = manager.ChallengeInstance as ChallengeMemoryInstance;
        check($"MoC {config.ID} starts on stage 1", memory is { Data.Memory.CurrentStage: 1 });
        if (memory == null) return;

        var win = new PVEBattleResultCsReq
            { EndStatus = BattleEndStatus.BattleEndWin, Stt = new BattleStatistics { RoundCnt = 1 } };
        await ClearStageMonsters(player);
        await memory.OnBattleEnd(new BattleInstance(player, player.LineupManager!.GetCurLineup()!, new List<EntityMonster>()), win);
        await ClearStageMonsters(player);
        await memory.OnBattleEnd(new BattleInstance(player, player.LineupManager.GetCurLineup()!, new List<EntityMonster>()), win);
        var state = memory.Data.Memory!;
        var saved = Saved(player)?.Memory;
        check("MoC final settle persists the finished state and remaining rounds",
            state.CurStatus == (uint)ChallengeStatus.ChallengeFinish &&
            saved is { CurStatus: (uint)ChallengeStatus.ChallengeFinish, CurrentStage: 2 } &&
            saved.RoundsLeft == state.RoundsLeft);

        await manager.StartChallenge(config.ID, null, null);
        if (manager.ChallengeInstance is ChallengeMemoryInstance failed)
            await failed.OnBattleEnd(null!, Result(BattleEndStatus.BattleEndNone, 0));
        check("MoC failed settle persists the failed state",
            Saved(player)?.Memory is { CurStatus: (uint)ChallengeStatus.ChallengeFailed });
    }

    private static async Task Boss(Action<string, bool> check)
    {
        var config = GameData.ChallengeConfigData.Values
            .Where(c => c.IsBoss() && c.StageNum >= 2)
            .MaxBy(c => c.ID);
        check("AS cache carries a two-stage boss challenge", config != null);
        if (config == null) return;

        var player = await NewPlayer(60404);
        var manager = player.ChallengeManager!;
        await manager.StartChallenge(config.ID, null, new ChallengeBossBuffInfo());
        var boss = manager.ChallengeInstance as ChallengeBossInstance;
        check($"AS {config.ID} starts on stage 1", boss is { Data.Boss.CurrentStage: 1 });
        if (boss == null) return;

        var bossBattle = new BattleInstance(player, player.LineupManager!.GetCurLineup()!, new List<EntityMonster>());
        boss.OnBattleStart(bossBattle);
        check("AS battle carries the 90004/90005 score header pair",
            TargetsOf(bossBattle, 1).SequenceEqual([(90004u, 0u), (90005u, 0u)]) && !bossBattle.BattleTargets.ContainsKey(5));

        var tolerated = true;
        try
        {
            await boss.OnBattleEnd(null!, Result(BattleEndStatus.BattleEndLose, 0));
        }
        catch (KeyNotFoundException)
        {
            tolerated = false;
        }

        check("AS phase settle tolerates an omitted type-1 target list",
            tolerated && boss.Data.Boss is { ScoreStage1: 0, CurrentStage: 1 });

        var reported = Result(BattleEndStatus.BattleEndLose, 0);
        var list = new BattleTargetList();
        list.BattleTargetList_.Add(new BattleTarget { Id = 1, Progress = 5000 });
        list.BattleTargetList_.Add(new BattleTarget { Id = 2, Progress = 3000 });
        reported.Stt.BattleTargetInfo.Add(1, list);
        await boss.OnBattleEnd(null!, reported);
        check("AS phase settle still sums reported score targets", boss.Data.Boss is { ScoreStage1: 8000 });

        var quit = Result(BattleEndStatus.BattleEndQuit, 0);
        var partial = new BattleTargetList();
        partial.BattleTargetList_.Add(new BattleTarget { Id = 1, Progress = 1200 });
        quit.Stt.BattleTargetInfo.Add(1, partial);
        await boss.OnBattleEnd(null!, quit);
        check("AS quit keeps the phase score instead of the partial progress", boss.Data.Boss is { ScoreStage1: 8000 });

        await boss.OnBattleEnd(null!, Result(BattleEndStatus.BattleEndNone, 0));
        check("AS failed settle persists the failed state",
            Saved(player)?.Boss is { CurStatus: (uint)ChallengeStatus.ChallengeFailed });
    }

    private static void Peak(Action<string, bool> check)
    {
        var config = GameData.ChallengePeakConfigData.Values
            .Where(c => c.BossExcel == null && c.NormalTargetList.Any(GameData.BattleTargetConfigData.ContainsKey))
            .MinBy(c => c.ID);
        check("Peak cache carries a mob level with battle targets", config != null);
        if (config == null) return;

        var player = new PlayerInstance(60402);
        var peak = new ChallengePeakInstance(player, new ChallengeStateData
        {
            Peak = new PeakChallengeState { CurrentPeakLevelId = (uint)config.ID }
        });
        var targetIds = config.NormalTargetList.Where(GameData.BattleTargetConfigData.ContainsKey).ToList();
        peak.AllBattleTargets.AddRange(targetIds);

        (uint, List<uint>)? missing = null;
        try
        {
            missing = peak.CalculateStars(Result(BattleEndStatus.BattleEndWin, 0));
        }
        catch (KeyNotFoundException)
        {
        }

        check("Peak settle tolerates an omitted type-5 target list",
            missing is { Item1: 0 } && missing.Value.Item2.Count == 0);

        var reported = Result(BattleEndStatus.BattleEndWin, 0);
        var list = new BattleTargetList();
        list.BattleTargetList_.AddRange(targetIds.Select(id => new BattleTarget
        {
            Id = (uint)id,
            Progress = (uint)GameData.BattleTargetConfigData[id].TargetParam
        }));
        reported.Stt.BattleTargetInfo.Add(5, list);
        var met = peak.CalculateStars(reported);
        check("Peak settle still counts reported targets",
            met.Item1 == Math.Min(targetIds.Count, 3) && met.Item2.Count == targetIds.Count);
    }

    /// <summary>
    ///     King-level wins run through the real settle path (StartChallenge -> OnBattleEnd -> SaveHistory) and the
    ///     head frame is read back. Expected frames come straight from ChallengePeakReward: medal tier n ->
    ///     BOSS_STAR_LIMIT_REWARD TypeValue n, colour medal -> BOSS_COLOR_TARGET_REWARD, each via RewardData.
    /// </summary>
    private static async Task PeakHeadFrame(Action<string, bool> check)
    {
        var groupId = GameData.GetCurrentChallengePeakGroupId();
        var group = GameData.ChallengePeakGroupConfigData.GetValueOrDefault(groupId);
        var king = group == null ? null : GameData.ChallengePeakConfigData.GetValueOrDefault(group.BossLevelID);
        var turnLimits = king?.NormalTargetList.Select(id => GameData.BattleTargetConfigData.GetValueOrDefault(id))
            .Where(target => target != null).Select(target => target!.TargetParam).OrderByDescending(x => x).ToList();
        check("Peak cache carries the current season's king with three distinct normal turn targets",
            king?.BossExcel != null && turnLimits is { Count: 3 } && turnLimits.Distinct().Count() == 3 &&
            turnLimits[2] >= 1);
        if (group == null || king?.BossExcel == null || turnLimits is not { Count: 3 }) return;
        var kingId = king.ID;

        var line =GameData.ChallengePeakRewardData.GetValueOrDefault(group.RewardGroupID) ?? [];
        uint FrameOf(ChallengePeakRewardExcel? row)
        {
            var reward = row == null ? null : GameData.RewardDataData.GetValueOrDefault(row.RewardID);
            return (uint)(reward?.GetItems().Select(item => item.Item1).FirstOrDefault(id =>
                GameData.ItemConfigData.GetValueOrDefault(id)?.ItemSubType == ItemSubTypeEnum.HeadIconFrame) ?? 0);
        }

        uint StarFrame(int stars) =>
            FrameOf(line.FirstOrDefault(row => row.RewardType == "BOSS_STAR_LIMIT_REWARD" && row.TypeValue == stars));
        var bronze = StarFrame(1);
        var silver = StarFrame(2);
        var gold = StarFrame(3);
        var colour = FrameOf(line.FirstOrDefault(row => row.RewardType == "BOSS_COLOR_TARGET_REWARD"));
        uint[] frames = [bronze, silver, gold, colour];
        check("Peak reward table gives the four medal tiers four distinct head-frame items",
            frames.All(id => id != 0) && frames.Distinct().Count() == 4);
        if (frames.Any(id => id == 0)) return;

        // Every normal target is a "win within N cycles" target: N+1 cycles misses it.
        var noTarget = (uint)turnLimits[0] + 1;
        var oneTarget = (uint)turnLimits[1] + 1;
        var twoTargets = (uint)turnLimits[2] + 1;
        const uint allTargets = 1; // all three met, but not the 0-cycle colour target
        const uint zeroCycle = 0;

        async Task<PlayerHeadFrameInfo> Win(PlayerInstance player, bool hard, uint cycles)
        {
            var manager = player.ChallengePeakManager!;
            manager.SetBossHard(groupId, hard);
            var team = (player.LineupManager!.GetExtraLineup(ExtraLineupType.LineupChallenge)?.BaseAvatars ?? [])
                .Select(avatar => avatar.BaseAvatarId).ToList();
            var started = await manager.StartChallenge(kingId, 0, team);
            if (started != Retcode.RetSucc || player.ChallengeManager!.ChallengeInstance is not ChallengePeakInstance run)
                throw new InvalidOperationException($"Peak king {kingId} did not start: {started}");

            var battle = new BattleInstance(player, player.LineupManager.GetCurLineup()!, new List<EntityMonster>());
            run.OnBattleStart(battle);
            var result = new PVEBattleResultCsReq
                { EndStatus = BattleEndStatus.BattleEndWin, Stt = new BattleStatistics { RoundCnt = cycles } };
            var targets = new BattleTargetList();
            targets.BattleTargetList_.AddRange(run.AllBattleTargets.Select(id => new BattleTarget
                { Id = (uint)id, Progress = cycles }));
            result.Stt.BattleTargetInfo.Add(5, targets);
            await ClearStageMonsters(player);
            await run.OnBattleEnd(battle, result);
            return player.Data.HeadFrame;
        }

        // Normal difficulty: the frame follows the medal tier and never steps down or is re-sent.
        var normal = await NewPlayer(60405);
        check("Peak normal king win with no target met grants no head frame",
            (await Win(normal, false, noTarget)).HeadFrameId == 0);
        check("Peak normal king win with 1 target met grants the bronze frame",
            (await Win(normal, false, oneTarget)).HeadFrameId == bronze);
        check("Peak normal king win with 2 targets met grants the silver frame",
            (await Win(normal, false, twoTargets)).HeadFrameId == silver);
        var goldFrame = await Win(normal, false, allTargets);
        check("Peak normal king win with 3 targets met (1 cycle) grants the gold frame",
            goldFrame.HeadFrameId == gold);
        check("Peak lower normal king win neither downgrades nor re-sends the gold frame",
            ReferenceEquals(await Win(normal, false, oneTarget), goldFrame) && goldFrame.HeadFrameId == gold);
        var colourFrame = await Win(normal, false, zeroCycle);
        check("Peak normal king 0-cycle win (colour medal at settlement) grants the colour frame, not gold",
            colourFrame.HeadFrameId == colour);
        check("Peak head frames expire at a timestamp the client can read, not long.MaxValue",
            colourFrame.HeadFrameExpireTime == PlayerHeadFrameInfo.NoSeasonExpireTime &&
            colourFrame.HeadFrameExpireTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
            colourFrame.HeadFrameExpireTime < 1L << 53);
        check("Peak 3-target normal win after the colour medal keeps the colour frame without re-sending it",
            ReferenceEquals(await Win(normal, false, allTargets), colourFrame) && colourFrame.HeadFrameId == colour);
        // The client only offers the display switch while a head-frame item is in the bag.
        check("Peak bag holds each head frame the normal player was given, once",
            frames.All(id => normal.InventoryManager!.GetItem((int)id)?.Count == 1));

        // Hard difficulty: any hard (jue jing) win is the colour medal, granted once.
        var hardPlayer = await NewPlayer(60406);
        var hardFrame = await Win(hardPlayer, true, allTargets);
        check("Peak hard king win grants the colour frame", hardFrame.HeadFrameId == colour);
        check("Peak repeated hard king win does not re-send the colour frame",
            ReferenceEquals(await Win(hardPlayer, true, allTargets), hardFrame));
        check("Peak normal 1-target king win after hard keeps the colour frame",
            (await Win(hardPlayer, false, oneTarget)).HeadFrameId == colour);

        bool OnlyColourInBag(PlayerInstance player) =>
            player.InventoryManager!.GetItem((int)colour)?.Count == 1 &&
            new[] { bronze, silver, gold }.All(id => player.InventoryManager.GetItem((int)id) == null);
        check("Peak bag holds the hard player's colour frame once and no lower tier", OnlyColourInBag(hardPlayer));

        // Claiming rewards never hands the head-frame rows out again (the client cannot claim them either).
        var frameRows = line.Where(row => row.RewardType is "BOSS_STAR_LIMIT_REWARD" or "BOSS_COLOR_TARGET_REWARD")
            .Select(row => (uint)row.ID).ToHashSet();
        var claimed = await hardPlayer.ChallengePeakManager!.TakeRewards(groupId, frameRows) ?? [];
        var claimedAll = await hardPlayer.ChallengePeakManager.TakeRewards(groupId, []) ?? [];
        check("Peak reward claims skip the head-frame rows and add no frame item",
            claimed.Count == 0 && claimedAll.All(rewardGroup => !frameRows.Contains(rewardGroup.RewardId)) &&
            OnlyColourInBag(hardPlayer));

        // A save holding the id the old "226000 + targets" formula wrote for a 0-target win is not a valid frame.
        var legacy = await NewPlayer(60407);
        legacy.Data.HeadFrame = new PlayerHeadFrameInfo { HeadFrameId = bronze - 1, HeadFrameExpireTime = long.MaxValue };
        check("Peak 1-target king win replaces a stored non-frame id with the bronze frame",
            GameData.ItemConfigData.GetValueOrDefault((int)bronze - 1)?.ItemSubType != ItemSubTypeEnum.HeadIconFrame &&
            (await Win(legacy, false, oneTarget)).HeadFrameId == bronze);
    }
}
