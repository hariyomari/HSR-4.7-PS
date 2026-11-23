using MemoryPack;
using Newtonsoft.Json;
namespace March7thHoney.Data.Excel;

[ResourceEntity("ItemComposeConfig.json")]
[MemoryPackable]
public partial class ItemComposeConfigExcel : ExcelResource
{
    public int ID { get; set; }
    public int ItemID { get; set; }
    public int CoinCost { get; set; }
    public List<MaterialItem> MaterialCost { get; set; } = [];

    // Appended after the existing members so an older Resource.bin still deserializes (with empty defaults);
    // ResourceCache.ValidateLoadedCache rejects such a cache when the raw table is available.
    /// <summary>"Normal", "Sepcial" (sic, the client's spelling: pay with any of SpecialMaterialCost) or "SelectedRelic".</summary>
    public string FormulaType { get; set; } = "";

    /// <summary>Relics a SelectedRelic formula may produce.</summary>
    public List<int> RelicList { get; set; } = [];

    /// <summary>Items the player picks from to pay SpecialMaterialCostNumber per composed item.</summary>
    public List<int> SpecialMaterialCost { get; set; } = [];

    public int SpecialMaterialCostNumber { get; set; }
    public int MaxCount { get; set; }

    [JsonIgnore] public bool IsSelectedRelic => FormulaType == "SelectedRelic";

    public override int GetId()
    {
        return ID;
    }

    public override void Loaded()
    {
        GameData.ItemComposeConfigData[ID] = this;
    }
}

[MemoryPackable]
public partial class MaterialItem
{
    public int ItemID { get; set; }
    public int ItemNum { get; set; }
}
