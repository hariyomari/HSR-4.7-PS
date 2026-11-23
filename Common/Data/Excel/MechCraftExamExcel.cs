using MemoryPack;
using March7thHoney.Enums.MechCraft;
using Newtonsoft.Json;

namespace March7thHoney.Data.Excel;

[ResourceEntity("MechCraftExam.json")]
[MemoryPackable]
public partial class MechCraftExamExcel : ExcelResource
{
    [JsonProperty("PHFMCACHFIJ")] public int ID { get; set; }
    [JsonProperty("CLGNIABAHII")] public int RequirementID { get; set; }
    [JsonProperty("KMLOGCDOMPG")] public int BoardWidth { get; set; }
    [JsonProperty("JNFHECCIGDG")] public int BoardHeight { get; set; }
    [JsonProperty("FGJFNACLHPB")] public int EndgamePlaceChipSchemeID { get; set; }
    [JsonProperty("NCIOIJHNLHD")] public int ExaminerActorID { get; set; }
    [JsonProperty("OENAMINOLLF")] public HashName Name { get; set; } = new();
    [JsonProperty("LCFNABHMODA")] public bool IsReturnToMaze { get; set; }
    [JsonProperty("KFNKBKPKNCC")] public HashName ExamDesc { get; set; } = new();
    [JsonProperty("IFHCECGLMHH")] public HashName ExamCmt { get; set; } = new();
    [JsonProperty("CCNENAKAHJE")] public int ReportID { get; set; }
    [JsonProperty("OPGAGANBICN")] public int DiyMechatronID { get; set; }
    [JsonProperty("KLNPCBJKDFI")] public List<MechCraftChipCount> AssistRecommendChips { get; set; } = [];

    public override int GetId() => ID;

    public override void Loaded()
    {
        GameData.MechCraftExamData.TryAdd(ID, this);
    }
}

[MemoryPackable]
public partial class MechCraftChipCount
{
    [JsonProperty("PFDHNIJBGKO")] public int ChipID { get; set; }
    [JsonProperty("HCDEOKNIGKG")] public int Count { get; set; }
}
