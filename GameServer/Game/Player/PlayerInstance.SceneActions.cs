using March7thHoney.Data.Config.Scene;
using March7thHoney.Data;
using March7thHoney.Database.Scene;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Player;

public partial class PlayerInstance
{
    private static readonly Logger SceneLogger = new("Scene");

    /// <summary>Default spawn entrance: the fallback for unloadable scenes and Raid planes without a raid run.</summary>
    public const int DefaultSpawnEntryId = 2000101;

    #region Scene Actions

    public async ValueTask OnMove()
    {
        if (SceneInstance != null)
        {
            var prop = SceneInstance.GetNearestSpring(25_000_000);

            var isInRange = prop != null;

            if (isInRange)
                if (LineupManager?.GetCurLineup()?.Heal(GameConstants.FULL_HP, true) == true)
                    await SendPacket(new PacketSyncLineupNotify(LineupManager.GetCurLineup()!));

            // Waypoint unlock is independent of the spring heal: Space Anchors / Calyx checkpoints far from any
            // spring must still flip CheckPointEnable + UnlockTeleportNotify when the player walks up to them.
            await SceneInstance.TryUnlockNearbyWaypoint(Data.Pos);
        }
    }

    /// <summary>Remembers a floor the player has entered (GetEnteredSceneScRsp with the story enabled).</summary>
    public void RecordEnteredScene(int planeId, int floorId)
    {
        if (SceneData == null || planeId == 0 || floorId == 0) return;
        if (!SceneData.EnteredSceneData.TryGetValue(planeId, out var floors))
        {
            floors = [];
            SceneData.EnteredSceneData[planeId] = floors;
        }

        if (!floors.Contains(floorId)) floors.Add(floorId);
    }

    /// <summary>Whether the map may offer this waypoint. Story off: every waypoint is unlocked (legacy behaviour).</summary>
    public bool IsTeleportUnlocked(int floorId, int mappingInfoId)
    {
        if (!MissionEnabled) return true;
        return SceneData != null && SceneData.UnlockedTeleportData.TryGetValue(floorId, out var list) &&
               list.Contains(mappingInfoId);
    }

    /// <summary>
    ///     Records a reached waypoint and, when <paramref name="notify" /> is set, sends UnlockTeleportNotify the
    ///     first time. Returns true when the unlock was new.
    /// </summary>
    public async ValueTask<bool> RecordTeleportUnlock(int floorId, int mappingInfoId, bool notify)
    {
        if (SceneData == null || mappingInfoId == 0) return false;
        if (!SceneData.UnlockedTeleportData.TryGetValue(floorId, out var list))
        {
            list = [];
            SceneData.UnlockedTeleportData[floorId] = list;
        }

        if (list.Contains(mappingInfoId)) return false;
        list.Add(mappingInfoId);

        if (notify)
            await SendPacket(new PacketUnlockTeleportNotify(mappingInfoId,
                SceneInstance?.FloorId == floorId ? SceneInstance.EntryId : Data.EntryId));
        return true;
    }

    public async ValueTask SetPropTimeline(int propEntityId, PropTimelineInfo info)
    {
        if (SceneInstance == null) return;
        SceneInstance.Entities.TryGetValue(propEntityId, out var entity);
        if (entity is not EntityProp prop) return;

        var data = new ScenePropTimelineData
        {
            BoolValue = info.TimelineBoolValue,
            ByteValue = info.TimelineByteValue.ToBase64()
        };

        // save to db
        SceneData!.PropTimelineData.TryGetValue(Data.FloorId, out var floorData);
        if (floorData == null)
        {
            floorData = new Dictionary<int, Dictionary<int, ScenePropTimelineData>>();
            SceneData.PropTimelineData[Data.FloorId] = floorData;
        }

        if (!floorData.ContainsKey(prop.GroupId))
            floorData[prop.GroupId] = new Dictionary<int, ScenePropTimelineData>();

        floorData[prop.GroupId][prop.PropInfo.ID] = data;

        prop.PropTimelineData = data;

        // handle mission / quest
        await MissionManager!.HandleFinishType(MissionFinishTypeEnum.TimeLineSetState);
        await MissionManager!.HandleFinishType(MissionFinishTypeEnum.TimeLineSetStateCnt);
    }

    public ScenePropTimelineData? GetScenePropTimelineData(int floorId, int groupId, int propId)
    {
        SceneData!.PropTimelineData.TryGetValue(floorId, out var floorData);
        if (floorData == null) return null;
        floorData.TryGetValue(groupId, out var groupData);
        if (groupData == null) return null;
        groupData.TryGetValue(propId, out var data);
        return data;
    }

