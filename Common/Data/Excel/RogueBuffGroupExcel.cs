using MemoryPack;
using March7thHoney.Util;
using Newtonsoft.Json;

namespace March7thHoney.Data.Excel;

// 4.5 ships RogueBuffGroup with obfuscated keys (GroupID / buff tag or child group list).
[ResourceEntity("RogueBuffGroup.json")]
[MemoryPackable]
public partial class RogueBuffGroupExcel : BaseRogueBuffGroupExcel
{
    [JsonProperty("GMLOGNJAIGI")] public int GroupID { get; set; }
    [JsonProperty("HECJCAMDGNO")] public List<int> BuffTagList { get; set; } = [];

    public override int GetId()
    {
        return GroupID;
    }

    public override void Loaded()
    {
        GroupId = GroupID;
        GameData.RogueBuffGroupData[GetId()] = this;
        LoadBuff();
    }

    public override void AfterAllDone()
    {
        LoadBuff();
    }

    public void LoadBuff()
    {
        if (IsLoaded) return;
        var count = 0;
        List<RogueBuffExcel> buffs = [.. GameData.RogueBuffData.Values.OfType<RogueBuffExcel>()];
        foreach (var buffId in BuffTagList)
        {
            if (buffs.FirstOrDefault(x => x.RogueBuffTag == buffId) is { } buff)
            {
                BuffList.SafeAdd(buff);
                count++;
            }
            else
            {
                // might is group id
                if (!GameData.RogueBuffGroupData.TryGetValue(buffId, out var group)) continue;
                if (group is not RogueBuffGroupExcel e) continue;
                e.LoadBuff();
                BuffList.SafeAddRange(e.BuffList);
                // A child still missing rows (not loaded yet) must leave this group open for AfterAllDone.
                if (e.IsLoaded) count++;
            }
        }

        if (count == BuffTagList.Count) IsLoaded = true;
    }
}
