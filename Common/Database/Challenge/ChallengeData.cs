using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.Database.Challenge;

[DbTable("Challenge")]
public class ChallengeData : BaseDatabaseDataHelper
{
    public Dictionary<int, ChallengeHistoryData> History { get; set; } = new();

    public string? ChallengeInstance { get; set; }
    public string Instance { get; set; } = ""; // placeholder

    public Dictionary<int, ChallengeGroupReward> TakenRewards { get; set; } = new();
    public Dictionary<int, List<int>> PeakTakenRewardIds { get; set; } = new();
    public Dictionary<int, ChallengePeakLevelData> PeakLevelDatas { get; set; } = new();

    public Dictionary<int, ChallengePeakBossLevelData> PeakBossLevelDatas { get; set; } = new();
    public int CurrentPeakGroupId { get; set; }

    // King boss (王棋) difficulty, persisted per peakGroupId so the player's choice of normal or hard mode (绝境)
    // survives a relogin.
    public Dictionary<int, bool> PeakBossHardByGroup { get; set; } = new();

    public string? TierceInstance { get; set; }

    public Dictionary<int, ChallengeTierceHistoryData> TierceHistory { get; set; } = new();

    public void Delete(int challengeId)
    {
        History.Remove(challengeId);
    }
}

[MemoryPackable]
public partial class ChallengeTierceHistoryData
{
    public int ChallengeId { get; set; }
    public bool IsPassed { get; set; }
    public uint Stars { get; set; }
    public List<uint> FinishedTargetList { get; set; } = [];
    // Per-stage slots are initialised lazily by the manager (NormaliseSlots) to avoid
    // Newtonsoft.Json appending deserialised entries onto a non-empty default list.
    public List<List<uint>> StageLineups { get; set; } = [];
    public List<uint> StageBuffIds { get; set; } = [];
    public List<bool> StageCleared { get; set; } = [];
    public List<uint> StageScores { get; set; } = [];
    public uint UsedCycleCount { get; set; }
    public uint RecordScore { get; set; }
    public uint RecordId { get; set; }
    public List<List<uint>> EditorStageLineups { get; set; } = [];
    public List<uint> EditorStageBuffIds { get; set; } = [];
    public List<uint> StageUsedCycles { get; set; } = [];
    public List<uint> StageDeadAvatarNums { get; set; } = [];
    public List<bool> StageMetricsRecorded { get; set; } = [];
}

[MemoryPackable]
public partial class ChallengePeakLevelData
{
    public int LevelId { get; set; }
    public uint RoundCnt { get; set; }
    public uint PeakStar { get; set; }
    public List<uint> BaseAvatarList { get; set; } = [];
    public List<uint> FinishedTargetList { get; set; } = [];
    // The members below hold the best record. They are separate from BaseAvatarList above, which is the lineup used
    // last, and are written only when the record is beaten.
    public bool HasRecord { get; set; }
    public List<uint> RecordAvatarList { get; set; } = [];

    // Placeholder: old blobs already contain this member slot, and MemoryPack refuses to deserialize them if it is
    // removed.
    public bool ReservedSlot { get; set; }
}

[MemoryPackable]
public partial class ChallengePeakBossLevelData
{
    public int LevelId { get; set; }
    public uint BuffId { get; set; }
    public bool IsHard { get; set; }
    public uint RoundCnt { get; set; }
    public uint PeakStar { get; set; }
    public List<uint> BaseAvatarList { get; set; } = [];
    public List<uint> FinishedTargetList { get; set; } = [];
    // The members below hold the best record. They are separate from BaseAvatarList / BuffId above, which are the
    // lineup and buff used last, and are not mirrored to the other difficulty.
    public bool HasRecord { get; set; }
    public List<uint> RecordAvatarList { get; set; } = [];
    public uint RecordBuffId { get; set; }

    // Placeholder: old blobs already contain this member slot, and MemoryPack refuses to deserialize them if it is
    // removed.
    public bool ReservedSlot { get; set; }
}

