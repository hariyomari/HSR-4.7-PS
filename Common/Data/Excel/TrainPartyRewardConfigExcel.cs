using MemoryPack;
namespace March7thHoney.Data.Excel;

// Party-car build levels: reaching a level takes RequireStar more stars than the level before it, and taking the
// level hands out RewardID.
[ResourceEntity("TrainPartyRewardConfig.json")]
[MemoryPackable]
public partial class TrainPartyRewardConfigExcel : ExcelResource
{
    public int Level { get; set; }
    public int RequireStar { get; set; }
    public int RewardID { get; set; }

    public override int GetId()
    {
        return Level;
    }

    public override void Loaded()
    {
        GameData.TrainPartyRewardConfigData.Add(Level, this);
    }
}
