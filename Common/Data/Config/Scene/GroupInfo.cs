using MemoryPack;
using March7thHoney.Data.Config.Task;
using March7thHoney.Database.Quests;
using March7thHoney.Enums;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.Enums.Task;
using March7thHoney.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace March7thHoney.Data.Config.Scene;

[MemoryPackable]
public partial class GroupInfo
{
    public int Id;

    [JsonConverter(typeof(StringEnumConverter))]
    public GroupLoadSideEnum LoadSide { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public GroupCategoryEnum Category { get; set; }

    public LevelGroupSystemUnlockCondition? SystemUnlockCondition { get; set; } = null;
    public string LevelGraph { get; set; } = "";
    public bool LoadOnInitial { get; set; }
    public string GroupName { get; set; } = "";
    public SavedValueLoadCondition SavedValueCondition { get; set; } = new();
    public AtmosphereCondition AtmosphereCondition { get; set; } = new();
    public LoadCondition LoadCondition { get; set; } = new();
    public LoadCondition UnloadCondition { get; set; } = new();
    public LoadCondition ForceUnloadCondition { get; set; } = new();

    [JsonConverter(typeof(StringEnumConverter))]
    public SaveTypeEnum SaveType { get; set; } = SaveTypeEnum.Save;

    public int OwnerMainMissionID { get; set; }
    public List<AnchorInfo> AnchorList { get; set; } = [];
    public List<MonsterInfo> MonsterList { get; set; } = [];
    public List<PropInfo> PropList { get; set; } = [];
    public List<NpcInfo> NPCList { get; set; } = [];
    public Dictionary<int, GroupPropertyConfigInfo> GroupPropertyMap { get; set; } = [];

    // Raw JSON consumed at load time by Load(); not cached (JObject isn't MemoryPack-serializable).
    [MemoryPackIgnore] public ValueSourceInfo? ValueSource { get; set; }

    [JsonIgnore] public LevelGraphConfigInfo? LevelGraphConfig { get; set; }

    [JsonIgnore] public Dictionary<string, List<int>> PropTriggerCustomString { get; set; } = [];
    [JsonIgnore] public List<string> ControlFloorSavedValue { get; set; } = [];
    [JsonIgnore] public List<int> RelatedBattleId { get; set; } = [];
    [JsonIgnore] public List<int> RelatedMissionId { get; set; } = [];

    // Append-only for MemoryPack compatibility. Null identifies caches created before this
    // resource flag was retained; raw resources normalize an omitted flag to false in Load().
    public bool? IsHoyoGroup { get; set; }

    /// <summary>
    ///     Authored refresh binding. A non-zero id is a loading mechanism, so the group is neither an
    ///     orphaned mission group nor an inert server group. Null identifies caches created before
    ///     this was retained; those groups are never classified as inert.
    /// </summary>
    public int? GroupRefreshID { get; set; }

    /// <summary>
    ///     Hidden chest a pad-puzzle group's ValueSource "Chest" names, stored only when the group's level
    ///     graph actually runs ChangePropState. Zero means there is nothing to reveal.
    /// </summary>
    [JsonIgnore] public int ChestRevealGroupId { get; set; }

    [JsonIgnore] public int ChestRevealInstId { get; set; }

    public void Load()
    {
        IsHoyoGroup ??= false;
        GroupRefreshID ??= 0;
        foreach (var prop in PropList) prop.Load(this);

        foreach (var source in ValueSource?.Values ?? [])
        {
            var key = source["Key"]?.ToString();
            if (key == "FSV")
            {
                var value = source["Value"];
                if (value != null)
                    ControlFloorSavedValue.Add(value.ToString());
            }
            else if (key == "Chest" && ChestRevealGroupId == 0)
            {
                var parts = source["Value"]?.ToString().Split(',');
                if (parts is { Length: 2 } &&
                    int.TryParse(parts[0].Trim(), out var chestGroup) &&
                    int.TryParse(parts[1].Trim(), out var chestInst) &&
                    chestGroup > 0 && chestInst > 0 &&
                    GraphContainsChangePropState(LevelGraphConfig))
                {
                    ChestRevealGroupId = chestGroup;
                    ChestRevealInstId = chestInst;
                }
            }
        }

        foreach (var info in LevelGraphConfig?.OnInitSequece ?? [])
        foreach (var configInfo in info.TaskList)
            if (configInfo is TriggerBattle battle)
                if (battle.EventID.GetValue() > 0)
                    RelatedBattleId.Add(battle.EventID.GetValue());

        foreach (var info in LevelGraphConfig?.OnStartSequece ?? [])
        foreach (var configInfo in info.TaskList)
            if (configInfo is TriggerBattle battle)
                if (battle.EventID.GetValue() > 0)
                    RelatedBattleId.Add(battle.EventID.GetValue());

        if (LoadSide != GroupLoadSideEnum.Client) return;
        foreach (var info in AtmosphereCondition.Conditions)
            if (info.TryGetValue("SubMissionID", out var value) && value is long v)
            {
                // try cast to int
                var missionId = (int)v;
                RelatedMissionId.Add(missionId);
            }
    }

    /// <summary>
    ///     The group's only shippable content is props whose overridden level graph lives under
    ///     Config/Level/DesignerBackup. Registering the group makes the client instantiate that prop and
    ///     abort scene entry. Any NPC, monster, or ordinary prop keeps the group.
    /// </summary>
    public bool OnlyEditorBackupProps()
    {
        if (NPCList.Any(npc => !npc.IsDelete && !npc.IsClientOnly && npc.LoadOnInitial)) return false;
        if (MonsterList.Any(monster => !monster.IsDelete && !monster.IsClientOnly)) return false;

        var props = PropList.Where(ShippableProp).ToList();
        if (props.Count == 0) return false;
        return props.All(prop => prop.HasEditorOnlyLevelGraph());
    }

    /// <summary>
    ///     Server mission content with no load mechanism of its own. Shipping it arms leftover interact
    ///     buttons whose performances target props the scene no longer has.
    /// </summary>
    public bool OrphanedMissionGroup()
    {
        return InertServerGroup() && HasMissionGraphProps();
    }

    /// <summary>
    ///     Server group the official engine never loads: LoadOnInitial is absent and no gate can admit it
    ///     later. Callers apply this on raid planes and the Simulated Universe lobby only.
    /// </summary>
    public bool InertServerGroup()
    {
        if (LoadSide != GroupLoadSideEnum.Server || LoadOnInitial || OwnerMainMissionID != 0 ||
            GroupRefreshID is null or not 0)
            return false;
        if (LoadCondition.Conditions.Count > 0 || UnloadCondition.Conditions.Count > 0 ||
            ForceUnloadCondition.Conditions.Count > 0 || SavedValueCondition.Conditions.Count > 0 ||
            (SystemUnlockCondition?.Conditions.Count ?? 0) > 0)
            return false;
        return true;
    }

    /// <summary>
    ///     Mission fight rather than an ordinary overworld encounter: the kill stays dead across the daily reset.
    /// </summary>
    public bool IsMissionFight()
    {
        if (OwnerMainMissionID != 0 || Category == GroupCategoryEnum.Mission) return true;
        if (string.Equals(GroupName.Trim(), "SubMission", StringComparison.OrdinalIgnoreCase)) return true;
        return LevelGraph.Replace('\\', '/').EndsWith("/Maze_Group_OpenTreasure_WhenMonsterDie.json",
            StringComparison.Ordinal);
    }

    public bool OpensTreasureOnMonsterDie()
    {
        if (LevelGraph.Contains("Maze_Group_OpenTreasure_WhenMonsterDie", StringComparison.Ordinal)) return true;
        return PropList.Any(prop =>
            prop.InitLevelGraph?.Contains("Maze_GroupProp_OpenTreasure_WhenMonsterDie", StringComparison.Ordinal) ==
            true);
    }

    private bool HasMissionGraphProps()
    {
        return PropList.Where(ShippableProp).Any(prop =>
            prop.InitLevelGraph?.StartsWith("Config/Level/Mission/", StringComparison.Ordinal) == true);
    }

    private static bool ShippableProp(PropInfo prop)
    {
        if (prop.IsDelete && prop.MappingInfoID != 0) return prop.LoadOnInitial;
        return !prop.IsDelete && prop.LoadOnInitial;
    }

    private static bool GraphContainsChangePropState(LevelGraphConfigInfo? graph)
    {
        if (graph == null) return false;
        return graph.OnInitSequece.SelectMany(sequence => sequence.TaskList)
            .Concat(graph.OnStartSequece.SelectMany(sequence => sequence.TaskList))
            .Any(TaskContainsChangePropState);
    }

    private static bool TaskContainsChangePropState(TaskConfigInfo task)
    {
        if (task.Type.Contains("ChangePropState", StringComparison.Ordinal)) return true;
        return task switch
        {
            PropStateExecute execute => execute.Execute.Any(TaskContainsChangePropState),
            PropSetupUITrigger trigger => trigger.ButtonCallback.Any(TaskContainsChangePropState),
            PredicateTaskList predicate => predicate.SuccessTaskList.Concat(predicate.FailedTaskList)
                .Any(TaskContainsChangePropState),
            _ => false
        };
    }
}

[MemoryPackable]
public partial class AtmosphereCondition
{
    public List<Dictionary<string, object>> Conditions { get; set; } = [];

