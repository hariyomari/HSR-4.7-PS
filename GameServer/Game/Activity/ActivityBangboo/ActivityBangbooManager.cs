using Google.Protobuf;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Database;
using March7thHoney.Database.ActivityBangboo;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Player.Components;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Activity.ActivityBangboo;

/// <summary>
///     Planarcadia Reverie (Bangboo collaboration, client module RPG.Client.ActivityBangboo, activity panel 50147,
///     modules 5014701/5014702). The lane battles run on the client; the server owns the progress the client's stores
///     are filled from on every refresh: unlocked and cleared stages, the Infinity holes (unlock flag, best wave per
///     hole, the run that can be continued with the board archive the client saved after its last cleared wave), the
///     posts read or liked and the anecdote groups watched. Field meanings come from the client code and are listed
///     in docs/development/activity-bangboo.md.
/// </summary>
public class ActivityBangbooManager(PlayerInstance player) : BasePlayerComponent(player)
{
    /// <summary>FDHNDGJCOAC 1: a story stage won, or an Infinity wave cleared and its board saved (SaveAsync).</summary>
    public const uint ReasonWin = 1;

    /// <summary>FDHNDGJCOAC 2: the stage was left (_ExitAsync, AbandonInfinityRunAsync); nothing is cleared.</summary>
    public const uint ReasonExit = 2;

    private static readonly Logger Logger = new("ActivityBangboo");

    public PlayerActivityBangbooData Data { get; } =
        DatabaseHelper.Instance!.GetInstanceOrCreateNew<PlayerActivityBangbooData>(player.Uid);

    private static ActivityBangbooConfig Config => GameData.ActivityBangbooConfig;

    private void MarkDirty() => DatabaseHelper.MarkDirty(Player.Uid);

    #region Unlocks

    public bool IsFinished(int levelId) => Data.FinishedLevelIds.Contains(levelId);

    /// <summary>
    ///     The Infinity holes open with the first clear of the configured stage (GILOPNKGNAA); never without an
    ///     Infinity hole in the config (a missing ActivityBangboo.json must not open holes the server cannot run).
    /// </summary>
    public bool IsInfinityUnlocked => Config.Holes.Any(x => x.IsInfinity) &&
                                      (Config.InfinityUnlockLevelId == 0 || IsFinished(Config.InfinityUnlockLevelId));

    /// <summary>
    ///     Story stages unlock in play order (each needs the one before it cleared), Infinity stages with the
    ///     Infinity holes, and stages outside every hole (the tutorial stage, the boss phases) are always open. With
    ///     <see cref="ActivityBangbooConfig.RequireUnlockSubMission" /> the stage's sub-mission must be finished too.
    /// </summary>
    public bool IsLevelUnlocked(int levelId)
    {
        var level = Config.GetLevel(levelId);
        if (level == null) return false;
        if (Config.RequireUnlockSubMission && level.UnlockSubMissionId != 0 &&
            Player.MissionManager?.GetSubMissionStatus(level.UnlockSubMissionId) != MissionPhaseEnum.Finish)
            return false;

        var hole = Config.GetHoleOfLevel(levelId);
        if (hole == null) return true;
        if (hole.IsInfinity) return IsInfinityUnlocked;
        var chain = Config.StoryChain();
        var index = chain.IndexOf(levelId);
        return index <= 0 || IsFinished(chain[index - 1]);
    }

    public List<int> UnlockedLevelIds() => Config.Levels.Select(x => x.Id).Where(IsLevelUnlocked).ToList();

    /// <summary>A Bangboo listed with an unlock stage is usable once that stage is cleared.</summary>
    public bool IsBangbooUnlocked(int bangbooId)
    {
        var bangboo = Config.GetBangboo(bangbooId);
        return bangboo != null && (bangboo.UnlockLevelId == 0 || IsFinished(bangboo.UnlockLevelId));
    }

    #endregion

    #region Protocol

