using MemoryPack;
using March7thHoney.Data.Activity;

namespace March7thHoney.Data.Excel;

[ResourceEntity("ActivityPanel.json")]
[MemoryPackable]
public partial class B51RacingActivityPanelExcel : ExcelResource
{
    public int PanelID { get; set; }
    public string UIPrefab { get; set; } = string.Empty;
    public string FinishConditions { get; set; } = string.Empty;
    public override int GetId() => PanelID;
    public override void AfterAllDone()
    {
        if (UIPrefab.EndsWith("/QuestB51RacingPanel.prefab", StringComparison.Ordinal))
            GameData.B51RacingData.CompleteResources(this);
    }
}

[ResourceEntity("ActivityQuestRewardConfig.json")]
[MemoryPackable]
public partial class B51RacingRewardAssociationExcel : ExcelResource
{
    public int ActivityRewardID { get; set; }
    public List<int> QuestTabGroupList { get; set; } = [];
    public int FinalRewardQuest { get; set; }
    public int ActivityModule { get; set; }
    public override int GetId() => ActivityRewardID;
    public override void Loaded() => B51RacingResourceCatalog.RewardAssociations[ActivityRewardID] = this;
}

[ResourceEntity("ActivityQuestRewardTab.json")]
[MemoryPackable]
public partial class B51RacingRewardGroupExcel : ExcelResource
{
    public int QuestTabGroupID { get; set; }
    public List<int> QuestTabList { get; set; } = [];
    public override int GetId() => QuestTabGroupID;
    public override void Loaded() => B51RacingResourceCatalog.RewardGroups[QuestTabGroupID] = this;
}

[ResourceEntity("ActivityQuestRewardData.json")]
[MemoryPackable]
public partial class B51RacingRewardTabExcel : ExcelResource
{
    public int QuestTabID { get; set; }
    public List<int> QuestList { get; set; } = [];
    public int ActivityModuleID { get; set; }
    public override int GetId() => QuestTabID;
    public override void Loaded() => B51RacingResourceCatalog.RewardTabs[QuestTabID] = this;
}

[ResourceEntity("FinishWay.json")]
[MemoryPackable]
public partial class B51RacingFinishWayExcel : ExcelResource
{
    public int ID { get; set; }
    public string FinishType { get; set; } = string.Empty;
    public override int GetId() => ID;
    public override void Loaded()
    {
        if (FinishType.StartsWith("B51Racing", StringComparison.Ordinal))
            B51RacingResourceCatalog.ActivityFinishWayIDs.Add(ID);
    }
}
