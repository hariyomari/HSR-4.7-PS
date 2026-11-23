using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.RogueTourn;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.Enums.TournRogue;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.RogueCommon;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.RogueTourn;

public partial class RogueTournManager : BasePlayerManager
{
    // RogueTournConstCommon RogueTourn_Level_MapEntrance: the DU map entrance a settled run returns to.
    private const int TournMapEntranceId = 1034102;

    // Official StartScRsp ships the tourn lineup at mp 5.
    private const int TournLineupMp = 5;

    // Capture: 50 Cosmic Fragments at run start.
    private const int StartCoinCount = 50;

    // The official three-mob combat room pays 15.
    private const int CoinPerMonster = 5;

    // Sizes a layer that ships neither Persona nor RogueTournLayerRoom rows (capture: 4 / 5 / 4).
    private const int DefaultLayerRoomCount = 4;

    // The two start modifiers are minted at the very top of a run.
    private const int StartModifierCount = RogueTournRunData.StartModifierCount;

    private static readonly Logger Logger = new("RogueTourn");

    public RogueTournManager(PlayerInstance player) : base(player)
    {
        Data = DatabaseHelper.Instance!.GetInstanceOrCreateNew<RogueTournData>(player.Uid);
        Data.Handbook ??= new RogueTournHandbookData();
        ResurrectRun();
    }

    public RogueTournData Data { get; }
    public RogueTournRunData? RunInstance { get; private set; }

    public RogueTournHandbookData Handbook => Data.Handbook ??= new RogueTournHandbookData();

    #region Persistence

    public void Save()
    {
        Data.RunInstance = RogueRunBlob.Serialize(RunInstance);
        DatabaseHelper.MarkDirty(Player.Uid);
    }

    private void ResurrectRun()
    {
        if (!RogueRunBlob.TryLoad<RogueTournRunData>(Data.RunInstance, out var run)) return;

        if (run == null || run.Status != RogueTournStates.RunDoing)
        {
            Data.RunInstance = null;
            return;
        }

        // A blob written before the 4.6 Persona fields carries them as null; rooms get their unique ids here.
        run.EnsureCollections();

        // Heal a persisted style action on a run whose game data carries no style object: it can never
        // be resolved and wedges the client's action queue.
        if (run.Pending?.Kind == RogueTournPendingKind.Style && !UsesPersonaStyle(run))
        {
            run.Pending = null;
            OpenFormulaDraft(run);
        }

        RunInstance = run;
    }

    #endregion

    #region Run Lifecycle

    public async ValueTask<(Retcode Ret, RogueTournRunData? Run)> StartRun(int areaId, List<int> avatarIds)
    {
        if (RunInstance != null) await ClearRun();

        if (!GameData.RogueTournAreaData.TryGetValue(areaId, out var area))
            return (Retcode.RetRogueAreaInvalid, null);

        var run = new RogueTournRunData
        {
            AreaId = areaId,
            TournMode = (int)area.TournMode,
            DifficultyId = area.DifficultyIDList.FirstOrDefault(),
            ItemValue = { [RogueTournStates.ItemValueCosmicFragment] = StartCoinCount },
            Status = RogueTournStates.RunDoing,
            LevelStatus = RogueTournStates.LevelProcessing,
            LevelReason = RogueTournStates.ReasonNone,
            PersonaStyle = Data.PersonaStyle != 0 ? Data.PersonaStyle : RogueTournResources.PersonaStyleDefault(),
            BaseAvatarIds = avatarIds,
            TransNo = Guid.NewGuid().ToString()
        };

        var ret = BuildLayers(run, area);
        if (ret != Retcode.RetSucc) return (ret, null);
        if (run.Layers.Count == 0) return (Retcode.RetRogueAreaInvalid, null);

        // The run opens with two source-less modifiers, sent before the scene change and mirrored in the snapshot.
        for (var i = 0; i < StartModifierCount; i++) run.AddModifier();

        run.CurLayerIndex = 1;
        run.Layers[0].Status = RogueTournStates.LayerProcessing;
        if (!EnterFirstRoom(run)) return (Retcode.RetRogueAreaInvalid, null);

        // Only a Persona run can carry the opening style select; every other run opens its starter
        // Equation draft directly.
        if (UsesPersonaStyle(run))
            OpenStyleAction(run);
        else
            OpenFormulaDraft(run);

        if (!await ActivateTournLineup(avatarIds)) return (Retcode.RetRogueStatusNotMatch, null);

        RunInstance = run;
        Save();
        return (Retcode.RetSucc, run);
    }

