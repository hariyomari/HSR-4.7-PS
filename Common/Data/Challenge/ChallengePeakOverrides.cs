using March7thHoney.Util;

namespace March7thHoney.Data.Challenge;

/// <summary>
///     Applies the Challenge Peak season maps of Config/Custom/ChallengePeak.json, plus the seasons
///     <see cref="ChallengePeakMapResolver" /> derives for what the file leaves out, to the group and level rows.
/// </summary>
/// <remarks>
///     Runs after the floors are loaded, because deriving a season reads them. A cache boot applies the cached
///     overrides first and the operator's file again afterwards, so every pass starts by clearing what an earlier
///     one left on the rows.
/// </remarks>
public static class ChallengePeakOverrides
{
    private static readonly Logger Logger = new("ChallengePeak");

    public static void Apply()
    {
        foreach (var level in GameData.ChallengePeakConfigData.Values)
        {
            level.OverrideMapEntranceId = 0;
            level.OverrideMazeGroupId = 0;
            level.OverrideNpcMonsterId = 0;
        }

        GameData.ChallengePeakDerivedGroups = [];
        if (!GameData.FloorInfoData.IsEmpty)
            foreach (var derived in ChallengePeakMapResolver.Derive(GameData.ChallengePeakOverrideConfig.ChallengePeak))
            {
                Logger.Debug(
                    $"ChallengePeak season {derived.GroupId} has no entry in ChallengePeak.json; derived from the " +
                    $"resources: map entrance {derived.MapEntranceId} (floor {derived.FloorId}), knight group " +
                    $"{derived.KnightMazeGroupId}, king group {derived.KingMazeGroupId}.");
                GameData.ChallengePeakDerivedGroups.Add(ChallengePeakMapResolver.ToOverride(derived));
            }

        foreach (var group in GameData.ChallengePeakGroups)
        {
            if (GameData.ChallengePeakGroupConfigData.TryGetValue(group.GroupId, out var groupExcel))
            {
                var firstStage = group.Stages.Values.FirstOrDefault();
                if (firstStage != null && firstStage.MapEntranceId > 0)
                {
                    groupExcel.MapEntranceID = firstStage.MapEntranceId;
                    groupExcel.MapEntranceBoss = firstStage.MapEntranceId;
                }
            }

            foreach (var stage in group.Stages)
            {
                if (!int.TryParse(stage.Key, out var levelId)) continue;
                if (!GameData.ChallengePeakConfigData.TryGetValue(levelId, out var levelExcel)) continue;

                levelExcel.OverrideMapEntranceId = stage.Value.MapEntranceId;
                levelExcel.OverrideMazeGroupId = stage.Value.MazeGroupId;
                levelExcel.OverrideNpcMonsterId = stage.Value.NpcMonsterId > 0
                    ? stage.Value.NpcMonsterId
                    : group.NpcMonsterIdDefault;
            }
        }

        foreach (var level in GameData.ChallengePeakConfigData.Values) level.RebuildChallengeMonsters();

        GameData.SyncCurrentChallengePeakGroupId();
    }
}
