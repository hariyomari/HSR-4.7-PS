using MemoryPack;

namespace March7thHoney.Data.Excel;

[ResourceEntity("CityShopRewardList.json")]
[MemoryPackable]
public partial class CityShopRewardListExcel : ExcelResource
{
    public int GroupID { get; set; }
    public int Level { get; set; }
    public int RewardID { get; set; }
    public int ItemNeed { get; set; }
    public int TotalItem { get; set; }

    public override int GetId() => GroupID * 1000 + Level;

    public override void Loaded()
    {
        if (!GameData.CityShopRewardListData.TryGetValue(GroupID, out var levels))
        {
            levels = [];
            GameData.CityShopRewardListData[GroupID] = levels;
        }

        levels[Level] = this;
    }
}