    public async ValueTask<bool> EnterScene(int entryId, int teleportId, bool sendPacket, int storyLineId = 0,
        bool mapTp = false)
    {
        // Preserve the existing return contract for callers that only need the over-map flag.
        return (await TryEnterScene(entryId, teleportId, sendPacket, storyLineId, mapTp)).IsOverMap;
    }

    public async ValueTask<(bool Success, bool IsOverMap)> TryEnterScene(int entryId, int teleportId,
        bool sendPacket, int storyLineId = 0, bool mapTp = false)
    {
        var beforeStoryLineId = StoryLineManager?.StoryLineData.CurStoryLineId ?? 0;
        if (entryId == 0)
        {
            // Story-line transitions with no entrance use the story's saved position.
            if (StoryLineManager == null || storyLineId == beforeStoryLineId) return (false, false);
            var beforeScene = SceneInstance;
            await StoryLineManager.EnterStoryLine(storyLineId, true);
            var transitioned = StoryLineManager.StoryLineData.CurStoryLineId == storyLineId
                               && SceneInstance != null && !ReferenceEquals(beforeScene, SceneInstance);
            return (transitioned, transitioned);
        }

        GameData.MapEntranceData.TryGetValue(entryId, out var entrance);
        if (entrance == null || !GameData.MazePlaneData.ContainsKey(entrance.PlaneID)) return (false, false);

        GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floorInfo);
        // GetFloorInfo hands back null for a floor the resource dump does not contain. Dereferencing it
        // used to throw, the handler answered a bare failure, and the client sat on the loading screen
        // with no way to tell what happened — report it instead.
        if (floorInfo == null)
        {
            SceneLogger.Debug($"EnterScene({entryId}) aborted: no floor data for P{entrance.PlaneID}_F{entrance.FloorID}.");
            return (false, false);
        }

        var startGroup = entrance.StartGroupID;
        var startAnchor = entrance.StartAnchorID;

        if (teleportId != 0)
        {
            floorInfo.CachedTeleports.TryGetValue(teleportId, out var teleport);
            if (teleport != null)
            {
                startGroup = teleport.AnchorGroupID;
                startAnchor = teleport.AnchorID;
            }
        }
        else if (startAnchor == 0)
        {
            startGroup = floorInfo.StartGroupID;
            startAnchor = floorInfo.StartAnchorID;
        }

        var anchor = ResolveAnchor(floorInfo, startGroup, startAnchor, true);
        if (anchor == null)
        {
            SceneLogger.Debug($"EnterScene({entryId}) aborted: no usable anchor on P{entrance.PlaneID}_F{entrance.FloorID} " +
                        $"(group {startGroup}, anchor {startAnchor}).");
            return (false, false);
        }

        // Validate the destination before changing the player's story-line state.
        if (storyLineId != beforeStoryLineId)
        {
            if (StoryLineManager == null) return (false, false);
            await StoryLineManager.EnterStoryLine(storyLineId, false);
            if (StoryLineManager.StoryLineData.CurStoryLineId != storyLineId) return (false, false);
            mapTp = false;
        }

        await MissionManager!.HandleFinishType(MissionFinishTypeEnum.EnterMapByEntrance, entrance);

        var beforeEntryId = Data.EntryId;

        var loaded = await TryLoadScene(entrance.PlaneID, entrance.FloorID, entryId, anchor.ToPositionProto(),
            anchor.ToRotationProto(), sendPacket, mapTp);

