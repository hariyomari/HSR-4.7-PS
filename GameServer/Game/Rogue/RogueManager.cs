using March7thHoney.Enums.Mission;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Rogue;
using March7thHoney.Enums.Rogue;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.RogueCommon;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Server.Packet.Send.Rogue;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Rogue;

public partial class RogueManager : BasePlayerManager
{
    // Official 4.4 capture grants 50 (Java/C# references say 100).
    private const int StartCoinCount = 50;
    private const int TalentCoinItemId = 32;

    // The official login capture lists aeons 1..7 although RogueAeon has 9 rows.
    private const int AeonLimit = 7;
    private const int RoomTypeElite = 6;
    private const int RoomTypeBoss = 7;
    // A query type is the field number of its CCKJAEDDDCL oneof arm, and versions renumber the arms (4.5 put the
    // function-unlock arm BBEKBJPHEJK at 15, 4.6 at 1, 4.7 at 10), so take it from the generated message, never a literal.
    private const uint CommonQueryTypeFunctionUnlock = CCKJAEDDDCL.BBEKBJPHEJKFieldNumber;

    public RogueManager(PlayerInstance player) : base(player)
    {
        RogueData = DatabaseHelper.Instance!.GetInstanceOrCreateNew<RogueData>(player.Uid);
        ResurrectInstance();
    }

    public RogueData RogueData { get; }
    public RogueInstance? RogueInstance { get; private set; }

    #region Persistence

    private void SaveRun()
    {
        RogueData.RunBlob = RogueRunBlob.Serialize(RogueInstance);
        DatabaseHelper.MarkDirty(Player.Uid);
    }

    private void ResurrectInstance()
    {
        if (!RogueRunBlob.TryLoad<RogueInstance>(RogueData.RunBlob, out var inst)) return;

        // Earlier builds marked the single-room tutorial won after its first encounter. Keep its real
        // task progress and selected blessings when recovering that unfinished run.
        if (inst is { Status: RogueStatus.Doing, IsWin: true } && IsFirstTutorial(inst) &&
            Player.MissionManager!.GetMainMissionStatus(4030001) == MissionPhaseEnum.Accept &&
            !IsTutorialGroupDefeated(inst, 10))
        {
            inst.IsWin = false;
            RogueInstance = inst;
            SaveRun();
            return;
        }

        // A tutorial victory still needs Quit/Leave for its mission settlement. Older builds left
        // a tutorial room in Play and omitted the victory notify; keep that saved win available to settle.
        if (inst is { Status: RogueStatus.Doing, IsWin: true } && HasPendingTutorialSettlement(inst) &&
            inst.GetCurrentRoom() is { } tutorialRoom)
        {
            tutorialRoom.Status = RogueRoomStatus.Finish;
            inst.ExploreWin = true;
            RogueInstance = inst;
            SaveRun();
            return;
        }

        // A run that already won but was never left is dropped: resuming it strands the client on a finished run.
        if (inst is { Status: RogueStatus.Doing, IsWin: false })
        {
            RogueInstance = inst;
            return;
        }

        RogueData.RunBlob = null;
        DatabaseHelper.MarkDirty(Player.Uid);
    }

    #endregion

    #region Run lifecycle

