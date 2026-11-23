using MemoryPack;
using Newtonsoft.Json;

namespace March7thHoney.Data.Excel;

// Like BaseRogueBuffExcel, the union keeps classic vs Divergent Universe groups apart in Resource.bin.
[MemoryPackable]
[MemoryPackUnion(0, typeof(RogueBuffGroupExcel))]
[MemoryPackUnion(1, typeof(RogueTournBuffGroupExcel))]
public abstract partial class BaseRogueBuffGroupExcel : ExcelResource
{
    public int GroupId { get; set; }
    [JsonIgnore] public List<BaseRogueBuffExcel> BuffList { get; set; } = [];
    [JsonIgnore] public bool IsLoaded { get; set; }

    public override int GetId()
    {
        return GroupId;
    }
}
