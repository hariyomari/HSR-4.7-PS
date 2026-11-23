using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.Enums;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.Packet.Send.Scene;

public class PacketGetSceneMapInfoScRsp : BasePacket
{
    public PacketGetSceneMapInfoScRsp(GetSceneMapInfoCsReq req, PlayerInstance player) : base(
        CmdIds.GetSceneMapInfoScRsp)
    {
        var rsp = new GetSceneMapInfoScRsp();

        foreach (var sceneIdentifier in req.SceneIdentifiers)
        {
            var floorId = sceneIdentifier.FloorId;
            var mazeMap = new SceneMapInfo
            {
                FloorId = floorId,
                DimensionId = player.SceneInstance?.FloorId == floorId
                    ? (uint)player.SceneInstance.ResolveDimensionId()
                    : 0,
                // Keep SceneIdentifier in sync with floor to satisfy newer client map-info parsing.
                SceneIdentifier = new SceneIdentifier
                {
                    FloorId = floorId
                }
            };
            var roomOwnerUid = player.TrainCakeCatchManager?.GetSceneTeleportRoomOwnerUid();
            if (sceneIdentifier.TeleportInfo != null || roomOwnerUid != null)
            {
                mazeMap.SceneIdentifier.TeleportInfo = new TeleportInfo
                {
                    TeleportId = roomOwnerUid ?? sceneIdentifier.TeleportInfo?.TeleportId ?? 0
                };
            }
            var mapDatas = GameData.MapEntranceData.Values.Where(x => x.FloorID == floorId).ToList();

            if (mapDatas.Count == 0)
            {
                rsp.SceneMapInfo.Add(mazeMap);
                continue;
            }

            var mapData = mapDatas.FirstOrDefault(x => x.ID == player.Data.EntryId)
                          ?? mapDatas.MinBy(x => x.ID)!;
            mazeMap.EntryId = (uint)mapData.ID;
            GameData.GetFloorInfo(mapData.PlaneID, mapData.FloorID, out var floorInfo);
            if (floorInfo == null)
            {
                rsp.SceneMapInfo.Add(mazeMap);
                continue;
            }

            if (mazeMap.DimensionId == 0)
                mazeMap.DimensionId = (uint)SceneInstance.ResolveDimensionId(floorInfo, mapData.ID);

            var currentScene = player.SceneInstance?.FloorId == floorId ? player.SceneInstance : null;
            mazeMap.ClientGroupMissionInfo = player.MissionManager!.BuildSceneMissionInfo(floorInfo, (int)floorId);

            // The map shows opened/total per MazeChest ChestType bucket, using live or saved prop states.
            // Official floor 20111001 mid-run: NORMAL 4/7, CHALLENGE 1/2, PUZZLE 1/5 (4 puzzle + 1 hidden).
            AppendFloorChestCounts(mazeMap, floorInfo, player, (int)floorId);

            var floorSavedData = currentScene?.BuildClientFloorSavedData()
                ?? player.SceneData!.FloorSavedData.GetValueOrDefault((int)floorId, []);

            // The scene already owns its loaded server groups; evaluate client groups from their resource
            // conditions because they have no server entities. Other floors have no runtime group list.
            var dimensionGroups = floorInfo.DimensionList.FirstOrDefault(x => x.ID == (int)mazeMap.DimensionId)?
                .GroupIDList.ToHashSet();
            var missionData = player.MissionManager!.Data;
            var runningBattleIds = player.MissionManager.GetRunningSubMissionList()
                .Where(x => x.FinishType == MissionFinishTypeEnum.StageWin)
                .Select(x => x.ParamInt1).ToHashSet();
            var dropInert = mapData.PlaneID == 80112 ||
                            GameData.MazePlaneData.TryGetValue(mapData.PlaneID, out var inertPlane) &&
                            inertPlane.PlaneType == PlaneTypeEnum.Raid;
            foreach (var group in floorInfo.Groups.Values)
            {
                if (currentScene != null && group.LoadSide != GroupLoadSideEnum.Client) continue;
                if (group.OnlyEditorBackupProps() || group.OrphanedMissionGroup()) continue;
                // Non-current floors have no live group list, so inert server groups would otherwise
                // pass the empty-condition checks and ship on the map.
                if (currentScene == null && dropInert && group.InertServerGroup()) continue;
                if (dimensionGroups != null && !dimensionGroups.Contains(group.Id)) continue;
                if (group.SystemUnlockCondition is { } unlock)
                {
                    var unlocked = unlock.Operation != OperationEnum.Or;
                    foreach (var conditionId in unlock.Conditions)
                    {
                        if (!GameData.GroupSystemUnlockDataData.TryGetValue(conditionId, out var config)) continue;
                        var matched = player.QuestManager?.FuncUnlockChecker.GetUnlockStatus(config.UnlockID) == true;
                        if (unlock.Operation == OperationEnum.Or && matched)
                        {
                            unlocked = true;
                            break;
                        }

                        if ((unlock.Operation == OperationEnum.And && !matched) ||
                            (unlock.Operation == OperationEnum.Not && matched))
                        {
                            unlocked = false;
                            break;
                        }
                    }

                    if (!unlocked) continue;
                }

                if (!player.MissionManager.IsGroupOwnerMissionActive((int)floorId, group)) continue;
                if (!group.LoadCondition.IsTrue(missionData) ||
                    group.UnloadCondition.IsTrue(missionData, false) ||
                    group.ForceUnloadCondition.IsTrue(missionData, false) ||
                    !group.SavedValueCondition.IsTrue(floorSavedData) ||
                    (group.RelatedBattleId.Count > 0 && !group.RelatedBattleId.Any(runningBattleIds.Contains)))
                    continue;

                mazeMap.MazeGroupList.Add(new MazeGroup { GroupId = (uint)group.Id });
            }

            // Loaded server groups can outlive an unload condition until scene reload (or be force-loaded).
            // Preserve them alongside conditionally loaded client groups and generated room groups.
            if (currentScene != null)
            {
                var groupIds = mazeMap.MazeGroupList.Select(x => x.GroupId).ToHashSet();
                foreach (var groupId in currentScene.Groups)
                    if (groupIds.Add((uint)groupId))
                        mazeMap.MazeGroupList.Add(new MazeGroup { GroupId = (uint)groupId });
            }

            if (player.MissionEnabled)
            {
                // Official: unlock_teleport_list grows as the player REACHES checkpoints (a fresh account ships
                // [] even for config-enabled springs); the persisted reached set is the single source of truth.
                if (player.SceneData!.UnlockedTeleportData.TryGetValue((int)floorId, out var unlocked))
                    foreach (var mappingInfoId in unlocked.Where(id => floorInfo.CachedTeleports.ContainsKey(id)))
                        mazeMap.UnlockTeleportList.Add((uint)mappingInfoId);
            }
            else
            {
                // Story off: every waypoint is open from the start (legacy behaviour).
                foreach (var teleport in floorInfo.CachedTeleports.Values)
                {
                    mazeMap.UnlockTeleportList.Add((uint)teleport.MappingInfoID);
                    mazeMap.MazePropList.Add(new MazePropState
                    {
                        GroupId = (uint)teleport.AnchorGroupID,
                        ConfigId = (uint)teleport.ID,
                        State = (uint)PropStateEnum.CheckPointEnable
                    });
                }
            }

            // Include persisted prop states so client-side scene map/prop status stays in sync after re-enter.
            if (player.SceneData!.ScenePropData.TryGetValue((int)floorId, out var floorPropData))
            {
                foreach (var (groupId, propDataList) in floorPropData)
                {
                    foreach (var propData in propDataList)
                    {
                        if (mazeMap.MazePropList.Any(x => x.GroupId == (uint)groupId && x.ConfigId == (uint)propData.PropId))
                            continue;

                        mazeMap.MazePropList.Add(new MazePropState
                        {
                            GroupId = (uint)groupId,
                            ConfigId = (uint)propData.PropId,
                            State = (uint)propData.State
                        });
                    }
                }
            }

            // Include currently loaded scene props for active scene floor (fallback when DB has no row yet).
            if (player.SceneInstance?.FloorId == floorId)
            {
                foreach (var prop in player.SceneInstance.Entities.Values.OfType<EntityProp>())
                {
                    if (mazeMap.MazePropList.Any(x => x.GroupId == (uint)prop.GroupId && x.ConfigId == (uint)prop.InstId))
                        continue;

                    mazeMap.MazePropList.Add(new MazePropState
                    {
                        GroupId = (uint)prop.GroupId,
                        ConfigId = (uint)prop.InstId,
                        State = (uint)prop.State
                    });
                }
            }

            foreach (var (key, value) in floorSavedData)
                mazeMap.FloorSavedValueMap[key] = value;

            if (!ConfigManager.Config.ServerOption.AutoLightSection)
            {
                // A rogue/raid floor reports the live scene's reveals; other floors the player's persisted record.
                if (GameData.MazePlaneData.TryGetValue(mapData.PlaneID, out var mapPlane) &&
                    mapPlane.PlaneType is PlaneTypeEnum.Rogue or PlaneTypeEnum.Raid)
                {
                    if (player.SceneInstance?.FloorId == mapData.FloorID)
                        mazeMap.LightenSectionList.AddRange(player.SceneInstance.RevealedSections.Order()
                            .Select(x => (uint)x));
                }
                else
                {
                    player.SceneData!.UnlockSectionIdList.TryGetValue(mapData.FloorID, out var sections);
                    foreach (var section in sections ?? []) mazeMap.LightenSectionList.Add((uint)section);
                }
            }
            else
            {
                mazeMap.LightenSectionList.AddRange(floorInfo.MapSections.Select(x => (uint)x));
            }

            rsp.SceneMapInfo.Add(mazeMap);
        }

        SetData(rsp);
    }

