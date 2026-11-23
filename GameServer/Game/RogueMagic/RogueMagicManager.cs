using March7thHoney.Enums.Mission;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.RogueMagic;
using March7thHoney.Enums.RogueMagic;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.RogueCommon;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.RogueMagic;

public class RogueMagicManager : BasePlayerManager
{
    private const int StartCoin = 100;
    private const int BasicRoundLimit = 2;
    private const int ExtraRoundLimit = 2;
    private const int DraftOptionCount = 3;
    private const int QueuePositionStep = 2;
    private const int MaxUnitLevel = 3;

    public RogueMagicManager(PlayerInstance player) : base(player)
    {
        Data = DatabaseHelper.Instance!.GetInstanceOrCreateNew<RogueMagicData>(player.Uid);
        ResurrectRun();
    }

    public RogueMagicData Data { get; }
    public RogueMagicRunData? RunInstance { get; private set; }

    #region Persistence

    public void Save()
    {
        Data.RunInstance = RogueRunBlob.Serialize(RunInstance);
        DatabaseHelper.MarkDirty(Player.Uid);
    }

    private void ResurrectRun()
    {
        if (!RogueRunBlob.TryLoad<RogueMagicRunData>(Data.RunInstance, out var run)) return;

        if (run == null || run.Status != RogueMagicStates.RunDoing)
        {
            Data.RunInstance = null;
            return;
        }

        RunInstance = run;
    }

    #endregion

    #region Run Lifecycle

    public async ValueTask<(Retcode Ret, RogueMagicRunData? Run)> StartRun(int areaId, int styleType,
        List<int> difficultyIds, List<int> avatarIds)
    {
        if (!GameData.RogueMagicAreaData.TryGetValue(areaId, out var area) || area.LayerIDList.Count == 0)
            return (Retcode.RetRogueAreaInvalid, null);

        List<int> baseAvatarIds = [];
        foreach (var avatarId in avatarIds)
        {
            var avatar = Player.AvatarManager!.GetFormalAvatar(avatarId);
            if (avatar == null) return (Retcode.RetAvatarNotExist, null);
            baseAvatarIds.Add(avatar.BaseAvatarId);
        }

        if (RunInstance != null) await ClearRun();

        var run = new RogueMagicRunData
        {
            AreaId = areaId,
            StyleType = styleType,
            DifficultyCompIds = difficultyIds.Where(GameData.RogueMagicDifficultyCompData.ContainsKey).ToList(),
            BaseAvatarIds = baseAvatarIds,
            TransNo = Guid.NewGuid().ToString(),
            Status = RogueMagicStates.RunDoing,
            LevelStatus = RogueMagicStates.LevelProcessing,
            Coin = StartCoin,
            BasicRoundCnt = BasicRoundLimit,
            ExtraRoundCnt = ExtraRoundLimit
        };

        for (var i = 0; i < area.LayerIDList.Count; i++)
        {
            var layer = BuildLayer(area.LayerIDList[i], i + 1);
            if (layer == null) return (Retcode.RetRogueAreaInvalid, null);
            run.Layers.Add(layer);
        }

        run.CurLayerIndex = 1;
        run.Layers[0].Status = RogueMagicStates.LayerProcessing;
        if (!InitRoom(run.Layers[0], 1, RogueMagicRoomTypeEnum.Battle)) return (Retcode.RetRogueAreaInvalid, null);

        if (!await ActivateLineup(baseAvatarIds)) return (Retcode.RetRogueStatusNotMatch, null);

        // The run opens on a level-1 scepter draft.
        EnqueueDraft(run, RogueMagicPendingKind.Scepter, 1, [], 1);
        RollFront(run);

        RunInstance = run;
        Save();
        return (Retcode.RetSucc, run);
    }