    [JsonConverter(typeof(StringEnumConverter))]
    public OperationEnum Operation { get; set; } = OperationEnum.And;
}

[MemoryPackable]
public partial class LoadCondition
{
    public List<Condition> Conditions { get; set; } = [];

    [JsonConverter(typeof(StringEnumConverter))]
    public OperationEnum Operation { get; set; } = OperationEnum.And;

    /// <summary>
    ///     Official deferral marker on an unload condition: once it turns true the group must stay in the
    ///     scene the player is currently in and only disappear on the next level load. Ignored for load
    ///     conditions (a load is always evaluated at build time anyway).
    /// </summary>
    public bool DelayToLevelReload { get; set; }

    public bool IsTrue(MissionData mission, bool defaultResult = true)
    {
        if (Conditions.Count == 0) return defaultResult;

        // check load condition
        List<Func<bool>> conditionChecks = [];

        foreach (var condition in Conditions)
            if (condition.Type == LevelGroupMissionTypeEnum.MainMission)
            {
                var status = mission.GetMainMissionStatus(condition.ID);
                if (!mission.MissionEnabled) status = MissionPhaseEnum.Finish;

                condition.Phase = condition.Phase == MissionPhaseEnum.Cancel
                    ? MissionPhaseEnum.Finish
                    : condition.Phase;

                conditionChecks.Add(CheckFunc);
                continue;

                bool CheckFunc()
                {
                    return status == condition.Phase;
                }
            }
            else
            {
                // sub mission
                var status = mission.GetSubMissionStatus(condition.ID);
                if (!mission.MissionEnabled) status = MissionPhaseEnum.Finish;
                condition.Phase = condition.Phase == MissionPhaseEnum.Cancel
                    ? MissionPhaseEnum.Finish
                    : condition.Phase;

                conditionChecks.Add(CheckFunc);
                continue;

                bool CheckFunc()
                {
                    return status == condition.Phase;
                }
            }

        return Operation switch
        {
            OperationEnum.And => GroupConditionLogic.CheckAnd(conditionChecks, defaultResult),
            OperationEnum.Or => GroupConditionLogic.CheckOr(conditionChecks, defaultResult),
            OperationEnum.Not => !GroupConditionLogic.CheckOr(conditionChecks, defaultResult),
            _ => defaultResult
        };
    }
}

/// <summary>Boolean combinators and comparisons shared by the group load and saved-value conditions.</summary>
file static class GroupConditionLogic
{
    /// <summary>True when every check passes; a check that throws is skipped.</summary>
    public static bool CheckAnd(List<Func<bool>> actions, bool defaultValue = true)
    {
        if (actions.Count == 0) return defaultValue;

        foreach (var action in actions)
            try
            {
                var returnValue = action.Invoke();
                if (!returnValue) return false;
            }
            catch
            {
                // A check that throws counts as neither passing nor failing.
            }

        return true;
    }

    /// <summary>True when any check passes; a check that throws is skipped.</summary>
    public static bool CheckOr(List<Func<bool>> actions, bool defaultValue = false)
    {
        if (actions.Count == 0) return defaultValue;
        foreach (var action in actions)
            try
            {
                var returnValue = action.Invoke();
                if (returnValue) return true;
            }
            catch
            {
                // A check that throws counts as neither passing nor failing.
            }

        return false;
    }

    public static bool CompareNumberByOperationEnum(int left, int right, CompareTypeEnum operation)
    {
        return operation switch
        {
            CompareTypeEnum.Greater => left > right,
            CompareTypeEnum.GreaterEqual => left >= right,
            CompareTypeEnum.NotEqual => left != right,
            CompareTypeEnum.Equal => left == right,
            CompareTypeEnum.LessEqual => left <= right,
            CompareTypeEnum.Less => left < right,
            _ => false
        };
    }
}

[MemoryPackable]
public partial class SavedValueLoadCondition
{
    public List<SavedValueCondition> Conditions { get; set; } = [];

