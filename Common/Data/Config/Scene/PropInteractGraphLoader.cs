using System.Collections.Concurrent;
using March7thHoney.Enums.Scene;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Config.Scene;

/// <summary>
///     Extracts the server-side PropReqInteract fan-out of a prop's InitLevelGraph and stores it on the
///     <see cref="PropInfo" /> (<see cref="PropInfo.InteractLinks" />) so the runtime never has to touch the raw
///     resource tree (the links ride along in Resource.bin).
/// </summary>
/// <remarks>
///     Graph shape handled, all observed in the dump:
///     <code>
///     PropStateExecute{TargetType, State?} → Execute[] (nested)
///       → PropReqInteract{TargetType, InteractID.FixedValue.Value}
///     </code>
///     wrapped optionally in PropSetupUITrigger.ButtonCallback / PropSetupTrigger.TaskList / SelectorConfig.TaskList.
///     Target forms: <c>SinglePropID.{GroupID,ID}.FixedValue.Value</c> (direct), <c>SinglePropKey.Key</c>
///     (resolved through the owner prop's ValueSource "group,inst" entry), or a bare TargetFetchAdvPropEx
///     (the enclosing block's own prop - the TreasureBox self-link that re-opens the chest UI; never a state
///     change, so it is dropped here). Unresolvable links or guards are dropped at load, which keeps the
///     runtime walk a pure lookup.
/// </remarks>
public static class PropInteractGraphLoader
{
    private static readonly ConcurrentDictionary<string, GraphParse> TemplateCache = new();

    public static void Populate(GroupInfo group, string resourcePath)
    {
        foreach (var prop in group.PropList)
        {
            if (string.IsNullOrEmpty(prop.InitLevelGraph)) continue;

            var parsed = TemplateCache.GetOrAdd(prop.InitLevelGraph, path => ParseGraph(resourcePath, path));
            prop.GraphHasOpenBranch = parsed.HasOpen;
            prop.GraphHasLockedBranch = parsed.HasLocked;
            prop.GraphHasRealClosedBranch = parsed.HasRealClosed;
            if (parsed.Links.Count == 0) continue;

            foreach (var template in parsed.Links)
            {
                if (!TryResolve(template.Target, prop, group.Id, out var groupId, out var instId)) continue;
                if (groupId == group.Id && instId == prop.ID) continue; // self-link: UI re-open, not a state change

                var link = new PropInteractLink
                {
                    GroupId = groupId,
                    InstId = instId,
                    InteractId = template.InteractId
                };

                var guardsOk = true;
                foreach (var guard in template.Guards)
                {
                    if (!TryResolve(guard.Target, prop, group.Id, out var guardGroup, out var guardInst))
                    {
                        guardsOk = false;
                        break;
                    }

                    link.Guards.Add(new PropInteractGuard
                    {
                        GroupId = guardGroup,
                        InstId = guardInst,
                        State = guard.State
                    });
                }

                if (guardsOk) prop.InteractLinks.Add(link);
            }
        }
    }

    private static bool TryResolve(TargetRef target, PropInfo owner, int ownerGroupId, out int groupId,
        out int instId)
    {
        groupId = 0;
        instId = 0;

        if (!string.IsNullOrEmpty(target.Key))
        {
            var value = owner.ValueSource?.Values.Find(x => x["Key"]?.ToString() == target.Key)?["Value"]
                ?.ToString();
            if (value == null) return false;
            var parts = value.Split(',');
            if (parts.Length < 2 || !int.TryParse(parts[0].Trim(), out groupId) ||
                !int.TryParse(parts[1].Trim(), out instId)) return false;
            return groupId != 0 && instId != 0;
        }

        if (target.InstId == 0) return false; // bare TargetFetchAdvPropEx → the block's own prop, no edge
        groupId = target.GroupId == 0 ? ownerGroupId : target.GroupId;
        instId = target.InstId;
        return true;
    }

    private static GraphParse ParseGraph(string resourcePath, string graphPath)
    {
        var result = new GraphParse();
        try
        {
            var file = new FileInfo(Path.Combine(resourcePath, graphPath));
            if (!file.Exists) return result;
            var graph = JObject.Parse(File.ReadAllText(file.FullName));
            foreach (var sequenceKey in new[] { "OnInitSequece", "OnStartSequece" })
            foreach (var sequence in graph[sequenceKey] as JArray ?? [])
                Walk(sequence["TaskList"] as JArray, [], result);
        }
        catch
        {
            // A malformed graph must not take the whole floor down - the prop simply has no fan-out.
        }

        return result;
    }

    private static void Walk(JArray? tasks, List<GuardTemplate> guards, GraphParse result)
    {
        if (tasks == null) return;
        foreach (var token in tasks)
        {
            if (token is not JObject task) continue;
            var type = (task["$type"]?.ToString() ?? task["Type"]?.ToString() ?? "").Replace("RPG.GameCore.", "");
            switch (type)
            {
                case "PropStateExecute":
                {
                    var nextGuards = guards;
                    var stateName = task["State"]?.ToString();
                    // An absent State is the zero enum (Closed). An unknown name is not a Closed branch.
                    if (string.IsNullOrEmpty(stateName) ||
                        stateName.Equals("Closed", StringComparison.OrdinalIgnoreCase))
                        result.HasRealClosed = true;
                    else if (stateName.Equals("Locked", StringComparison.OrdinalIgnoreCase))
                        result.HasLocked = true;
                    else if (stateName.Equals("Open", StringComparison.OrdinalIgnoreCase))
                        result.HasOpen = true;

                    if (!string.IsNullOrEmpty(stateName) && Enum.TryParse<PropStateEnum>(stateName, out var state))
                        nextGuards = [.. guards, new GuardTemplate(ReadTarget(task["TargetType"] as JObject), state)];
                    Walk(task["Execute"] as JArray, nextGuards, result);
                    break;
                }
                case "PropReqInteract":
                {
                    var interactId = task["InteractID"]?["FixedValue"]?["Value"]?.Value<int>() ?? 0;
                    if (interactId == 0) break;
                    result.Links.Add(new LinkTemplate(ReadTarget(task["TargetType"] as JObject), interactId, guards));
                    break;
                }
                case "PropSetupUITrigger":
                case "PropSetupTrigger":
                case "SelectorConfig":
                    Walk(task["ButtonCallback"] as JArray, guards, result);
                    Walk(task["TaskList"] as JArray, guards, result);
                    break;
            }
        }
    }

    private static TargetRef ReadTarget(JObject? targetType)
    {
        if (targetType == null) return new TargetRef(0, 0, "");
        var single = targetType["SinglePropID"];
        var groupId = single?["GroupID"]?["FixedValue"]?["Value"]?.Value<int>();
        var instId = single?["ID"]?["FixedValue"]?["Value"]?.Value<int>();
        if (groupId != null && instId != null) return new TargetRef(groupId.Value, instId.Value, "");
        var key = targetType["SinglePropKey"]?["Key"]?.ToString() ?? "";
        return new TargetRef(0, 0, key);
    }

    private sealed record TargetRef(int GroupId, int InstId, string Key);

    private sealed record GuardTemplate(TargetRef Target, PropStateEnum State);

    private sealed record LinkTemplate(TargetRef Target, int InteractId, List<GuardTemplate> Guards);

    private sealed class GraphParse
    {
        public List<LinkTemplate> Links { get; } = [];
        public bool HasOpen { get; set; }
        public bool HasLocked { get; set; }
        public bool HasRealClosed { get; set; }
    }
}