    /// <summary>The GetData body (ABCMHMIHJBO) as CBAPNPOMOII hands it to the client's stores.</summary>
    public ABCMHMIHJBO ToDataProto()
    {
        var proto = new ABCMHMIHJBO
        {
            GILOPNKGNAA = IsInfinityUnlocked,
            IFDOJGKJEBN = ToRunProto()
        };
        proto.EEJKJMCMJBA.AddRange(UnlockedLevelIds().Select(x => (uint)x));
        proto.ICHOFAHCIIF.AddRange(Data.FinishedLevelIds.Select(x => (uint)x));
        proto.IEKHMACAFMK.AddRange(Data.ReadPerformanceGroupIds.Select(x => (uint)x));
        foreach (var (holeId, wave) in Data.InfinityMaxWave.OrderBy(x => x.Key))
            proto.LOBHIIILPCH.Add(new MJMPBCBGJHH { IKJPIFBFEAG = (uint)holeId, EMMGMLIBHGP = (uint)wave });
        foreach (var (postId, post) in Data.Posts.OrderBy(x => x.Key))
            proto.AAHPCIOOLHB.Add(ToPostProto(postId, post));
        return proto;
    }

    /// <summary>
    ///     The Infinity run (JDKBCJPLNHF): IKJPIFBFEAG hole, OHIDKMKBIGJ the wave to play next, BBEMIGELGJC the saved
    ///     board. Null when no run can be continued.
    /// </summary>
    public JDKBCJPLNHF? ToRunProto()
    {
        var run = Data.InfinityRun;
        if (run.HoleId == 0) return null;
        var proto = new JDKBCJPLNHF { IKJPIFBFEAG = (uint)run.HoleId, OHIDKMKBIGJ = (uint)run.Wave };
        if (run.Archive.Length > 0)
            try
            {
                proto.BBEMIGELGJC = JDCCDGMJMII.Parser.ParseFrom(run.Archive);
            }
            catch (InvalidProtocolBufferException e)
            {
                Logger.Debug($"uid {Player.Uid}: the saved Infinity archive of hole {run.HoleId} does not parse; sent without it.", e);
            }

        return proto;
    }

    public static IHNJGNPKKOA ToPostProto(int postId, ActivityBangbooPostState post) => new()
    {
        CBGODLHCLPK = (uint)postId,
        BNBCGFOBIPH = post.IsRead,
        IAJAOMAKAHA = post.IsLiked
    };

    #endregion

    #region Stages

    public sealed record StartResult(Retcode Retcode, int LevelId, string GameTransNo = "", JDKBCJPLNHF? Run = null);

    /// <summary>
    ///     Start a stage with the Bangboo picked for it. The reply must echo the stage and carry a GameTransNo
    ///     (MAOBJJGOCJB._StartPreparedGameAsync refuses a blank one); an Infinity stage also needs the run whose
    ///     wave is the stage's position in its hole (ODAGFOCMOBJ.KJPFECFJECA), so wave 1 starts a new run and a
    ///     later wave continues the saved one.
    /// </summary>
    public StartResult StartGame(int levelId, IReadOnlyCollection<int> bangbooIds)
    {
        if (Config.GetLevel(levelId) == null || !IsLevelUnlocked(levelId)) return new StartResult(Retcode.RetFail, levelId);
        if (Config.Bangboos.Count > 0 && !bangbooIds.All(IsBangbooUnlocked)) return new StartResult(Retcode.RetFail, levelId);

        JDKBCJPLNHF? run = null;
        var hole = Config.GetHoleOfLevel(levelId);
        if (hole is { IsInfinity: true })
        {
            var wave = hole.LevelIds.IndexOf(levelId) + 1;
            if (wave == 1)
            {
                // Starting the first wave drops whatever run was saved (the client clears it first anyway).
                EndInfinityRun();
                Data.InfinityRun = new ActivityBangbooInfinityRun { HoleId = hole.Id, Wave = 1 };
            }
            else if (Data.InfinityRun.HoleId != hole.Id || Data.InfinityRun.Wave != wave)
            {
                return new StartResult(Retcode.RetFail, levelId);
            }

            run = ToRunProto();
        }

        var transNo = $"{Player.Uid}-{++Data.NextGameSerial}-{levelId}";
        Data.CurrentGame = new ActivityBangbooGame { LevelId = levelId, GameTransNo = transNo };
        MarkDirty();
        return new StartResult(Retcode.RetSucc, levelId, transNo, run);
    }

    public sealed record SettleResult(
        Retcode Retcode,
        int LevelId,
        List<ItemData> Rewards,
        int HistoricalMaxWave = 0,
        List<int>? NewLevelIds = null,
        List<int>? NewBangbooIds = null,
        bool InfinityNewlyUnlocked = false)
    {
        public bool HasUnlocks => NewLevelIds is { Count: > 0 } || NewBangbooIds is { Count: > 0 } || InfinityNewlyUnlocked;
    }

