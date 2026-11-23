using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using March7thHoney.Data.Config;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Config.Task;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.Enums.Task;
using March7thHoney.GameServer.Game.Avatar;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.LevelTasks;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
public class LevelTask(PlayerInstance player)
{
    public PlayerInstance Player { get; } = player;

    #region Prop Target

    public async ValueTask<object?> TargetFetchAdvPropEx(TargetEvaluator act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        await ValueTask.CompletedTask;

        if (act is TargetFetchAdvPropEx fetch)
        {
            if (fetch.FetchType != TargetFetchAdvPropFetchTypeEnum.SinglePropByPropID) return null;
            foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                if (entity is EntityProp prop && prop.GroupId == fetch.SinglePropID.GroupID.GetValue() &&
                    prop.InstId == fetch.SinglePropID.ID.GetValue())
                    return prop;
        }

        return null;
    }

    #endregion

    #region Manage

    public void TriggerInitAct(LevelInitSequeceConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        foreach (var task in act.TaskList) TriggerTask(task, subMission, group);
    }

    public void TriggerStartAct(LevelStartSequeceConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        foreach (var task in act.TaskList) TriggerTask(task, subMission, group);
    }

    private void TriggerTask(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        try
        {
            var methodName = act.Type.Replace("RPG.GameCore.", "");

            var method = GetOrCreateExecuteTask(methodName);
            if (method != null) _ = method(act, subMission, group);
        }
        catch
        {
        }
    }

    private ExecuteTask? GetOrCreateExecuteTask(string methodName)
    {
        // try to get from cache
        if (_cachedTasks.TryGetValue(methodName, out var method)) return method;
        var methodProp = GetType().GetMethod(methodName);
        if (methodProp == null) return null;

        method = (ExecuteTask)Delegate.CreateDelegate(typeof(ExecuteTask), this, methodProp);
        _cachedTasks[methodName] = method; // cached

        return method;
    }

    private delegate ValueTask<object?> ExecuteTask(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null);

    private readonly ConcurrentDictionary<string, ExecuteTask> _cachedTasks = [];

    #endregion

    #region Task

    public async ValueTask<object?> PlayMessage(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        if (act is PlayMessage message) await Player.MessageManager!.AddMessageSection(message.MessageSectionID);

        return null;
    }

    public async ValueTask<object?> DestroyProp(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        if (act is DestroyProp destroyProp)
            foreach (var entity in Player.SceneInstance!.Entities.Values)
                if (entity is EntityProp prop && prop.GroupId == destroyProp.GroupID.GetValue() &&
                    prop.InstId == destroyProp.ID.GetValue())
                    await Player.SceneInstance.RemoveEntity(entity);

        return null;
    }

