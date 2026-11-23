using System.Text.RegularExpressions;
using MemoryPack;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Quest;

namespace March7thHoney.Data.Activity;

[MemoryPackable]
public partial class B51RacingResourceCatalog
{
    public Dictionary<int, B51RacingCycleExcel> Cycles { get; set; } = [];
    public Dictionary<int, B51RacingChallengeCarExcel> ChallengeCars { get; set; } = [];
    public Dictionary<int, B51RacingCarExcel> Cars { get; set; } = [];
    public Dictionary<int, B51RacingPartExcel> Parts { get; set; } = [];
    public Dictionary<int, B51RacingDriverExcel> Drivers { get; set; } = [];
    public Dictionary<int, B51RacingLiveryExcel> Paints { get; set; } = [];
    public Dictionary<int, B51RacingMatchExcel> Matches { get; set; } = [];
    public Dictionary<int, B51RacingTrackExcel> Tracks { get; set; } = [];
    public HashSet<int> RewardQuestIDs { get; set; } = [];
    public HashSet<int> ChallengeMatchIDs { get; set; } = [];
    public HashSet<int> ChallengeDriverIDs { get; set; } = [];
    public int PanelID { get; set; }
    public int ActivityModuleID { get; set; }
    public int RaidID { get; set; }
    public int UnlockSubMissionID { get; set; }
    public int MaxCarNumber { get; set; }

    internal static HashSet<int> ActivityFinishWayIDs { get; } = [];

    internal static Dictionary<int, B51RacingRewardAssociationExcel> RewardAssociations { get; } = [];
    internal static Dictionary<int, B51RacingRewardGroupExcel> RewardGroups { get; } = [];
    internal static Dictionary<int, B51RacingRewardTabExcel> RewardTabs { get; } = [];

    private static int IntConstant(string name) => checked((int)(GameData.ConstValueClientData.GetValueOrDefault($"Activity_B51Racing_{name}")?.IntValue ?? 0));

    private static IEnumerable<int> IntListConstant(string name) =>
        GameData.ConstValueClientData.GetValueOrDefault($"Activity_B51Racing_{name}")?.ArrayValue?
            .Select(value => checked((int)(value.IntValue ?? 0))) ?? [];

    public void CompleteResources(B51RacingActivityPanelExcel panel)
    {
        PanelID = panel.PanelID;
        RaidID = IntConstant("RaidID");
        UnlockSubMissionID = IntConstant("Activity_Unlock_Sub_Mission_ID");
        MaxCarNumber = IntConstant("Car_Max_Number");
        ChallengeMatchIDs.UnionWith(IntListConstant("Challenge_Match_List"));
        ChallengeDriverIDs.UnionWith(IntListConstant("Challenge_DriverList"));
        RewardQuestIDs.UnionWith(IntListConstant("Resident_Reward_QuestList"));
        foreach (Match match in Regex.Matches(panel.FinishConditions, @"\[QuestClose:(\d+)\]"))
            RewardQuestIDs.Add(int.Parse(match.Groups[1].Value));

        if (!RewardAssociations.TryGetValue(PanelID, out var association))
            throw new InvalidDataException($"B51Racing panel {PanelID} has no reward association.");
        ActivityModuleID = association.ActivityModule;
        if (association.FinalRewardQuest != 0) RewardQuestIDs.Add(association.FinalRewardQuest);
        foreach (var groupID in association.QuestTabGroupList)
        {
            if (!RewardGroups.TryGetValue(groupID, out var group))
                throw new InvalidDataException($"B51Racing reward group {groupID} is missing.");
            foreach (var tabID in group.QuestTabList)
            {
                if (!RewardTabs.TryGetValue(tabID, out var tab) || tab.ActivityModuleID != ActivityModuleID)
                    throw new InvalidDataException($"B51Racing reward tab {tabID} is missing.");
                RewardQuestIDs.UnionWith(tab.QuestList);
            }
        }
        foreach (var quest in GameData.QuestDataData.Values.Where(quest => quest.QuestType == 1 && quest.UnlockType == QuestUnlockTypeEnum.AutoUnlock && quest.QuestTitle.Hash.IsZero &&
                     ActivityFinishWayIDs.Contains(quest.FinishWayID)))
            RewardQuestIDs.Add(quest.QuestID);
        foreach (var questID in RewardQuestIDs)
            if (!GameData.QuestDataData.TryGetValue(questID, out var quest) || !GameData.FinishWayData.ContainsKey(quest.FinishWayID))
                throw new InvalidDataException($"B51Racing quest {questID} has no completion data.");
        Validate();
        ActivityFinishWayIDs.Clear();
        RewardAssociations.Clear();
        RewardGroups.Clear();
        RewardTabs.Clear();
    }

    public void Validate()
    {
        if (Cycles.Count == 0 || ChallengeCars.Count == 0 || Paints.Count == 0 || ChallengeMatchIDs.Count == 0 ||
            ChallengeDriverIDs.Count == 0 || RaidID <= 0 || UnlockSubMissionID <= 0 || MaxCarNumber <= 0)
            throw new InvalidDataException("B51Racing resources are incomplete.");
        foreach (var matchID in ChallengeMatchIDs)
            if (!Matches.TryGetValue(matchID, out var match) || match.TrackIDList.Count == 0 ||
                match.TrackIDList.Any(id => !Tracks.TryGetValue(id, out var track) || track.TotalLap <= 0 || string.IsNullOrEmpty(track.LittleGameConfig)))
                throw new InvalidDataException($"B51Racing challenge {matchID} has no valid track.");
        foreach (var (carID, challengeCar) in ChallengeCars)
            if (!Cars.TryGetValue(carID, out var car) || car.Type != "Player" ||
                car.PartIDList.Concat(challengeCar.PartIDList).Any(id => !Parts.ContainsKey(id)) ||
                car.PartIDList.Select(id => Parts[id].Type).Distinct().Count() != 3)
                throw new InvalidDataException($"B51Racing car {carID} has invalid parts.");
        if (ChallengeDriverIDs.Any(id => !Drivers.ContainsKey(id)))
            throw new InvalidDataException("B51Racing challenge drivers are missing.");
    }
}
