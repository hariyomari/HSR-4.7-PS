using Google.Protobuf;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Generated;
using March7thHoney.Database.RealityShow;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Activity.RealityShow;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet;
using March7thHoney.GameServer.Server.Packet.Recv.RealityShow;
using March7thHoney.Kcp;
using March7thHoney.Kcp.KcpSharp;
using March7thHoney.Proto;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

// Interastral Peace Gala: Road to Fame (RealityShow, 4.8 command group 10785-10799): the data query, both stages of a
// challenge through the real handlers and BattleManager.EndBattle, the reset, the friend ranking, the activity quests
// and the saved records. The start request replays the one the 4.7.51 client sent and never got an answer to
// (challenge 2, warm-up, own avatars 1407/1413/1415/1409 as "upgrade available").
internal static class RealityShowRegression
{
    public static async Task Run(string repository, string scratch, KcpConversation peer, Action<string, bool> check,
        Func<Func<Task>, Task<List<(ushort Id, byte[] Body)>>> capture)
    {
        Directory.CreateDirectory(Path.Combine(scratch, "Custom"));
        File.Copy(Path.Combine(repository, "Config/Custom/RealityShow.json"),
            Path.Combine(scratch, "Custom/RealityShow.json"), overwrite: true);
        var savedConfig = GameData.RealityShowConfig;
        var config = ResourceManager.LoadCustomFile<RealityShowConfig>("RealityShow", "RealityShow");
        check("RealityShow config has five challenges with ascending buff thresholds, one buff per tier and the grade ladder",
            config is { Challenges.Count: 5, HpBarBreakGrades: [0, 100, 300, 600, 1200] } &&
            config.Challenges.Select(x => x.Id).Order().SequenceEqual([1, 2, 3, 4, 5]) &&
            config.Challenges.All(x => x.BuffIdList.Count == 3 && x.BuffUpNumList.Count == 3 &&
                                       x.BuffUpNumList.SequenceEqual(x.BuffUpNumList.Order()) &&
                                       x.SpecialAvatarList.Count > 0 && x.BuffUpType == "ShieldBreak") &&
            config.GetChallenge(2) is { ModuleId: 5014302, StageId1: 4320036, StageId2: 4320136 } challenge2 &&
            challenge2.GetBuffLevel(4) == 0 && challenge2.GetBuffLevel(5) == 1 && challenge2.GetBuffLevel(23) == 2 &&
            challenge2.GetBuffLevel(99) == 3 && config.GetResultLevel(99) == 0 && config.GetResultLevel(100) == 1 &&
            config.GetResultLevel(650) == 3 && config.GetResultLevel(1200) == 4);
        if (config == null) return;
        GameData.RealityShowConfig = config;

        // The client tells the stages apart by these exact ids; the final stage is the lone boss of the challenge.
        check("every challenge's stages are in the cache, the final stage fights the challenge's boss, the buffs exist",
            config.Challenges.All(x =>
                GameData.StageConfigData.GetValueOrDefault(x.StageId1) is { MonsterList.Count: > 0 } &&
                GameData.StageConfigData.GetValueOrDefault(x.StageId2) is { MonsterList: [{ Monster0: var boss }] } &&
                boss == x.MonsterId) &&
            config.Challenges.SelectMany(x => x.BuffIdList).All(id => GameData.MazeBuffData.ContainsKey(id * 10 + 1)));

        var schedule = JsonConvert.DeserializeObject<ActivityConfig>(
            File.ReadAllText(Path.Combine(repository, "Config/Custom/ActivityConfig.json")));
        check("the five challenge modules are scheduled on panel 50143 and every request and quest type has a handler",
            config.Challenges.All(x =>
                schedule?.ScheduleData.Any(row => row.ActivityId == x.ModuleId && row.PanelId == 50143) == true) &&
            new[] { CmdIds.CKMHMAJPJEP, CmdIds.OHFEIJJDJIE, CmdIds.IOOHJLNKKKD, CmdIds.NKEMNFGCBLO }
                .All(id => HandlerManager.GetHandler(id) != null) &&
            (int)MissionFinishTypeEnum.RealityShowBuffNum == 470011 && (int)MissionFinishTypeEnum.RealityShowResult == 470012 &&
            MissionManager.FinishTypeHandlers.ContainsKey(MissionFinishTypeEnum.RealityShowBuffNum) &&
            MissionManager.FinishTypeHandlers.ContainsKey(MissionFinishTypeEnum.RealityShowResult));

        var player = new PlayerInstance(60060);
        await player.InitializeAsync();
        foreach (var avatarId in new[] { 1407, 1413, 1415, 1409, 1001 })
            await player.AvatarManager!.AddAvatar(avatarId, false, false);
        var connection = new ProbeConnection(peer) { Player = player };
        player.Connection = connection;
        var manager = player.GetComponent<RealityShowManager>();
        var savedSchedule = GameData.ActivityConfig;
        if (schedule != null) GameData.ActivityConfig = schedule;

        // The checked-in Resource.bin predates these enum members, so its FinishWay rows carry Unknown for them. Type
        // the quest rows the flow advances in memory, as a cache rebuilt with this code has them, and restore after.
        (int QuestId, MissionFinishTypeEnum Type)[] quests =
        [
            (6091005, MissionFinishTypeEnum.RealityShowBuffNum), (6091006, MissionFinishTypeEnum.RealityShowResult),
            (6091007, MissionFinishTypeEnum.RealityShowResult), (6091008, MissionFinishTypeEnum.RealityShowResult),
            (6091025, MissionFinishTypeEnum.RealityShowResult), (6091001, MissionFinishTypeEnum.RealityShowBuffNum)
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

        check("the cache has the Road to Fame quests, accepted for the flow", questsReady);
        bool Finished(int questId) => player.QuestManager!.GetQuestInfo(questId)?.QuestStatus is QuestStatus.QuestFinish
            or QuestStatus.QuestClose;

        try
        {
            Task<List<(ushort Id, byte[] Body)>> Request(Handler handler, IMessage req) =>
                capture(() => handler.OnHandle(connection, [], req.ToByteArray()));
            T Last<T>(List<(ushort Id, byte[] Body)> frames, int cmd, MessageParser<T> parser) where T : IMessage<T> =>
                parser.ParseFrom(frames.Last(frame => frame.Id == cmd).Body);
            async Task<List<LFIKNHJBDBJ>> GetData() =>
                Last(await Request(new HandlerRealityShowGetDataCsReq(), new CKMHMAJPJEP()), CmdIds.PAMDEKPLLEL,
                    PAMDEKPLLEL.Parser).OGDEMOAKJOK.ToList();
            OHFEIJJDJIE StartReq(uint challenge, bool firstStage, AvatarType type, params uint[] ids)
            {
                var req = new OHFEIJJDJIE { ChallengeId = challenge, LAJAGHBKIJH = firstStage };
                req.AvatarList.AddRange(ids.Select(id => new KPNHGDGLICN { AvatarType = type, Id = id }));
                return req;
            }
            async Task<IFMNCFKBEGL> Start(OHFEIJJDJIE req) =>
                Last(await Request(new HandlerRealityShowStartChallengeCsReq(), req), CmdIds.IFMNCFKBEGL,
                    IFMNCFKBEGL.Parser);
            async Task<(List<(ushort Id, byte[] Body)> Frames, NOEGOMIJDAH? Notify)> End(BattleEndStatus status,
                uint shields = 0, uint hpBars = 0, double actionValue = 0)
            {
                var frames = await capture(async () => await player.BattleManager!.EndBattle(new PVEBattleResultCsReq
                {
                    EndStatus = status,
                    Stt = new BattleStatistics
                    {
                        TotalDelayCumulate = actionValue,
                        MHPDKEDHELL = new GBJMLKNLEPD
                        {
                            MFOHGCNOMLB = new IHDKFBDLNGL { MNIBAEDPCHA = shields, KMBPEBAFDLF = hpBars, LGIFBIOJKFH = 7 }
                        }
                    }
                }));
                var notify = frames.Any(f => f.Id == CmdIds.NOEGOMIJDAH)
                    ? Last(frames, CmdIds.NOEGOMIJDAH, NOEGOMIJDAH.Parser)
                    : null;
                return (frames, notify);
            }
            bool NotifyBeforeResult(List<(ushort Id, byte[] Body)> frames) =>
                frames.FindIndex(f => f.Id == CmdIds.NOEGOMIJDAH) is var n and >= 0 &&
                n < frames.FindIndex(f => f.Id == CmdIds.PVEBattleResultScRsp);

            var fresh = await GetData();
            check("a fresh account gets every challenge, in id order, with no record",
                fresh.Select(x => x.ChallengeId).SequenceEqual(new uint[] { 1, 2, 3, 4, 5 }) &&
                fresh.All(x => !x.LMINNGCDDOK && x.CNEJKLABHDG == 0 && x.HAGDKKBAAKK == 0 && x.KHBJOAHCCCE.Count == 0));

            const AvatarType Own = AvatarType.AvatarUpgradeAvailableType;
            var refused = new[]
            {
                (await Start(StartReq(2, false, Own, 1407))).Retcode == (uint)Retcode.RetChallengeNotFinish,
                (await Start(StartReq(2, true, Own))).Retcode == (uint)Retcode.RetChallengeLineupEmpty,
                (await Start(StartReq(9, true, Own, 1407))).Retcode == (uint)Retcode.RetChallengeNotExist,
                (await Start(StartReq(2, true, Own, 1407, 1413, 1415, 1409, 1001))).Retcode == (uint)Retcode.RetReqParaInvalid,
                (await Start(StartReq(2, true, AvatarType.AvatarTrialType, 3161506))).Retcode ==
                (uint)Retcode.RetChallengeLineupAvatarTypeInvalid,
                (await Start(StartReq(2, true, AvatarType.AvatarAssistType, 1407))).Retcode ==
                (uint)Retcode.RetChallengeLineupAvatarTypeInvalid,
                (await Start(StartReq(2, true, Own, 1407, 1407))).Retcode == (uint)Retcode.RetLineupAvatarAlreadyIn,
                (await Start(StartReq(2, true, Own, 1213))).Retcode == (uint)Retcode.RetAvatarNotExist
            };
            check("final stage before the warm-up, an empty or oversized team, an unknown challenge, a trial avatar from " +
                  "another challenge, a support avatar, a duplicate and an avatar not owned are refused without a battle",
                refused.All(x => x) && player.BattleInstance == null);

            GameData.ActivityConfig = new ActivityConfig
            {
                ScheduleData = { new March7thHoney.Data.Custom.ActivityScheduleData { ActivityId = 1, EndTime = long.MaxValue } }
            };
            var closed = await Start(StartReq(2, true, Own, 1407));
            if (schedule != null) GameData.ActivityConfig = schedule;
            check("a challenge whose module is outside its schedule is refused",
                closed.Retcode == (uint)Retcode.RetChallengeNotUnlock && player.BattleInstance == null);

            var lineupBefore = player.LineupManager!.GetCurLineup()!.BaseAvatars!.Select(x => x.BaseAvatarId).ToList();
            var warmUpReq = StartReq(2, true, Own, 1407, 1413, 1415, 1409);
            var warmUp = await Start(warmUpReq);
            check("the captured warm-up request starts the challenge's warm-up stage with the four own avatars",
                warmUp is { Retcode: 0, ChallengeId: 2, BattleInfo.StageId: 4320036 } &&
                warmUp.BattleInfo.BattleAvatarList.Select(x => x.Id).SequenceEqual(new uint[] { 1407, 1413, 1415, 1409 }) &&
                warmUp.BattleInfo.BattleAvatarList.All(x => x.AvatarType == Own) &&
                warmUp.BattleInfo.BuffList.All(b => b.Id is < 3250001 or > 3250015) &&
                warmUp.NEMJKLMIJOL is { ChallengeId: 2, LMINNGCDDOK: false } &&
                warmUp.NEMJKLMIJOL.KHBJOAHCCCE.SequenceEqual(warmUpReq.AvatarList) &&
                warmUp.KHBJOAHCCCE.SequenceEqual(warmUpReq.AvatarList) &&
                player.BattleInstance?.RealityShowContext is { IsFirstStage: true, Challenge.Id: 2 });
            check("a second start while the battle runs is refused",
                (await Start(warmUpReq)).Retcode == (uint)Retcode.RetInBattleNow);

            var (warmUpFrames, warmUpNotify) = await End(BattleEndStatus.BattleEndWin, shields: 16);
            check("the warm-up settles 16 shields into buff level 2 before the battle result",
                warmUpNotify is { ChallengeId: 2, BCLOOPGAIKJ: { HAGDKKBAAKK: 2, MNIBAEDPCHA: 16 }, OEFFDKDCIMC: null } &&
                NotifyBeforeResult(warmUpFrames) &&
                manager.Data.Challenges[2] is { IsFirstStageFinished: true, ShieldBreakCount: 16, BuffLevel: 2 } &&
                player.BattleInstance == null);
            check("the activity team leaves the player's own lineup alone",
                player.LineupManager.GetCurLineup()!.BaseAvatars!.Select(x => x.BaseAvatarId).SequenceEqual(lineupBefore));
            check("buff level 2 completes challenge 2's buff quest and not challenge 1's",
                Finished(6091005) && !Finished(6091001));

            var finalReq = StartReq(2, false, Own, 1407, 1413);
            var final = await Start(finalReq);
            var finalBuffs = final.BattleInfo?.BuffList.Select(b => b.Id).ToHashSet() ?? [];
            check("the final stage starts StageID2 carrying the two buff tiers the warm-up earned",
                final is { Retcode: 0, BattleInfo.StageId: 4320136 } && finalBuffs.Contains(3250007) &&
                finalBuffs.Contains(3250008) && !finalBuffs.Contains(3250009) &&
                player.BattleInstance?.RealityShowContext is { IsFirstStage: false });

            var (finalFrames, finalNotify) = await End(BattleEndStatus.BattleEndWin, hpBars: 650, actionValue: 1234.5);
            check("the final stage settles 650 HP bars and the action value consumed before the battle result",
                finalNotify is { ChallengeId: 2, OEFFDKDCIMC: { NKIIHODEFBP: 650, FMBFAPOCIDF: 1234.5 }, BCLOOPGAIKJ: null } &&
                NotifyBeforeResult(finalFrames) &&
                manager.Data.Challenges[2] is { MaxHpBarBreakCount: 650, ConsumedActionValue: 1234.5, IsFirstStageFinished: true } &&
                manager.Data.Challenges[2].BestAvatarIds.SequenceEqual([1407, 1413]));
            check("grade S (650 HP bars) completes the B, A and S quests but not SS",
                Finished(6091006) && Finished(6091007) && Finished(6091008) && !Finished(6091025));

            await Start(finalReq);
            await End(BattleEndStatus.BattleEndWin, hpBars: 300, actionValue: 10);
            var keptBest = manager.Data.Challenges[2] is { MaxHpBarBreakCount: 650, ConsumedActionValue: 1234.5 };
            await Start(finalReq);
            await End(BattleEndStatus.BattleEndWin, hpBars: 650, actionValue: 1000);
            var tieWon = manager.Data.Challenges[2] is { MaxHpBarBreakCount: 650, ConsumedActionValue: 1000 };
            await Start(finalReq);
            var (_, lostNotify) = await End(BattleEndStatus.BattleEndLose, hpBars: 900, actionValue: 1);
            check("a worse run keeps the best, a tie with less action value replaces it, a lost battle settles nothing",
                keptBest && tieWon && lostNotify == null &&
                manager.Data.Challenges[2] is { MaxHpBarBreakCount: 650, ConsumedActionValue: 1000 });

            var afterFinal = (await GetData()).Single(x => x.ChallengeId == 2);
            check("GetData returns the challenge's warm-up result, best record and last team",
                afterFinal is { LMINNGCDDOK: true, HAGDKKBAAKK: 2, MNIBAEDPCHA: 16, CNEJKLABHDG: 650, LIHEPONEGOB: 1000 } &&
                afterFinal.KHBJOAHCCCE.SequenceEqual(finalReq.AvatarList));

            var reset = Last(await Request(new HandlerRealityShowResetFirstStageCsReq(), new IOOHJLNKKKD { ChallengeId = 2 }),
                CmdIds.FKHNFMFIKGL, FKHNFMFIKGL.Parser);
            var unknownReset = Last(await Request(new HandlerRealityShowResetFirstStageCsReq(),
                new IOOHJLNKKKD { ChallengeId = 9 }), CmdIds.FKHNFMFIKGL, FKHNFMFIKGL.Parser);
            check("resetting the warm-up clears its result but keeps the best final stage; an unknown challenge is refused",
                reset is { Retcode: 0, NEMJKLMIJOL: { ChallengeId: 2, LMINNGCDDOK: false, HAGDKKBAAKK: 0, MNIBAEDPCHA: 0, CNEJKLABHDG: 650 } } &&
                unknownReset.Retcode == (uint)Retcode.RetChallengeNotExist &&
                (await Start(finalReq)).Retcode == (uint)Retcode.RetChallengeNotFinish);

            var rank = Last(await Request(new HandlerRealityShowFriendRankCsReq(), new NKEMNFGCBLO { ChallengeId = 2 }),
                CmdIds.JBPOKKNFOKM, JBPOKKNFOKM.Parser);
            check("the friend ranking answers for the challenge (no friends, no entries)",
                rank is { Retcode: 0, ChallengeId: 2, MJCCFIINAJC.Count: 0 });

            // The records are written to the PlayerRealityShow table and read back from SQLite.
            DatabaseHelper.Instance!.SaveDatabase();
            await using var db = new SqliteConnection($"Data Source={Path.Combine(scratch, "regression.db")}");
            await db.OpenAsync();
            var stored = EntityStores.Map[typeof(PlayerRealityShowData)].LoadAll(db).OfType<PlayerRealityShowData>()
                .Single(data => data.Uid == player.Uid);
            check("Road to Fame records are stored in the PlayerRealityShow table",
                stored.Challenges.GetValueOrDefault(2) is
                    { IsFirstStageFinished: false, MaxHpBarBreakCount: 650, ConsumedActionValue: 1000, Lineup.Count: 2 } record &&
                record.Lineup.All(x => x.AvatarType == (int)Own) && record.BestAvatarIds.SequenceEqual([1407, 1413]));
        }
        finally
        {
            foreach (var (row, type) in restoreTypes) row.FinishType = type;
            GameData.RealityShowConfig = savedConfig;
            GameData.ActivityConfig = savedSchedule;
            player.BattleInstance = null;
            player.Connection = null;
        }
    }
}
