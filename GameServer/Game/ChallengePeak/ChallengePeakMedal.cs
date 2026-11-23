using March7thHoney.Data;
using March7thHoney.Database.Challenge;
using March7thHoney.Enums.Challenge;
using March7thHoney.Enums.Item;

namespace March7thHoney.GameServer.Game.ChallengePeak;

/// <summary>
///     Medal ranks of the King boss (王棋) and the limited-time head frame that goes with each rank.
///     <para>
///         Ranks follow the client's <c>RPG.Client.ChallengePeakBoss.CalculateRank(bool, int)</c>: reaching the color
///         target gives the color medal (彩钻); otherwise 1/2/3 finished normal targets give bronze/silver/gold and 0
///         gives none. The color target is the hard_mode_has_passed flag sent to the client (settlement and the
///         overview both decide it with <see cref="IsColorMedalClear" />).
///     </para>
///     <para>
///         Head frames are read from ChallengePeakReward: BOSS_STAR_LIMIT_REWARD (TypeValue = rank) and
///         BOSS_COLOR_TARGET_REWARD (color medal) → RewardData → the item whose subtype is HeadIconFrame. The client's
///         <c>ChallengePeakReward._CheckRewardCanTake</c> always returns false for these two types, so they can only
///         be granted by the server according to the record and never through a reward-claim request.
///     </para>
/// </summary>
public static class ChallengePeakMedal
{
    public const string StarFrameRewardType = "BOSS_STAR_LIMIT_REWARD";
    public const string ColorFrameRewardType = "BOSS_COLOR_TARGET_REWARD";

    public static bool IsHeadFrameReward(string rewardType)
    {
        return rewardType is StarFrameRewardType or ColorFrameRewardType;
    }

    /// <summary>
    ///     Color target: a hard-mode (绝境) clear, or a normal clear in 0 cycles (official capture: a 0-cycle normal
    ///     clear sends hard_mode_has_passed, a 3-cycle one does not).
    /// </summary>
    public static bool IsColorMedalClear(bool isHard, uint roundCnt)
    {
        return isHard || roundCnt == 0;
    }

    /// <summary>Whether the color target has been reached, judged from each difficulty's own best record.</summary>
    public static bool HasColorMedal(ChallengePeakBossLevelData? easyRec, ChallengePeakBossLevelData? hardRec)
    {
        return (hardRec?.HasRecord == true && IsColorMedalClear(true, hardRec.RoundCnt)) ||
               (easyRec?.HasRecord == true && IsColorMedalClear(false, easyRec.RoundCnt));
    }

    public static ChallengePeakRankEnum CalculateRank(bool colorMedalFinished, int passedTargetCount)
    {
        if (colorMedalFinished) return ChallengePeakRankEnum.ColorMedal;
        return passedTargetCount is >= (int)ChallengePeakRankEnum.Bronze and <= (int)ChallengePeakRankEnum.Gold
            ? (ChallengePeakRankEnum)passedTargetCount
            : ChallengePeakRankEnum.None;
    }

    /// <summary>
    ///     Medal rank for the King boss best records: the color target takes priority; otherwise the number of targets
    ///     finished in the normal-difficulty best record.
    /// </summary>
    public static ChallengePeakRankEnum GetBossRank(ChallengePeakBossLevelData? easyRec,
        ChallengePeakBossLevelData? hardRec)
    {
        var easyTargets = easyRec?.HasRecord == true ? easyRec.FinishedTargetList.Count : 0;
        return CalculateRank(HasColorMedal(easyRec, hardRec), easyTargets);
    }

    /// <summary>The head frame item for a rank in this reward group, or 0 when the config has none.</summary>
    public static uint GetHeadFrameItemId(int rewardGroupId, ChallengePeakRankEnum rank)
    {
        if (rank == ChallengePeakRankEnum.None ||
            !GameData.ChallengePeakRewardData.TryGetValue(rewardGroupId, out var rewardLine))
            return 0;

        var row = rewardLine.Where(x => IsHeadFrameReward(x.RewardType))
            .OrderBy(x => x.ID)
            .FirstOrDefault(x => GetRewardRank(x.RewardType, x.TypeValue) == rank);
        return row == null ? 0 : GetHeadFrameItemOfReward(row.RewardID);
    }

    /// <summary>
    ///     The rank a head frame item belongs to; None when it is not a Challenge Peak (异相仲裁) head frame, which
    ///     includes the invalid IDs that older versions composed.
    /// </summary>
    public static ChallengePeakRankEnum GetHeadFrameRank(uint itemId)
    {
        if (itemId == 0) return ChallengePeakRankEnum.None;

        var best = ChallengePeakRankEnum.None;
        foreach (var row in GameData.ChallengePeakRewardData.Values.SelectMany(x => x))
        {
            if (!IsHeadFrameReward(row.RewardType) || GetHeadFrameItemOfReward(row.RewardID) != itemId) continue;
            var rank = GetRewardRank(row.RewardType, row.TypeValue);
            if (rank > best) best = rank;
        }

        return best;
    }

    private static ChallengePeakRankEnum GetRewardRank(string rewardType, int typeValue)
    {
        return rewardType switch
        {
            ColorFrameRewardType => ChallengePeakRankEnum.ColorMedal,
            StarFrameRewardType => CalculateRank(false, typeValue),
            _ => ChallengePeakRankEnum.None
        };
    }

    private static uint GetHeadFrameItemOfReward(int rewardId)
    {
        if (!GameData.RewardDataData.TryGetValue(rewardId, out var reward)) return 0;
        foreach (var (itemId, _) in reward.GetItems())
            if (GameData.ItemConfigData.GetValueOrDefault(itemId)?.ItemSubType == ItemSubTypeEnum.HeadIconFrame)
                return (uint)itemId;
        return 0;
    }
}
