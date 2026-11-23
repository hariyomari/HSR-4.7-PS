using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Drop;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.MarkChest;
using March7thHoney.GameServer.Server.Packet.Send.Scene;

namespace March7thHoney.GameServer.Game.Scene;

public partial class SceneInstance
{
    private const int PropInteractFanOutMaxDepth = 8;

    /// <summary>
    ///     Runs the server-side half of the interacted prop's InitLevelGraph: the PropReqInteract edges its
    ///     PropStateExecute blocks declare (<see cref="PropInfo.InteractLinks" />).
    /// </summary>
    /// <remarks>
    ///     Semantics, derived from the robot treasure-hunt graphs and the official wire (floor 20111001 group
    ///     47: the scan trigger's interact reveals chest 60102 Hidden → ChestClosed in the same refresh):
    ///     guards evaluate against the CURRENT scene state - including states this very pass just set - the
    ///     interacted prop is never a target, a target already at the interact's TargetState is skipped, and
    ///     each changed prop's own graph is re-driven recursively with a seen-set against config cycles.
    ///     Returns the props whose state changed, excluding the interacted one.
    /// </remarks>
    public async ValueTask<List<EntityProp>> ApplyPropInteractFanOut(EntityProp interacted)
    {
        var changed = new List<EntityProp>();
        if (interacted.PropInfo.InteractLinks.Count == 0) return changed;

        var seen = new HashSet<(int GroupId, int InstId)> { (interacted.GroupId, interacted.InstId) };
        await FanOutFrom(interacted, seen, changed, 0);
        return changed;
    }