    public async ValueTask<object?> TriggerCustomString(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is TriggerCustomString triggerCustomString)
        {
            foreach (var groupInfo in Player.SceneInstance?.FloorInfo?.Groups ?? [])
                if (groupInfo.Value.PropTriggerCustomString.TryGetValue(triggerCustomString.CustomString.Value,
                        out var list))
                    foreach (var id in list)
                    foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                        if (entity is EntityProp prop && prop.GroupId == groupInfo.Key && prop.InstId == id)
                            await prop.SetState(PropStateEnum.Closed);

            await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.PropState);
        }

        return null;
    }

    public async ValueTask<object?> EnterMap(TaskConfigInfo act, SubMissionData subMission, GroupInfo? group = null)
    {
        if (act is EnterMap enterMap)
            await Player.EnterSceneByEntranceId(enterMap.EntranceID, enterMap.GroupID, enterMap.AnchorID, true);

        return null;
    }

    public async ValueTask<object?> EnterMapByCondition(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is EnterMapByCondition enterMapByCondition)
            await Player.EnterSceneByEntranceId(enterMapByCondition.EntranceID.GetValue(), 0, 0, true);

        return null;
    }

    public async ValueTask<object?> TriggerPerformance(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is TriggerPerformance triggerPerformance)
        {
            if (triggerPerformance.PerformanceType == ELevelPerformanceTypeEnum.E)
                Player.TaskManager?.PerformanceTrigger.TriggerPerformanceE(triggerPerformance.PerformanceID,
                    subMission);
            else if (triggerPerformance.PerformanceType == ELevelPerformanceTypeEnum.D)
                Player.TaskManager?.PerformanceTrigger.TriggerPerformanceD(triggerPerformance.PerformanceID,
                    subMission);
        }

        await ValueTask.CompletedTask;

        return null;
    }

    public async ValueTask<object?> PredicateTaskList(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is PredicateTaskList predicateTaskList)
        {
            // handle predicateCondition
            var methodName = predicateTaskList.Predicate.Type.Replace("RPG.GameCore.", "");

            var method = GetOrCreateExecuteTask(methodName);
            if (method == null) return null;

            var resp = await method(predicateTaskList.Predicate, subMission, group);
            if (resp is true)
                foreach (var task in predicateTaskList.SuccessTaskList)
                    TriggerTask(task, subMission, group);
            else
                foreach (var task in predicateTaskList.FailedTaskList)
                    TriggerTask(task, subMission, group);
        }

        return null;
    }

    public async ValueTask<object?> ChangePropState(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        await ApplyMissionPropState(subMission);
        return null;
    }

    /// <summary>
    ///     The body of the ChangePropState level task, callable without a TaskConfigInfo so the scene
    ///     reconcile pass (MissionManager.ApplyRunningMissionPropStates) can re-run it on a scene build.
    /// </summary>
    public async ValueTask ApplyMissionPropState(SubMissionData subMission)
    {
        if (Player.SceneInstance == null) return;
        if (subMission.SubMissionInfo?.ID == SceneCompatRules.RogueGalleryUnlockSubMission)
        {
            await Player.MissionManager!.RefreshRogueGalleryProps();
            return;
        }

        var (groupId, instId) = ResolveMissionPropTarget(subMission);
        if (groupId == 0 || instId == 0) return;

        foreach (var entity in Player.SceneInstance.Entities.Values)
            if (entity is EntityProp prop && prop.GroupId == groupId && prop.InstId == instId)
                try
                {
                    var (target, viaClosed) = StepToClosedOrNext(prop.Excel.PropStateList, prop.State);
                    if (target == prop.State) continue; // nothing left to advance - keep it idempotent

                    await prop.SetState(target);

                    // Elevator: only the advance-to-next branch unlocks the linked controllers.
                    if (viaClosed) continue;

                    foreach (var id in prop.PropInfo.UnlockControllerID)
                    foreach (var entity2 in Player.SceneInstance!.Entities.Values)
                        if (entity2 is EntityProp prop2 && prop2.GroupId == id.Key &&
                            id.Value.Contains(prop2.InstId))
                            await prop2.SetState(PropStateEnum.Closed);
                }
                catch
                {
                    // ignored
                }
    }

    /// <summary>
    ///     The prop a sub-mission's ChangePropState task targets. PropState subs carry it in
    ///     ParamInt1 (group) / ParamInt2 (inst); story Talk subs whose graph's ChangePropState unlocks an
    ///     interactive controller leave those empty (the CablePuzzle controller sub 101030104 unlocks 21/300007
    ///     so the player can press it), so the target falls back to the one the graph itself names.
    /// </summary>
    public static (int GroupId, int InstId) ResolveMissionPropTarget(SubMissionData subMission)
    {
        var info = subMission.SubMissionInfo;
        if (info is { FinishType: MissionFinishTypeEnum.PropState, ParamInt1: not 0, ParamInt2: not 0 })
            return (info.ParamInt1, info.ParamInt2);

        return ResolveGraphPropTarget(subMission.SubMissionTaskInfo);
    }

    /// <summary>
    ///     The first SinglePropByPropID target named by a graph's own prop tasks (PropSetupUITrigger /
    ///     PropSetupTrigger / PropStateExecute). The tasks operating on one interactive prop share its
    ///     SinglePropID, so no task-type preference is applied. (0, 0) when the graph names none.
    /// </summary>
    public static (int GroupId, int InstId) ResolveGraphPropTarget(LevelGraphConfigInfo? graph)
    {
        if (graph == null) return (0, 0);

        foreach (var task in graph.OnInitSequece.SelectMany(x => x.TaskList)
                     .Concat(graph.OnStartSequece.SelectMany(x => x.TaskList)))
        {
            var target = task switch
            {
                PropSetupUITrigger t => t.TargetType,
                PropSetupTrigger t => t.TargetType,
                PropStateExecute t => t.TargetType,
                _ => null
            };
            if (target is not TargetFetchAdvPropEx { FetchType: TargetFetchAdvPropFetchTypeEnum.SinglePropByPropID }
                fetch) continue;

            var groupId = fetch.SinglePropID.GroupID.GetValue();
            var instId = fetch.SinglePropID.ID.GetValue();
            if (groupId != 0 && instId != 0) return (groupId, instId);
        }

        return (0, 0);
    }

    /// <summary>
    ///     Where a mission's ChangePropState task should move a prop: straight to Closed when the prop
    ///     declares that state (a console going from Locked to interactable), otherwise one step forward
    ///     along its declared state list.
    /// </summary>
    /// <remarks>
    ///     Returns <paramref name="current" /> unchanged when there is nothing to do - the prop is already
    ///     at the end of its list, or its current state is not in the list at all because something opened
    ///     it ahead of the mission. The old code indexed <c>PropStateList[IndexOf(state) + 1]</c>, which
    ///     threw on the last entry and was swallowed, so a prop that had already been advanced silently
    ///     stopped responding to its mission. Being a no-op instead also makes the task safe to re-run,
    ///     which is what lets the scene reconcile itself on every (re)build.
    /// </remarks>
    public static (PropStateEnum State, bool ViaClosed) StepToClosedOrNext(List<PropStateEnum> stateList,
        PropStateEnum current)
    {
        if (stateList.Contains(PropStateEnum.Closed)) return (PropStateEnum.Closed, true);

        var index = stateList.IndexOf(current);
        if (index >= 0 && index + 1 < stateList.Count) return (stateList[index + 1], false);

        return (current, false);
    }

    public async ValueTask<object?> CreateTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        // ParamInt1 is only an avatar id for these finish types. A bare CreateTrialPlayer
        // beat (StageWin 20133003 on the Cocolia chain) must not grant that number as a trial.
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
        {
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);
            return null;
        }

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
        {
            foreach (var x in subMission.SubMissionInfo.ParamIntList ?? [])
                await Player.LineupManager!.AddAvatarToCurTeam(x);
            return null;
        }

        await Player.LineupManager!.GrantBareStoryTrial(subMission);
        return null;
    }

    public async ValueTask<object?> ReplaceTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            foreach (var x in ids)
                await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false);
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);
        }

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            foreach (var x in ids)
                await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false);
            foreach (var x in subMission.SubMissionInfo.ParamIntList ?? [])
                await Player.LineupManager!.AddAvatarToCurTeam(x);
        }

        return null;
    }

    public async ValueTask<object?> StoryLineReplaceTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.StoryLineAddTrialAvatar)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            foreach (var x in ids)
                await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false);
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);
        }

        return null;
    }

    public async ValueTask<object?> ReplaceVirtualTeam(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (Player.LineupManager!.GetCurLineup()?.IsExtraLineup() != true) return null;

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            foreach (var x in ids)
                await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false);
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);
        }

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
        {
            var ids = Player.LineupManager!.GetCurLineup()?.BaseAvatars?.ToList() ?? [];
            foreach (var x in ids)
                await Player.LineupManager!.RemoveAvatarFromCurTeam(x.BaseAvatarId, false);
            foreach (var x in subMission.SubMissionInfo.ParamIntList ?? [])
                await Player.LineupManager!.AddAvatarToCurTeam(x);
        }

        return null;
    }

    public async ValueTask<object?> CreateHeroTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatar)
            await Player.LineupManager!.AddAvatarToCurTeam(subMission.SubMissionInfo.ParamInt1);

        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.GetTrialAvatarList)
        {
            var list = TrailblazerAvatar.ExcludeOppositeGenderTrials(subMission.SubMissionInfo.ParamIntList ?? [],
                Player.Data.CurrentGender, TrailblazerAvatar.AvatarIdFromTrialDigits);

            foreach (var x in list)
                await Player.LineupManager!.AddAvatarToCurTeam(x);
        }

        return null;
    }

    public async ValueTask<object?> DestroyTrialPlayer(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (subMission.SubMissionInfo?.FinishType == MissionFinishTypeEnum.DelTrialAvatar)
            await Player.LineupManager!.RemoveAvatarFromCurTeam(subMission.SubMissionInfo.ParamInt1);

        return null;
    }

    public async ValueTask<object?> ChangeGroupState(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (group != null)
            foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                if (entity is EntityProp prop && prop.GroupId == group.Id)
                    if (prop.Excel.PropStateList.Contains(PropStateEnum.Open))
                        await prop.SetState(PropStateEnum.Open);

        return null;
    }

    public async ValueTask<object?> TriggerEntityServerEvent(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (group != null)
            foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                if (entity is EntityProp prop && prop.GroupId == group.Id)
                    if (prop.Excel.PropStateList.Contains(PropStateEnum.Open) &&
                        (prop.State == PropStateEnum.Closed || prop.State == PropStateEnum.Locked))
                        await prop.SetState(PropStateEnum.Open);

        return null;
    }

    public async ValueTask<object?> TriggerEntityEvent(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is TriggerEntityEvent triggerEntityEvent)
            if (group != null)
                foreach (var entity in Player.SceneInstance?.Entities.Values.ToList() ?? [])
                    if (entity is EntityProp prop && prop.GroupId == group.Id &&
                        prop.InstId == triggerEntityEvent.InstanceID.GetValue())
                        if (prop.Excel.PropStateList.Contains(PropStateEnum.Closed))
                            await prop.SetState(PropStateEnum.Closed);

        return null;
    }

    public async ValueTask<object?> PropSetupUITrigger(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is PropSetupUITrigger propSetupUiTrigger)
            foreach (var task in propSetupUiTrigger.ButtonCallback)
                TriggerTask(task, subMission, group);

        await ValueTask.CompletedTask;

        return null;
    }

    public async ValueTask<object?> PropStateExecute(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        if (act is PropStateExecute propStateExecute)
        {
            // handle targetType
            var methodName = propStateExecute.TargetType.Type.Replace("RPG.GameCore.", "");

            var method = GetType().GetMethod(methodName);
            if (method != null)
            {
                var resp = method.Invoke(this, [propStateExecute.TargetType, subMission, group]);
                if (resp is EntityProp result) await result.SetState(propStateExecute.State);
            }
        }

        return null;
    }

    #endregion

    #region Task Condition

    public async ValueTask<object?> ByCompareSubMissionState(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        await ValueTask.CompletedTask;

        if (act is ByCompareSubMissionState compare)
        {
            var mission = Player.MissionManager!.GetSubMissionStatus(compare.SubMissionID);
            return mission.ToStateEnum() == compare.SubMissionState;
        }

        return false;
    }

    public async ValueTask<object?> ByCompareFloorSavedValue(TaskConfigInfo act, SubMissionData subMission,
        GroupInfo? group = null)
    {
        await ValueTask.CompletedTask;

        if (act is ByCompareFloorSavedValue compare)
        {
            var value = Player.SceneData!.FloorSavedData.GetValueOrDefault(Player.Data.FloorId, []);
            return compare.CompareType switch
            {
                CompareTypeEnum.Equal => value.GetValueOrDefault(compare.Name, 0) == compare.CompareValue,
                CompareTypeEnum.Greater => value.GetValueOrDefault(compare.Name, 0) > compare.CompareValue,
                CompareTypeEnum.Less => value.GetValueOrDefault(compare.Name, 0) < compare.CompareValue,
                CompareTypeEnum.GreaterEqual => value.GetValueOrDefault(compare.Name, 0) >= compare.CompareValue,
                CompareTypeEnum.LessEqual => value.GetValueOrDefault(compare.Name, 0) <= compare.CompareValue,
                CompareTypeEnum.NotEqual => value.GetValueOrDefault(compare.Name, 0) != compare.CompareValue,
                _ => false
            };
        }

        return false;
    }

    #endregion
}