    /// <summary>
    ///     A layer loads a plane drawn per run from the season's PlanePool (Config/Custom/RogueTournRoomPlan.json), its
    ///     authored plan plane, or the shipped room-template plane for its index. Rooms come from the plan's room
    ///     types, the Persona layer rows or the RogueTournLayerRoom rows; a Persona slot with a preset always plays
    ///     that preset's card. Every layer has its own difficulty, whose LevelList is the room mobs' level ladder.
    /// </summary>
    private static Retcode BuildLayers(RogueTournRunData run, RogueTournAreaExcel area)
    {
        if (area.LayerIDList.Count == 0) return Retcode.RetRogueAreaInvalid;

        var season = RogueTournResources.SeasonPlan(area.TournMode);
        // One shuffle per run: the layers take distinct planes until the pool runs out. A copy, because the plan
        // is shared by every run.
        var planes = (season?.PlanePool ?? []).ToList();
        for (var i = planes.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (planes[i], planes[j]) = (planes[j], planes[i]);
        }

        for (var i = 0; i < area.LayerIDList.Count; i++)
        {
            var layerId = area.LayerIDList[i];
            var planeIndex = i + 1;
            var plan = season?.Plane(planeIndex);
            var layer = new RogueTournLayerData
            {
                LayerId = layerId,
                LayerIndex = planeIndex,
                DifficultyId = i < area.DifficultyIDList.Count ? area.DifficultyIDList[i] : 0
            };
            var template = false;

            if (plan != null)
            {
                layer.PlaneId = plan.PlaneID;
                layer.EntranceId = plan.EntranceID;
                layer.AnchorGroup = plan.AnchorGroup;
            }

            if (i < planes.Count)
            {
                // The draw wins and drops the plan's entrance/anchor, which name the plan's plane.
                layer.PlaneId = planes[i];
                layer.EntranceId = 0;
                layer.AnchorGroup = 0;
            }
            else if (layer.PlaneId == 0 && RogueTournResources.GuidePlane(planeIndex) is var guidePlane and not 0)
            {
                template = true;
                layer.PlaneId = guidePlane;
            }

            if (layer.PlaneId == 0) return Retcode.RetRogueAreaInvalid;
            if (layer.EntranceId == 0) layer.EntranceId = RogueTournResources.PlaneEntranceId(layer.PlaneId);
            if (layer.AnchorGroup == 0) layer.AnchorGroup = RogueTournResources.PlaneAnchorGroup(layer.PlaneId);

            // A template plane only hosts the room types it authors (it ships no Reward room, for instance).
            List<int> pool = [];
            if (template)
            {
                pool = RogueTournResources.RoomTypes(layer.PlaneId);
                if (pool.Count == 0) return Retcode.RetRogueAreaInvalid;
            }

            var types = RoomTypeSequence(layerId, planeIndex, area.LayerIDList.Count, plan, pool);
            var slots = RogueTournResources.PersonaRooms(layerId);
            var blockCount = RogueTournResources.RoomBlocks(layer.PlaneId).Count;
            for (var n = 0; n < types.Count; n++)
            {
                var roomIndex = n + 1;
                var roomType = types[n];
                var composition = RogueTournResources.CompositionForRoomType(roomType);
                // A preset-less slot's level is a runtime draw no table names; the plan authors one transcript.
                var level = plan != null && n < plan.RoomLevels.Count && plan.RoomLevels[n] > 0 ? plan.RoomLevels[n] : 1;
                var presetId = 0;
                if (n < slots.Count && slots[n].RoomIndex == roomIndex &&
                    RogueTournResources.PersonaRoomCard(slots[n].PresetID) is { } card)
                {
                    (composition, level, roomType) = card;
                    presetId = slots[n].PresetID;
                }

                // The plane's blocks are its addressable rooms: a layer longer than the block count re-visits a
                // geometry and repeats the id, and the id and anchor must name the same block.
                var roomNum = roomIndex;
                var anchorGroup = layer.AnchorGroup;
                if (blockCount > 0)
                {
                    roomNum = (roomIndex - 1) % blockCount + 1;
                    var blockArea = RogueTournResources.RoomBlockArea(layer.PlaneId, roomIndex);
                    if (blockArea != 0) anchorGroup = blockArea;
                }
                else if (template)
                {
                    anchorGroup = RogueTournResources.RoomAnchorGroup(layer.PlaneId, roomType, anchorGroup);
                }

                layer.Rooms.Add(new RogueTournRoomData
                {
                    RoomId = layer.EntranceId * 100 + roomNum,
                    RoomIndex = roomIndex,
                    RoomType = roomType,
                    Status = RogueTournStates.RoomNone,
                    PlaneId = layer.PlaneId,
                    EntranceId = layer.EntranceId,
                    AnchorGroup = anchorGroup,
                    CompositionType = composition,
                    Level = level,
                    PresetId = presetId,
                    MonsterLevel = RogueTournResources.DifficultyLevelAt(layer.DifficultyId, roomIndex)
                });
            }

            if (layer.Rooms.Count == 0) return Retcode.RetRogueAreaInvalid;
            run.Layers.Add(layer);
        }

        // Number the run's rooms 1..N across every layer: the 4.6 client addresses rooms by this id.
        var uid = 0;
        foreach (var room in run.Layers.SelectMany(layer => layer.Rooms)) room.UniqueId = ++uid;
        return Retcode.RetSucc;
    }

