using MemoryPack;
namespace March7thHoney.Data.Excel;

[ResourceEntity("ContentPackageConfig.json")]
[MemoryPackable]
public partial class ContentPackageConfigExcel : ExcelResource
{
    public int ContentID { get; set; }
    public List<int> MainMissionIDList { get; set; } = [];
    public int ActivityModuleID { get; set; }
    public int InitEntranceID { get; set; }
    public int AfterGuideEntranceID { get; set; }

    public override int GetId()
    {
        return ContentID;
    }

    public override void Loaded()
    {
        GameData.ContentPackageConfigData.Add(ContentID, this);
    }

    public bool TouchesFloor(int floorId)
    {
        foreach (var entranceId in new[] { InitEntranceID, AfterGuideEntranceID })
        {
            if (entranceId == 0) continue;
            if (!GameData.MapEntranceData.TryGetValue(entranceId, out var entrance)) continue;
            if (entrance.FloorID == floorId) return true;
        }

        return false;
    }

    /// <summary>
    ///     Finds the group that holds this activity's standing entrance: the start group of the after-guide
    ///     entrance, when one of the package's own missions owns it. An activity with a guide moves its entrance
    ///     from InitEntranceID to AfterGuideEntranceID and parks the entrance NPC, together with its map icon, in
    ///     a group owned by a hidden main mission that stays in progress for as long as the activity runs
    ///     (150137: entrance 2058105 is floor 20581001 group 159, owned by 8048209, whose NPC carries the
    ///     ACTIVITY_ENTRANCE icon 2589). A package whose two entrances are the same has no such group: its
    ///     start group belongs to the story mission that opens the activity.
    /// </summary>
    public bool TryGetStandingEntrance(out int floorId, out int groupId, out int ownerMainMissionId)
    {
        floorId = groupId = ownerMainMissionId = 0;
        if (ActivityModuleID == 0 || AfterGuideEntranceID == 0 || AfterGuideEntranceID == InitEntranceID)
            return false;
        if (!GameData.MapEntranceData.TryGetValue(AfterGuideEntranceID, out var entrance)) return false;

        GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floorInfo);
        if (floorInfo == null || !floorInfo.Groups.TryGetValue(entrance.StartGroupID, out var group)) return false;
        if (group.OwnerMainMissionID == 0 || !MainMissionIDList.Contains(group.OwnerMainMissionID)) return false;

        floorId = entrance.FloorID;
        groupId = group.Id;
        ownerMainMissionId = group.OwnerMainMissionID;
        return true;
    }
}
