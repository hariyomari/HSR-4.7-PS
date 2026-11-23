using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.TrainParty;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.TrainParty;

// Field mappings verified in OSBETAWin4.7.51 (TrainPartyBuildManager and TrainPartyGameInstance);
// some generated names describe older versions:
// * TrainPartyData: unlock_area_num (11) is the current TrainPartyProgress turn ID; _SetCurTurnID
//   requires a matching row. HCJJPLKELGC (15) holds the running board; absent means clear it.
// * TrainPartyArea: step_id_list (12) is unlocked static props, static_prop_id_list (8) finished steps,
//   verify_step_id_list (13) blueprints excluding FirstStep, area_step_info (1) the current goal and
//   step states (0 locked, 1 buildable, 3 finished), and progress (5) completion (>= 100 is complete).
// * TrainPartyInfo: cur_fund (2), star_num (4), JJGKIPDGOGP (9) highest claimed build level,
//   CMCNNJKJNPK (11) exhibits, MHABCMLHCGD (13) owned DIY props, area_list (14) unlocked areas,
//   BHEJCEJHLIB (15) build level. TrainPartyTakeBuildLevelAwardScRsp.FPHFHCGNNGM (12) updates
//   the claimed level; the client offers a reward while that level is below the build level.
// * TrainPartyBuildStartStepScRsp updates only funds; TrainPartyBuildingUpdateNotify sends results.
// * The server owns the floor values used by group conditions and item bindings: Progress_<Room>,
//   Build_<Room>_Finished, Progress_PartyTrainGlobal and Build_Has*.
// See TrainPartyGameplayRun for the board simulation's limits.
public class TrainPartyManager(PlayerInstance player) : BasePlayerManager<TrainData>(player)
{
    // Areas layout version; see TrainData.AreaDataVersion.
    public const int CurrentAreaDataVersion = 1;

    // The first TrainPartyProgress row, where the gameplay turn starts.
    private const int FirstTurnId = 1;

    private const JIIEHEBIBMO StepLocked = 0;
    private const JIIEHEBIBMO StepBuildable = (JIIEHEBIBMO)1;
    private const JIIEHEBIBMO StepFinished = (JIIEHEBIBMO)3;

    public TrainPartyGameplayRun? GameplayRun { get; private set; }

    public void MarkDataDirty()
    {
        MarkDirty();
    }

    public void EnsureDefaults()
    {
        var changed = EnsureBuildState();

        if (Data.RecordId == 0)
        {
            Data.RecordId = 5;
            changed = true;
        }

        if (Data.WorldId == 0)
        {
            Data.WorldId = 501;
            changed = true;
        }

        if (Data.RefreshTime == 0)
        {
            Data.RefreshTime = 1780264800;
            changed = true;
        }

        if (Data.PassengerRecordMarks.Count == 0)
        {
            Data.PassengerRecordMarks.Add(1004);
            changed = true;
        }

        if (Data.UnlockedPassengerIds.Count == 0)
        {
            Data.UnlockedPassengerIds.AddRange(DefaultPassengers.Select(x => x.PassengerId));
            changed = true;
        }

        if (Data.Cards.Count == 0)
        {
            Data.Cards.AddRange(DefaultCards.Select(x => new TrainCardInfo
                { CardId = x.CardId, UniqueId = x.UniqueId, CurIndex = x.CurIndex }));
            changed = true;
        }

        if (Data.MoveHistory.Count == 0)
        {
            Data.MoveHistory.AddRange(DefaultMoveHistory.Select(x => new TrainMoveInfo
                { CardId = x.CardId, BoardIndex = x.BoardIndex, UniqueId = x.UniqueId }));
            changed = true;
        }

        if (Data.GameplayPassengers.Count == 0)
        {
            Data.GameplayPassengers.AddRange(DefaultGameplayPassengers.Select(x => new TrainPendingPassengerInfo
                { PassengerId = x.PassengerId, Hp = x.Hp, Atk = x.Atk }));
            changed = true;
        }

        if (Data.GameplaySkills.Count == 0)
        {
            Data.GameplaySkills.AddRange(DefaultGameplaySkills.Select(x => new TrainSkillInfo
                { SkillId = x.SkillId, SkillLevel = x.SkillLevel, Count = x.Count }));
            changed = true;
        }

        if (Data.GameplayType == 0)
        {
            Data.GameplayType = 5;
            changed = true;
        }

        if (Data.GameplayRound == 0)
        {
            Data.GameplayRound = 1;
            changed = true;
        }

        if (changed) MarkDirty();
    }