    // Each layer stages on one random non-Adventure RogueMagicRoomGen entrance, sized by RogueMagicLayerRoom.
    private static RogueMagicLayerData? BuildLayer(int layerId, int layerIndex)
    {
        var roomCount = GameData.RogueMagicLayerIdRoomCountDict.GetValueOrDefault(layerId);
        var entrances = GameData.RogueMagicRoomGenData
            .Where(config => config.RoomType != RogueMagicRoomTypeEnum.Adventure)
            .Select(config => config.EntranceId)
            .Distinct()
            .ToList();
        if (roomCount <= 0 || entrances.Count == 0) return null;

        var layer = new RogueMagicLayerData
        {
            LayerId = layerId,
            LayerIndex = layerIndex,
            Status = RogueMagicStates.LayerNone,
            EntranceId = entrances.RandomElement()
        };
        for (var index = 1; index <= roomCount; index++) layer.Rooms.Add(new RogueMagicRoomData { RoomIndex = index });
        return layer;
    }

    public static List<RogueMagicRoomTypeEnum> RoomTypesAt(int entranceId)
    {
        return GameData.RogueMagicRoomGenData
            .Where(config => config.EntranceId == entranceId)
            .Select(config => config.RoomType)
            .Distinct()
            .ToList();
    }

    private static bool InitRoom(RogueMagicLayerData layer, int roomIndex, RogueMagicRoomTypeEnum type)
    {
        var room = layer.Rooms.FirstOrDefault(r => r.RoomIndex == roomIndex);
        if (room == null) return false;

        var configs = GameData.RogueMagicRoomGenData
            .Where(config => config.EntranceId == layer.EntranceId && config.RoomType == type)
            .ToList();
        if (configs.Count == 0 || !GameData.MapEntranceData.TryGetValue(layer.EntranceId, out var entrance))
            return false;
        var roomConfig = configs.RandomElement();

        foreach (var processing in layer.Rooms.Where(r => r.Status == RogueMagicStates.RoomProcessing))
            processing.Status = RogueMagicStates.RoomFinish;

        var roomIds = GameData.RogueMagicRoomData.Values
            .Where(excel => excel.RogueRoomType == type)
            .Select(excel => excel.RogueRoomID)
            .ToList();

        room.RoomType = (int)type;
        room.RoomId = roomIds.Count > 0 ? roomIds.RandomElement() : 0;
        room.Status = RogueMagicStates.RoomProcessing;
        room.EntranceId = roomConfig.EntranceId;
        room.PlaneId = entrance.PlaneID;
        room.AnchorGroup = roomConfig.AnchorGroup;
        room.AnchorId = roomConfig.AnchorId;
        room.LoadGroups = [.. roomConfig.DefaultLoadBasicGroup, .. roomConfig.DefaultLoadGroup, .. roomConfig.SubMonsterGroup];
        layer.CurRoomIndex = roomIndex;

        var next = layer.Rooms.FirstOrDefault(r => r.RoomIndex == roomIndex + 1);
        if (next is { Status: RogueMagicStates.RoomNone }) next.Status = RogueMagicStates.RoomInited;
        return true;
    }

    public Retcode EnterRoom(int roomIndex, RogueMagicRoomTypeEnum type)
    {
        var run = RunInstance;
        var layer = run?.CurLayer();
        if (run == null || layer == null) return Retcode.RetRogueStatusNotMatch;
        if (roomIndex <= 0 || roomIndex > layer.Rooms.Count) return Retcode.RetRogueStatusNotMatch;
        if (!InitRoom(layer, roomIndex, type)) return Retcode.RetRogueAreaInvalid;

        Save();
        return Retcode.RetSucc;
    }

    public Retcode EnterLayer(RogueMagicRoomTypeEnum type)
    {
        var run = RunInstance;
        if (run == null || run.CurLayerIndex >= run.Layers.Count) return Retcode.RetRogueStatusNotMatch;

        var current = run.CurLayer();
        if (current != null)
        {
            current.Status = RogueMagicStates.LayerFinish;
            foreach (var room in current.Rooms.Where(r => r.Status == RogueMagicStates.RoomProcessing))
                room.Status = RogueMagicStates.RoomFinish;
        }

        run.CurLayerIndex++;
        var next = run.CurLayer()!;
        next.Status = RogueMagicStates.LayerProcessing;
        if (!InitRoom(next, 1, type)) return Retcode.RetRogueAreaInvalid;

        Save();
        return Retcode.RetSucc;
    }