    public async ValueTask<Retcode> StartRogue(int areaId, int aeonId, List<int> avatarIds, bool trial)
    {
        // A resurrected stale run is abandoned: the official instance is transient, a relog always starts fresh.
        if (RogueInstance != null) await ClearRun();

        if (!GameData.RogueAreaConfigData.TryGetValue(areaId, out var area)) return Retcode.RetRogueAreaInvalid;

        var inst = new RogueInstance
        {
            AreaId = areaId,
            MapId = ResolveRunMapId(area),
            AeonId = aeonId,
            Coin = StartCoinCount,
            Status = RogueStatus.Doing,
            ActionSeq = 1,
            BaseAvatarIds = avatarIds,
            Trial = trial
        };
        inst.InitRooms();
        if (inst.StartSiteId == 0 || inst.Rooms.Count == 0) return Retcode.RetRogueAreaInvalid;

        if (!await ActivateRogueLineup(avatarIds)) return Retcode.RetRogueStatusNotMatch;

        var room = inst.EnterRoom(inst.StartSiteId);
        RogueInstance = inst;
        if (room == null || !await EnterRoomScene(room))
        {
            await ClearRun();
            return Retcode.RetRogueAreaInvalid;
        }

        SaveRun();
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.RogueEnterRoomWithLevel, inst);
        return Retcode.RetSucc;
    }

    public async ValueTask<(Retcode Ret, RogueRoomData? Room)> EnterRoom(int siteId)
    {
        if (RogueInstance == null || IsTutorial(RogueInstance) &&
            (!IsTutorialRoomCleared(RogueInstance) || RogueInstance.BuffSelect?.HasSelection == true))
            return (Retcode.RetRogueStatusNotMatch, null);

        var room = RogueInstance.EnterRoom(siteId);
        if (room == null) return (Retcode.RetRogueStatusNotMatch, null);
        if (!await EnterRoomScene(room)) return (Retcode.RetFail, null);

        SaveRun();
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.RogueEnterRoomWithLevel, RogueInstance);
        return (Retcode.RetSucc, room);
    }

    public void QuitRogue()
    {
        if (RogueInstance == null) return;
        RogueInstance.Status = RogueStatus.Finish;
        SaveRun();
    }

    public async ValueTask<RogueInstance?> FinishRun()
    {
        var inst = RogueInstance;
        if (inst == null) return null;

        var area = GameData.RogueAreaConfigData.GetValueOrDefault(inst.AreaId);
        var completedRooms = inst.IsWin ? inst.CurrentRoomProgress : Math.Max(0, inst.CurrentRoomProgress - 1);
        inst.RoomScore = area?.ScoreMap.GetValueOrDefault(completedRooms) ?? 0;
        inst.EarnedTalentCoin = inst.RoomScore / 10;
        inst.Status = RogueStatus.Finish;

        if (inst.EarnedTalentCoin > 0)
            await Player.InventoryManager!.AddItem(TalentCoinItemId, inst.EarnedTalentCoin);

        if (area != null && !RogueData.FirstPassAreas.Contains(inst.AreaId))
            RogueData.FirstPassAreas.Add(inst.AreaId);
        RogueData.ExploreScore += inst.RoomScore;

        // A won run advances the area's difficulty tier while the next tier's map exists.
        if (inst.IsWin && area != null)
        {
            var tier = RogueData.AreaDifficulty.GetValueOrDefault(inst.AreaId);
            if (tier == 0) tier = Math.Max(1, area.Difficulty);
            if (GameData.RogueMapData.ContainsKey(area.AreaProgress * 100 + tier + 1))
                RogueData.AreaDifficulty[inst.AreaId] = tier + 1;
        }

        RogueData.FinishCnt++;
        SaveRun();
        if (inst.IsWin)
            await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.RoguePassAreaProgress, inst);
        await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.RogueFinishCnt);
        return inst;
    }

    public async ValueTask<bool> LeaveRun()
    {
        if (RogueInstance == null) return false;

        await ClearRun();
        return await ExitToLobby();
    }

    /// <summary>
    ///     Leaves a Simulated Universe room whose run no longer exists. A won-but-not-left run is dropped on
    ///     relog while login still restores its room scene, so LeaveRogue has no run to settle.
    /// </summary>
    public async ValueTask<bool> LeaveOrphanedRoom()
    {
        if (RogueInstance != null) return false;
        if (Player.SceneInstance is not
            { Excel.PlaneType: PlaneTypeEnum.Rogue, GameModeType: GameModeTypeEnum.RogueExplore }) return false;

        return await ExitToLobby();
    }

    private async ValueTask<bool> ExitToLobby()
    {
        await RogueExtraLineup.Release(Player);
        await Player.EnterScene(RogueLobby.EntranceId, 0, false);
        return Player.SceneInstance?.EntryId == RogueLobby.EntranceId;
    }

    private async ValueTask ClearRun()
    {
        RogueInstance = null;
        await RogueExtraLineup.Release(Player);
        SaveRun();
    }

    private int ResolveRunMapId(RogueAreaConfigExcel area)
    {
        var tier = RogueData.AreaDifficulty.GetValueOrDefault(area.RogueAreaID);
        if (tier == 0) tier = Math.Max(1, area.Difficulty);
        var mapId = area.AreaProgress * 100 + tier;
        return GameData.RogueMapData.ContainsKey(mapId) ? mapId : area.MapId;
    }

    /// <summary>Restore an unfinished room's saved roster after the generic login extra-lineup cleanup.</summary>
    public async ValueTask<bool> RestoreLineupAtLogin()
    {
        if (RogueInstance is not { Status: RogueStatus.Doing } inst ||
            inst.IsWin && !(HasPendingTutorialSettlement(inst) && inst.GetCurrentRoom()?.Status == RogueRoomStatus.Finish) ||
            inst.BaseAvatarIds.Count == 0 || GetActiveRoomExcel(Player.Data.EntryId) == null) return false;

        var lineupManager = Player.LineupManager!;
        var lineup = lineupManager.GetExtraLineup(ExtraLineupType.LineupRogue);
        if (lineup?.BaseAvatars == null || !lineup.BaseAvatars
                .Select(slot => slot.SpecialAvatarId > 0 ? slot.SpecialAvatarId : slot.BaseAvatarId)
                .SequenceEqual(inst.BaseAvatarIds))
        {
            lineupManager.SetExtraLineup(ExtraLineupType.LineupRogue, inst.BaseAvatarIds);
            lineup = lineupManager.GetExtraLineup(ExtraLineupType.LineupRogue);
            if (lineup?.BaseAvatars is not { Count: > 0 }) return false;
            lineup.Mp = 3;
        }

        // Unlike a fresh run, resuming must retain the trial/formal avatars' saved HP and energy.
        await lineupManager.SetExtraLineup(ExtraLineupType.LineupRogue, false);
        lineupManager.RecalculateExtraMpCount();
        DatabaseHelper.MarkDirty(Player.Uid);
        return true;
    }

    private ValueTask<bool> ActivateRogueLineup(List<int> avatarIds)
    {
        // The official rogue lineup starts at 3 technique points, not the challenge full reset, and a trial roster
        // is refilled like an owned one.
        return RogueExtraLineup.Activate(Player, ExtraLineupType.LineupRogue, avatarIds,
            new RogueLineupStart(3, RogueExtraLineup.HalfSp, RogueLineupRefill.LineupSlots));
    }

    private async ValueTask<bool> EnterRoomScene(RogueRoomData room)
    {
        if (!GameData.RogueRoomData.TryGetValue(room.RoomId, out var roomExcel)) return false;

        // Spawn at the room's group anchor; the entrance's own start anchor is a positionless placeholder.
        await Player.EnterSceneByEntranceId(roomExcel.MapEntrance, roomExcel.GroupID, 1, false);
        return Player.SceneInstance?.EntryId == roomExcel.MapEntrance;
    }

    #endregion

    #region Scene

    public RogueRoomExcel? GetActiveRoomExcel(int entryId)
    {
        if (RogueInstance is not { Status: RogueStatus.Doing } inst) return null;
        var room = inst.GetCurrentRoom();
        if (room == null || !GameData.RogueRoomData.TryGetValue(room.RoomId, out var excel)) return null;
        return excel.MapEntrance == entryId ? excel : null;
    }

    /// <summary>The rooms the current room's exit portals lead to, in ascending site order.</summary>
    public List<RogueDoorTarget> GetRoomExitTargets()
    {
        if (RogueInstance?.GetCurrentRoom() is not { } room) return [];
        return room.NextSiteIds
            .Order()
            .Select(siteId => RogueInstance.Rooms.GetValueOrDefault(siteId))
            .OfType<RogueRoomData>()
            .Select(next => new RogueDoorTarget(next.RoomId, next.SiteId))
            .ToList();
    }

    /// <summary>
    ///     Opens the current room's exit portals once the room is cleared and no blessing draft is pending.
    ///     Official 4.5: the portal is re-sent with prop state Open right after the draft resolves, before
    ///     HandleRogueCommonPendingActionScRsp; the client only sends EnterRogueMapRoomCsReq through an open portal.
    /// </summary>
    public async ValueTask<bool> OpenRoomExit()
    {
        if (RogueInstance is not { Status: RogueStatus.Doing } inst || inst.BuffSelect?.HasSelection == true ||
            IsTutorial(inst) && !IsTutorialRoomCleared(inst))
            return false;
        var scene = Player.SceneInstance;
        var roomExcel = scene == null ? null : GetActiveRoomExcel(scene.EntryId);
        if (scene == null || roomExcel == null) return false;

        var doors = scene.GetEntitiesInGroup<EntityProp>(roomExcel.GroupID)
            .Where(door => door.RogueDoorTarget != null && door.State != PropStateEnum.Open)
            .ToList();
        if (doors.Count == 0) return false;

        foreach (var door in doors)
            door.State = ScenePropRules.RogueDoorOpenState(door.Excel, PropStateEnum.Open);
        await Player.SendPacket(new PacketSceneGroupRefreshScNotify(Player, doors.Cast<BaseGameEntity>().ToList(),
            null, SceneGroupRefreshType.Afibfmafncc));
        return true;
    }

    public int GetCurrentMonsterLevel()
    {
        if (RogueInstance == null) return 0;
        return GameData.RogueMapData.TryGetValue(RogueInstance.MapId, out var sites) &&
               sites.TryGetValue(RogueInstance.CurrentSiteId, out var site) && site.LevelList.Count > 0
            ? site.LevelList[0]
            : 0;
    }

    public static int ResolveRogueMonsterId(int content)
    {
        // Dump corrections verified against official captures: the tutorial elite ships 1006 and the
        // map-2 boss 2007, neither resolves; the official server uses groups 1101 and 2101.
        content = content switch
        {
            1006 => 1101,
            2007 => 2101,
            _ => content
        };

        if (GameData.RogueMonsterGroupData.TryGetValue(content, out var group) &&
            group.RogueMonsterListAndWeight.Count > 0)
            return PickWeighted(group.RogueMonsterListAndWeight);

        var fallbackId = content * 10 + 1;
        return GameData.RogueMonsterData.ContainsKey(fallbackId) ? fallbackId : 0;
    }

    private static int PickWeighted(Dictionary<string, double> weights)
    {
        var roll = Random.Shared.NextDouble() * weights.Values.Sum();
        foreach (var (id, weight) in weights)
        {
            roll -= weight;
            if (roll < 0) return int.TryParse(id, out var value) ? value : 0;
        }

        return 0;
    }

    #endregion

    #region Battle

    public void OnBattleStart(BattleInstance battle)
    {
        if (RogueInstance is not { Status: RogueStatus.Doing } inst) return;
        if (Player.SceneInstance?.Excel.PlaneType != PlaneTypeEnum.Rogue) return;
        // Tourn, chess and magic rooms are Rogue planes too; their loaders retag the scene, so only claim RogueExplore.
        if (Player.SceneInstance.GameModeType != GameModeTypeEnum.RogueExplore) return;

        // Despite its generated name, battle_rogue_magic_info also identifies classic Rogue.
        // Official 4.6 tutorial battle seq 15037 carries mode 1 in this shared envelope.
        battle.MagicInfo = new BattleRogueMagicInfo
        {
            OOPCPMANCBO = new IJLEFNJPFKP { RogueMode = 1 }
        };

        foreach (var buff in inst.Buffs.Values)
        {
            battle.Buffs.Add(new MazeBuff(buff.BuffId, buff.Level, -1) { WaveFlag = -1 });

            // The path-resonance buff also carries its aeon's battle event, starting full.
            if (GameData.RogueBuffData.GetValueOrDefault(buff.BuffId * 100 + buff.Level) is RogueBuffExcel
                {
                    BattleEventBuffType: RogueBuffAeonTypeEnum.BattleEventBuff
                } excel &&
                GameData.RogueBattleEventData.TryGetValue(excel.RogueBuffType, out var battleEvent))
                battle.BattleEvents[buff.BuffId] = new BattleEventInstance(battleEvent.BattleEventID, 10000, 10000);
        }

        var level = GetCurrentMonsterLevel();
        if (level > 0) battle.CustomLevel = level;

        battle.OnBattleEnd += OnBattleEnd;
    }

    private async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        if (RogueInstance is not { Status: RogueStatus.Doing } inst) return;

        if (req.EndStatus != BattleEndStatus.BattleEndWin)
        {
            inst.Status = RogueStatus.Finish;
            SaveRun();
            return;
        }

        var room = inst.GetCurrentRoom();
        if (room == null) return;

        await RefreshTutorialProps(afterBattle: true);

        // The official win flow sends an empty plane event before the draft to mark the battle plane settled.
        await Player.SendPacket(new PacketScenePlaneEventScNotify(new ItemList()));

        // Tutorial room 100 is classified as a normal room but contains three encounters and a boss.
        // Its actual boss victory ends exploration without a draft (official 4.6 seq 15464-15465).
        var tutorialBossWin = IsFirstTutorial(inst) && battle.EntityMonsters.Any(monster =>
            monster is { GroupId: 10, InstId: 200001, IsAlive: false });
        if (room.NextSiteIds.Count == 0 && (!IsTutorial(inst) || IsTutorialRoomCleared(inst)))
            inst.IsWin = true;

        var coinGained = false;
        if (room.RoomType == RoomTypeBoss || tutorialBossWin)
        {
            inst.BuffSelect = null;
            inst.ExploreWin = true;
        }
        else
        {
            inst.Coin += Random.Shared.Next(20, 41) * Math.Max(1, battle.EntityMonsters.Count);
            coinGained = true;

            var pool = ResolveDraftPool(room, battle);
            if (pool.Count == 0)
            {
                // A last-room win with no draft pool is the leader battle that completes the explore.
                if (inst.IsWin) inst.ExploreWin = true;
            }
            else
            {
                inst.ActionSeq += 2;
                inst.BuffSelect = RogueBuffSelectMenu.Create(inst, pool);
            }
        }

        await FinishTutorialRoom(inst);
        if (inst is { IsWin: true, ExploreWin: true } && room.Status != RogueRoomStatus.Finish)
        {
            room.Status = RogueRoomStatus.Finish;
            await Player.SendPacket(new PacketSyncRogueMapRoomScNotify(inst));
            await Player.MissionManager!.TriggerCountEvent(MissionFinishTypeEnum.RogueExploreRoomFinishCnt);
        }
        SaveRun();

        if (inst.BuffSelect?.HasSelection == true)
        {
            // The 4.5 client needs the identical draft notify twice to arm RogueSelectBuffPage.
            await Player.SendPacket(new PacketSyncRogueCommonPendingActionScNotify(BuildPendingAction(inst)));
            await Player.SendPacket(new PacketSyncRogueCommonPendingActionScNotify(BuildPendingAction(inst)));
        }
        else if (!inst.IsWin)
        {
            // A win with nothing to draft clears the room on the spot; a draft opens the exit when it resolves.
            await OpenRoomExit();
        }

        if (coinGained) await Player.SendPacket(new PacketSyncRogueVirtualItemInfoScNotify(inst.Coin));

        if (inst is { IsWin: true, ExploreWin: true })
        {
            inst.ExploreWin = false;
            await Player.SendPacket(new PacketSyncRogueExploreWinScNotify());
        }
    }

    private static List<BaseRogueBuffExcel> ResolveDraftPool(RogueRoomData room, BattleInstance battle)
    {
        var groupId = room.RoomType == RoomTypeElite ? RogueDraftGroups.Elite : RogueDraftGroups.Normal;

        // The official draft pool is the killed group's room content + 8000 when that pool exists.
        if (GameData.RogueRoomData.TryGetValue(room.RoomId, out var roomExcel))
            foreach (var killedGroup in battle.EntityMonsters.Select(x => x.GroupId).Distinct())
            {
                var content = roomExcel.GroupWithContent.GetValueOrDefault(killedGroup);
                if (content == 0 || GetBuffPool(content + 8000).Count == 0) continue;
                groupId = content + 8000;
                break;
            }

        return GetBuffPool(groupId);
    }

    private static List<BaseRogueBuffExcel> GetBuffPool(int groupId)
    {
        return GameData.RogueBuffGroupData.TryGetValue(groupId, out var group)
            ? group.BuffList.Where(x => x is RogueBuffExcel).ToList()
            : [];
    }

    public Retcode? SelectBuff(int buffId)
    {
        if (RogueInstance?.BuffSelect is not { HasSelection: true } menu) return null;
        if (buffId == 0 || menu.Select(RogueInstance, buffId) == null) return Retcode.RetRogueSelectBuffNotExist;

        TryOfferAeonBuff(RogueInstance);
        SaveRun();
        return Retcode.RetSucc;
    }

    public GGGBAIAPDLO? RerollBuff()
    {
        if (RogueInstance?.BuffSelect is not { } menu || !menu.Reroll(RogueInstance)) return null;

        SaveRun();
        return BuildBuffSelectInfo(menu);
    }

    // Path resonance (legacy thresholds): the aeon buff after 3 same-path blessings, then enhance drafts at
    // 6/10/14, at most 4 in total. Counts come from owned buffs so nothing extra is persisted.
    private static void TryOfferAeonBuff(RogueInstance inst)
    {
        if (inst.BuffSelect?.HasSelection == true) return;
        if (!GameData.RogueAeonData.TryGetValue(inst.AeonId, out var aeon)) return;

        var owned = inst.Buffs.Values
            .Select(x => GameData.RogueBuffData.GetValueOrDefault(x.BuffId * 100 + x.Level))
            .OfType<RogueBuffExcel>()
            .ToList();
        var pathCount = owned.Count(x => x.RogueBuffType == aeon.RogueBuffType && !x.IsAeonBuff);
        var aeonCount = owned.Count(x => x.IsAeonBuff);
        var required = aeonCount switch
        {
            0 => 3,
            1 => 6,
            2 => 10,
            3 => 14,
            _ => int.MaxValue
        };
        if (pathCount < required) return;

        List<BaseRogueBuffExcel> pool = aeonCount == 0
            ? GameData.RogueAeonBuffData.TryGetValue(inst.AeonId, out var aeonBuff) ? [aeonBuff] : []
            : [.. GameData.RogueAeonEnhanceData.GetValueOrDefault(inst.AeonId, [])];

        // Aeon drafts offer every candidate and cannot be rerolled.
        var menu = RogueBuffSelectMenu.Create(inst, pool, pool.Count, 0);
        if (!menu.HasSelection) return;

        inst.ActionSeq += 2;
        inst.BuffSelect = menu;
    }

    #endregion

    #region Talent

    public bool HasTalent(int talentId)
    {
        return RogueData.Talents.Contains(talentId);
    }

    public async ValueTask<bool> EnableTalent(int talentId)
    {
        if (!GameData.RogueTalentData.TryGetValue(talentId, out var talent)) return false;
        if (HasTalent(talentId)) return true;

        var inventory = Player.InventoryManager!;
        if (talent.Cost.Any(cost => (inventory.GetItem(cost.ItemID)?.Count ?? 0) < cost.ItemNum)) return false;
        foreach (var cost in talent.Cost) await inventory.RemoveItem(cost.ItemID, cost.ItemNum);

        RogueData.Talents.Add(talentId);
        DatabaseHelper.MarkDirty(Player.Uid);
        return true;
    }

    #endregion

    #region Proto

    public AGEHGBKOBEF BuildRogueGameInfo()
    {
        var info = new AGEHGBKOBEF { LFLCDNGALME = BuildRogueInfoData() };
        if (RogueInstance != null) info.CFDIKLBINHK = BuildCurrentInfo(RogueInstance);
        return info;
    }

    private EDOIEMCFFCA BuildRogueInfoData()
    {
        var data = new EDOIEMCFFCA
        {
            NIKDPODBIEF = new MBCBDJNKFEB(),
            HHFAAFOBKME = new CGNNMPIKBBB(),
            ACLKMFCDMED = BuildAreaInfo(),
            AEFMMOHKCHJ = BuildAeonInfo()
        };

        var season = GetCurrentSeason();
        if (season != null)
        {
            data.HHFAAFOBKME.LJNGPCKPJLF = (uint)season.RogueSeason;
            data.HHFAAFOBKME.BeginTime = new DateTimeOffset(season.BeginTimeDate).ToUnixTimeSeconds();
            data.HHFAAFOBKME.EndTime = new DateTimeOffset(season.EndTimeDate).ToUnixTimeSeconds();
        }

        return data;
    }

    private static RogueManagerExcel? GetCurrentSeason()
    {
        var now = DateTime.Now;
        return GameData.RogueManagerData.Values
            .Where(x => x.BeginTimeDate <= now && now < x.EndTimeDate)
            .OrderBy(x => x.BeginTimeDate)
            .FirstOrDefault();
    }

    private GOIEJOCFKEE BuildAreaInfo()
    {
        var info = new GOIEJOCFKEE();
        var areaIds = GetCurrentSeason()?.RogueAreaIDList ?? [.. GameData.RogueAreaConfigData.Keys];
        foreach (var areaId in areaIds)
        {
            var firstPassed = RogueData.FirstPassAreas.Contains(areaId);

            // Lock (0) is never sent: proto3 drops a zero enum and the entrance panel NREs on the missing field.
            var row = new CAJEBOPPMCJ
            {
                AreaId = (uint)areaId,
                ALAJJMKOMML = firstPassed,
                AreaStatus = firstPassed ? RogueAreaStatus.FirstPass : RogueAreaStatus.Unlock
            };

            if (RogueInstance?.AreaId == areaId)
            {
                row.NHDLEFAAPOB = RogueInstance.Status;
                row.MapId = (uint)RogueInstance.MapId;
                row.KNDHMNBIIDF = (uint)RogueInstance.CurrentRoomProgress;
            }

            info.OJGOAOPLMLB.Add(row);
        }

        return info;
    }

    private static GFGHGCIMKGA BuildAeonInfo()
    {
        var info = new GFGHGCIMKGA();
        info.OPAEGCBNACJ.AddRange(GameData.RogueAeonData.Keys
            .Where(id => id is > 0 and <= AeonLimit)
            .OrderBy(id => id)
            .Select(id => (uint)id));
        return info;
    }

    private static HNKAOFDGLIE BuildCurrentInfo(RogueInstance inst)
    {
        return new HNKAOFDGLIE
        {
            Status = inst.Status,
            ILOFGCAFHAH = inst.IsWin,
            COOOMDKGODF = BuildMapInfo(inst),
            DHEKLJDBNNP = new AGEOGDOGDMM { RogueMoney = (uint)inst.Coin },
            DNPCCFFFBHN = BuildMiracleInfo(inst),
            FMIGOKIDKJL = BuildPendingAction(inst),
            EIOHFJMFOKF = BuildLineupInfo(inst),
            AEFMMOHKCHJ = new CIFCAIEDOLK(),
            HABJHGPFGEB = BuildBuffList(inst)
        };
    }

    private static DNDMLICBKEF BuildMapInfo(RogueInstance inst)
    {
        var map = new DNDMLICBKEF
        {
            ANHDBMOPGDC = (uint)inst.CurrentSiteId,
            HPKMGKPPELC = (uint)(inst.GetCurrentRoom()?.RoomId ?? 0),
            AreaId = (uint)inst.AreaId,
            MapId = (uint)inst.MapId
        };
        foreach (var room in inst.Rooms.Values.OrderBy(x => x.SiteId))
            map.LLNICHDDIAN.Add(BuildRoom(room, room.Status));
        return map;
    }

    public static PAIJCBOKIFO BuildRoom(RogueRoomData room, RogueRoomStatus status)
    {
        return new PAIJCBOKIFO
        {
            RoomId = (uint)room.RoomId,
            MAJKFIMPOOF = (uint)room.SiteId,
            CAGDNHCOCKM = status
        };
    }

    private static HIKOBOMBMPB BuildMiracleInfo(RogueInstance inst)
    {
        var list = new OEDNNHAOHAB();
        foreach (var miracle in inst.Miracles.Values)
            list.MAJGMMCOCIG.Add(new EFDAIBDBFEA { NPHAJEKCBKF = (uint)miracle.MiracleId, TotalCount = 1 });
        return new HIKOBOMBMPB { DNPCCFFFBHN = list };
    }

    public static INHBMHCJJCN BuildPendingAction(RogueInstance inst)
    {
        var action = new INHBMHCJJCN();
        if (inst.BuffSelect is not { HasSelection: true } menu) return action;

        action.QueuePosition = (uint)inst.ActionSeq;
        action.Detail = new DCFPEIMAMCN { PCHGEGFBKLF = BuildBuffSelectInfo(menu) };
        return action;
    }

    private static GGGBAIAPDLO BuildBuffSelectInfo(RogueBuffSelectMenu menu)
    {
        // Byte-exact with the official 4.5 draft: no source_type and no zero-valued counters.
        var info = new GGGBAIAPDLO
        {
            CanRoll = menu.CanRoll,
            NKGGAOJLCMD = 1,
            EIALMEADLGF = 1,
            FIMMFJCKCNK = 1
        };
        info.EBMEEKJGPPM.AddRange(menu.SelectBuffs.Select(x => (uint)x.RogueBuffType).Distinct());
        foreach (var buff in menu.SelectBuffs)
            info.LEOFNINDJDB.Add(new KJAMNOGFAMJ { BuffId = (uint)buff.MazeBuffID, HAGDKKBAAKK = (uint)buff.MazeBuffLevel });
        return info;
    }

    private static ENOBKLGCHOD BuildLineupInfo(RogueInstance inst)
    {
        // Revive cost shown by the client: item 31 x80 (official capture).
        var info = new ENOBKLGCHOD
        {
            MFEODNJNGDP = new KCMFAIBMOHB
            {
                LNIEFKBMIPD = new ItemCostData
                {
                    ItemList = { new ItemCost { PileItem = new PileItem { ItemId = 31, ItemNum = 80 } } }
                }
            }
        };

        // Trial rosters travel in the story-line list with an empty base list, like the official response.
        var avatarIds = inst.BaseAvatarIds.Select(x => (uint)x);
        if (inst.Trial) info.UnfinishedStoryLineIdList.AddRange(avatarIds);
        else info.NOEPBNMJFGL.AddRange(avatarIds);
        return info;
    }

    private static CFMHEMGEANC BuildBuffList(RogueInstance inst)
    {
        var list = new CFMHEMGEANC();
        foreach (var buff in inst.Buffs.Values)
            list.CHCMEKAFKAF.Add(new ABOFJEIMLLJ { BuffId = (uint)buff.BuffId, Level = (uint)buff.Level });
        return list;
    }

    public static GKHGFCBMLBL BuildFinishInfo(RogueInstance inst)
    {
        var area = GameData.RogueAreaConfigData.GetValueOrDefault(inst.AreaId);
        var info = new GKHGFCBMLBL
        {
            ScoreId = (uint)(area?.ScoreMap.GetValueOrDefault(inst.CurrentRoomProgress) ?? 0),
            IsWin = inst.IsWin,
            GMIIMAHLIJF = 1,
            AreaId = (uint)inst.AreaId,
            RecordInfo = new KCOEJJKGNIG()
        };

        info.RecordInfo.MAJGMMCOCIG.AddRange(inst.Miracles.Keys.Select(x => (uint)x));
        for (var slot = 0; slot < inst.BaseAvatarIds.Count; slot++)
            info.RecordInfo.AvatarList.Add(new ALBLEFHHJIC
            {
                AvatarType = inst.Trial ? AvatarType.AvatarTrialType : AvatarType.AvatarFormalType,
                Slot = (uint)slot,
                Id = (uint)inst.BaseAvatarIds[slot]
            });
        foreach (var buff in inst.Buffs.Values)
            info.RecordInfo.BuffList.Add(new ABOFJEIMLLJ { BuffId = (uint)buff.BuffId, Level = (uint)buff.Level });

        return info;
    }

    public FDBDLBAPDBE BuildTalentInfo()
    {
        var info = new FDBDLBAPDBE();
        foreach (var talentId in GameData.RogueTalentData.Keys.OrderBy(x => x))
            info.BLHJIPBHHFO.Add(new OKPIPFKONMG
            {
                CKHCLKOALKE = (uint)talentId,
                Status = HasTalent(talentId) ? RogueTalentStatus.Enable : RogueTalentStatus.Unlock
            });
        return info;
    }

    public static NHGJEIHKKHB BuildHandbookData()
    {
        var data = new NHGJEIHKKHB();
        foreach (var id in GameData.RogueHandBookEventData.Keys.OrderBy(x => x))
            data.PMIFFBHOOOO.Add(new NMHMGHEHHCM { PNHGLNPDFEA = (uint)id, ALAJJMKOMML = true });
        foreach (var id in GameData.RogueHandbookMiracleData.Keys.OrderBy(x => x))
            data.MAJGMMCOCIG.Add(new CIGDBCKOFBN { NPNMLPNBHDP = (uint)id, ALAJJMKOMML = true });
        foreach (var id in GameData.RogueBuffData.Values.OfType<RogueBuffExcel>().Select(x => x.MazeBuffID)
                     .Distinct().OrderBy(x => x))
            data.BuffList.Add(new BPHIKPCNBHM { MazeBuffId = (uint)id });
        return data;
    }

    public static IEnumerable<CCKJAEDDDCL> BuildCommonQueryEntries(IEnumerable<uint> types)
    {
        // One entry per requested type in request order; the other arms are per-account progress with no
        // table source and stay branch-less. The function-unlock arm is the client's only source of rogue
        // function unlocks.
        foreach (var type in types)
        {
            var entry = new CCKJAEDDDCL();
            if (type == CommonQueryTypeFunctionUnlock)
            {
                entry.BBEKBJPHEJK = new AHLALEEJOEG();
                entry.BBEKBJPHEJK.LFCGPOKMJLC.AddRange(Enum.GetValues<MGJDNGHDIKG>());
            }

            yield return entry;
        }
    }

    public static GAAMBBFPCHB BuildCycleScoreData()
    {
        // ConstValueCommon is not loaded server-side; these are its 4.5 CycleScore_StartDate/Refresh rows (UTC+8).
        var anchor = new DateTimeOffset(2026, 2, 9, 4, 0, 0, TimeSpan.FromHours(8));
        var period = TimeSpan.FromDays(7);
        var now = DateTimeOffset.UtcNow;
        var begin = now < anchor ? anchor : anchor + (now - anchor).Ticks / period.Ticks * period;
        return new GAAMBBFPCHB
        {
            StartTime = begin.ToUnixTimeSeconds(),
            EndTime = (begin + period).ToUnixTimeSeconds() - 1
        };
    }

    #endregion
}
