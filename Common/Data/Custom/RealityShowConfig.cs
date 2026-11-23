using Newtonsoft.Json;

namespace March7thHoney.Data.Custom;

/// <summary>
///     Interastral Peace Gala: Road to Fame (星际和平盛典：成名之路): client module ActivityRealityShow, panel 50143,
///     challenge modules 5014301-5014305, stage type RealityShowActivity. The challenge rows come from the
///     ExcelOutput table ActivityRealityShowConfig, which the resource export does not name, so
///     Config/Custom/RealityShow.json carries them, decoded from the OSBETAWin4.7.51 DesignData
///     (see docs/development/reality-show.md). Loaded from Config/Custom on every boot and not stored in Resource.bin.
/// </summary>
public class RealityShowConfig
{
    /// <summary>ConstValueClient ActivityRealityShowHPPunctured: HP bars needed for the C / B / A / S / SS grade.</summary>
    [JsonProperty("hpBarBreakGrades")] public List<int> HpBarBreakGrades { get; set; } = [];

    [JsonProperty("challenges")] public List<RealityShowChallengeConfig> Challenges { get; set; } = [];

    public RealityShowChallengeConfig? GetChallenge(int id) => Challenges.FirstOrDefault(x => x.Id == id);

    /// <summary>Grade index of an HP bar count: 0 C, 1 B, 2 A, 3 S, 4 SS (the client's RealityShowResultLevel).</summary>
    public int GetResultLevel(int hpBarBreakCount)
    {
        var level = 0;
        for (var i = 0; i < HpBarBreakGrades.Count; i++)
            if (hpBarBreakCount >= HpBarBreakGrades[i])
                level = i;
        return level;
    }
}

public class RealityShowChallengeConfig
{
    [JsonProperty("id")] public int Id { get; set; }

    /// <summary>The challenge's activity module; its schedule row opens the challenge.</summary>
    [JsonProperty("moduleId")] public int ModuleId { get; set; }

    /// <summary>
    ///     Warm-up stage (预热阶段). The client tells the two stages apart by these exact ids
    ///     (BattleResultProcess._IsRealityShowFirstStage), so the per-world-level variants must not be used.
    /// </summary>
    [JsonProperty("stageId1")] public int StageId1 { get; set; }

    /// <summary>Final stage (决赛阶段), fought against the boss's 9999 HP bars.</summary>
    [JsonProperty("stageId2")] public int StageId2 { get; set; }

    [JsonProperty("eventId1")] public int EventId1 { get; set; }
    [JsonProperty("eventId2")] public int EventId2 { get; set; }
    [JsonProperty("battleAreaId1")] public int BattleAreaId1 { get; set; }
    [JsonProperty("battleAreaId2")] public int BattleAreaId2 { get; set; }
    [JsonProperty("monsterId")] public int MonsterId { get; set; }

    /// <summary>Trial avatars (SpecialAvatar ids) offered by the team editor.</summary>
    [JsonProperty("specialAvatarList")] public List<int> SpecialAvatarList { get; set; } = [];

    /// <summary>Final-stage buffs (MazeBuff ids), one per tier; buff level N carries tiers 1..N.</summary>
    [JsonProperty("buffIdList")] public List<int> BuffIdList { get; set; } = [];

    /// <summary>What the warm-up counts for the buff tiers; every 4.7.51 row uses ShieldBreak.</summary>
    [JsonProperty("buffUpType")] public string BuffUpType { get; set; } = "";

    /// <summary>Warm-up count needed for each buff tier, ascending.</summary>
    [JsonProperty("buffUpNumList")] public List<int> BuffUpNumList { get; set; } = [];

    /// <summary>Buff level earned by a warm-up count: the number of tier thresholds reached.</summary>
    public int GetBuffLevel(int count) => Math.Min(BuffUpNumList.Count(threshold => count >= threshold), BuffIdList.Count);
}
