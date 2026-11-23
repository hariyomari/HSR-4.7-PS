using MemoryPack;
namespace March7thHoney.Data.Excel;

[ResourceEntity("GridFightDivisionStage.json")]
[MemoryPackable]
public partial class GridFightDivisionStageExcel : ExcelResource
{
    public uint DivisionID { get; set; }
    public uint EnemyDifficultyLevel { get; set; }
    public List<uint> AffixChooseNumList { get; set; } = [];
    public uint ExpModify { get; set; }
    public uint SeasonID { get; set; }
    // Table key: entering an encounter stage looks up GridFightBinaryDiffAddRule by
    // (BinaryNodeDiffAddRule, encounter quality).
    public uint BinaryNodeDiffAddRule { get; set; }

    public override int GetId()
    {
        return (int)DivisionID;
    }

    public override void Loaded()
    {
        GameData.GridFightDivisionStageData.TryAdd(DivisionID, this);
    }
}
