using MemoryPack;
using March7thHoney.Enums.GridFight;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace March7thHoney.Data.Excel;

[ResourceEntity("GridFightEnhance.json")]
[MemoryPackable]
public partial class GridFightEnhanceExcel : ExcelResource
{
    public uint ID { get; set; }
    public uint GroupID { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public GridFightEnhanceConditionEnum SelectCondition { get; set; }

    public List<GridFightRatioValue> EffectParamList { get; set; } = [];

    /// <summary>The equipment ID the option grants; for a cost-up option, the target role ID.</summary>
    public uint FirstEffectParam => (uint)Math.Max(0, EffectParamList.FirstOrDefault()?.Value ?? 0);

    public override int GetId()
    {
        return (int)ID;
    }

    public override void Loaded()
    {
        GameData.GridFightEnhanceData.TryAdd(ID, this);
    }
}
