using Google.Protobuf;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.ActivityBangboo;
using March7thHoney.Database.Generated;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Activity.ActivityBangboo;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet;
using March7thHoney.GameServer.Server.Packet.Recv.ActivityBangboo;
using March7thHoney.Kcp;
using March7thHoney.Kcp.KcpSharp;
using March7thHoney.Proto;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

// Planarcadia Reverie (ActivityBangboo, 4.8 command group MFNJLCNMBON 10602-10640): the whole server-side flow through
// the real handlers - data, story unlocks, rewards and the change notify, the Infinity run with its saved board,
// posts, anecdotes, and the activity quests the settle statistics advance.
internal static class ActivityBangbooRegression
{
    private const uint Fail = (uint)Retcode.RetFail;

    public static async Task Run(string repository, string scratch, KcpConversation peer, Action<string, bool> check,
        Func<Func<Task>, Task<List<(ushort Id, byte[] Body)>>> capture)
    {
        Directory.CreateDirectory(Path.Combine(scratch, "Custom"));
        File.Copy(Path.Combine(repository, "Config/Custom/ActivityBangboo.json"),
            Path.Combine(scratch, "Custom/ActivityBangboo.json"), overwrite: true);
        var savedConfig = GameData.ActivityBangbooConfig;
        var config = ResourceManager.LoadCustomFile<ActivityBangbooConfig>("ActivityBangboo", "ActivityBangboo");
        check("ActivityBangboo config has the three story holes, three 12-wave Infinity holes, 49 stages and 18 Bangboo",
            config != null && config.Holes.Count(h => !h.IsInfinity) == 3 &&
            config.Holes.Count(h => h is { IsInfinity: true, LevelIds.Count: 12 }) == 3 && config.Levels.Count == 49 &&
            config.Bangboos.Count == 18 && config.Holes.SelectMany(h => h.LevelIds).All(id => config.GetLevel(id) != null) &&
            config.StoryChain().SequenceEqual(new[] { 101, 102, 103, 201, 202, 203, 301, 302, 303, 304 }) &&
            config.GetBangboo(4) is { UnlockLevelId: 101, TypeValue: 1 } && config.GetBangboo(8)?.TypeValue == 2);
        if (config == null) return;
        GameData.ActivityBangbooConfig = config;

        var schedule = JsonConvert.DeserializeObject<ActivityConfig>(
            File.ReadAllText(Path.Combine(repository, "Config/Custom/ActivityConfig.json")));
        check("Planarcadia Reverie modules 5014701/5014702 are scheduled on panel 50147 and every request has a handler",
            config.ActivityModuleIds.All(id =>
                schedule?.ScheduleData.Any(row => row.ActivityId == id && row.PanelId == 50147) == true) &&
            new[]
            {
                CmdIds.FDFNDGOOCGJ, CmdIds.MGJEKPCMNDD, CmdIds.HBHPDMLDLBL, CmdIds.HKIFIOMLLCE, CmdIds.BNMEGDLCIKO,
                CmdIds.MBCCKEIJAGF, CmdIds.FFPHBNAGJNP
            }.All(id => HandlerManager.GetHandler(id) != null));
        MissionFinishTypeEnum[] finishTypes =
        [
            MissionFinishTypeEnum.BangbooLevelFinish, MissionFinishTypeEnum.BangbooReadPost,
            MissionFinishTypeEnum.BangbooDeployCount, MissionFinishTypeEnum.BangbooMaxDeployCountInLevel,
            MissionFinishTypeEnum.BangbooFinishNpcGroupPerformance, MissionFinishTypeEnum.BangbooTacticsKillCount,
            MissionFinishTypeEnum.BangbooMaxEnergyAtWin, MissionFinishTypeEnum.BangbooMaxSimultaneousIdCount
        ];
        check("the eight Bangboo finish types use the client's values 480500-480507 and have handlers",
            finishTypes.Select(t => (int)t).SequenceEqual(Enumerable.Range(480500, 8)) &&
            finishTypes.All(MissionManager.FinishTypeHandlers.ContainsKey));

        var player = new PlayerInstance(60050);
        await player.InitializeAsync();
        var connection = new ProbeConnection(peer) { Player = player };
        player.Connection = connection;

        // The checked-in Resource.bin predates these enum members, so its FinishWay rows carry Unknown for them. Type
        // the quest rows the flow advances in memory, as a cache rebuilt with this code has them, and restore after.
        (int QuestId, MissionFinishTypeEnum Type)[] quests =
        [
            (6094001, MissionFinishTypeEnum.BangbooLevelFinish), (6094004, MissionFinishTypeEnum.BangbooLevelFinish),
            (6094005, MissionFinishTypeEnum.BangbooDeployCount), (6094008, MissionFinishTypeEnum.BangbooDeployCount),
            (6094009, MissionFinishTypeEnum.BangbooDeployCount), (6094010, MissionFinishTypeEnum.BangbooReadPost),
            (6094013, MissionFinishTypeEnum.BangbooFinishNpcGroupPerformance),
            (4055801, MissionFinishTypeEnum.BangbooMaxDeployCountInLevel),
            (4055802, MissionFinishTypeEnum.BangbooTacticsKillCount),
            (4055803, MissionFinishTypeEnum.BangbooMaxEnergyAtWin),
            (4055804, MissionFinishTypeEnum.BangbooMaxSimultaneousIdCount)
        ];
        var restoreTypes = new List<(FinishWayExcel Row, MissionFinishTypeEnum Type)>();
        var questsReady = true;
        foreach (var (questId, type) in quests)
        {
            var row = GameData.QuestDataData.GetValueOrDefault(questId) is { } quest
                ? GameData.FinishWayData.GetValueOrDefault(quest.FinishWayID)
                : null;
            if (row == null)
            {
                questsReady = false;
                continue;
            }

            restoreTypes.Add((row, row.FinishType));
            row.FinishType = type;
            if (player.QuestManager!.GetQuestInfo(questId) == null) await player.QuestManager.AcceptQuest(questId, false);
            questsReady &= player.QuestManager.GetQuestInfo(questId)?.QuestStatus == QuestStatus.QuestDoing;
        }

        check("the cache has the Planarcadia Reverie quests and achievements, accepted for the flow", questsReady);
        int Progress(int questId) => player.QuestManager!.GetQuestProgress(questId);
        bool Finished(int questId) => player.QuestManager!.GetQuestInfo(questId)?.QuestStatus is QuestStatus.QuestFinish
            or QuestStatus.QuestClose;

        try
        {
            Task<List<(ushort Id, byte[] Body)>> Request(Handler handler, IMessage req) =>
                capture(() => handler.OnHandle(connection, [], req.ToByteArray()));
            T Last<T>(List<(ushort Id, byte[] Body)> frames, int cmd, MessageParser<T> parser) where T : IMessage<T> =>
                parser.ParseFrom(frames.Last(frame => frame.Id == cmd).Body);
            async Task<ABCMHMIHJBO> GetData() =>
                Last(await Request(new HandlerActivityBangbooGetDataCsReq(), new FDFNDGOOCGJ()), CmdIds.BGCOEFMFEHA,
                    BGCOEFMFEHA.Parser).FJCAKOIDONG;
            async Task<BOBEICIPCNK> Start(uint level, params uint[] bangboo)
            {
                var req = new HKIFIOMLLCE { IJAPHCOHMIN = level };
                req.OEDLOIPDGKG.AddRange(bangboo.Length > 0 ? bangboo : [1, 2, 3]);
                return Last(await Request(new HandlerActivityBangbooStartGameCsReq(), req), CmdIds.BOBEICIPCNK,
                    BOBEICIPCNK.Parser);
            }

            async Task<(EBHKOMEILOJ Reply, LIEHHGLAEHG? Notify)> Settle(uint level, string transNo, uint reason,
                MPAJNPPLKMP? stats = null, JDCCDGMJMII? archive = null)
            {
                var frames = await Request(new HandlerActivityBangbooSettleCsReq(), new MBCCKEIJAGF
                {
                    IJAPHCOHMIN = level, OJHNAJCAKNB = transNo, PKKMAAIJEOD = (FDHNDGJCOAC)reason, FFCBOJAACMD = stats,
                    BBEMIGELGJC = archive
                });
                var reply = Last(frames, CmdIds.EBHKOMEILOJ, EBHKOMEILOJ.Parser);
                var notify = frames.Any(f => f.Id == CmdIds.LIEHHGLAEHG)
                    ? Last(frames, CmdIds.LIEHHGLAEHG, LIEHHGLAEHG.Parser)
                    : null;
                if (notify != null)
                    check($"settling stage {level} sends the reply before the change notify",
                        frames.FindIndex(f => f.Id == CmdIds.EBHKOMEILOJ) <
                        frames.FindIndex(f => f.Id == CmdIds.LIEHHGLAEHG));
                return (reply, notify);
            }

            async Task<EBHKOMEILOJ> Play(uint level, uint reason = 1, MPAJNPPLKMP? stats = null,
                JDCCDGMJMII? archive = null) =>
                (await Settle(level, (await Start(level)).OJHNAJCAKNB, reason, stats, archive)).Reply;

            // One stage's statistics: three 1-star and two 2-star Combat Bangboo 1, one Tactics Bangboo 8, two kills
            // by Tactics Bangboo, 150 energy left, at most three distinct Bangboo on the field.
            MPAJNPPLKMP Stats(uint simultaneous = 3) => new()
            {
                CLICEAKBDJK =
                {
                    new MMPEJCJEEHO { FFPMBJPCAGG = 1, BDCOFIOCJFO = 1, HCDEOKNIGKG = 3 },
                    new MMPEJCJEEHO { FFPMBJPCAGG = 1, BDCOFIOCJFO = 2, HCDEOKNIGKG = 2 },
                    new MMPEJCJEEHO { FFPMBJPCAGG = 8, BDCOFIOCJFO = 1, HCDEOKNIGKG = 1 }
                },
                CBIBKFKHLMI = 2, ACGMHLIGFBA = 150, FPOCOJHBLPI = simultaneous, CBBALOIKCPC = 1, LGJGNFBJKIH = 6
            };

            var fresh = await GetData();
            check("a fresh account sees the first story stage and the stages outside the holes open, nothing cleared",
                fresh.EEJKJMCMJBA.Order().SequenceEqual(new uint[] { 100, 101, 305, 306 }) &&
                fresh.ICHOFAHCIIF.Count == 0 && !fresh.GILOPNKGNAA && fresh.IFDOJGKJEBN == null &&
                fresh.LOBHIIILPCH.Count == 0 && fresh.AAHPCIOOLHB.Count == 0 && fresh.IEKHMACAFMK.Count == 0);

            check("a locked stage, an unknown stage and a Bangboo not unlocked yet are refused",
                (await Start(102)).JKCOAIGJMMM == Fail && (await Start(999)).JKCOAIGJMMM == Fail &&
                (await Start(101, 1, 4)).JKCOAIGJMMM == Fail);

            var started = await Start(101);
            check("starting stage 101 echoes it with a non-blank GameTransNo and no Infinity run",
                started is { JKCOAIGJMMM: 0, IJAPHCOHMIN: 101, IFDOJGKJEBN: null } &&
                !string.IsNullOrWhiteSpace(started.OJHNAJCAKNB));
            check("a settle with another GameTransNo or stage is refused",
                (await Settle(101, started.OJHNAJCAKNB + "x", 1)).Reply.JKCOAIGJMMM == Fail &&
                (await Settle(102, started.OJHNAJCAKNB, 1)).Reply.JKCOAIGJMMM == Fail);
            var left = await Settle(101, started.OJHNAJCAKNB, 2, Stats());
            check("leaving stage 101 settles without a clear, reward or notify",
                left.Reply is { JKCOAIGJMMM: 0, IJAPHCOHMIN: 101 } && left.Reply.ENGHMIAMPLG.ItemList_.Count == 0 &&
                left.Notify == null && !(await GetData()).ICHOFAHCIIF.Contains(101u));

            var startedAgain = await Start(101);
            var won = await Settle(101, startedAgain.OJHNAJCAKNB, 1, Stats());
            var rewardItems = GameData.RewardDataData.GetValueOrDefault(333101)?.GetItems()
                .Where(item => item.Item1 > 0 && item.Item2 > 0).Select(item => (uint)item.Item1).ToHashSet() ?? [];
            check("the first clear of stage 101 grants its RewardData 333101",
                won.Reply is { JKCOAIGJMMM: 0, IJAPHCOHMIN: 101, HPKEGPGPPOK: 0 } && rewardItems.Count > 0 &&
                won.Reply.ENGHMIAMPLG.ItemList_.Count > 0 &&
                won.Reply.ENGHMIAMPLG.ItemList_.All(item => rewardItems.Contains(item.ItemId)));
            check("the first clear notifies stage 102 and Bangboo 4 as newly unlocked",
                won.Notify != null && won.Notify.DGNGMJMPFGK.Count == 2 &&
                won.Notify.DGNGMJMPFGK.Single(c => c.MMCGBGDJIPNCase == CAGDONKMOGB.MMCGBGDJIPNOneofCase.CEMIOADEMHA)
                    .CEMIOADEMHA.LNMPJKPEHAE.SequenceEqual(new uint[] { 102 }) &&
                won.Notify.DGNGMJMPFGK.Single(c => c.MMCGBGDJIPNCase == CAGDONKMOGB.MMCGBGDJIPNOneofCase.FADGOOJBAKE)
                    .FADGOOJBAKE.LNMPJKPEHAE.SequenceEqual(new uint[] { 4 }));
            check("a settle cannot be repeated with the same GameTransNo",
                (await Settle(101, startedAgain.OJHNAJCAKNB, 1)).Reply.JKCOAIGJMMM == Fail);
            var replay = await Play(101);
            check("replaying a cleared stage settles without a reward or notify",
                replay is { JKCOAIGJMMM: 0 } && replay.ENGHMIAMPLG.ItemList_.Count == 0);
            var afterFirst = await GetData();
            check("after stage 101 the data lists it cleared and opens 102",
                afterFirst.ICHOFAHCIIF.SequenceEqual(new uint[] { 101 }) && afterFirst.EEJKJMCMJBA.Contains(102u) &&
                !afterFirst.EEJKJMCMJBA.Contains(103u) && (await Start(102, 1, 4)).JKCOAIGJMMM == 0);

            // Statistics came with the left and the won settle of stage 101 only.
            check("settle statistics advance the deploy, kill and energy quests",
                Progress(6094005) == 4 && Progress(6094008) == 2 && Progress(6094009) == 0 &&
                Progress(4055801) == 2 && Progress(4055802) == 4 && Progress(4055803) == 150 &&
                Progress(4055804) == 0);

            await Play(102);
            await Play(103);
            check("three story clears finish the 'complete 3 stages' quest", Finished(6094001));
            foreach (var level in new uint[] { 201, 202, 203, 301, 302, 303 }) await Play(level);
            check("the Infinity holes stay closed before the last story stage",
                !(await GetData()).GILOPNKGNAA && (await Start(401)).JKCOAIGJMMM == Fail);
            var lastStory = await Settle(304, (await Start(304)).OJHNAJCAKNB, 1);
            check("clearing stage 304 opens the Infinity holes and notifies it",
                lastStory.Notify?.DGNGMJMPFGK.Any(c => c is { MMCGBGDJIPNCase: CAGDONKMOGB.MMCGBGDJIPNOneofCase.GILOPNKGNAA, GILOPNKGNAA: true }) == true);
            var storyDone = await GetData();
            check("after the story every stage is open, the Infinity holes included",
                storyDone.GILOPNKGNAA && config.Levels.All(l => storyDone.EEJKJMCMJBA.Contains((uint)l.Id)) &&
                storyDone.ICHOFAHCIIF.Count == 10);

            // Infinity: wave n is stage n of the hole; a win saves the board and moves the run to the next wave.
            check("an Infinity wave after the first needs a saved run", (await Start(402)).JKCOAIGJMMM == Fail);
            var wave1 = await Start(401);
            check("starting wave 1 opens a run on hole 4 at wave 1 without a board",
                wave1 is { JKCOAIGJMMM: 0, IFDOJGKJEBN: { IKJPIFBFEAG: 4, OHIDKMKBIGJ: 1, BBEMIGELGJC: null } });
            var board = new JDCCDGMJMII { GJHFNCIEFGJ = 402, JCAEANLAGMB = 35, OEDLOIPDGKG = { 1, 2, 3 } };
            board.GAIENNLBCMM = new DJBKKNNBHKF
                { IJKIBLJEGGE = { new NGBNJKJIDLJ { ICEPLFPKAHC = "cell", LOJCINOAHIP = 4, EFJHBDICDCH = 900 } } };
            var saved = await Settle(401, wave1.OJHNAJCAKNB, 1, Stats(simultaneous: 5), board);
            var afterSave = await GetData();
            check("clearing wave 1 records one wave and keeps the board for wave 2",
                saved.Reply is { JKCOAIGJMMM: 0, IJAPHCOHMIN: 401, HPKEGPGPPOK: 1 } &&
                afterSave.IFDOJGKJEBN is { IKJPIFBFEAG: 4, OHIDKMKBIGJ: 2 } &&
                afterSave.IFDOJGKJEBN.BBEMIGELGJC.Equals(board) &&
                afterSave.LOBHIIILPCH.Single() is { IKJPIFBFEAG: 4, EMMGMLIBHGP: 1 });
            check("the most distinct Bangboo in an Infinity stage counts for the achievement", Progress(4055804) == 5);

            var wave2 = await Start(402);
            check("wave 2 starts with the saved board", wave2.IFDOJGKJEBN is { IKJPIFBFEAG: 4, OHIDKMKBIGJ: 2 } &&
                                                       wave2.IFDOJGKJEBN.BBEMIGELGJC.Equals(board) &&
                                                       (await Start(403)).JKCOAIGJMMM == Fail);
            var quit = await Settle(402, wave2.OJHNAJCAKNB, 2);
            check("leaving wave 2 keeps the run on wave 2",
                quit.Reply is { JKCOAIGJMMM: 0, HPKEGPGPPOK: 1 } &&
                (await GetData()).IFDOJGKJEBN is { IKJPIFBFEAG: 4, OHIDKMKBIGJ: 2 });
            var lost = await Settle(402, (await Start(402)).OJHNAJCAKNB, 3);
            var afterLoss = await GetData();
            check("losing wave 2 ends the run and keeps the best of one wave",
                lost.Reply is { JKCOAIGJMMM: 0, HPKEGPGPPOK: 1 } && afterLoss.IFDOJGKJEBN == null &&
                afterLoss.LOBHIIILPCH.Single() is { IKJPIFBFEAG: 4, EMMGMLIBHGP: 1 });

            await Play(401, archive: board);
            await Play(402, archive: board);
            var wave3 = await Play(403, archive: board);
            check("three waves in one run raise the best to three and finish the wave-3 quest",
                wave3 is { HPKEGPGPPOK: 3 } && Finished(6094004));
            var cleared = Last(await Request(new HandlerActivityBangbooSettleInfinityRunCsReq(),
                new FFPHBNAGJNP { IDCGICECPHO = new IHFFJHNEFMF { CPFCKNMOKDL = 1 } }), CmdIds.EEJOPBDAHNJ,
                EEJOPBDAHNJ.Parser);
            var afterClear = await GetData();
            check("settling the Infinity run clears it and keeps the best of three waves",
                cleared.JKCOAIGJMMM == 0 && afterClear.IFDOJGKJEBN == null &&
                afterClear.LOBHIIILPCH.Single() is { IKJPIFBFEAG: 4, EMMGMLIBHGP: 3 } &&
                Last(await Request(new HandlerActivityBangbooSettleInfinityRunCsReq(), new FFPHBNAGJNP()),
                    CmdIds.EEJOPBDAHNJ, EEJOPBDAHNJ.Parser).JKCOAIGJMMM == 0);

            // Posts and anecdotes.
            var read = Last(await Request(new HandlerActivityBangbooReadPostCsReq(), new MGJEKPCMNDD { CBGODLHCLPK = 101 }),
                CmdIds.MHJNBHCDPHK, MHJNBHCDPHK.Parser);
            var liked = Last(await Request(new HandlerActivityBangbooSetLikeCsReq(),
                new HBHPDMLDLBL { CBGODLHCLPK = 101, IAJAOMAKAHA = true }), CmdIds.NIIFNPFEKIG, NIIFNPFEKIG.Parser);
            var unknownPost = Last(await Request(new HandlerActivityBangbooReadPostCsReq(),
                new MGJEKPCMNDD { CBGODLHCLPK = 999 }), CmdIds.MHJNBHCDPHK, MHJNBHCDPHK.Parser);
            await Request(new HandlerActivityBangbooReadPostCsReq(), new MGJEKPCMNDD { CBGODLHCLPK = 101 });
            check("reading and liking a post echoes it and keeps it in the data; an unknown post is refused",
                read is { JKCOAIGJMMM: 0, AMFEFIDDMDA: { CBGODLHCLPK: 101, BNBCGFOBIPH: true, IAJAOMAKAHA: false } } &&
                liked is { JKCOAIGJMMM: 0, AMFEFIDDMDA: { CBGODLHCLPK: 101, BNBCGFOBIPH: true, IAJAOMAKAHA: true } } &&
                unknownPost.JKCOAIGJMMM == Fail &&
                (await GetData()).AAHPCIOOLHB.Single() is { CBGODLHCLPK: 101, BNBCGFOBIPH: true, IAJAOMAKAHA: true } &&
                Progress(6094010) == 1);

            var watched = Last(await Request(new HandlerActivityBangbooFinishPerformanceCsReq(),
                new BNMEGDLCIKO { CIGGAEJNEFM = 180 }), CmdIds.PPHGPJDEHDA, PPHGPJDEHDA.Parser);
            await Request(new HandlerActivityBangbooFinishPerformanceCsReq(), new BNMEGDLCIKO { CIGGAEJNEFM = 180 });
            var unknownGroup = Last(await Request(new HandlerActivityBangbooFinishPerformanceCsReq(),
                new BNMEGDLCIKO { CIGGAEJNEFM = 999 }), CmdIds.PPHGPJDEHDA, PPHGPJDEHDA.Parser);
            check("an anecdote group watched is echoed, listed once and counted once; an unknown group is refused",
                watched is { JKCOAIGJMMM: 0, CIGGAEJNEFM: 180 } && unknownGroup.JKCOAIGJMMM == Fail &&
                (await GetData()).IEKHMACAFMK.SequenceEqual(new uint[] { 180 }) && Progress(6094013) == 1);

            // The progress is written to the PlayerActivityBangboo table and reads back from SQLite.
            DatabaseHelper.Instance!.SaveDatabase();
            await using var db = new SqliteConnection($"Data Source={Path.Combine(scratch, "regression.db")}");
            await db.OpenAsync();
            var stored = EntityStores.Map[typeof(PlayerActivityBangbooData)].LoadAll(db).OfType<PlayerActivityBangbooData>()
                .Single(data => data.Uid == player.Uid);
            check("Planarcadia Reverie progress is stored in the PlayerActivityBangboo table",
                stored.FinishedLevelIds.Count == 13 && stored.InfinityMaxWave.GetValueOrDefault(4) == 3 &&
                stored.InfinityRun.HoleId == 0 && stored.Posts.GetValueOrDefault(101) is { IsRead: true, IsLiked: true } &&
                stored.ReadPerformanceGroupIds.SequenceEqual(new[] { 180 }) && stored.NextGameSerial > 0);
        }
        finally
        {
            foreach (var (row, type) in restoreTypes) row.FinishType = type;
            GameData.ActivityBangbooConfig = savedConfig;
            player.Connection = null;
        }
    }
}