    [JsonConverter(typeof(StringEnumConverter))]
    public OperationEnum Operation { get; set; } = OperationEnum.And;

    public bool IsTrue(Dictionary<string, int> savedValue, bool defaultResult = true)
    {
        if (Conditions.Count == 0) return defaultResult;

        bool Matches(SavedValueCondition condition)
        {
            var status = savedValue.GetValueOrDefault(condition.SavedValueName, 0);
            // Runtime exports omit the comparator for equality. Existing resource caches retain
            // that omission as Unknow, so normalize it here without changing the cache format.
            var comparison = condition.Operation == CompareTypeEnum.Unknow
                ? CompareTypeEnum.Equal
                : condition.Operation;
            return GroupConditionLogic.CompareNumberByOperationEnum(status, condition.Value, comparison);
        }

        // defaultResult only applies to an empty condition list, not to a failed comparison.
        return Operation switch
        {
            OperationEnum.And => Conditions.All(Matches),
            OperationEnum.Or => Conditions.Any(Matches),
            OperationEnum.Not => !Conditions.Any(Matches),
            _ => false
        };
    }
}

[MemoryPackable]
public partial class Condition
{
    [JsonConverter(typeof(StringEnumConverter))]
    public LevelGroupMissionTypeEnum Type { get; set; } = LevelGroupMissionTypeEnum.MainMission;

    public int ID { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public MissionPhaseEnum Phase { get; set; } = MissionPhaseEnum.Accept;
}

[MemoryPackable]
public partial class SavedValueCondition
{
    public string SavedValueName { get; set; } = "";

    public int Value { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public CompareTypeEnum Operation { get; set; } = CompareTypeEnum.Unknow;
}

[MemoryPackable]
public partial class LevelGroupSystemUnlockCondition
{
    public List<int> Conditions { get; set; } = [];

    [JsonConverter(typeof(StringEnumConverter))]
    public OperationEnum Operation { get; set; }
}

[MemoryPackable]
public partial class GroupPropertyConfigInfo
{
    public int ID { get; set; }
    public string Name { get; set; } = "";

    [JsonConverter(typeof(StringEnumConverter))]
    public GroupPropertySideEnum Side { get; set; }

    public short DefaultValue { get; set; }
    public short MaxValue { get; set; }
    public short MinValue { get; set; }
}
