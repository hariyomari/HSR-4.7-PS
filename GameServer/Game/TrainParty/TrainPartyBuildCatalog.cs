using March7thHoney.Data;
using March7thHoney.Data.Excel;

namespace March7thHoney.GameServer.Game.TrainParty;

// An area's goal selects step rows via StepGroupList. Each finished step costs CoinCost and unlocks
// its StaticPropIDList. The client hides FirstStep from blueprints; finishing it opens the other steps.
public static class TrainPartyBuildCatalog
{
    public static IEnumerable<TrainPartyAreaConfigExcel> Areas =>
        GameData.TrainPartyAreaConfigData.Values.OrderBy(x => x.ID);

    public static TrainPartyAreaConfigExcel? GetArea(int areaId)
    {
        return GameData.TrainPartyAreaConfigData.GetValueOrDefault(areaId);
    }

    public static List<TrainPartyAreaGoalConfigExcel> GetGoals(int areaId)
    {
        return GameData.TrainPartyAreaGoalConfigData.Values.Where(x => x.AreaID == areaId).OrderBy(x => x.ID)
            .ToList();
    }

    public static List<TrainPartyStepConfigExcel> GetGoalSteps(TrainPartyAreaGoalConfigExcel goal)
    {
        return GameData.TrainPartyStepConfigData.Values.Where(x => goal.StepGroupList.Contains(x.GroupID))
            .OrderBy(x => x.SortID).ThenBy(x => x.ID).ToList();
    }

    public static List<TrainPartyStepConfigExcel> GetAreaSteps(int areaId)
    {
        return GetGoals(areaId).SelectMany(GetGoalSteps).DistinctBy(x => x.ID).ToList();
    }

    // The goal the client tracks for an area: the first one with an unfinished step, else the last one.
    public static TrainPartyAreaGoalConfigExcel? GetCurrentGoal(int areaId, IReadOnlySet<int> finishedSteps)
    {
        var goals = GetGoals(areaId);
        return goals.FirstOrDefault(goal => GetGoalSteps(goal).Any(step => !finishedSteps.Contains(step.ID)))
               ?? goals.LastOrDefault();
    }

    public static bool IsStepUnlocked(int areaId, int stepId, IReadOnlySet<int> finishedSteps)
    {
        var firstStep = GetArea(areaId)?.FirstStep ?? 0;
        return firstStep == 0 || stepId == firstStep || finishedSteps.Contains(firstStep)
               || !GameData.TrainPartyStepConfigData.ContainsKey(firstStep);
    }

    // Area progress is the share of the area's steps that are finished, 0-100; the client treats >= 100 as complete.
    public static int GetProgress(int areaId, IReadOnlySet<int> finishedSteps)
    {
        var steps = GetAreaSteps(areaId);
        if (steps.Count == 0) return 100;
        return steps.Count(step => finishedSteps.Contains(step.ID)) * 100 / steps.Count;
    }

    // Whole-car progress: the share of every table area's steps that are finished.
    public static int GetGlobalProgress(IReadOnlyDictionary<int, HashSet<int>> finishedStepsByArea)
    {
        var steps = Areas.SelectMany(area => GetAreaSteps(area.ID).Select(step => (area.ID, step.ID))).ToList();
        if (steps.Count == 0) return 100;
        return steps.Count(x => finishedStepsByArea.TryGetValue(x.Item1, out var finished) && finished.Contains(x.Item2)) *
            100 / steps.Count;
    }

    // Stars an area adds for the ProgressBonusList thresholds its progress has reached.
    public static int GetStars(int areaId, int progress)
    {
        return (GetArea(areaId)?.ProgressBonusList ?? []).Where(bonus => progress >= bonus.Progress)
            .Sum(bonus => bonus.AddStar);
    }

    // Build level reached with this many stars: each TrainPartyRewardConfig level needs RequireStar more stars than
    // the level before it.
    public static int GetBuildLevel(int stars)
    {
        var level = 0;
        var needed = 0;
        foreach (var row in GameData.TrainPartyRewardConfigData.Values.OrderBy(x => x.Level))
        {
            needed += row.RequireStar;
            if (needed > stars) break;
            level = row.Level;
        }

        return level;
    }

    // The room an area builds, from its bar-carriage block ("Bar_2F_Bedroom" -> "Bedroom"); null without block data.
    public static string? GetRoomName(TrainPartyAreaConfigExcel area)
    {
        var block = (area.ShowBlockList ?? []).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        if (block == null) return null;
        var room = block[(block.LastIndexOf('_') + 1)..];
        return room.Length == 0 ? null : room;
    }
}