    /// <param name="countAsFinished">False when a run that never started is rolled back (RogueFinishCnt).</param>
    public async ValueTask LeaveRun(bool countAsFinished = true)
    {
        var hadRun = RunInstance != null;
        await ClearRun();
        if (hadRun && countAsFinished)
            await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.RogueFinishCnt);
    }

    /// <summary>Leaves the run (leave or settle) and returns to the rogue lobby; false when the lobby scene fails.</summary>
    public async ValueTask<bool> LeaveRunToLobby()
    {
        await LeaveRun();
        var (success, _) = await Player.TryEnterScene(RogueLobby.EntranceId, 0, false);
        return success;
    }

    private async ValueTask ClearRun()
    {
        RunInstance = null;
        await RogueExtraLineup.Release(Player);
        Save();
    }

    private ValueTask<bool> ActivateLineup(List<int> baseAvatarIds)
    {
        return RogueExtraLineup.Activate(Player, ExtraLineupType.LineupMagicRogue, baseAvatarIds,
            new RogueLineupStart(null, RogueExtraLineup.HalfSp, RogueLineupRefill.RequestedFormalAvatars));
    }

    #endregion

    #region Scene

    /// <summary>
    ///     Entity loader for the Rogue plane scene of the current magic room, or null for any other Rogue plane.
    ///     Also stamps the scene's game mode (MagicRogue = 20; the plane's own type is 5).
    /// </summary>
    public SceneEntityLoader? CreateEntityLoader(SceneInstance scene)
    {
        var run = RunInstance;
        var room = run?.Status == RogueMagicStates.RunDoing ? run.CurRoom() : null;
        if (run == null || room == null || room.PlaneId != scene.PlaneId) return null;

        scene.GameModeType = GameModeTypeEnum.MagicRogue;
        return new RogueMagicEntityLoader(scene, run, room);
    }

    public async ValueTask<bool> EnterRoomScene(RogueMagicRoomData? room, bool sendPacket)
    {
        if (room == null || room.EntranceId == 0) return false;

        await Player.EnterSceneByEntranceId(room.EntranceId, room.AnchorGroup, room.AnchorId, sendPacket);
        return Player.SceneInstance?.PlaneId == room.PlaneId && Player.Data.EntryId == room.EntranceId;
    }

    #endregion

    #region Battle

    public void OnBattleStart(BattleInstance battle)
    {
        var run = RunInstance;
        if (run is not { Status: RogueMagicStates.RunDoing }) return;
        // A persisted run must not claim ordinary world fights fought outside the magic rooms.
        if (Player.SceneInstance?.GameModeType != GameModeTypeEnum.MagicRogue) return;

        // EndBattle keys "rogue room kill, no open-world drops" off this flag.
        battle.IsTournRogue = true;
        battle.MagicInfo = RogueMagicProtoBuilder.BuildBattleInfo(run);
        battle.OnBattleEnd += OnBattleEnd;
    }

    private async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        var run = RunInstance;
        if (run is not { Status: RogueMagicStates.RunDoing }) return;
        if (req.EndStatus != BattleEndStatus.BattleEndWin) return;

        var room = run.CurRoom();
        if (room == null) return;

        // Rounds past the standard limit spend the run's extra-round budget.
        var usedRounds = (int)(req.Stt?.RoundCnt ?? 0);
        if (usedRounds > run.BasicRoundCnt)
            run.ExtraRoundCnt -= Math.Min(usedRounds - run.BasicRoundCnt, run.ExtraRoundCnt);

        var stages = Math.Max(1, battle.Stages.Count);
        if (room.RoomType == (int)RogueMagicRoomTypeEnum.Boss)
        {
            EnqueueDraft(run, RogueMagicPendingKind.Scepter, 1, [], stages);
            EnqueueDraft(run, RogueMagicPendingKind.MagicUnit, 1, [(int)RogueMagicUnitCategoryEnum.Ultra], stages);
            run.Coin += Random.Shared.Next(150, 200) * stages;
        }
        else
        {
            EnqueueDraft(run, RogueMagicPendingKind.MagicUnit, 1, [(int)RogueMagicUnitCategoryEnum.Common], stages);
            run.Coin += Random.Shared.Next(30, 50) * stages;
        }

        RollFront(run);
        Save();

        await SendCoinNotify();
        await SendPendingNotify();
    }

    #endregion

    #region Pending Action

    private static void EnqueueDraft(RogueMagicRunData run, RogueMagicPendingKind kind, int level,
        List<int> unitCategories, int amount)
    {
        for (var i = 0; i < amount; i++)
        {
            run.ActionSeq += QueuePositionStep;
            run.PendingActions.Add(new RogueMagicPendingActionData
            {
                QueuePos = run.ActionSeq,
                Kind = kind,
                DraftLevel = level,
                UnitCategories = unitCategories
            });
        }
    }

    // Options roll when an action reaches the front, so a later draft never offers what an earlier pick took.
    private static void RollFront(RogueMagicRunData run)
    {
        while (run.PendingActions.Count > 0)
        {
            var action = run.PendingActions[0];
            if (action.OptionIds.Count > 0) return;

            var pool = action.Kind == RogueMagicPendingKind.Scepter ? ScepterPool(run, action) : UnitPool(run, action);
            var picks = pool.OrderBy(_ => Random.Shared.Next()).Take(DraftOptionCount).ToList();
            if (picks.Count == 0)
            {
                run.PendingActions.RemoveAt(0);
                continue;
            }

            action.OptionIds = picks.Select(pick => pick.Id).ToList();
            action.OptionLevels = picks.Select(pick => pick.Level).ToList();
            return;
        }
    }

    private static List<(int Id, int Level)> ScepterPool(RogueMagicRunData run, RogueMagicPendingActionData action)
    {
        var owned = run.Scepters.Select(scepter => scepter.ScepterId).ToHashSet();
        return GameData.RogueMagicScepterData.Values
            .Where(excel => excel.ScepterLevel == action.DraftLevel && !owned.Contains(excel.ScepterID))
            .Select(excel => (excel.ScepterID, excel.ScepterLevel))
            .ToList();
    }

    private static List<(int Id, int Level)> UnitPool(RogueMagicRunData run, RogueMagicPendingActionData action)
    {
        var owned = run.Units.Select(unit => unit.UnitId).ToHashSet();
        return GameData.RogueMagicUnitData.Values
            .Where(excel => excel.MagicUnitLevel == action.DraftLevel &&
                            action.UnitCategories.Contains((int)excel.MagicUnitCategory) &&
                            excel.MagicUnitType != RogueMagicMountTypeEnum.Active &&
                            !owned.Contains(excel.MagicUnitID))
            .Select(excel => (excel.MagicUnitID, excel.MagicUnitLevel))
            .ToList();
    }

    private static void AddUnit(RogueMagicRunData run, int unitId, int level)
    {
        run.Units.Add(new RogueMagicUnitData { UniqueId = run.NextUnitUniqueId++, UnitId = unitId, Level = level });
        ComposeUnits(run);
    }

    // Three copies of a unit at one level merge into the next level; each merge opens a draft in that unit's category.
    private static void ComposeUnits(RogueMagicRunData run)
    {
        var groups = run.Units
            .GroupBy(unit => (unit.UnitId, unit.Level))
            .Where(group => group.Key.Level < MaxUnitLevel && group.Count() >= 3)
            .ToList();

        foreach (var group in groups)
        {
            if (!GameData.RogueMagicUnitData.TryGetValue(group.Key.UnitId * 100 + group.Key.Level + 1, out var next))
                continue;

            var merges = group.Count() / 3;
            foreach (var unit in group.Take(merges * 3)) run.Units.Remove(unit);
            for (var i = 0; i < merges; i++)
                run.Units.Add(new RogueMagicUnitData
                {
                    UniqueId = run.NextUnitUniqueId++,
                    UnitId = next.MagicUnitID,
                    Level = next.MagicUnitLevel
                });

            EnqueueDraft(run, RogueMagicPendingKind.MagicUnit, group.Key.Level, [(int)next.MagicUnitCategory], merges);
        }
    }

    /// <summary>
    ///     Resolves a HandleRogueCommonPendingActionCsReq (5611) answer to an open RogueMagic draft. Returns null when
    ///     the run does not own the answer so the caller can fall through to the other rogue modes. Pushes the next
    ///     action itself; the caller only sends the returned ScRsp.
    /// </summary>
    public async ValueTask<HandleRogueCommonPendingActionScRsp?> HandlePendingAction(OFEDNNJIJFL req)
    {
        var run = RunInstance;
        if (run is not { Status: RogueMagicStates.RunDoing }) return null;
        if (Player.SceneInstance?.GameModeType != GameModeTypeEnum.MagicRogue) return null;

        // Both scepter-carrying answer arms are accepted; the draft's own options gate what is granted.
        var scepter = req.JPGOMLFBMGP?.NBKMDMGMOGK ?? req.MELNIIHHLMC?.PJJNMNJMKBG;
        var unit = req.CDBKLEJLJFE?.ACCCINPPINC;

        RogueMagicPendingKind kind;
        int id;
        if (scepter != null)
        {
            kind = RogueMagicPendingKind.Scepter;
            id = (int)scepter.GCAHKLNGFCK;
        }
        else if (unit != null)
        {
            kind = RogueMagicPendingKind.MagicUnit;
            id = (int)unit.CFKKKPNKHEL;
        }
        else
        {
            return null;
        }

        var front = run.PendingActions.FirstOrDefault();
        if (front == null || front.Kind != kind) return null;

        // A bogus option still consumes the action, or the select dialog wedges.
        run.PendingActions.RemoveAt(0);
        var index = front.OptionIds.IndexOf(id);
        var ret = Retcode.RetSucc;
        if (index < 0)
            ret = Retcode.RetRogueSelectBuffNotExist;
        else if (kind == RogueMagicPendingKind.Scepter)
            run.Scepters.Add(new RogueMagicScepterData { ScepterId = id, Level = front.OptionLevels[index] });
        else
            AddUnit(run, id, front.OptionLevels[index]);

        RollFront(run);
        Save();
        await SendPendingNotify();

        return new HandleRogueCommonPendingActionScRsp
        {
            Retcode = (uint)ret,
            QueuePosition = req.MPLGEEGOAMC,
            BFNEPPCNOOI = req.MPLGEEGOAMC
        };
    }

    #endregion

    #region Notify

    public async ValueTask SendLevelNotify(List<int>? roomIndexes = null)
    {
        var run = RunInstance;
        var layer = run?.CurLayer();
        if (run == null || layer == null) return;
        await Player.SendPacket(BasePacket.Create(CmdIds.RogueMagicLevelInfoUpdateScNotify,
            RogueMagicProtoBuilder.BuildLevelNotify(run, layer, roomIndexes)));
    }

    public async ValueTask SendPendingNotify()
    {
        var run = RunInstance;
        if (run == null || run.PendingActions.Count == 0) return;
        await Player.SendPacket(BasePacket.Create(CmdIds.SyncRogueCommonPendingActionScNotify,
            RogueMagicProtoBuilder.BuildPendingNotify(run)));
    }

    public async ValueTask SendCoinNotify()
    {
        if (RunInstance == null) return;
        await Player.SendPacket(BasePacket.Create(CmdIds.CommonRogueVirtualItemInfoScNotify,
            RogueMagicProtoBuilder.BuildCoinNotify(RunInstance)));
    }

    #endregion
}
