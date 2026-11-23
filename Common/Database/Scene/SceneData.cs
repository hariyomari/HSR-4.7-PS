using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Enums.Scene;
using March7thHoney.Proto;
using March7thHoney.Util;
using Google.Protobuf;

namespace March7thHoney.Database.Scene;

[DbTable("Scene")]
public class SceneData : BaseDatabaseDataHelper
{
    public Dictionary<int, Dictionary<int, List<ScenePropData>>> ScenePropData { get; set; } =
        []; // Dictionary<FloorId, Dictionary<GroupId, ScenePropData>>

    public Dictionary<int, List<int>> UnlockSectionIdList { get; set; } = []; // Dictionary<FloorId, List<SectionId>>

    public Dictionary<int, Dictionary<int, string>> CustomSaveData { get; set; } =
        []; // Dictionary<EntryId, Dictionary<GroupId, SaveData>>

    public Dictionary<int, Dictionary<string, int>> FloorSavedData { get; set; } =
        []; // Dictionary<FloorId, Dictionary<SaveDataKey, SaveDataValue>>

    public Dictionary<int, Dictionary<int, Dictionary<int, ScenePropTimelineData>>> PropTimelineData { get; set; } =
        []; // Dictionary<FloorId, Dictionary<GroupId, Dictionary<PropId, ScenePropTimelineData>>>

    public Dictionary<int, List<SceneMarkedChestData>> MarkedChestData { get; set; } =
        []; // Dictionary<FuncId, List<ScenePropTimelineData>>

    public Dictionary<int, Dictionary<int, Dictionary<string, int>>> GroupPropertyData { get; set; } =
        []; // Dictionary<FloorId, Dictionary<GroupId, Dictionary<Key, Value>>>

    public SceneEraFlipperData EraFlipperData { get; set; } = new();

    public SceneRotatableRegionData RotatableRegionData { get; set; } = new();

    public Dictionary<int, int> FloorTargetPuzzleGroupData { get; set; } = new();

    public Dictionary<int, SwitchHandInfo> SwitchHandData { get; set; } = new();

    /// <summary>
    ///     Teleport waypoints (MappingInfoID) the player has actually reached, per floor - the official
    ///     unlock_teleport_list. Written by EntityProp.SetState(CheckPointEnable) and
    ///     SceneInstance.TryUnlockNearbyWaypoint; independent of the group SaveType so Reset-group checkpoints
    ///     stay unlocked too. Appended for persisted-data compatibility.
    /// </summary>
    public Dictionary<int, List<int>> UnlockedTeleportData { get; set; } = []; // Dictionary<FloorId, List<MappingInfoID>>

    /// <summary>
    ///     Floors the player has actually entered, per plane - the official entered_scene_info_list the map's region
    ///     unlock reads. Written by PlayerInstance.LoadScene. Appended for persisted-data compatibility.
    /// </summary>
    public Dictionary<int, List<int>> EnteredSceneData { get; set; } = []; // Dictionary<PlaneId, List<FloorId>>

    /// <summary>
    ///     Overworld monsters this player has killed. Ordinary marks expire at the daily reset; permanent marks
    ///     (chest guardians, trotters, mission fights) do not. Appended as its own column.
    /// </summary>
    public List<DefeatedMonsterRecord> DefeatedMonsters { get; set; } = [];

    public int GetFloorSavedValue(int floorId, string key)
    {
        if (FloorSavedData.TryGetValue(floorId, out var data) && data.TryGetValue(key, out var value)) return value;

        // get default value if not found
        var floor = GameData.GetFloorInfo(floorId);
        if (floor == null) return 0;

        var savedValue = floor.FloorSavedValue.FirstOrDefault(x => x.Name == key);
        return savedValue?.DefaultValue ?? 0;
    }

    public Dictionary<string, int> GetFloorSavedValues(int floorId)
    {
        var floor = GameData.GetFloorInfo(floorId);
        if (floor == null) return [];

        var savedValues = new Dictionary<string, int>();
        foreach (var value in floor.FloorSavedValue) savedValues[value.Name] = GetFloorSavedValue(floorId, value.Name);

        return savedValues;
    }
}

[MemoryPackable]
public partial class SwitchHandInfo
{
    public int ConfigId { get; set; }
    public int CoinNum { get; set; }
    public Position Pos { get; set; } = new();
    public Position Rot { get; set; } = new();
    public uint State { get; set; } = 101;
    public byte[] ByteValue { get; set; } = [];

    // TODO: the conversion to its protobuf message was removed with the 4.3 protocol, where that message
    // disappeared or was renamed; its current counterpart has not been identified.
}

[MemoryPackable]
public partial class ScenePropData
{
    public int PropId { get; set; }
    public PropStateEnum State { get; set; }
}

[MemoryPackable]
public partial class SceneEraFlipperData
{
    public int CurRegionId { get; set; }
    public Dictionary<int, int> RegionState { get; set; } = []; // Dictionary<RegionId, State>
}

[MemoryPackable]
public partial class SceneRotatableRegionData
{
    public int CurRegionId { get; set; }
    public int Energy { get; set; }
    public int MaxEnergy { get; set; }
    public int RotateValue { get; set; }
}

[MemoryPackable]
public partial class ScenePropTimelineData
{
    public bool BoolValue { get; set; }
    public string ByteValue { get; set; } = ""; // Base64

    public PropTimelineInfo ToProto()
    {
        return new PropTimelineInfo
        {
            TimelineBoolValue = BoolValue,
            TimelineByteValue = ByteString.FromBase64(ByteValue)
        };
    }
}

[MemoryPackable]
public partial class DefeatedMonsterRecord
{
    public int FloorId { get; set; }
    public int DimensionId { get; set; }
    public int GroupId { get; set; }
    public int InstId { get; set; }
    public bool Permanent { get; set; }
    public int DayKey { get; set; }
}

[MemoryPackable]
public partial class SceneMarkedChestData
{
    public int ConfigId { get; set; }
    public int GroupId { get; set; }
    public int FloorId { get; set; }
    public int PlaneId { get; set; }

    public MarkChestInfo ToProto()
    {
        return new MarkChestInfo
        {
            ConfigId = (uint)ConfigId,
            FloorId = (uint)FloorId,
            GroupId = (uint)GroupId,
            PlaneId = (uint)PlaneId
        };
    }
}
