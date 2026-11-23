using MemoryPack;
using March7thHoney.Data.Activity;

namespace March7thHoney.Data.Excel;

[ResourceEntity("ActivityPanel.json")]
[MemoryPackable]
public partial class MechCraftActivityPanelExcel : ExcelResource
{
    public int PanelID { get; set; }
    public string UIPrefab { get; set; } = string.Empty;

    public override int GetId() => PanelID;

    public override void AfterAllDone()
    {
        if (UIPrefab.EndsWith("/QuestMechCraftPanel.prefab", StringComparison.Ordinal))
            MechCraftResourceCatalog.CompleteResources(PanelID);
    }
}

[ResourceEntity("ActivityQuestRewardConfig.json")]
[MemoryPackable]
public partial class MechCraftRewardAssociationExcel : ExcelResource
{
    public int ActivityRewardID { get; set; }
    public List<int> QuestTabGroupList { get; set; } = [];
    public int FinalRewardQuest { get; set; }
    public int ActivityModule { get; set; }

    public override int GetId() => ActivityRewardID;

    public override void Loaded()
    {
        MechCraftResourceCatalog.RewardAssociations[ActivityRewardID] = this;
    }
}

[ResourceEntity("ActivityQuestRewardTab.json")]
[MemoryPackable]
public partial class MechCraftRewardGroupExcel : ExcelResource
{
    public int QuestTabGroupID { get; set; }
    public List<int> QuestTabList { get; set; } = [];

    public override int GetId() => QuestTabGroupID;

    public override void Loaded()
    {
        MechCraftResourceCatalog.RewardGroups[QuestTabGroupID] = this;
    }
}

[ResourceEntity("ActivityQuestRewardData.json")]
[MemoryPackable]
public partial class MechCraftRewardTabExcel : ExcelResource
{
    public int QuestTabID { get; set; }
    public List<int> QuestList { get; set; } = [];
    public int ActivityModuleID { get; set; }

    public override int GetId() => QuestTabID;

    public override void Loaded()
    {
        MechCraftResourceCatalog.RewardTabs[QuestTabID] = this;
    }
}
