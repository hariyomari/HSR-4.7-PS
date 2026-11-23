using MemoryPack;

namespace March7thHoney.Data.Excel;

[ResourceEntity("CityShopConfig.json")]
[MemoryPackable]
public partial class CityShopConfigExcel : ExcelResource
{
    public int ShopID { get; set; }
    public int RewardListGroupID { get; set; }
    public int ItemID { get; set; }
    public int MaxLevel { get; set; }
    public int WorldID { get; set; }

    public override int GetId() => ShopID;

    public override void Loaded()
    {
        GameData.CityShopConfigData[ShopID] = this;
    }
}
