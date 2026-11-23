using March7thHoney.Data;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using MemoryPack;
using Newtonsoft.Json;

namespace March7thHoney.Data.Excel;

/// <summary>
///     One row of MainMissionSchedule.json: the activity module that owns a main mission's
///     availability window. A bound mission is takeable only while that module is scheduled.
/// </summary>
[ResourceEntity("MainMissionSchedule.json", OptionalFileNames = "MainMissionSchedule.json")]
[MemoryPackable]
public partial class MainMissionScheduleExcel : ExcelResource
{
    public int MainMissionID { get; set; }
    public int ActivityModuleID { get; set; }

    /// <summary>Progress survives the event ending; login cleanup must not delete the mission.</summary>
    public bool IsNotDelete { get; set; }

    public override int GetId()
    {
        return MainMissionID;
    }

    public override void Loaded()
    {
        if (MainMissionID <= 0) return;
        GameData.MainMissionScheduleData[MainMissionID] = this;
    }

    private static int _loadState;

    /// <summary>
    ///     Fills <see cref="GameData.MainMissionScheduleData" /> from JSON when an older resource cache
    ///     restored an empty table. A missing file leaves the table empty, which the accept scan treats
    ///     as "no bindings" (every mission follows its own gates).
    /// </summary>
    public static void EnsureLoaded()
    {
        if (Volatile.Read(ref _loadState) != 0) return;
        lock (GameData.MainMissionScheduleData)
        {
            if (_loadState != 0) return;
            if (GameData.MainMissionScheduleData.Count == 0)
                TryLoadFromDisk();
            _loadState = 1;
        }
    }

    private static void TryLoadFromDisk()
    {
        var file = ResourcePaths.FindExcel("MainMissionSchedule.json");
        if (file == null) return;

        try
        {
            var rows = JsonConvert.DeserializeObject<List<MainMissionScheduleExcel>>(File.ReadAllText(file.FullName));
            foreach (var row in rows ?? []) row.Loaded();
        }
        catch (Exception ex)
        {
            Logger.Warn(I18NManager.Translate("Server.Log.MissionScheduleUnavailable"), ex);
        }
    }

    private static readonly Logger Logger = new("MissionSchedule");
}