    /// <summary>
    ///     Settle the stage that was started (MBCCKEIJAGF). Only <see cref="ReasonWin" /> clears it: the first clear
    ///     grants the stage reward and may unlock stages, Bangboo or the Infinity holes. In an Infinity hole a win saves
    ///     the board for the next wave, leaving keeps the last saved wave, and any other reason ends the run. The reply
    ///     must echo the stage, or the client skips its settlement screen.
    /// </summary>
    public async ValueTask<SettleResult> Settle(int levelId, string gameTransNo, uint reason, MPAJNPPLKMP? stats,
        JDCCDGMJMII? archive)
    {
        var game = Data.CurrentGame;
        if (game.LevelId == 0 || game.LevelId != levelId || game.GameTransNo != gameTransNo)
            return new SettleResult(Retcode.RetFail, levelId, []);
        Data.CurrentGame = new ActivityBangbooGame();

        var isWin = reason == ReasonWin;
        var hole = Config.GetHoleOfLevel(levelId);
        var maxWave = 0;
        if (hole is { IsInfinity: true })
            maxWave = SettleInfinityWave(hole, levelId, reason, archive);

        var firstClear = isWin && !IsFinished(levelId);
        List<ItemData> rewards = [];
        List<int>? newLevels = null;
        List<int>? newBangboos = null;
        var infinityUnlocked = false;
        if (firstClear)
        {
            var unlockedBefore = UnlockedLevelIds();
            var infinityBefore = IsInfinityUnlocked;
            Data.FinishedLevelIds.Add(levelId);
            newLevels = UnlockedLevelIds().Except(unlockedBefore).ToList();
            newBangboos = Config.Bangboos.Where(x => x.UnlockLevelId == levelId).Select(x => x.Id).ToList();
            infinityUnlocked = !infinityBefore && IsInfinityUnlocked;
        }

        MarkDirty();

        var rewardId = firstClear ? Config.GetLevel(levelId)?.RewardId ?? 0 : 0;
        if (rewardId > 0) rewards = await Player.InventoryManager!.HandleReward(rewardId);

        var missions = Player.MissionManager!;
        if (firstClear)
            await missions.HandleFinishType(MissionFinishTypeEnum.BangbooLevelFinish,
                new ActivityBangbooLevelCleared(levelId), false);
        if (stats != null)
        {
            var settled = ToSettledEvent(levelId, isWin, stats);
            foreach (var type in SettleFinishTypes)
                await missions.HandleFinishType(type, settled, false);
        }

        await Player.QuestManager!.SyncQuest();

        return new SettleResult(Retcode.RetSucc, levelId, rewards, maxWave, newLevels, newBangboos, infinityUnlocked);
    }

    private static readonly MissionFinishTypeEnum[] SettleFinishTypes =
    [
        MissionFinishTypeEnum.BangbooDeployCount, MissionFinishTypeEnum.BangbooMaxDeployCountInLevel,
        MissionFinishTypeEnum.BangbooTacticsKillCount, MissionFinishTypeEnum.BangbooMaxEnergyAtWin,
        MissionFinishTypeEnum.BangbooMaxSimultaneousIdCount
    ];

    /// <summary>MPAJNPPLKMP: CLICEAKBDJK placements, CBIBKFKHLMI Tactics kills, ACGMHLIGFBA energy at the end, FPOCOJHBLPI most distinct Bangboo.</summary>
    private static ActivityBangbooSettled ToSettledEvent(int levelId, bool isWin, MPAJNPPLKMP stats) => new(
        levelId, isWin,
        stats.CLICEAKBDJK.Select(x => new ActivityBangbooPlacement((int)x.FFPMBJPCAGG, (int)x.BDCOFIOCJFO, (int)x.HCDEOKNIGKG))
            .ToList(),
        (int)stats.CBIBKFKHLMI, (int)stats.ACGMHLIGFBA, (int)stats.FPOCOJHBLPI);