    /// <summary>
    ///     Applies an InteractConfig row to a prop of this scene and runs what the interaction triggers: chest drops,
    ///     puzzle and console groups, door unlocks, mission progress, plane events and floor saved values. Returns
    ///     null for an unknown entity and the unchanged prop when the interact does not apply to its state.
    /// </summary>
    public async ValueTask<EntityProp?> InteractProp(int propEntityId, int interactId)
    {
        Entities.TryGetValue(propEntityId, out var entity);
        if (entity is not EntityProp prop) return null;
        GameData.InteractConfigData.TryGetValue(interactId, out var config);
        if (config == null || config.SrcState != prop.State) return prop;
        var oldState = prop.State;
        await prop.SetState(config.TargetState);
        var newState = prop.State;

        switch (prop.Excel.PropType)
        {
            case PropTypeEnum.PROP_TREASURE_CHEST:
                if (oldState == PropStateEnum.ChestClosed && newState == PropStateEnum.ChestUsed)
                {
                    // TODO: Filter treasure chest
                    var items = DropService.CalculateDropsFromProp(prop.PropInfo.ChestID);
                    await Player.InventoryManager!.AddItems(items);
                    await Player.SendPacket(new PacketOpenChestScNotify(prop.PropInfo.ChestID));

                    var notifyMark = false;
                    foreach (var markedChest in Player.SceneData!.MarkedChestData.Values)
                    {
                        var chest = markedChest.Find(x =>
                            x.FloorId == FloorId && x.GroupId == prop.GroupId &&
                            x.ConfigId == prop.PropInfo.ID);

                        if (chest == null) continue;
                        markedChest.Remove(chest);
                        notifyMark = true;
                    }

                    if (notifyMark)
                        await Player.SendPacket(new PacketMarkChestChangedScNotify(Player));
                }

                break;
            case PropTypeEnum.PROP_DESTRUCT:
                if (newState == PropStateEnum.Closed) await prop.SetState(PropStateEnum.Open);
                break;
            case PropTypeEnum.PROP_MAZE_JIGSAW:
                switch (newState)
                {
                    case PropStateEnum.Closed:
                    {
                        foreach (var p in GetEntitiesInGroup<EntityProp>(prop.GroupId))
                            if (p.Excel.PropType == PropTypeEnum.PROP_TREASURE_CHEST)
                            {
                                await p.SetState(PropStateEnum.ChestClosed);
                            }
                            else if (p.Excel.PropType == prop.Excel.PropType)
                            {
                                // Skip
                            }
                            else
                            {
                                await p.SetState(PropStateEnum.Open);
                            }

                        break;
                    }
                    case PropStateEnum.Open:
                    {
                        foreach (var p in GetEntitiesInGroup<EntityProp>(prop.GroupId).Where(p =>
                                     p.Excel.PropType is not PropTypeEnum.PROP_TREASURE_CHEST &&
                                     p.Excel.PropType != prop.Excel.PropType))
                            await p.SetState(PropStateEnum.Open);

                        break;
                    }
                }

                break;
            case PropTypeEnum.PROP_MAZE_PUZZLE:
                if (newState is PropStateEnum.Closed or PropStateEnum.Open)
                    foreach (var p in GetEntitiesInGroup<EntityProp>(prop.GroupId))
                    {
                        if (p.Excel.PropType == PropTypeEnum.PROP_TREASURE_CHEST)
                        {
                            await p.SetState(PropStateEnum.ChestClosed);
                        }
                        else if (p.Excel.PropType == prop.Excel.PropType)
                        {
                            // Skip
                        }
                        else
                        {
                            await p.SetState(PropStateEnum.Open);
                        }

                        await Player.MissionManager!.OnPlayerInteractWithProp();
                    }

                break;
            case PropTypeEnum.PROP_ORDINARY:
                if (prop.PropInfo.CommonConsole)
                    // set group
                    foreach (var p in GetEntitiesInGroup<EntityProp>(prop.GroupId))
                    {
                        await p.SetState(newState);

                        await Player.MissionManager!.OnPlayerInteractWithProp();
                    }

                if (SceneCompatRules.IsGroupStateConsole(prop.Excel.ID))
                {
                    foreach (var p in GetEntitiesInGroup<EntityProp>(prop.GroupId))
                        await p.SetState(newState);

                    await Player.MissionManager!.OnPlayerInteractWithProp();
                }

                if (prop.PropInfo.Name.Contains("Piece"))
                {
                    var pieceDone = GetEntitiesInGroup<EntityProp>(prop.GroupId)
                        .Where(p => p.PropInfo.Name.Contains("Piece")).All(p => p.State == PropStateEnum.Closed);

                    if (pieceDone)
                        // set JigsawSir to open
                        foreach (var p in GetEntitiesInGroup<EntityProp>(prop.GroupId)
                                     .Where(p => p.PropInfo.Name.Contains("JigsawSir") &&
                                                 p.State != PropStateEnum.Closed))
                            await p.SetState(PropStateEnum.TriggerEnable);
                }

                break;
        }

        // Prop-graph fan-out (RPG.GameCore.PropReqInteract): the interacted prop's InitLevelGraph may request
        // sibling interacts server-side - the robot treasure-hunt scan (floor 20111001 g47) reveals its chest
        // this way. The target's own graph is re-driven recursively so the whole chain lands at once.
        await ApplyPropInteractFanOut(prop);

        // for door unlock
        if (prop.PropInfo.UnlockDoorID.Count > 0)
            foreach (var p in prop.PropInfo.UnlockDoorID.SelectMany(id =>
                         GetEntitiesInGroup<EntityProp>(id.Key)
                             .Where(p => id.Value.Contains(p.PropInfo.ID))))
            {
                await p.SetState(PropStateEnum.Open);
                await Player.MissionManager!.OnPlayerInteractWithProp();
            }

        // Pad-puzzle groups reveal the hidden chest their ValueSource names on a real interaction, before
        // the PropState gate that waits on that chest is checked.
        if (FloorInfo?.Groups.TryGetValue(prop.GroupId, out var interactedGroup) == true)
            await RevealAuthoredChest(interactedGroup);

        // for mission
        await Player.MissionManager!.OnPlayerInteractWithProp();

        // plane event
        await Player.InventoryManager!.HandlePlaneEvent(prop.PropInfo.EventID);

        // handle plugin event

        var floorSavedKey = prop.PropInfo.Name.Replace("Controller_", "");
        var key = $"FSV_ML{floorSavedKey}{(config.TargetState == PropStateEnum.Open ? "Started" : "Complete")}";

        if (prop.Group.GroupName.Contains("JigsawPuzzle") && prop.Group.GroupName.Contains("MainLine"))
        {
            var splits = prop.Group.GroupName.Split('_');
            key =
                $"JG_ML_{splits[3]}_Puzzle{(config.TargetState == PropStateEnum.Open ? "Started" : "Complete")}";
        }

        if (FloorInfo?.FloorSavedValue.Find(x => x.Name == key) != null)
        {
            // should save
            var plane = PlaneId;
            var floor = FloorId;
            Player.SceneData!.FloorSavedData.TryGetValue(floor, out var value);
            if (value == null)
            {
                value = [];
                Player.SceneData.FloorSavedData[floor] = value;
            }

            value[key] = 1; // ParamString[2] is the key
            await Player.SendPacket(new PacketUpdateFloorSavedValueNotify(key, 1, Player));

            Player.TaskManager?.SceneTaskTrigger.TriggerFloor(plane, floor, this);
            Player.MissionManager?.HandleFinishType(MissionFinishTypeEnum.FloorSavedValue);
        }

        if (prop.PropInfo.IsLevelBtn) await prop.SetState(PropStateEnum.Closed);

        return prop;
    }

    private async ValueTask FanOutFrom(EntityProp from, HashSet<(int GroupId, int InstId)> seen,
        List<EntityProp> changed, int depth)
    {
        if (depth > PropInteractFanOutMaxDepth) return;

        foreach (var link in from.PropInfo.InteractLinks)
        {
            var key = (link.GroupId, link.InstId);
            if (seen.Contains(key)) continue;
            if (!GuardsHold(link.Guards)) continue;
            if (!GameData.InteractConfigData.TryGetValue(link.InteractId, out var interact)) continue;

            var target = FindProp(link.GroupId, link.InstId);
            if (target == null || target.State == interact.TargetState) continue;

            await target.SetState(interact.TargetState);
            seen.Add(key);
            changed.Add(target);

            // The robot chain continues through the target's own graph (300007 → 300001 → 300004).
            await FanOutFrom(target, seen, changed, depth + 1);
        }
    }

    private bool GuardsHold(List<PropInteractGuard> guards)
    {
        foreach (var guard in guards)
        {
            var prop = FindProp(guard.GroupId, guard.InstId);
            if (prop == null || prop.State != guard.State) return false;
        }

        return true;
    }

    private EntityProp? FindProp(int groupId, int instId)
    {
        foreach (var entity in Entities.Values)
            if (entity is EntityProp { IsGmSpawned: false } prop && prop.GroupId == groupId && prop.InstId == instId)
                return prop;
        return null;
    }
}