    private static List<int> RoomTypeSequence(int layerId, int planeIndex, int layerCount, RogueTournPlanePlan? plan,
        List<int> pool)
    {
        // A layer's room count is not the plane's block count: 3012 ships 7 rooms on a 3-block plane.
        if (plan is { RoomTypes.Count: > 0 }) return [.. plan.RoomTypes];

        var count = DefaultLayerRoomCount;
        var personaRooms = RogueTournResources.PersonaRooms(layerId).Count;
        var rows = RogueTournResources.RoomsOfLayerCount(layerId);
        if (personaRooms > 0)
            count = personaRooms;
        else if (rows > 0)
            count = rows;
        else if (layerCount >= 3 && planeIndex == 2)
            count = 5;

        return GeneratedRoomTypes(count, pool);
    }

    // Opens on a battle, closes on the Boss, cycles the pool in between.
    private static List<int> GeneratedRoomTypes(int count, List<int> pool)
    {
        if (count <= 0) return [];
        if (pool.Count == 0)
            pool =
            [
                (int)RogueTournRoomTypeEnum.Battle, (int)RogueTournRoomTypeEnum.Event,
                (int)RogueTournRoomTypeEnum.Reward, (int)RogueTournRoomTypeEnum.Encounter,
                (int)RogueTournRoomTypeEnum.Coin, (int)RogueTournRoomTypeEnum.Boss
            ];

        var types = new List<int>(count);
        for (var n = 0; n < count; n++) types.Add(pool[n % pool.Count]);
        if (pool.Contains((int)RogueTournRoomTypeEnum.Battle)) types[0] = (int)RogueTournRoomTypeEnum.Battle;
        if (pool.Contains((int)RogueTournRoomTypeEnum.Boss)) types[count - 1] = (int)RogueTournRoomTypeEnum.Boss;
        return types;
    }

    private static bool EnterFirstRoom(RogueTournRunData run)
    {
        var layer = run.CurLayer();
        if (layer == null || layer.Rooms.Count == 0) return false;
        layer.CurRoomIndex = 1;
        layer.Rooms[0].Status = RogueTournStates.RoomProcessing;
        GrantRoomStyleExp(run);
        return true;
    }

