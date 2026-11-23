using MemoryPack;
using March7thHoney.Enums.MechCraft;
using Newtonsoft.Json;

namespace March7thHoney.Data.Excel;

[ResourceEntity("MechCraftChip.json")]
[MemoryPackable]
public partial class MechCraftChipExcel : ExcelResource
{
    [JsonProperty("PHFMCACHFIJ")] public int ID { get; set; }
    [JsonProperty("OENAMINOLLF")] public HashName Name { get; set; } = new();
    [JsonProperty("PMIEAEGJNMJ")] public int Rarity { get; set; }
    [JsonProperty("NFHLAIMKFPI")] public string ShapeID { get; set; } = string.Empty;
    [JsonProperty("GJBOKLDCKEG")] public List<MechCraftCell> Cells { get; set; } = [];
    [JsonProperty("KOGHELCCHJC")] public Dictionary<MechCraftAttributeType, int> Attributes { get; set; } = [];
    [JsonProperty("FBFCPNADPKB")] public MechCraftChipTraitType TraitType { get; set; }
    [JsonProperty("FODGHIDJAPP")] public List<int> EffectIDList { get; set; } = [];

    public override int GetId() => ID;

    public override void Loaded()
    {
        GameData.MechCraftChipData.TryAdd(ID, this);
    }
}

[MemoryPackable]
public partial class MechCraftCell
{
    [JsonProperty("GAOAMKBNDPN")] public int X { get; set; }
    [JsonProperty("EBJKJAGJGPK")] public int Y { get; set; }
}