[MemoryPackable]
public partial class ChallengeHistoryData(int uid, int challengeId)
{
    [MemoryPackConstructor]
    public ChallengeHistoryData() : this(0, 0)
    {
    }

    public int OwnerId { get; set; } = uid;

    public int ChallengeId { get; set; } = challengeId;
    public int GroupId { get; set; }
    public int TakenReward { get; set; }
    public int Stars { get; set; }
    public int Score { get; set; }
    public int ScoreOne { get; set; }
    public int ScoreTwo { get; set; }
    public int BuffOne { get; set; }
    public int BuffTwo { get; set; }
    public bool FirstNodeWon { get; set; }
    public bool SecondNodeWon { get; set; }

    public void SetStars(int stars)
    {
        Stars = Math.Max(Stars, stars);
    }

    public int GetTotalStars()
    {
        var total = 0;
        for (var i = 0; i < 3; i++) total += (Stars & (1 << i)) != 0 ? 1 : 0;
        return total;
    }

    public Proto.Challenge ToProto()
    {
        var hasSplitScore = ScoreOne > 0 || ScoreTwo > 0;
        var isBossChallenge = GameData.ChallengeConfigData.TryGetValue(ChallengeId, out var config) && config.IsBoss();
        var proto = new Proto.Challenge
        {
            ChallengeId = (uint)ChallengeId,
            TakenReward = (uint)TakenReward,
            ScoreId = (uint)(hasSplitScore ? ScoreOne : Score),
            ScoreTwo = (uint)ScoreTwo,
            Star = (uint)Stars
        };

        if (isBossChallenge && (FirstNodeWon || SecondNodeWon || BuffOne > 0 || BuffTwo > 0 || hasSplitScore))
        {
            proto.StageInfo = new ChallengeStageInfo
            {
                BossInfo = new ChallengeBossInfo
                {
                    FirstNode = new ChallengeBossSingleNodeInfo
                    {
                        BuffId = (uint)BuffOne,
                        IsWin = FirstNodeWon,
                        MaxScore = (uint)ScoreOne,
                        MBHCJPONJOM = FirstNodeWon
                    },
                    SecondNode = new ChallengeBossSingleNodeInfo
                    {
                        BuffId = (uint)BuffTwo,
                        IsWin = SecondNodeWon,
                        MaxScore = (uint)ScoreTwo,
                        MBHCJPONJOM = SecondNodeWon
                    },
                    Unk1 = true
                }
            };
        }

        return proto;
    }
}

[MemoryPackable]
public partial class ChallengeInstanceData
{
    public Position StartPos { get; set; } = new();
    public Position StartRot { get; set; } = new();
    public int ChallengeId { get; set; } = 0;
    public int CurrentStage { get; set; }
    public int CurrentExtraLineup { get; set; }
    public int Status { get; set; }
    public bool HasAvatarDied { get; set; }

    public int SavedMp { get; set; }
    public int RoundsLeft { get; set; }
    public int Stars { get; set; }
    public int ScoreStage1 { get; set; }
    public int ScoreStage2 { get; set; }
    public List<int> StoryBuffs { get; set; } = [];
    public List<int> BossBuffs { get; set; } = [];
}

[MemoryPackable]
public partial class ChallengeGroupReward(int uid, int groupId)
{
    [MemoryPackConstructor]
    public ChallengeGroupReward() : this(0, 0)
    {
    }

    public int OwnerUid = uid;
    public int GroupId { get; set; } = groupId;
    public long TakenStars { get; set; }

    public bool HasTakenReward(int starCount)
    {
        return (TakenStars & (1L << starCount)) != 0;
    }

    public void SetTakenReward(int starCount)
    {
        TakenStars |= 1L << starCount;
    }

    public ChallengeGroup ToProto()
    {
        var proto = new ChallengeGroup
        {
            GroupId = (uint)GroupId,
            TakenStarsCountReward = (ulong)TakenStars
        };

        return proto;
    }
}