        return (loaded, loaded && (beforeEntryId != Data.EntryId || beforeStoryLineId != storyLineId));
    }

    /// <summary>
    ///     Moves the saved location to an entrance without building a scene, for login-time relocation that runs before
    ///     the login scene load. A null <paramref name="pos" />/<paramref name="rot" /> uses the entrance's start anchor.
    ///     Returns false, leaving the location untouched, when the entrance, its floor or its anchor is unusable or its
    ///     plane type is one of <paramref name="rejectPlaneTypes" />.
    /// </summary>
    public bool TryPlaceAtEntrance(int entryId, Position? pos = null, Position? rot = null,
        params PlaneTypeEnum[] rejectPlaneTypes)
    {
        if (!GameData.MapEntranceData.TryGetValue(entryId, out var entrance)) return false;
        if (!GameData.MazePlaneData.TryGetValue(entrance.PlaneID, out var plane) ||
            rejectPlaneTypes.Contains(plane.PlaneType))
            return false;

        if (pos == null || rot == null)
        {
            GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floor);
            var anchor = floor == null
                ? null
                : ResolveAnchor(floor, entrance.StartGroupID, entrance.StartAnchorID, false);
            if (anchor == null) return false;
            pos = anchor.ToPositionProto();
            rot = anchor.ToRotationProto();
        }

        Data.PlaneId = entrance.PlaneID;
        Data.FloorId = entrance.FloorID;
        Data.EntryId = entryId;
        Data.Pos = pos;
        Data.Rot = rot;
        March7thHoney.Database.DatabaseHelper.MarkDirty(Uid);
        return true;
    }

    public async ValueTask EnterSceneByEntranceId(int entranceId, int anchorGroupId, int anchorId, bool sendPacket)
    {
        GameData.MapEntranceData.TryGetValue(entranceId, out var entrance);
        if (entrance == null) return;

        GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floorInfo);
        if (floorInfo == null)
        {
            SceneLogger.Debug(
                $"EnterSceneByEntranceId({entranceId}) aborted: no floor data for P{entrance.PlaneID}_F{entrance.FloorID}.");
            return;
        }

        var startGroup = anchorGroupId == 0 ? entrance.StartGroupID : anchorGroupId;
        var startAnchor = anchorId == 0 ? entrance.StartAnchorID : anchorId;

        if (startAnchor == 0)
        {
            startGroup = floorInfo.StartGroupID;
            startAnchor = floorInfo.StartAnchorID;
        }

        var anchor = ResolveAnchor(floorInfo, startGroup, startAnchor, true);
        if (anchor == null)
        {
            SceneLogger.Debug($"EnterSceneByEntranceId({entranceId}) aborted: no usable anchor on " +
                        $"P{entrance.PlaneID}_F{entrance.FloorID} (group {startGroup}, anchor {startAnchor}).");
            return;
        }

        await MissionManager!.HandleFinishType(MissionFinishTypeEnum.EnterMapByEntrance, entrance);
        await LoadScene(entrance.PlaneID, entrance.FloorID, entranceId, anchor.ToPositionProto(),
            anchor.ToRotationProto(), sendPacket);
    }

    /// <summary>
    ///     The requested anchor, else the floor's own start anchor, else (with <paramref name="anyAnchor" />) the
    ///     first anchor of any group. An entrance may point at a group the dump does not carry; the fallbacks turn
    ///     that into a wrong spawn spot instead of a client stuck on the loading screen.
    /// </summary>
    private static AnchorInfo? ResolveAnchor(FloorInfo floor, int groupId, int anchorId, bool anyAnchor)
    {
        var anchor = floor.GetAnchorInfo(groupId, anchorId) ??
                     floor.GetAnchorInfo(floor.StartGroupID, floor.StartAnchorID);
        if (anchor != null || !anyAnchor) return anchor;
        return floor.Groups.Values.SelectMany(x => x.AnchorList).FirstOrDefault();
    }

    public async ValueTask MoveTo(Position position)
    {
        Data.Pos = position;
        await SendPacket(new PacketSceneEntityMoveScNotify(this));
    }

    public void MoveTo(EntityMotion motion)
    {
        Data.Pos = motion.Motion.Pos.ToPosition();
        Data.Rot = motion.Motion.Rot.ToPosition();
    }

    public async ValueTask MoveTo(Position pos, Position rot)
    {
        Data.Pos = pos;
        Data.Rot = rot;
        await SendPacket(new PacketSceneEntityMoveScNotify(this));
    }

    private readonly HashSet<long> _loadingFloors = [];

    public async ValueTask LoadScene(int planeId, int floorId, int entryId, Position pos, Position rot, bool sendPacket,
        bool mapTp = false)
    {
        await TryLoadScene(planeId, floorId, entryId, pos, rot, sendPacket, mapTp);
    }

    private async ValueTask<bool> TryLoadScene(int planeId, int floorId, int entryId, Position pos, Position rot,
        bool sendPacket, bool mapTp = false)
    {
        GameData.MazePlaneData.TryGetValue(planeId, out var plane);
        if (plane == null) return false;

        B51RacingManager?.ClearRace();
        var floorKey = ((long)planeId << 32) | (uint)floorId;
        if (!_loadingFloors.Add(floorKey)) return false; // cycle guard: floor already loading (orphan level-graph EnterMap) -> stack overflow
        try
        {
            if (plane.PlaneType == PlaneTypeEnum.Raid && RaidManager!.Data.CurRaidId == 0)
            {
                await EnterScene(DefaultSpawnEntryId, 0, sendPacket);
                return false; // The requested raid was not entered, even if the fallback succeeded.
            }

            Data.Pos = pos;
            Data.Rot = rot;
            var notSendMove = true;
            if (planeId != Data.PlaneId || floorId != Data.FloorId || entryId != Data.EntryId || SceneInstance == null ||
                !mapTp)
            {
                var previousScene = SceneInstance;
                // Rogue rooms on the same floor reuse client actors (official 4.6 seq 28808/29174).
                // Reserve the old entity range before loading props, then restore the actor IDs.
                Dictionary<(int AvatarId, AvatarType Type), int>? retainedAvatarIds = null;
                var previousLastEntityId = 0;
                if (!sendPacket && previousScene != null && previousScene.PlaneId == planeId &&
                    previousScene.FloorId == floorId && previousScene.EntryId == entryId &&
                    RogueManager?.GetActiveRoomExcel(entryId) != null)
                {
                    retainedAvatarIds = previousScene.AvatarInfo.Values.ToDictionary(
                        avatar => (avatar.AvatarInfo.BaseAvatarId, avatar.AvatarType), avatar => avatar.EntityId);
                    previousLastEntityId = previousScene.LastEntityId;
                }
                if (previousScene != null) await previousScene.OnDestroy();
                SceneInstance instance = new(this, plane, floorId, entryId) { LastEntityId = previousLastEntityId };
                SceneInstance = instance;
                await instance.LoadEntitiesAsync();

                await instance.SyncLineup(true, retainedAvatarIds);
                Data.PlaneId = planeId;
                Data.FloorId = floorId;
                Data.EntryId = entryId;
                RecordEnteredScene(planeId, floorId);
                March7thHoney.Database.DatabaseHelper.MarkDirty(Uid);
            }
            else if (StoryLineManager?.StoryLineData.CurStoryLineId == 0 &&
                     mapTp) // only send move packet when not in story line and mapTp
            {
                notSendMove = false;
            }

            if (MissionManager != null)
            {
                await MissionManager.OnPlayerChangeScene();
                await MissionManager.HandleFinishType(MissionFinishTypeEnum.EnterFloor);
                await MissionManager.HandleFinishType(MissionFinishTypeEnum.EnterPlane);
                await MissionManager.HandleFinishType(MissionFinishTypeEnum.NotInFloor);
                await MissionManager.HandleFinishType(MissionFinishTypeEnum.NotInPlane);
                if (SceneInstance != null) await SceneInstance.SyncGroupInfo();
            }

            if (sendPacket && notSendMove)
            {
                await SendPacket(new PacketSyncServerSceneChangeNotify());
                await SendPacket(new PacketEnterSceneByServerScNotify(SceneInstance!));
            }
            else if (sendPacket && !notSendMove) // send move packet
                await SendPacket(new PacketSceneEntityMoveScNotify(this));

            return Data.PlaneId == planeId && Data.FloorId == floorId && Data.EntryId == entryId;
        }
        finally
        {
            _loadingFloors.Remove(floorKey);
        }
    }

    public ScenePropData? GetScenePropData(int floorId, int groupId, int propId)
    {
        if (SceneData != null)
            if (SceneData.ScenePropData.TryGetValue(floorId, out var floorData))
                if (floorData.TryGetValue(groupId, out var groupData))
                {
                    var propData = groupData.Find(x => x.PropId == propId);
                    return propData;
                }

        return null;
    }

    public void SetScenePropData(int floorId, int groupId, int propId, PropStateEnum state)
    {
        if (SceneData != null)
        {
            if (!SceneData.ScenePropData.TryGetValue(floorId, out var floorData))
            {
                floorData = [];
                SceneData.ScenePropData.Add(floorId, floorData);
            }

            if (!floorData.TryGetValue(groupId, out var groupData))
            {
                groupData = [];
                floorData.Add(groupId, groupData);
            }

            var propData = groupData.Find(x => x.PropId == propId); // find prop data
            if (propData == null)
            {
                propData = new ScenePropData
                {
                    PropId = propId,
                    State = state
                };
                groupData.Add(propData);
            }
            else
            {
                propData.State = state;
            }
        }
    }

    public void SetGroupCustomSaveData(int entryId, int groupId, string saveData)
    {
        if (SceneData == null) return;

        if (!SceneData.CustomSaveData.TryGetValue(entryId, out var groupData))
        {
            groupData = [];
            SceneData.CustomSaveData.Add(entryId, groupData);
        }

        groupData[groupId] = saveData;
    }

    public void EnterSection(int sectionId)
    {
        // Section 0 is a real, enterable id. A rogue/raid plane records reveals on the scene, not the player.
        if (SceneInstance is { IsTransientPlane: true })
        {
            SceneInstance.RevealedSections.Add(sectionId);
            return;
        }

        if (SceneInstance != null)
        {
            SceneData!.UnlockSectionIdList.TryGetValue(SceneInstance.FloorId, out var unlockList);
            if (unlockList == null)
            {
                unlockList = [sectionId];
                SceneData.UnlockSectionIdList.Add(SceneInstance.FloorId, unlockList);
            }
            else
            {
                SceneData.UnlockSectionIdList[SceneInstance.FloorId].Add(sectionId);
            }
        }
    }

    public async ValueTask ForceQuitBattle()
    {
        if (BattleInstance != null)
        {
            BattleInstance = null;
        }
    }

    #endregion
}