    public Retcode EnterRoom(int roomIndex)
    {
        var run = RunInstance;
        var layer = run?.CurLayer();
        if (run == null || layer == null) return Retcode.RetRogueStatusNotMatch;

        if (roomIndex == 0) roomIndex = layer.CurRoomIndex + 1;
        if (roomIndex <= 0 || roomIndex > layer.Rooms.Count) return Retcode.RetRogueStatusNotMatch;

        foreach (var room in layer.Rooms.Where(room => room.Status == RogueTournStates.RoomProcessing))
            room.Status = RogueTournStates.RoomFinish;

        layer.CurRoomIndex = roomIndex;
        var target = layer.Rooms[roomIndex - 1];
        target.Status = RogueTournStates.RoomProcessing;
        // Entering a room grants the run's Persona style exp; the rows ride this walk's result stream.
        GrantRoomStyleExp(run);
        Save();
        return Retcode.RetSucc;
    }

    /// <summary>
    ///     The walk the client's room-select trigger drives. The client fires it after loading every room,
    ///     so it only advances when the room the player stands in is already cleared.
    /// </summary>
    public (Retcode Ret, RogueTournRoomData? Room, bool Moved) SelectNextRoom()
    {
        var run = RunInstance;
        var room = run?.CurRoom();
        if (run == null || room == null) return (Retcode.RetRogueStatusNotMatch, null, false);
        if (room.Status != RogueTournStates.RoomFinish) return (Retcode.RetSucc, room, false);

        var layer = run.CurLayer();
        if (layer == null) return (Retcode.RetRogueStatusNotMatch, null, false);

        if (layer.CurRoomIndex < layer.Rooms.Count)
        {
            var ret = EnterRoom(0);
            return ret != Retcode.RetSucc ? (ret, null, false) : (Retcode.RetSucc, run.CurRoom(), true);
        }

        // Past the last room of the last layer the run is left untouched; the finish/settle path owns that.
        if (run.CurLayerIndex < run.Layers.Count && EnterLayer() == Retcode.RetSucc && run.CurRoom() is { } next)
            return (Retcode.RetSucc, next, true);

        return (Retcode.RetSucc, room, false);
    }

    public Retcode EnterLayer()
    {
        var run = RunInstance;
        if (run == null || run.CurLayerIndex >= run.Layers.Count) return Retcode.RetRogueStatusNotMatch;

        var current = run.CurLayer();
        if (current != null) current.Status = RogueTournStates.LayerFinish;

        run.CurLayerIndex++;
        var next = run.CurLayer();
        if (next == null) return Retcode.RetRogueStatusNotMatch;

        next.Status = RogueTournStates.LayerProcessing;
        if (!EnterFirstRoom(run)) return Retcode.RetRogueAreaInvalid;

        Save();
        return Retcode.RetSucc;
    }

