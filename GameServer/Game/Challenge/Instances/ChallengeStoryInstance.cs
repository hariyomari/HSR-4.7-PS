using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Friend;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Challenge.Definitions;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.Challenge;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.Proto;
using March7thHoney.GameServer.Game.Challenge;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Challenge.Instances;

public class ChallengeStoryInstance(PlayerInstance player, ChallengeStateData data)
    : BaseLegacyChallengeInstance(player, data)
{
    #region Properties

    private StoryChallengeState Story => Data.Story!;

    public override ChallengeConfigExcel Config { get; } =
        GameData.ChallengeConfigData[(int)data.Story!.ChallengeMazeId];

    #endregion

    #region Serialization

    public override CurChallenge ToProto()
    {
        return new CurChallenge
        {
            ChallengeId = Story.ChallengeMazeId,
            ExtraLineupType = (ExtraLineupType)Story.CurrentExtraLineup,
            Status = (ChallengeStatus)Story.CurStatus,
            StageInfo = new ChallengeCurBuffInfo
            {
                CurStoryBuffs = new ChallengeStoryBuffList
                {
                    BuffList = { Story.Buffs }
                }
            },
            RoundCount = (uint)Config.ChallengeCountDown,
            ScoreId = Story.ScoreStage1,
            ScoreTwo = Story.ScoreStage2
        };
    }

    #endregion

    #region Setter & Getter

    public override uint GetStars()
    {
        return Story.Stars;
    }

    public override uint GetScore1()
    {
        return Story.ScoreStage1;
    }

    public override uint GetScore2()
    {
        return Story.ScoreStage2;
    }

    public void SetCurrentExtraLineup(ExtraLineupType type)
    {
        Story.CurrentExtraLineup = (ChallengeLineupType)type;
    }

    public int GetTotalScore()
    {
        return (int)(Story.ScoreStage1 + Story.ScoreStage2);
    }

    public override int GetCurrentExtraLineupType()
    {
        return (int)Story.CurrentExtraLineup;
    }

    public override void SetStartPos(Position pos)
    {
        Story.StartPos = pos;
    }

    public override void SetStartRot(Position rot)
    {
        Story.StartRot = rot;
    }

    public override void SetSavedMp(int mp)
    {
        Story.SavedMp = (uint)mp;
    }

    public override Dictionary<int, List<ChallengeConfigExcel.ChallengeMonsterInfo>> GetStageMonsters()
    {
        return Story.CurrentStage == 1
            ? Config.ChallengeMonsters1
            : Config.ChallengeMonsters2;
    }

    #endregion

    #region Handlers

    public override void OnBattleStart(BattleInstance battle)
    {
        base.OnBattleStart(battle);

        battle.RoundLimit = Config.ChallengeCountDown;

        battle.Buffs.Add(new MazeBuff(Config.MazeBuffID, 1, -1)
        {
            WaveFlag = -1
        });

        if (Config.StoryExcel == null) return;
        ChallengeScoreTargets.AddStoryTargets(battle, Config.StoryExcel.BattleTargetID!, GetTotalScore());

        if (Story.Buffs.Count < Story.CurrentStage) return;
        var buffId = Story.Buffs[(int)(Story.CurrentStage - 1)];
        battle.Buffs.Add(new MazeBuff((int)buffId, 1, -1)
        {
            WaveFlag = -1
        });
    }

    public override async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        switch (req.EndStatus)
        {
            case BattleEndStatus.BattleEndWin:
                RecordStageScore(req.Stt.ChallengeScore);

                // Get monster count in stage
                long monsters = Player.SceneInstance!.Entities.Values.OfType<EntityMonster>().Count();

                if (monsters == 0) await AdvanceStage();

                // Set saved technique points (This will be restored if the player resets the challenge)
                Story.SavedMp = (uint)Player.LineupManager!.GetCurLineup()!.Mp;
                break;
            case BattleEndStatus.BattleEndQuit:
                // Reset technique points and move back to start position
                var lineup = Player.LineupManager!.GetCurLineup()!;
                lineup.Mp = (int)Story.SavedMp;
                await Player.MoveTo(Story.StartPos, Story.StartRot);
                await Player.SendPacket(new PacketSyncLineupNotify(lineup));
                break;
            case BattleEndStatus.BattleEndLose:
                RecordStageScore(req.Stt.ChallengeScore);
                await AdvanceStage();
                break;
            default:
                Story.CurStatus = (int)ChallengeStatus.ChallengeFailed;
                await Player.SendPacket(new PacketChallengeSettleNotify(this));
                Player.ChallengeManager!.SaveInstance(this);
                break;
        }
    }

    // The 10003 score target is seeded with the run total, so the client reports a cumulative score.
    // Subtract only the other stage: a stage split over several battles already includes its own
    // earlier score in the seed. Quit/fail results must not overwrite the score.
    public void RecordStageScore(uint challengeScore)
    {
        var otherStageScore = Story.CurrentStage == 1 ? Story.ScoreStage2 : Story.ScoreStage1;
        var stageScore = challengeScore > otherStageScore ? challengeScore - otherStageScore : 0;

        if (Story.CurrentStage == 1)
            Story.ScoreStage1 = stageScore;
        else
            Story.ScoreStage2 = stageScore;
    }

    public uint CalculateStars()
    {
        var targets = Config.ChallengeTargetID!;
        var stars = 0u;

        for (var i = 0; i < targets.Count; i++)
        {
            if (!GameData.ChallengeTargetData.ContainsKey(targets[i])) continue;

            var target = GameData.ChallengeTargetData[targets[i]];

            switch (target.ChallengeTargetType)
            {
                case ChallengeTargetExcel.ChallengeType.TOTAL_SCORE:
                    if (GetTotalScore() >= target.ChallengeTargetParam1) stars += 1u << i;
                    break;
            }
        }

        return Math.Min(stars, 7);
    }

    private async ValueTask AdvanceStage()
    {
        if (Story.CurrentStage >= Config.StageNum)
        {
            // Last stage
            Story.CurStatus = (int)ChallengeStatus.ChallengeFinish;
            IsWin = true;
            Story.Stars = CalculateStars();

            // Save history
            Player.ChallengeManager!.AddHistory((int)Story.ChallengeMazeId, (int)GetStars(), GetTotalScore(),
                (int)Story.ScoreStage1, (int)Story.ScoreStage2,
                Story.Buffs.Count > 0 ? (int)Story.Buffs[0] : 0,
                Story.Buffs.Count > 1 ? (int)Story.Buffs[1] : 0,
                Story.ScoreStage1 > 0, Story.ScoreStage2 > 0);

            // Send challenge result data
            await Player.SendPacket(new PacketChallengeSettleNotify(this));

            // Call MissionManager
            await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.ChallengeFinish, this);
            // ChallengeFinishCnt ParamIntList: 1 Forgotten Hall, 2 Pure Fiction, 3 Apocalyptic Shadow.
            if (IsWin)
                await Player.MissionManager.TriggerCountEvent(MissionFinishTypeEnum.ChallengeFinishCnt, 1, 2);

            // save
            Player.ChallengeManager.SaveBattleRecord(this);

            // add development
            Player.FriendRecordData!.AddAndRemoveOld(new FriendDevelopmentInfoPb
            {
                DevelopmentType = (DevelopmentType)8,
                Params = { { "ChallengeId", (uint)Config.ID } }
            });

            // Persist the finished state and stage-2 score; otherwise a relog restores a replayable stage 2.
            Player.ChallengeManager.SaveInstance(this);
        }
        else
        {
            Story.CurrentStage++;

            // Switch to the second-half lineup first, so that LoadEntity uses LineupChallenge2 for the avatar entities
            // when EnterScene builds the new scene.
            SetCurrentExtraLineup(ExtraLineupType.LineupChallenge2);
            await Player.LineupManager!.SetExtraLineup((ExtraLineupType)GetCurrentExtraLineupType());

            // In the ChallengeStory config MapEntranceID == MapEntranceID2 (verified for 92/92 entries); fall back to
            // MapEntranceID when MapEntranceID2 is 0. EnterScene triggers ChallengeEntityLoader.LoadEntity, which
            // picks ChallengeMonsters2 for CurrentStage == 2 and loads the second-half boss group.
            var entranceId = Config.MapEntranceID2 != 0 ? Config.MapEntranceID2 : Config.MapEntranceID;
            await Player.EnterScene(entranceId, 0, true);

            await Player.SendPacket(new PacketChallengeLineupNotify((ExtraLineupType)Story.CurrentExtraLineup));

            Story.SavedMp = (uint)Player.LineupManager.GetCurLineup()!.Mp;
            Story.StartPos = Player.Data.Pos!;
            Story.StartRot = Player.Data.Rot!;

            Player.ChallengeManager!.SaveInstance(this);
        }
    }

    #endregion
}
