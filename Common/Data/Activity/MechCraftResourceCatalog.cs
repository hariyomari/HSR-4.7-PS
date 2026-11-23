using March7thHoney.Data.Excel;

namespace March7thHoney.Data.Activity;

public static class MechCraftResourceCatalog
{
    internal static Dictionary<int, MechCraftRewardAssociationExcel> RewardAssociations { get; } = [];
    internal static Dictionary<int, MechCraftRewardGroupExcel> RewardGroups { get; } = [];
    internal static Dictionary<int, MechCraftRewardTabExcel> RewardTabs { get; } = [];

    private static readonly Dictionary<int, int> BetaAttributeLevelThresholds = new() { [1] = 520, [2] = 1120, [3] = 2100 };

    public static IReadOnlySet<int> RewardQuestIDs => GameData.MechCraftRewardQuestIDs;
    public static IReadOnlyDictionary<int, int> AttributeLevelThresholds => GameData.MechCraftAttributeLevelThresholds;
    public static IReadOnlyList<int> BlockingSubMissionIDs => IntListConstant("Entrance_Block_Gameplay_SubmissionIDs");
    public static int TutorialSubMissionID => IntConstant("Tutorial_SubMission_ID");
    public static int ActivityPanelID => GameData.MechCraftActivityPanelID;
    public static int ActivityModuleID => GameData.MechCraftActivityModuleID;

    public static int IntConstant(string name)
    {
        return GameData.MechCraftIntegerConstants.TryGetValue(ConstantName(name), out var value) ? value : 0;
    }

    public static IReadOnlyList<int> IntListConstant(string name)
    {
        return GameData.MechCraftIntegerListConstants.TryGetValue(ConstantName(name), out var value) ? value : [];
    }

    private static string ConstantName(string name)
    {
        return name.StartsWith("MechCraft_", StringComparison.Ordinal) ? name : $"MechCraft_{name}";
    }

    internal static void CompleteResources(int panelID)
    {
        GameData.MechCraftIntegerConstants.Clear();
        GameData.MechCraftIntegerListConstants.Clear();
        GameData.MechCraftAttributeLevelThresholds.Clear();
        foreach (var (name, value) in GameData.ConstValueClientData)
        {
            if (!name.StartsWith("MechCraft_", StringComparison.Ordinal)) continue;
            if (value.IntValue is >= int.MinValue and <= int.MaxValue)
                GameData.MechCraftIntegerConstants[name] = (int)value.IntValue.Value;
            if (value.ArrayValue != null && value.ArrayValue.All(entry => entry.IntValue is >= int.MinValue and <= int.MaxValue))
                GameData.MechCraftIntegerListConstants[name] = value.ArrayValue.Select(entry => (int)entry.IntValue!.Value).ToList();
            if (name != "MechCraft_Attribute_Level_To_MinValueMap" || value.MapValue == null) continue;
            foreach (var (key, threshold) in value.MapValue)
                if (int.TryParse(key, out var level) && threshold.IntValue is >= 0 and <= int.MaxValue)
                    GameData.MechCraftAttributeLevelThresholds[level] = (int)threshold.IntValue.Value;
        }

        // TurnBasedGameData collapses every ConstValue map to its last entry keyed by the value type name, e.g.
        // {"IntValue":{"IntValue":2100}}. When only that entry survives and it still matches the top level of the
        // complete 4.6 beta table, use the beta table instead of leaving every attribute at level 0.
        if (GameData.MechCraftAttributeLevelThresholds.Count == 0 &&
            GameData.ConstValueClientData.TryGetValue("MechCraft_Attribute_Level_To_MinValueMap", out var collapsed) &&
            collapsed.MapValue is { Count: 1 } collapsedMap &&
            collapsedMap.Values.Single().IntValue == BetaAttributeLevelThresholds[BetaAttributeLevelThresholds.Keys.Max()])
            foreach (var (level, threshold) in BetaAttributeLevelThresholds)
                GameData.MechCraftAttributeLevelThresholds[level] = threshold;

        if (!RewardAssociations.TryGetValue(panelID, out var association))
            throw new InvalidDataException($"MechCraft activity {panelID} has no reward association.");
        var quests = new HashSet<int>(IntListConstant("ResidentQuestIDs"));
        foreach (var groupID in association.QuestTabGroupList)
        {
            if (!RewardGroups.TryGetValue(groupID, out var group))
                throw new InvalidDataException($"MechCraft reward group {groupID} is missing.");
            foreach (var tabID in group.QuestTabList)
            {
                if (!RewardTabs.TryGetValue(tabID, out var tab) || tab.ActivityModuleID != association.ActivityModule)
                    throw new InvalidDataException($"MechCraft reward tab {tabID} is missing or belongs to another activity.");
                quests.UnionWith(tab.QuestList);
            }
        }

        if (association.FinalRewardQuest != 0) quests.Add(association.FinalRewardQuest);
        foreach (var questID in quests)
            if (!GameData.QuestDataData.TryGetValue(questID, out var quest) || !GameData.FinishWayData.ContainsKey(quest.FinishWayID))
                throw new InvalidDataException($"MechCraft reward quest {questID} is missing its completion data.");
        GameData.SetMechCraftActivityCatalog(panelID, association.ActivityModule, quests);
        RewardAssociations.Clear();
        RewardGroups.Clear();
        RewardTabs.Clear();
    }
}
