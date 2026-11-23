using MemoryPack;
namespace March7thHoney.Data.Excel;

[ResourceEntity("TrainPartyAreaConfig.json")]
[MemoryPackable]
public partial class TrainPartyAreaConfigExcel : ExcelResource
{
    public int ID { get; set; }
    public int RequireAreaID { get; set; }
    public int FirstStep { get; set; }

    // Append-only below (MemoryPack member order). Room blocks of the bar carriage, e.g. "Bar_2F_Bedroom"; the
    // block's room name also names the floor saved values Progress_<Room> and Build_<Room>_Finished.
    public List<string> ShowBlockList { get; set; } = [];

    // Stars the area adds once its progress reaches each threshold.
    public List<TrainPartyAreaProgressBonus> ProgressBonusList { get; set; } = [];

    // A cache written before the two lists existed reads them back as null.
    [MemoryPackOnDeserialized]
    private void FillMissingLists()
    {
        ShowBlockList ??= [];
        ProgressBonusList ??= [];
    }

    public override int GetId()
    {
        return ID;
    }

    public override void Loaded()
    {
        GameData.TrainPartyAreaConfigData.Add(ID, this);
    }
}

[MemoryPackable]
public partial class TrainPartyAreaProgressBonus
{
    public int Progress { get; set; }
    public int AddStar { get; set; }
}