    #region Build state

    // Brings the persisted build state in line with the build tables; returns whether anything changed.
    private bool EnsureBuildState()
    {
        var changed = false;

        if (Data.AreaDataVersion < CurrentAreaDataVersion)
        {
            if (Data.Areas.Count == 0)
                CompleteAllAreas();
            else
                ConvertLegacyAreas();

            Data.AreaDataVersion = CurrentAreaDataVersion;
            changed = true;
        }

        // Areas added by a later table version open once their requirement is met.
        if (UnlockReachableAreas().Count > 0) changed = true;

        // Every DIY prop of the table is owned; keep the acquire time of props owned before.
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var propId in GameData.TrainPartyDynamicConfigData.Keys)
            if (Data.PropTimes.TryAdd(propId, now))
                changed = true;

        if (Data.TurnId <= 0)
        {
            Data.TurnId = FirstTurnId;
            changed = true;
        }

        return changed;
    }

    // A new party car starts with every area of the tables built; keeps the DIY placements of areas already owned.
    public void CompleteAllAreas()
    {
        foreach (var areaConfig in TrainPartyBuildCatalog.Areas)
        {
            var area = GetOrAddArea(areaConfig.ID);
            area.StepList = TrainPartyBuildCatalog.GetAreaSteps(areaConfig.ID).Select(step => step.ID).ToList();
        }

        Data.AreaDataVersion = CurrentAreaDataVersion;
        MarkDirty();
    }

    // The old defaults stored the captured static prop IDs in StepList. Keep real step IDs and turn the static props
    // back into the steps that unlock them.
    private void ConvertLegacyAreas()
    {
        foreach (var area in Data.Areas.Values)
        {
            var legacy = area.StepList.ToHashSet();
            area.StepList = TrainPartyBuildCatalog.GetAreaSteps(area.AreaId)
                .Where(step => legacy.Contains(step.ID) ||
                               (step.StaticPropIDList.Count > 0 && step.StaticPropIDList.All(legacy.Contains)))
                .Select(step => step.ID)
                .ToList();
        }
    }

    private TrainAreaInfo GetOrAddArea(int areaId)
    {
        if (Data.Areas.TryGetValue(areaId, out var area)) return area;

        area = new TrainAreaInfo { AreaId = areaId };
        Data.Areas[areaId] = area;
        return area;
    }

    // Opens every area whose RequireAreaID is complete (or that has none); returns the newly opened area IDs.
    private List<int> UnlockReachableAreas()
    {
        var unlocked = new List<int>();
        foreach (var areaConfig in TrainPartyBuildCatalog.Areas)
        {
            if (Data.Areas.ContainsKey(areaConfig.ID)) continue;
            if (areaConfig.RequireAreaID != 0 &&
                (!Data.Areas.TryGetValue(areaConfig.RequireAreaID, out var required) || GetProgress(required) < 100))
                continue;

            Data.Areas[areaConfig.ID] = new TrainAreaInfo { AreaId = areaConfig.ID };
            unlocked.Add(areaConfig.ID);
        }

        return unlocked;
    }

    private static HashSet<int> FinishedSteps(TrainAreaInfo area)
    {
        return area.StepList.ToHashSet();
    }

    private static int GetProgress(TrainAreaInfo area)
    {
        return TrainPartyBuildCatalog.GetProgress(area.AreaId, FinishedSteps(area));
    }

    private bool OwnsProp(int propId)
    {
        return Data.PropTimes.ContainsKey(propId);
    }

    public int GetStarNum()
    {
        return Data.Areas.Values.Sum(area => TrainPartyBuildCatalog.GetStars(area.AreaId, GetProgress(area)));
    }

    public int GetBuildLevel()
    {
        return TrainPartyBuildCatalog.GetBuildLevel(GetStarNum());
    }

    #endregion

    #region Floor saved values

    private const string GlobalProgressValue = "Progress_PartyTrainGlobal";

    // Emit floor values for floors declaring Progress_PartyTrainGlobal. Area blocks match Progress_<Room>
    // and, case-insensitively, Build_<Room>_Finished. Set unmapped Build_Has* and Build_ExRoom_Finished
    // only on full completion; return nothing if the cache lacks the area blocks.
    public Dictionary<int, Dictionary<string, int>> BuildFloorSavedValues()
    {
        EnsureDefaults();

        var result = new Dictionary<int, Dictionary<string, int>>();
        var rooms = TrainPartyBuildCatalog.Areas
            .Select(area => (Area: area, Room: TrainPartyBuildCatalog.GetRoomName(area)))
            .Where(x => x.Room != null)
            .ToList();
        if (rooms.Count == 0) return result;

        var finishedByArea = Data.Areas.ToDictionary(x => x.Key, x => FinishedSteps(x.Value));
        var global = TrainPartyBuildCatalog.GetGlobalProgress(finishedByArea);

        foreach (var floor in GameData.FloorInfoData.Values)
        {
            var names = floor.FloorSavedValue.Select(x => x.Name).ToList();
            if (!names.Contains(GlobalProgressValue)) continue;

            var values = new Dictionary<string, int> { [GlobalProgressValue] = global };
            foreach (var (area, room) in rooms)
            {
                var progress = finishedByArea.TryGetValue(area.ID, out var finished)
                    ? TrainPartyBuildCatalog.GetProgress(area.ID, finished)
                    : 0;
                var progressName = names.FirstOrDefault(name =>
                    string.Equals(name, $"Progress_{room}", StringComparison.OrdinalIgnoreCase));
                if (progressName != null) values[progressName] = progress;
                var finishedName = names.FirstOrDefault(name =>
                    string.Equals(name, $"Build_{room}_Finished", StringComparison.OrdinalIgnoreCase));
                if (finishedName != null) values[finishedName] = progress >= 100 ? 1 : 0;
            }

            if (global >= 100)
                foreach (var name in names.Where(name => name.StartsWith("Build_", StringComparison.Ordinal)))
                    values.TryAdd(name, 1);

            result[floor.FloorID] = values;
        }

        return result;
    }

    // Writes the build-derived floor saved values into the player's scene data; returns the changed values per floor.
    public Dictionary<int, Dictionary<string, int>> ApplyFloorSavedValues()
    {
        var changed = new Dictionary<int, Dictionary<string, int>>();
        var sceneData = Player.SceneData;
        if (sceneData == null) return changed;

        foreach (var (floorId, values) in BuildFloorSavedValues())
        foreach (var (name, value) in values)
        {
            if (sceneData.GetFloorSavedValue(floorId, name) == value) continue;
            if (!sceneData.FloorSavedData.TryGetValue(floorId, out var saved))
                sceneData.FloorSavedData[floorId] = saved = [];
            saved[name] = value;
            if (!changed.TryGetValue(floorId, out var floorChanges)) changed[floorId] = floorChanges = [];
            floorChanges[name] = value;
        }

        if (changed.Count > 0) MarkDirty();
        return changed;
    }

    // Applies the floor saved values and refreshes the scene when the player stands on a changed party-car floor.
    public async ValueTask SyncFloorSavedValuesAsync()
    {
        var changed = ApplyFloorSavedValues();
        var scene = Player.SceneInstance;
        if (scene == null || !changed.TryGetValue(scene.FloorId, out var current)) return;

        await Player.SendPacket(new PacketUpdateFloorSavedValueNotify(current, Player));
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.FloorSavedValue);
        await scene.SyncGroupInfo();
    }

    #endregion

    #region Build requests

    public (TrainPartyBuildStartStepScRsp Rsp, TrainPartyBuildingUpdateNotify? Notify) BuildStartStep(
        TrainPartyBuildStartStepCsReq req)
    {
        EnsureDefaults();

        var rsp = new TrainPartyBuildStartStepScRsp { CurFund = (uint)Data.Fund };
        var areaId = (int)req.AreaId;
        var stepId = (int)req.DNLIGEOFPEL;

        if (!Data.Areas.TryGetValue(areaId, out var area) || TrainPartyBuildCatalog.GetArea(areaId) == null)
        {
            rsp.Retcode = (uint)Retcode.RetReqParaInvalid;
            return (rsp, null);
        }

        var finished = FinishedSteps(area);
        var goal = TrainPartyBuildCatalog.GetGoals(areaId)
            .FirstOrDefault(x => TrainPartyBuildCatalog.GetGoalSteps(x).Any(step => step.ID == stepId));
        var step = goal == null ? null : GameData.TrainPartyStepConfigData.GetValueOrDefault(stepId);
        if (goal == null || step == null || finished.Contains(stepId) ||
            (req.GKENFCLLLJC != 0 && req.GKENFCLLLJC != goal.ID) ||
            !TrainPartyBuildCatalog.IsStepUnlocked(areaId, stepId, finished))
        {
            rsp.Retcode = (uint)Retcode.RetReqParaInvalid;
            return (rsp, null);
        }

        if (Data.Fund < step.CoinCost)
        {
            rsp.Retcode = (uint)Retcode.RetTrainPartyCoinNotEnough;
            return (rsp, null);
        }

        var starsBefore = GetStarNum();
        var levelBefore = TrainPartyBuildCatalog.GetBuildLevel(starsBefore);

        Data.Fund -= step.CoinCost;
        area.StepList.Add(stepId);
        finished.Add(stepId);

        // The client may place a prop in a slot the step opens; take it when it names an owned prop of this area.
        if (req.PFICNBHIHBA is { DiyDynamicId: not 0 } placement && IsAreaSlot(areaId, (int)placement.DiceSlotId) &&
            OwnsProp((int)placement.DiyDynamicId))
            PlaceProp(areaId, (int)placement.DiceSlotId, (int)placement.DiyDynamicId);

        var notify = new TrainPartyBuildingUpdateNotify();
        notify.OOABOBHEKEH.Add(new FFMMFBMOBEG { FCKNCNDEKAE = new NFNKNNFJILK { IOJOIOHIEAB = (uint)Data.Fund } });
        foreach (var staticId in step.StaticPropIDList)
            notify.OOABOBHEKEH.Add(new FFMMFBMOBEG
                { NEFBGMNGKBN = new CNFPBAJNIEL { AreaId = (uint)areaId, JBEEKBJMIIA = (uint)staticId } });

        var areaUpdate = new AIOCPFLCNLB
        {
            AreaId = (uint)areaId,
            Progress = (uint)GetProgress(area),
            GKENFCLLLJC = (uint)goal.ID
        };
        areaUpdate.FOOMIPJJKOG.AddRange(BuildStepStates(areaId, goal, finished));
        notify.OOABOBHEKEH.Add(new FFMMFBMOBEG { MFNELHNLNGI = areaUpdate });

        // Finishing the first step hands out the blueprints of the area's other steps.
        if (TrainPartyBuildCatalog.GetArea(areaId)?.FirstStep == stepId)
        {
            var blueprints = new CBJGONHJAAC { AreaId = (uint)areaId };
            blueprints.FOOMIPJJKOG.AddRange(BuildBlueprints(areaId, finished));
            if (blueprints.FOOMIPJJKOG.Count > 0)
                notify.OOABOBHEKEH.Add(new FFMMFBMOBEG { FPMNNMMPOKL = new IGNNGONLFAJ { AreaList = { blueprints } } });
        }

        foreach (var unlockedAreaId in UnlockReachableAreas())
        {
            notify.OOABOBHEKEH.Add(new FFMMFBMOBEG { CNGOKIKBGAJ = new FAHBCNPNBNC { NLJMJCGFFMD = (uint)unlockedAreaId } });
            var unlockedGoal = TrainPartyBuildCatalog.GetCurrentGoal(unlockedAreaId, new HashSet<int>());
            if (unlockedGoal == null) continue;

            var newGoal = new PPLKBFMCGLD { BPGBEEOHPLJ = (uint)unlockedGoal.ID };
            newGoal.FOOMIPJJKOG.AddRange(BuildStepStates(unlockedAreaId, unlockedGoal, new HashSet<int>()));
            notify.OOABOBHEKEH.Add(new FFMMFBMOBEG { LLCOCEJHKLD = newGoal });
        }

        var stars = GetStarNum();
        var level = TrainPartyBuildCatalog.GetBuildLevel(stars);
        if (stars != starsBefore || level != levelBefore)
            notify.OOABOBHEKEH.Add(new FFMMFBMOBEG
                { FHBMGFFBOLP = new DOCMDCPJIEH { StarNum = (uint)stars, BHEJCEJHLIB = (uint)level } });

        MarkDirty();
        rsp.CurFund = (uint)Data.Fund;
        return (rsp, notify);
    }

    public TrainPartyBuildDiyScRsp BuildDiy(TrainPartyBuildDiyCsReq req)
    {
        EnsureDefaults();

        var areaId = (int)req.AreaId;
        var slotId = (int)req.DiceSlotId;
        var propId = (int)req.DiyDynamicId;
        var rsp = new TrainPartyBuildDiyScRsp { AreaId = req.AreaId };

        if (!Data.Areas.ContainsKey(areaId) || !IsAreaSlot(areaId, slotId) || (propId != 0 && !OwnsProp(propId)))
        {
            rsp.Retcode = (uint)Retcode.RetReqParaInvalid;
            return rsp;
        }

        rsp.JKPJFKCBIEM.AddRange(PlaceProp(areaId, slotId, propId));
        MarkDirty();
        return rsp;
    }

    // Slot IDs are prefixed by their area ID (slot 1100101 belongs to area 11).
    private static bool IsAreaSlot(int areaId, int slotId)
    {
        return slotId > 0 && slotId / 100000 == areaId;
    }

    // Puts a prop in a slot (0 empties it). A prop belongs to one slot at a time, so it leaves any other slot first.
    // Returns every slot that changed.
    private List<BHOPIKPAMPJ> PlaceProp(int areaId, int slotId, int propId)
    {
        var changes = new List<BHOPIKPAMPJ>();

        if (propId != 0)
            foreach (var other in Data.Areas.Values)
            foreach (var otherSlot in other.DynamicInfo.Where(x => x.Value == propId).Select(x => x.Key).ToList())
            {
                if (other.AreaId == areaId && otherSlot == slotId) continue;
                other.DynamicInfo.Remove(otherSlot);
                changes.Add(SlotUpdate(other.AreaId, otherSlot, 0));
            }

        var area = Data.Areas[areaId];
        if (propId == 0)
            area.DynamicInfo.Remove(slotId);
        else
            area.DynamicInfo[slotId] = propId;

        changes.Add(SlotUpdate(areaId, slotId, propId));
        return changes;
    }

    private static BHOPIKPAMPJ SlotUpdate(int areaId, int slotId, int propId)
    {
        return new BHOPIKPAMPJ
        {
            AreaId = (uint)areaId,
            OOFPPPCEIDP = new AreaDynamicInfo { DiceSlotId = (uint)slotId, DiyDynamicId = (uint)propId }
        };
    }

    // The display wall is sent whole on every change and replaces the stored one; props the player does not own
    // are dropped.
    public OIIPJFDNPAH SetSelfDisplay(OIIPJFDNPAH? selfDisplay)
    {
        EnsureDefaults();

        if (selfDisplay != null)
        {
            Data.SelfDisplay = selfDisplay.OFOKOHGPEDO
                .Select(x => new TrainSelfDisplayEntry { Id = x.Id, Slot = x.Slot, Type = x.Type }).ToList();
            Data.SelfDisplayDynamics = selfDisplay.GOEJIMNENDC
                .Where(x => x.DiceSlotId != 0 && x.DiyDynamicId != 0 && OwnsProp((int)x.DiyDynamicId))
                .GroupBy(x => (int)x.DiceSlotId)
                .ToDictionary(x => x.Key, x => (int)x.Last().DiyDynamicId);
            Data.SelfDisplayBadges = selfDisplay.POLMJFIKNHN
                .Select(x => new TrainSelfDisplayBadge
                    { Slot = x.DiceSlotId, BadgeId = x.EIKLMFPMJJD, ExpireTime = x.ExpireTime })
                .ToList();
            MarkDirty();
        }

        return BuildSelfDisplayProto();
    }

    public uint SetBadgeAutoFill(bool enabled)
    {
        EnsureDefaults();
        Data.BadgeAutoFill = enabled;
        MarkDirty();
        return 0;
    }

    // Takes every build level reached but not yet taken in one go (the request names no level).
    public async ValueTask<TrainPartyTakeBuildLevelAwardScRsp> TakeBuildLevelAward()
    {
        EnsureDefaults();

        var rsp = new TrainPartyTakeBuildLevelAwardScRsp
            { FPHFHCGNNGM = (uint)Data.TakenBuildLevel, ItemList = new ItemList() };
        var level = GetBuildLevel();
        var rows = GameData.TrainPartyRewardConfigData.Values
            .Where(row => row.Level > Data.TakenBuildLevel && row.Level <= level)
            .OrderBy(row => row.Level)
            .ToList();
        if (rows.Count == 0 || rows.Any(row => !GameData.RewardDataData.ContainsKey(row.RewardID)))
        {
            rsp.Retcode = (uint)Retcode.RetReqParaInvalid;
            return rsp;
        }

        var items = new List<ItemData>();
        foreach (var reward in rows.Select(row => GameData.RewardDataData[row.RewardID]))
        {
            if (reward.Hcoin > 0) items.Add(new ItemData { ItemId = (int)VirtualItemEnum.Hcoin, Count = reward.Hcoin });
            items.AddRange(reward.GetItems().Select(item => new ItemData { ItemId = item.Item1, Count = item.Item2 }));
        }

        // Commit the taken level before granting, so a repeated request cannot take the same levels again.
        Data.TakenBuildLevel = level;
        MarkDirty();

        rsp.FPHFHCGNNGM = (uint)level;
        rsp.ItemList.ItemList_.AddRange(items.Select(item => item.ToProto()));
        await Player.InventoryManager!.AddItems(items);
        return rsp;
    }

    #endregion

    #region Build protos

    public TrainPartyData ToProto()
    {
        EnsureDefaults();
        var data = new TrainPartyData
        {
            RecordId = (uint)Data.RecordId,
            TrainPartyInfo = ToPartyInfo(),
            PassengerInfo = ToPassenger(),
            UnlockAreaNum = (uint)Data.TurnId
        };

        if (GameplayRun != null) data.HCJJPLKELGC = GameplayRun.BuildGameplayData();
        return data;
    }

    public TrainPartyInfo ToPartyInfo()
    {
        EnsureDefaults();

        var stars = GetStarNum();
        var info = new TrainPartyInfo
        {
            IEEAPCFKOJC = true,
            CurFund = (uint)Data.Fund,
            StarNum = (uint)stars,
            BHEJCEJHLIB = (uint)TrainPartyBuildCatalog.GetBuildLevel(stars),
            JJGKIPDGOGP = (uint)Data.TakenBuildLevel,
            CMCNNJKJNPK = BuildSelfDisplayProto()
        };

        info.MHABCMLHCGD.AddRange(Data.PropTimes.OrderBy(x => x.Key)
            .Select(x => new GAGGCODGDLB { PropId = (uint)x.Key, Time = x.Value }));

        // The client looks every listed area up in its area table and throws on an unknown one.
        info.AreaList.AddRange(Data.Areas.Values
            .Where(area => TrainPartyBuildCatalog.GetArea(area.AreaId) != null)
            .OrderBy(area => area.AreaId)
            .Select(BuildAreaProto));
        return info;
    }

    private TrainPartyArea BuildAreaProto(TrainAreaInfo area)
    {
        var finished = FinishedSteps(area);
        var steps = TrainPartyBuildCatalog.GetAreaSteps(area.AreaId);
        var proto = new TrainPartyArea
        {
            AreaId = (uint)area.AreaId,
            Progress = (uint)TrainPartyBuildCatalog.GetProgress(area.AreaId, finished)
        };

        var goal = TrainPartyBuildCatalog.GetCurrentGoal(area.AreaId, finished);
        if (goal != null)
        {
            proto.AreaStepInfo = new AreaStepInfo { GKENFCLLLJC = (uint)goal.ID };
            proto.AreaStepInfo.BKKLGCLOBCD.AddRange(BuildStepStates(area.AreaId, goal, finished));
        }

        // Field 8: finished steps.
        proto.StaticPropIdList.AddRange(steps.Where(step => finished.Contains(step.ID)).Select(step => (uint)step.ID));
        // Field 12: the static props those steps unlocked.
        proto.StepIdList.AddRange(steps.Where(step => finished.Contains(step.ID))
            .SelectMany(step => step.StaticPropIDList).Distinct().Select(id => (uint)id));
        // Field 13: blueprints.
        proto.VerifyStepIdList.AddRange(BuildBlueprints(area.AreaId, finished));

        proto.DynamicInfo.AddRange(area.DynamicInfo.OrderBy(x => x.Key).Select(x => new AreaDynamicInfo
        {
            DiceSlotId = (uint)x.Key,
            DiyDynamicId = (uint)x.Value
        }));
        return proto;
    }

    private static IEnumerable<IDBCDHIOKEG> BuildStepStates(int areaId, TrainPartyAreaGoalConfigExcel goal,
        IReadOnlySet<int> finished)
    {
        return TrainPartyBuildCatalog.GetGoalSteps(goal).Select(step => new IDBCDHIOKEG
        {
            DNLIGEOFPEL = (uint)step.ID,
            Status = finished.Contains(step.ID) ? StepFinished
                : TrainPartyBuildCatalog.IsStepUnlocked(areaId, step.ID, finished) ? StepBuildable
                : StepLocked
        });
    }

    // Blueprints are the buildable or built steps other than the area's FirstStep, which the client never lists.
    private static IEnumerable<uint> BuildBlueprints(int areaId, IReadOnlySet<int> finished)
    {
        var firstStep = TrainPartyBuildCatalog.GetArea(areaId)?.FirstStep ?? 0;
        return TrainPartyBuildCatalog.GetAreaSteps(areaId)
            .Where(step => step.ID != firstStep && TrainPartyBuildCatalog.IsStepUnlocked(areaId, step.ID, finished))
            .Select(step => (uint)step.ID);
    }

    private OIIPJFDNPAH BuildSelfDisplayProto()
    {
        var proto = new OIIPJFDNPAH();
        proto.OFOKOHGPEDO.AddRange(Data.SelfDisplay.Select(x => new CLLBJDEJFNF
        {
            Id = x.Id,
            Slot = x.Slot,
            Type = x.Type
        }));
        proto.GOEJIMNENDC.AddRange(Data.SelfDisplayDynamics.OrderBy(x => x.Key).Select(x => new AreaDynamicInfo
        {
            DiceSlotId = (uint)x.Key,
            DiyDynamicId = (uint)x.Value
        }));
        proto.POLMJFIKNHN.AddRange(Data.SelfDisplayBadges.Select(x => new POFNNGOCEOK
        {
            DiceSlotId = x.Slot,
            EIKLMFPMJJD = x.BadgeId,
            ExpireTime = x.ExpireTime
        }));
        return proto;
    }

    public TrainPartyPassengerInfo ToPassenger()
    {
        EnsureDefaults();

        var info = new TrainPartyPassengerInfo();
        info.GHPFANGEMAN.AddRange(Data.PassengerRecordMarks);
        info.PassengerInfoList.AddRange(DefaultPassengers.Select(x => new TrainPartyPassenger
        {
            PassengerId = x.PassengerId,
            RecordId = x.RecordId,
            GHPFANGEMAN = { x.Badges }
        }));
        return info;
    }

    #endregion

    #region Gameplay

    public uint UseCard(uint cardUniqueId)
    {
        EnsureDefaults();
        Data.LastUsedCardId = (int)cardUniqueId;
        MarkDirty();
        return 0;
    }

    // Gameplay (board minigame) — simulated by TrainPartyGameplayRun (best-effort 4.4 protocol).
    public uint StartGameplay(uint gameplayType, out EPHKIEHONGB gameplayData)
    {
        EnsureDefaults();

        Data.GameplayType = (int)gameplayType;
        Data.GameplayRound = 1;
        Data.GameplayQueuePosition = 0;
        MarkDirty();

        GameplayRun = TrainPartyGameplayRun.Create(Data, gameplayType);
        gameplayData = GameplayRun.BuildGameplayData();
        return 0;
    }

    public uint HandlePendingAction(TrainPartyHandlePendingActionCsReq req, out TrainPartyHandlePendingActionScRsp rsp)
    {
        EnsureDefaults();

        rsp = new TrainPartyHandlePendingActionScRsp();

        // Client may resume a board without an explicit start (e.g. after relog).
        GameplayRun ??= TrainPartyGameplayRun.Create(Data, (uint)Data.GameplayType);

        var ret = GameplayRun.HandleAnswer(req, rsp);

        Data.GameplayRound = (int)GameplayRun.Round;
        Data.GameplayQueuePosition = (int)GameplayRun.QueuePosition;
        MarkDirty();

        return ret;
    }

    // Drains the deltas produced by the last answered pending action; null when there is nothing to push.
    public TrainPartySyncUpdateScNotify? BuildGameplaySyncNotify()
    {
        if (GameplayRun == null || GameplayRun.PendingDeltas.Count == 0) return null;

        var notify = new TrainPartySyncUpdateScNotify();
        notify.JKPJFKCBIEM.AddRange(GameplayRun.PendingDeltas);
        GameplayRun.PendingDeltas.Clear();
        return notify;
    }

    // Ends the current board run (leave or settle). Returns true when a run was actually active.
    public bool ResetGameplay()
    {
        if (GameplayRun == null) return false;

        GameplayRun = null;
        Data.GameplayRound = 1;
        Data.GameplayQueuePosition = 0;
        MarkDirty();
        return true;
    }

    public TrainPartyMoveScNotify BuildMoveNotify()
    {
        var notify = new TrainPartyMoveScNotify { DNGKGLFMGGE = (uint)Data.GameplayRound };
        if (GameplayRun != null) notify.JABPLBJAKKM.AddRange(GameplayRun.Moves);
        return notify;
    }

    public TrainPartySettleNotify BuildSettleNotify()
    {
        var notify = new TrainPartySettleNotify { FGIHKKKOGJD = (uint)Data.LastUsedCardId };
        if (GameplayRun != null) notify.CBFPJAJAKGI = GameplayRun.BuildStatus();
        return notify;
    }

    public TrainPartyGamePlaySettleNotify BuildGamePlaySettleNotify()
    {
        EnsureDefaults();
        var notify = new TrainPartyGamePlaySettleNotify
        {
            RecordId = (uint)Data.RecordId,
            // Field 6 is the turn after the settle (TrainPartyGameInstance.OnGameplaySettle -> _SetCurTurnID).
            UnlockAreaNum = (uint)Data.TurnId,
            ENBJCMPCMLK = ToPassenger()
        };

        notify.CEJPCMHEEMN.AddRange(Data.UnlockedPassengerIds);
        return notify;
    }

    #endregion

    // Board-minigame and passenger seed data captured from a 4.2 account. Out of the build flow above; copied per
    // player, never shared.
    private static readonly TrainCardInfo[] DefaultCards =
    [
        new() { CardId = 107, UniqueId = 1 },
        new() { CardId = 321, CurIndex = 2, UniqueId = 3 },
        new() { CardId = 207, CurIndex = 4, UniqueId = 4 },
        new() { CardId = 206, CurIndex = 5, UniqueId = 5 }
    ];

    private static readonly TrainMoveInfo[] DefaultMoveHistory =
    [
        new() { CardId = 6101, BoardIndex = 2, UniqueId = 2 },
        new() { CardId = 6102, BoardIndex = 3, UniqueId = 3 },
        new() { CardId = 6103, BoardIndex = 4, UniqueId = 4 },
        new() { CardId = 6104, BoardIndex = 5, UniqueId = 5 },
        new() { CardId = 2001, BoardIndex = 6, UniqueId = 6 },
        new() { CardId = 8001, BoardIndex = 7, UniqueId = 7 },
        new() { CardId = 4001, BoardIndex = 8, UniqueId = 8 },
        new() { CardId = 6105, BoardIndex = 9, UniqueId = 9 },
        new() { CardId = 1001, BoardIndex = 10, UniqueId = 10 },
        new() { CardId = 6101, BoardIndex = 11, UniqueId = 11 },
        new() { CardId = 2001, BoardIndex = 12, UniqueId = 12 }
    ];

    private static readonly TrainPendingPassengerInfo[] DefaultGameplayPassengers =
    [
        new() { PassengerId = 1001, Hp = 350, Atk = 35 },
        new() { PassengerId = 1006, Hp = 300, Atk = 30 },
        new() { PassengerId = 1007, Hp = 300, Atk = 30 },
        new() { PassengerId = 1008, Hp = 300, Atk = 30 },
        new() { PassengerId = 1009, Hp = 300, Atk = 30 }
    ];

    private static readonly TrainSkillInfo[] DefaultGameplaySkills =
    [
        new() { SkillId = 214, SkillLevel = 1, Count = 1 },
        new() { SkillId = 208, SkillLevel = 1, Count = 1 }
    ];

    private static readonly (uint PassengerId, uint RecordId, uint[] Badges)[] DefaultPassengers =
    [
        (1001, 4, [103u, 101u, 102u]),
        (1002, 4, [202u, 203u, 201u]),
        (1003, 3, [301u, 302u, 303u]),
        (1004, 3, [502u, 501u]),
        (1005, 4, [401u, 403u, 402u]),
        (1006, 2, [601u]),
        (1007, 2, [701u]),
        (1008, 2, [801u]),
        (1009, 1, [901u])
    ];
}
