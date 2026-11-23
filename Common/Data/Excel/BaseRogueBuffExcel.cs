using MemoryPack;
using March7thHoney.Enums.Rogue;
using March7thHoney.Proto;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace March7thHoney.Data.Excel;

// RogueBuffData mixes classic and Divergent Universe rows; the union keeps the subtype through Resource.bin,
// otherwise every cached row comes back as the base type and `is RogueBuffExcel` filters match nothing.
[MemoryPackable]
[MemoryPackUnion(0, typeof(RogueBuffExcel))]
[MemoryPackUnion(1, typeof(RogueTournBuffExcel))]
public abstract partial class BaseRogueBuffExcel : ExcelResource
{
    public int MazeBuffID { get; set; }
    public int MazeBuffLevel { get; set; }
    public int RogueBuffType { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public RogueBuffCategoryEnum RogueBuffCategory { get; set; }

    public int RogueBuffTag { get; set; }

    public override int GetId()
    {
        return MazeBuffID * 100 + MazeBuffLevel;
    }

    // TODO: the conversion to its protobuf message was removed with the 4.3 protocol, where that message
    // disappeared or was renamed; its current counterpart has not been identified.
}