    public async ValueTask LeaveRun()
    {
        var hadRun = RunInstance != null;
        await ClearRun();
        if (hadRun) await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.RogueFinishCnt);
    }

    /// <summary>Leaves the run and returns to the rogue lobby; false when the lobby scene fails.</summary>
    public async ValueTask<bool> LeaveRunToLobby()
    {
        await LeaveRun();
        var (success, _) = await Player.TryEnterScene(RogueLobby.EntranceId, 0, false);
        return success;
    }

    /// <summary>
    ///     Drops a settled run and its lineup, then returns to the Divergent Universe map (the lobby when the map is
    ///     absent); the scene-change notify precedes the new scene. False when that scene fails.
    /// </summary>
    public async ValueTask<bool> LeaveSettledRun()
    {
        await LeaveRun();
        await Player.SendPacket(new PacketSyncServerSceneChangeNotify());
        var entrance = GameData.MapEntranceData.ContainsKey(TournMapEntranceId)
            ? TournMapEntranceId
            : RogueLobby.EntranceId;
        var (success, _) = await Player.TryEnterScene(entrance, 0, false);
        return success;
    }

    /// <summary>Drops the live run (the /du reset escape hatch). Account progress stays.</summary>
    public async ValueTask<bool> ResetRun()
    {
        var hadRun = RunInstance != null || !string.IsNullOrEmpty(Data.RunInstance);
        await ClearRun();
        return hadRun;
    }

    private async ValueTask ClearRun()
    {
        RunInstance = null;
        await RogueExtraLineup.Release(Player);
        Save();
    }

    private ValueTask<bool> ActivateTournLineup(List<int> avatarIds)
    {
        return RogueExtraLineup.Activate(Player, ExtraLineupType.LineupTournRogue, avatarIds,
            new RogueLineupStart(TournLineupMp, RogueExtraLineup.HalfSp, RogueLineupRefill.RequestedFormalAvatars));
    }

    /// <summary>
    ///     The notifies the official sends at the very top of a run, before the scene change that opens it: the
    ///     tourn lineup teardown, one RogueModifierAddNotify per start modifier (no source_type) and an empty
    ///     SyncTurnFoodNotify.
    /// </summary>
    public async ValueTask SendStartPrelude()
    {
        await Player.SendPacket(new PacketExtraLineupDestroyNotify(ExtraLineupType.LineupTournRogue));
        if (RunInstance != null)
            foreach (var id in RunInstance.Modifiers)
                await Player.SendPacket(BasePacket.Create(CmdIds.RogueModifierAddNotify,
                    new RogueModifierAddNotify { Modifier = RogueTournProtoBuilder.BuildModifierRow(id, false, false, 0) }));
        await Player.SendPacket(BasePacket.Create(CmdIds.SyncTurnFoodNotify, new SyncTurnFoodNotify()));
    }

    #endregion

    #region Scene

    /// <summary>
    ///     Entity loader for a Rogue plane scene that belongs to the current room, or null for any other
    ///     Rogue plane. Also stamps the scene's game mode (TournRogue = 17; the plane's own type is 5).
    /// </summary>
    public SceneEntityLoader? CreateEntityLoader(SceneInstance scene)
    {
        var room = RunInstance?.Status == RogueTournStates.RunDoing ? RunInstance.CurRoom() : null;
        if (room == null || room.PlaneId != scene.PlaneId) return null;

        scene.GameModeType = GameModeTypeEnum.TournRogue;
        return new RogueTournEntityLoader(scene, room);
    }

    /// <summary>
    ///     Puts the player into the room. Inside the same layer the live scene is re-targeted at the room's block
    ///     (the official keeps one scene per layer); otherwise the plane is (re)entered, announced by an empty
    ///     SyncServerSceneChangeNotify for a layer's first room. The position version the client adopts advances
    ///     +1 per room and +2 on a new layer. A room with nothing to fight is over on arrival and its doors open
    ///     before the caller serializes the scene.
    /// </summary>
    public async ValueTask<bool> EnterRoomScene(RogueTournRoomData? room, bool forceRebuild = false)
    {
        var run = RunInstance;
        if (run == null || room == null || room.EntranceId == 0) return false;
        if (!GameData.MapEntranceData.TryGetValue(room.EntranceId, out var entrance)) return false;

        GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floor);
        var anchor = floor != null && floor.Groups.TryGetValue(room.AnchorGroup, out var group)
            ? group.AnchorList.FirstOrDefault()
            : null;

        var scene = Player.SceneInstance;
        if (!forceRebuild && run.Status == RogueTournStates.RunDoing && scene is
            {
                GameModeType: GameModeTypeEnum.TournRogue, EntityLoader: RogueTournEntityLoader loader
            } && scene.PlaneId == entrance.PlaneID && scene.FloorId == entrance.FloorID)
        {
            var loaded = await loader.SwapRoom(room);
            if (anchor != null)
            {
                Player.Data.Pos = anchor.ToPositionProto();
                Player.Data.Rot = anchor.ToRotationProto();
            }

            foreach (var groupId in loaded)
                Player.TaskManager?.SceneTaskTrigger.TriggerGroup(scene.PlaneId, scene.FloorId, groupId);
        }
        else
        {
            if (room.RoomIndex <= 1) await Player.SendPacket(new PacketSyncServerSceneChangeNotify());
            await Player.EnterSceneByEntranceId(room.EntranceId, room.AnchorGroup, anchor?.ID ?? 0, false);
            scene = Player.SceneInstance;
            if (scene?.PlaneId != entrance.PlaneID || Player.Data.EntryId != room.EntranceId) return false;
        }

        // The opening room echoes the client's own version; later rooms advance it, a new layer by two.
        var steps = run.CurLayerIndex <= 1 && room.RoomIndex <= 1 ? 0 : room.RoomIndex <= 1 ? 2 : 1;
        Player.ClientPosVersion += steps;
        scene!.ClientPosVersion = Player.ClientPosVersion;

        if (RogueTournEntityLoader.AliveMonsterCount(scene) == 0)
        {
            if (RoomNeedsNoTrigger()) FinishRoomByGate();
            await RogueTournEntityLoader.OpenRoomDoors(scene, false);
        }

        return true;
    }

    // The server-initiated scene change pair: the empty reset nudge, then the scene itself.
    public async ValueTask PushSceneChange()
    {
        if (Player.SceneInstance == null) return;
        await Player.SendPacket(new PacketSyncServerSceneChangeNotify());
        await Player.SendPacket(new PacketEnterSceneByServerScNotify(Player.SceneInstance));
    }

    // The room the player just entered as a FLPDILMMCFK result row: how the official delivers every room change.
    public async ValueTask SendSceneResult()
    {
        if (Player.SceneInstance == null) return;
        await SendActionResults([
            new RogueTournActionResult
            {
                Kind = RogueTournActionResultKind.Scene,
                Scene = RogueTournProtoBuilder.BuildSceneResultPair(Player)
            }
        ]);
    }

    /// <summary>Whether the current room has nothing left to wait for, so arriving in it already clears it.</summary>
    public bool RoomNeedsNoTrigger()
    {
        var run = RunInstance;
        var room = run?.CurRoom();
        if (run == null || run.Status != RogueTournStates.RunDoing || room == null ||
            room.Status == RogueTournStates.RoomFinish) return false;
        // An Adventure card is a task room whose task is not modelled: declaring it done would open an unearned door.
        if (room.RoomType == (int)RogueTournRoomTypeEnum.Adventure) return false;
        return !(run.RoomDialogues.TryGetValue(room.UniqueId, out var dialogue) && !dialogue.Closed);
    }

    /// <summary>
    ///     Marks the current room finished because nothing in it is left to beat, then deals what the cleared room
    ///     owes (gift first, chooser last) into the run's single pending slot. False when already finished.
    /// </summary>
    public bool FinishRoomByGate()
    {
        var run = RunInstance;
        var room = run?.CurRoom();
        if (run == null || run.Status != RogueTournStates.RunDoing || room == null ||
            room.Status == RogueTournStates.RoomFinish) return false;

        room.Status = RogueTournStates.RoomFinish;
        OpenClearedRoomAction(run);
        Save();
        return true;
    }

    /// <summary>
    ///     A room emptied without a battle (an in-scene kill) is the same event to the run: it finishes, its doors
    ///     open and the client is told. No-op while monsters remain.
    /// </summary>
    public async ValueTask<bool> ClearRoomIfMonstersGone()
    {
        var scene = Player.SceneInstance;
        if (RunInstance == null || scene == null || !RogueTournEntityLoader.IsTournRoomScene(Player) ||
            RogueTournEntityLoader.AliveMonsterCount(scene) > 0) return false;

        var finished = FinishRoomByGate();
        var opened = await RogueTournEntityLoader.OpenRoomDoors(scene, true);
        if (finished) await SendLevelNotify();
        return finished || opened.Count > 0;
    }

    #endregion

    #region Battle

    public void OnBattleStart(BattleInstance battle)
    {
        var run = RunInstance;
        if (run == null || run.Status != RogueTournStates.RunDoing) return;
        // A persisted run must not claim ordinary world fights fought outside the tourn rooms.
        if (Player.SceneInstance?.GameModeType != GameModeTypeEnum.TournRogue) return;

        // Room kills pay the run currency, never the open-world drop table; the run's blessings ride the battle.
        battle.IsTournRogue = true;
        foreach (var (buffId, level) in run.Buffs.OrderBy(entry => entry.Key))
            battle.Buffs.Add(new MazeBuff(buffId, level, -1) { WaveFlag = -1 });
        battle.OnBattleEnd += OnBattleEnd;
    }

    /// <summary>
    ///     A won room battle pays the run currency, clears the room and deals the 4.6 blessing draft; the purse, the
    ///     draft and the room status all precede PVEBattleResultScRsp. The cleared room's doors then open, and the
    ///     last room of the last layer finishes the run here (the client opens its settle page off those pushes).
    ///     A loss is not settled: the Divergent Universe flow returns the player to the room.
    /// </summary>
    private async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        var run = RunInstance;
        if (run == null || run.Status != RogueTournStates.RunDoing) return;
        if (req.EndStatus != BattleEndStatus.BattleEndWin) return;

        var room = run.CurRoom();
        if (room == null) return;

        run.AddCoin(CoinPerMonster * Math.Max(1, battle.EntityMonsters.Count));
        if (room.Status == RogueTournStates.RoomProcessing) room.Status = RogueTournStates.RoomFinish;
        OpenBuffDraft(run);
        Save();

        await Player.SendPacket(BasePacket.Create(CmdIds.CommonRogueVirtualItemInfoScNotify,
            RogueTournProtoBuilder.BuildVirtualItemNotify(run)));
        await SendPendingNotify();
        await SendLevelNotify();

        // The official re-adds the area group's door in CustomState04 once the room is cleared.
        if (Player.SceneInstance is { } scene && RogueTournEntityLoader.IsTournRoomScene(Player))
            await RogueTournEntityLoader.OpenRoomDoors(scene, true);

        await ClearRoomIfMonstersGone();
        await FinishRunIfLastRoom();
    }

    #endregion

    #region Notify

    public async ValueTask SendLevelNotify(bool withRoom = true)
    {
        if (RunInstance == null) return;
        await Player.SendPacket(BasePacket.Create(CmdIds.RogueTournLevelInfoUpdateScNotify,
            RogueTournProtoBuilder.BuildLevelNotify(RunInstance, withRoom)));
    }

    public async ValueTask SendPendingNotify()
    {
        if (RunInstance?.Pending == null) return;
        await Player.SendPacket(BasePacket.Create(CmdIds.SyncRogueCommonPendingActionScNotify,
            RogueTournProtoBuilder.BuildPendingNotify(RunInstance)));
    }

    public async ValueTask SendRoomRegistrations()
    {
        if (RunInstance == null) return;
        var notify = RogueTournProtoBuilder.BuildRoomRegistrationNotify(RunInstance);
        if (notify != null)
            await Player.SendPacket(BasePacket.Create(CmdIds.SyncRogueCommonActionResultScNotify, notify));
    }

    public async ValueTask SendRoomCardZone()
    {
        if (RunInstance == null) return;
        var notify = RogueTournProtoBuilder.BuildRoomCardZoneNotify(RunInstance);
        if (notify != null)
            await Player.SendPacket(BasePacket.Create(CmdIds.RogueTournPersonaRoomCardZoneUpdateScNotify, notify));
    }

    // Blessing grants attach to the buff holder first, then every row goes out as its own result notify.
    public async ValueTask SendActionResults(List<RogueTournActionResult> results)
    {
        if (results.Count == 0) return;
        foreach (var notify in RogueTournProtoBuilder.BuildBuffGrantNotifies(results))
            await Player.SendPacket(BasePacket.Create(CmdIds.SyncEntityBuffChangeListScNotify, notify));
        foreach (var result in results)
            if (RogueTournProtoBuilder.BuildActionResult(result) is { } notify)
                await Player.SendPacket(BasePacket.Create(CmdIds.SyncRogueCommonActionResultScNotify, notify));
    }

    #endregion
}