    /// <summary>Applies a settled Infinity wave to the run; returns the hole's best wave afterwards.</summary>
    private int SettleInfinityWave(ActivityBangbooHoleConfig hole, int levelId, uint reason, JDCCDGMJMII? archive)
    {
        var wave = hole.LevelIds.IndexOf(levelId) + 1;
        var run = Data.InfinityRun;
        if (run.HoleId == hole.Id && run.Wave == wave)
        {
            if (reason == ReasonWin)
            {
                RecordWave(hole.Id, wave);
                if (wave >= hole.LevelIds.Count)
                    Data.InfinityRun = new ActivityBangbooInfinityRun();
                else
                {
                    run.Wave = wave + 1;
                    run.Archive = archive?.ToByteArray() ?? [];
                }
            }
            else if (reason != ReasonExit)
            {
                EndInfinityRun();
            }
        }

        return Data.InfinityMaxWave.GetValueOrDefault(hole.Id);
    }

    private void RecordWave(int holeId, int clearedWaves)
    {
        if (clearedWaves > Data.InfinityMaxWave.GetValueOrDefault(holeId)) Data.InfinityMaxWave[holeId] = clearedWaves;
    }

    /// <summary>Ends the saved Infinity run, keeping the waves it cleared as the hole's best.</summary>
    private void EndInfinityRun()
    {
        var run = Data.InfinityRun;
        if (run.HoleId == 0) return;
        RecordWave(run.HoleId, run.Wave - 1);
        Data.InfinityRun = new ActivityBangbooInfinityRun();
    }

    /// <summary>
    ///     FFPHBNAGJNP: the client settles the Infinity run without playing on (giving up between waves, or clearing
    ///     the old run before a new one, <c>_SettlePreparationAsync</c> / <c>_EnsureEndlessRunClearedAsync</c>).
    ///     Idempotent: with no run left it still succeeds.
    /// </summary>
    public Retcode SettleInfinityRun()
    {
        EndInfinityRun();
        if (Config.GetHoleOfLevel(Data.CurrentGame.LevelId) is { IsInfinity: true })
            Data.CurrentGame = new ActivityBangbooGame();
        MarkDirty();
        return Retcode.RetSucc;
    }

    #endregion

    #region Posts and anecdotes

    private static bool IsKnownPost(int postId) =>
        postId > 0 && (Config.PostIds.Count == 0 || Config.PostIds.Contains(postId));

    /// <summary>Mark a post read (MGJEKPCMNDD); the reply carries the post so the client can match its id.</summary>
    public async ValueTask<(Retcode Retcode, IHNJGNPKKOA? Post)> ReadPost(int postId)
    {
        if (!IsKnownPost(postId)) return (Retcode.RetFail, null);
        var post = Data.Posts.GetValueOrDefault(postId) ?? new ActivityBangbooPostState();
        Data.Posts[postId] = post;
        var firstRead = !post.IsRead;
        post.IsRead = true;
        MarkDirty();
        if (firstRead)
            await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.BangbooReadPost,
                new ActivityBangbooPostRead(postId));
        return (Retcode.RetSucc, ToPostProto(postId, post));
    }

    /// <summary>Like or unlike a post (HBHPDMLDLBL: CBGODLHCLPK post, IAJAOMAKAHA like).</summary>
    public (Retcode Retcode, IHNJGNPKKOA? Post) SetLike(int postId, bool isLiked)
    {
        if (!IsKnownPost(postId)) return (Retcode.RetFail, null);
        var post = Data.Posts.GetValueOrDefault(postId) ?? new ActivityBangbooPostState();
        Data.Posts[postId] = post;
        post.IsLiked = isLiked;
        MarkDirty();
        return (Retcode.RetSucc, ToPostProto(postId, post));
    }

    /// <summary>An anecdote group watched to the end (BNMEGDLCIKO, OOOOHFOLEIA._FinishPerformanceAsync).</summary>
    public async ValueTask<Retcode> FinishPerformance(int groupId)
    {
        if (groupId <= 0 || (Config.PerformanceGroupIds.Count > 0 && !Config.PerformanceGroupIds.Contains(groupId)))
            return Retcode.RetFail;
        if (Data.ReadPerformanceGroupIds.Contains(groupId)) return Retcode.RetSucc;
        Data.ReadPerformanceGroupIds.Add(groupId);
        MarkDirty();
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.BangbooFinishNpcGroupPerformance,
            new ActivityBangbooPerformanceFinished(groupId));
        return Retcode.RetSucc;
    }

    #endregion
}