    private static void AppendFloorChestCounts(SceneMapInfo mazeMap, FloorInfo floorInfo, PlayerInstance player,
        int floorId)
    {
        var counts = new Dictionary<ChestType, (uint Opened, uint Exist)>
        {
            [ChestType.MapInfoChestTypeNormal] = (0, 0),
            [ChestType.MapInfoChestTypePuzzle] = (0, 0),
            [ChestType.MapInfoChestTypeChallenge] = (0, 0)
        };

        var liveScene = player.SceneInstance?.FloorId == floorId ? player.SceneInstance : null;

        foreach (var group in floorInfo.Groups.Values)
        foreach (var prop in group.PropList)
        {
            if (prop.IsDelete || prop.ChestID == 0) continue;
            if (!GameData.MazeChestData.TryGetValue(prop.ChestID, out var chest)) continue;

            var bucket = ChestBucket(chest.ChestType);
            var (opened, exist) = counts[bucket];
            exist++;

            var state = liveScene?.GetEntitiesInGroup<EntityProp>(group.Id)
                .FirstOrDefault(x => x.InstId == prop.ID)?.State ?? player.GetScenePropData(floorId, group.Id, prop.ID)?.State;
            if (state is PropStateEnum.ChestUsed or PropStateEnum.EventOpen) opened++;

            counts[bucket] = (opened, exist);
        }

        foreach (var chestType in new[]
                 {
                     ChestType.MapInfoChestTypeNormal, ChestType.MapInfoChestTypePuzzle,
                     ChestType.MapInfoChestTypeChallenge
                 })
            mazeMap.ChestList.Add(new ChestInfo
            {
                ChestType = chestType,
                OpenedNum = counts[chestType].Opened,
                ExistNum = counts[chestType].Exist
            });
    }

    /// <summary>
    ///     Maps a MazeChest row's ChestType tags to the wire's three buckets. Priority challenge > normal >
    ///     puzzle; HIDDEN/SPECIAL_HIDDEN tally into the puzzle bucket (floor 20111001's 4 puzzle + 1 hidden
    ///     reports existNum 5 on the official wire).
    /// </summary>
    private static ChestType ChestBucket(List<ChestTypeEnum> tags)
    {
        if (tags.Contains(ChestTypeEnum.CHEST_TREASURE_CHALLENGE)) return ChestType.MapInfoChestTypeChallenge;
        if (tags.Contains(ChestTypeEnum.CHEST_TREASURE_NORMAl)) return ChestType.MapInfoChestTypeNormal;
        if (tags.Contains(ChestTypeEnum.CHEST_TREASURE_PUZZLE) || tags.Contains(ChestTypeEnum.CHEST_TREASURE_HIDDEN) ||
            tags.Contains(ChestTypeEnum.CHEST_SPECIAL_HIDDEN)) return ChestType.MapInfoChestTypePuzzle;
        return ChestType.MapInfoChestTypeNormal;
    }
}
