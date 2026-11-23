using MemoryPack;
using March7thHoney.Data.Config.Task;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Config.SummonUnit;

[MemoryPackable]
public partial class SummonUnitConfigInfo
{
    public string GroupConfigName { get; set; } = "";
    public string ConfigEntityPath { get; set; } = "";

    public string TickLodTemplateName { get; set; } = "";

    public string ShoesType { get; set; } = "";
    public bool ShowShadow { get; set; }
    public bool ColliderIsTrigger { get; set; }

    public string AttachPoint { get; set; } = "";

    public DynamicFloat Duration { get; set; } = new();

    public SummonUnitTriggerConfigInfo TriggerConfig { get; set; } = new();

    // Lifecycle tasks, appended after TriggerConfig for MemoryPack; caches built before them restore null.
    public List<TaskConfigInfo>? OnCreate { get; set; } = [];
    public List<TaskConfigInfo>? OnDestroy { get; set; } = [];


    public static SummonUnitConfigInfo LoadFromJsonObject(JObject obj)
    {
        SummonUnitConfigInfo info = new();
        if (obj.TryGetValue(nameof(GroupConfigName), out var value))
            info.GroupConfigName = value.ToString();
        if (obj.TryGetValue(nameof(ConfigEntityPath), out value))
            info.ConfigEntityPath = value.ToString();
        if (obj.TryGetValue(nameof(TickLodTemplateName), out value))
            info.TickLodTemplateName = value.ToString();
        if (obj.TryGetValue(nameof(ShoesType), out value))
            info.ShoesType = value.ToString();
        if (obj.TryGetValue(nameof(ShowShadow), out value))
            info.ShowShadow = value.Value<bool>();
        if (obj.TryGetValue(nameof(ColliderIsTrigger), out value))
            info.ColliderIsTrigger = value.Value<bool>();
        if (obj.TryGetValue(nameof(AttachPoint), out value))
            info.AttachPoint = value.ToString();
        if (obj.TryGetValue(nameof(Duration), out value))
            info.Duration = value.ToObject<DynamicFloat>() ?? new DynamicFloat();

        if (obj.TryGetValue(nameof(TriggerConfig), out value))
            info.TriggerConfig = SummonUnitTriggerConfigInfo.LoadFromJsonObject((value as JObject)!);
        if (obj.TryGetValue(nameof(OnCreate), out value) && value is JArray onCreate)
            info.OnCreate = onCreate.OfType<JObject>().Select(TaskConfigInfo.LoadFromJsonObject).ToList();
        if (obj.TryGetValue(nameof(OnDestroy), out value) && value is JArray onDestroy)
            info.OnDestroy = onDestroy.OfType<JObject>().Select(TaskConfigInfo.LoadFromJsonObject).ToList();
        return info;
    }
}
