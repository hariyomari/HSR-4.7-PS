using March7thHoney.Data;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.TrainParty;
using March7thHoney.GameServer.Server.Packet.Send.TrainParty;
using March7thHoney.Proto;

// Party-car build state against the cached TrainParty tables. Field meanings follow the OSBETAWin4.7.51 client
// (TrainPartyBuildManager / TrainPartyBuildAreaInfo): TrainPartyArea field 12 (step_id_list) carries the unlocked
// static props the room shows, field 8 (static_prop_id_list) the finished steps, field 13 the blueprints.
static class TrainPartyBuildRegression
{
    private const uint Buildable = 1;
    private const uint Finished = 3;

    public static async Task Run(Action<string, bool> check)
    {
        if (GameData.TrainPartyAreaConfigData.Count == 0 || GameData.TrainPartyStepConfigData.Count == 0)
        {
            check("cache carries the TrainParty build tables", false);
            return;
        }

        // Added to the cache by scripts/TrainPartyCacheUpgrade; without them stars, levels and floor values stay empty.
        check("cache carries the TrainParty room blocks, star thresholds and build levels",
            GameData.TrainPartyAreaConfigData.Values.All(area =>
                TrainPartyBuildCatalog.GetRoomName(area) != null && area.ProgressBonusList is { Count: > 0 }) &&
            GameData.TrainPartyRewardConfigData.Count > 0);

        await DefaultRoomIsBuilt(check);
        await LegacyStaticListsBecomeSteps(check);
        await StepBuildFlow(check);
        await DiyPlacement(check);
    }

    private static async Task<PlayerInstance> NewPlayer(int uid)
    {
        var player = new PlayerInstance(uid);
        await player.InitializeAsync();
        return player;
    }

    private const string GlobalProgress = "Progress_PartyTrainGlobal";

    // The party-car floors and the area each Progress_<Room> value belongs to.
    private static List<March7thHoney.Data.Config.Scene.FloorInfo> TrainFloors() => GameData.FloorInfoData.Values
        .Where(floor => floor.FloorSavedValue.Any(value => value.Name == GlobalProgress)).ToList();

    private static string ProgressName(int areaId) =>
        $"Progress_{TrainPartyBuildCatalog.GetRoomName(GameData.TrainPartyAreaConfigData[areaId])}";

    private static string FinishedName(March7thHoney.Data.Config.Scene.FloorInfo floor, int areaId) =>
        floor.FloorSavedValue.Select(value => value.Name).FirstOrDefault(name => string.Equals(name,
            $"Build_{TrainPartyBuildCatalog.GetRoomName(GameData.TrainPartyAreaConfigData[areaId])}_Finished",
            StringComparison.OrdinalIgnoreCase)) ?? "";

    private static async Task DefaultRoomIsBuilt(Action<string, bool> check)
    {
        var player = await NewPlayer(60033);
        var rsp = TrainPartyGetDataScRsp.Parser.ParseFrom(new PacketTrainPartyGetDataScRsp(player).Data);
        var data = rsp.TrainPartyData;
        check("TrainPartyGetDataScRsp is built from the player: party info present, turn 1, no running board",
            data is { TrainPartyInfo: not null, PassengerInfo: not null, UnlockAreaNum: 1, HCJJPLKELGC: null });
        if (data?.TrainPartyInfo == null) return;

        var areas = data.TrainPartyInfo.AreaList.ToDictionary(x => (int)x.AreaId);
        check("a new party car lists every table area",
            GameData.TrainPartyAreaConfigData.Keys.Order().SequenceEqual(areas.Keys.Order()));
        check("every area ships all its steps finished, their static props unlocked and progress 100",
            GameData.TrainPartyAreaConfigData.Keys.All(areaId =>
            {
                if (!areas.TryGetValue(areaId, out var area)) return false;
                var steps = TrainPartyBuildCatalog.GetAreaSteps(areaId);
                var statics = steps.SelectMany(step => step.StaticPropIDList).Select(id => (uint)id).ToHashSet();
                return steps.Count > 0 && area.Progress == 100 &&
                       area.StaticPropIdList.ToHashSet().SetEquals(steps.Select(step => (uint)step.ID)) &&
                       area.StepIdList.ToHashSet().SetEquals(statics) &&
                       area.AreaStepInfo?.BKKLGCLOBCD.All(state => (uint)state.Status == Finished) == true;
            }));
        check("the area blueprints leave out each area's FirstStep",
            areas.Values.All(area => !area.VerifyStepIdList.Contains(
                (uint)GameData.TrainPartyAreaConfigData[(int)area.AreaId].FirstStep)));
        check("every DIY prop of the table is owned with an acquire time",
            GameData.TrainPartyDynamicConfigData.Keys.All(id =>
                data.TrainPartyInfo.MHABCMLHCGD.Any(prop => prop.PropId == id && prop.Time > 0)));

        var totalStars = GameData.TrainPartyAreaConfigData.Values.Sum(area => area.ProgressBonusList.Sum(x => x.AddStar));
        var topLevel = GameData.TrainPartyRewardConfigData.Keys.DefaultIfEmpty().Max();
        check("a complete car has every area star and the top build level, with no level taken yet",
            totalStars > 0 && data.TrainPartyInfo.StarNum == totalStars &&
            data.TrainPartyInfo.BHEJCEJHLIB == topLevel && data.TrainPartyInfo.JJGKIPDGOGP == 0);

        // Floor saved values: no scene is loaded, so the sync only writes the player's scene data.
        var manager = player.TrainPartyManager!;
        var floors = TrainFloors();
        var lightOff = floors.Select(floor => player.SceneData!.GetFloorSavedValue(floor.FloorID, "Utility_IsLightOff")).ToList();
        await manager.SyncFloorSavedValuesAsync();
        check("a complete car puts every party-car floor at 100 with every room finished and every Build_ flag set",
            floors.Count > 0 && floors.All(floor =>
                player.SceneData!.GetFloorSavedValue(floor.FloorID, GlobalProgress) == 100 &&
                GameData.TrainPartyAreaConfigData.Keys.All(areaId =>
                    floor.FloorSavedValue.All(value => value.Name != ProgressName(areaId)) ||
                    player.SceneData.GetFloorSavedValue(floor.FloorID, ProgressName(areaId)) == 100) &&
                floor.FloorSavedValue.Where(value => value.Name.StartsWith("Build_", StringComparison.Ordinal))
                    .All(value => player.SceneData.GetFloorSavedValue(floor.FloorID, value.Name) == 1)));
        check("the room values match a Progress_ value for every area on the bar carriage floor",
            floors.Any(floor => GameData.TrainPartyAreaConfigData.Keys.All(areaId =>
                floor.FloorSavedValue.Any(value => value.Name == ProgressName(areaId)))));
        check("floor values the build does not own keep their value",
            floors.Select(floor => player.SceneData!.GetFloorSavedValue(floor.FloorID, "Utility_IsLightOff"))
                .SequenceEqual(lightOff));
        check("a second sync finds nothing to change", manager.ApplyFloorSavedValues().Count == 0);

        // Build-level rewards: one request takes every level reached.
        var rewardItems = GameData.TrainPartyRewardConfigData.Values
            .SelectMany(row => GameData.RewardDataData[row.RewardID].GetItems()).ToList();
        var materialId = rewardItems.Select(item => item.Item1).FirstOrDefault(id =>
            GameData.ItemConfigData.TryGetValue(id, out var item) &&
            item.ItemMainType == March7thHoney.Enums.Item.ItemMainTypeEnum.Material);
        int MaterialCount() => player.InventoryManager!.Data.MaterialItems.Where(x => x.ItemId == materialId).Sum(x => x.Count);
        var materialBefore = MaterialCount();
        var taken = await manager.TakeBuildLevelAward();
        check("taking the build-level award hands out every reached level at once",
            taken.Retcode == 0 && taken.FPHFHCGNNGM == topLevel && manager.Data.TakenBuildLevel == topLevel &&
            taken.ItemList.ItemList_.Count >= rewardItems.Count &&
            (materialId == 0 || MaterialCount() - materialBefore ==
                rewardItems.Where(item => item.Item1 == materialId).Sum(item => item.Item2)));
        var materialAfter = MaterialCount();
        var again = await manager.TakeBuildLevelAward();
        check("a second take is refused and grants nothing",
            again.Retcode == (uint)Retcode.RetReqParaInvalid && again.FPHFHCGNNGM == topLevel &&
            again.ItemList.ItemList_.Count == 0 && MaterialCount() == materialAfter &&
            manager.ToPartyInfo().JJGKIPDGOGP == topLevel);
    }

    private static async Task LegacyStaticListsBecomeSteps(Action<string, bool> check)
    {
        var player = await NewPlayer(60034);
        var manager = player.TrainPartyManager!;
        var firstArea = GameData.TrainPartyAreaConfigData.Values.First(x => x.RequireAreaID == 0);
        var steps = TrainPartyBuildCatalog.GetAreaSteps(firstArea.ID);
        var kept = steps.Take(2).ToList();

        // The old defaults stored captured static prop IDs where step IDs belong.
        manager.Data.AreaDataVersion = 0;
        manager.Data.Areas.Clear();
        manager.Data.Areas[firstArea.ID] = new March7thHoney.Database.TrainParty.TrainAreaInfo
        {
            AreaId = firstArea.ID,
            StepList = kept.SelectMany(step => step.StaticPropIDList).ToList()
        };
        manager.EnsureDefaults();

        check("captured static prop lists convert to the steps that unlock them",
            manager.Data.AreaDataVersion == TrainPartyManager.CurrentAreaDataVersion &&
            manager.Data.Areas[firstArea.ID].StepList.Order().SequenceEqual(kept.Select(step => step.ID).Order()));
        check("areas whose required area is not complete stay locked",
            manager.Data.Areas.Keys.All(areaId =>
                areaId == firstArea.ID || GameData.TrainPartyAreaConfigData[areaId].RequireAreaID == 0));
    }

    private static async Task StepBuildFlow(Action<string, bool> check)
    {
        var player = await NewPlayer(60035);
        var manager = player.TrainPartyManager!;
        manager.EnsureDefaults();
        var firstArea = GameData.TrainPartyAreaConfigData.Values.First(x => x.RequireAreaID == 0);
        var areaId = firstArea.ID;
        var steps = TrainPartyBuildCatalog.GetAreaSteps(areaId);
        var firstStep = steps.First(step => step.ID == firstArea.FirstStep);
        var laterStep = steps.First(step => step.ID != firstArea.FirstStep);

        manager.Data.Areas.Clear();
        manager.Data.Areas[areaId] = new March7thHoney.Database.TrainParty.TrainAreaInfo { AreaId = areaId };
        manager.Data.Fund = firstStep.CoinCost - 1;

        TrainPartyBuildStartStepCsReq Req(int stepId) => new() { AreaId = (uint)areaId, DNLIGEOFPEL = (uint)stepId };

        var early = manager.BuildStartStep(Req(laterStep.ID));
        check("a step before the area's FirstStep is rejected without spending",
            early.Rsp.Retcode == (uint)Retcode.RetReqParaInvalid && early.Notify == null &&
            manager.Data.Fund == firstStep.CoinCost - 1 && manager.Data.Areas[areaId].StepList.Count == 0);

        var poor = manager.BuildStartStep(Req(firstStep.ID));
        check("a step the fund cannot pay is rejected",
            poor.Rsp.Retcode == (uint)Retcode.RetTrainPartyCoinNotEnough && poor.Notify == null &&
            manager.Data.Areas[areaId].StepList.Count == 0);

        var wrongGoal = manager.BuildStartStep(new TrainPartyBuildStartStepCsReq
            { AreaId = (uint)areaId, DNLIGEOFPEL = (uint)firstStep.ID, GKENFCLLLJC = 999999 });
        check("a step named under another goal is rejected", wrongGoal.Rsp.Retcode == (uint)Retcode.RetReqParaInvalid);

        manager.Data.Fund = steps.Sum(step => step.CoinCost) + 7;
        var built = manager.BuildStartStep(Req(firstStep.ID));
        var updates = built.Notify?.OOABOBHEKEH.ToList() ?? [];
        var areaUpdate = updates.Select(x => x.MFNELHNLNGI).FirstOrDefault(x => x != null);
        check("building the FirstStep spends its cost and reports the fund",
            built.Rsp.Retcode == 0 && built.Rsp.CurFund == (uint)manager.Data.Fund &&
            manager.Data.Fund == steps.Sum(step => step.CoinCost) + 7 - firstStep.CoinCost &&
            updates.Any(x => x.FCKNCNDEKAE?.IOJOIOHIEAB == (uint)manager.Data.Fund));
        check("the build notify unlocks the step's static props in its area",
            firstStep.StaticPropIDList.All(id =>
                updates.Any(x => x.NEFBGMNGKBN is { } unlock && unlock.AreaId == areaId && unlock.JBEEKBJMIIA == id)));
        check("the area update finishes the FirstStep and opens the other steps",
            areaUpdate != null && areaUpdate.AreaId == areaId && areaUpdate.Progress == 100 / steps.Count &&
            areaUpdate.FOOMIPJJKOG.Single(x => x.DNLIGEOFPEL == firstStep.ID).Status == (JIIEHEBIBMO)Finished &&
            areaUpdate.FOOMIPJJKOG.Where(x => x.DNLIGEOFPEL != firstStep.ID)
                .All(x => (uint)x.Status == Buildable));
        check("finishing the FirstStep hands out the other steps' blueprints",
            updates.Select(x => x.FPMNNMMPOKL).FirstOrDefault(x => x != null)?.AreaList
                .Single(x => x.AreaId == areaId).FOOMIPJJKOG.Order()
                .SequenceEqual(steps.Where(step => step.ID != firstStep.ID).Select(step => (uint)step.ID).Order()) ==
            true);

        // Without the cached room names no floor matches; the first party-car floor then lets the checks fail plainly.
        var floor = TrainFloors().FirstOrDefault(x => x.FloorSavedValue.Any(value => value.Name == ProgressName(areaId)))
                    ?? TrainFloors().First();
        var hasFlag = floor.FloorSavedValue.First(value =>
            value.Name.StartsWith("Build_Has", StringComparison.Ordinal)).Name;
        var hasFlagBefore = player.SceneData!.GetFloorSavedValue(floor.FloorID, hasFlag);
        manager.ApplyFloorSavedValues();
        var totalSteps = TrainPartyBuildCatalog.Areas.Sum(x => TrainPartyBuildCatalog.GetAreaSteps(x.ID).Count);
        check("a partly built room sets its progress, leaves it unfinished and keeps the item flags",
            player.SceneData.GetFloorSavedValue(floor.FloorID, ProgressName(areaId)) == 100 / steps.Count &&
            player.SceneData.GetFloorSavedValue(floor.FloorID, FinishedName(floor, areaId)) == 0 &&
            player.SceneData.GetFloorSavedValue(floor.FloorID, GlobalProgress) == 100 / totalSteps &&
            player.SceneData.GetFloorSavedValue(floor.FloorID, hasFlag) == hasFlagBefore);
        check("progress below the first star threshold reports no star change",
            updates.All(x => x.FHBMGFFBOLP == null) && manager.GetStarNum() == 0);

        var fundAfterFirst = manager.Data.Fund;
        var again = manager.BuildStartStep(Req(firstStep.ID));
        check("a finished step cannot be bought twice",
            again.Rsp.Retcode == (uint)Retcode.RetReqParaInvalid && manager.Data.Fund == fundAfterFirst &&
            manager.Data.Areas[areaId].StepList.Count(id => id == firstStep.ID) == 1);

        TrainPartyBuildingUpdateNotify? last = null;
        foreach (var step in steps.Where(step => step.ID != firstStep.ID))
            last = manager.BuildStartStep(Req(step.ID)).Notify;
        var dependents = GameData.TrainPartyAreaConfigData.Values.Where(x => x.RequireAreaID == areaId)
            .Select(x => (uint)x.ID).Order().ToList();
        check("completing the area unlocks the areas that require it, each with its goal",
            last != null && manager.Data.Fund == 7 &&
            last.OOABOBHEKEH.Where(x => x.CNGOKIKBGAJ != null).Select(x => x.CNGOKIKBGAJ.NLJMJCGFFMD).Order()
                .SequenceEqual(dependents) &&
            last.OOABOBHEKEH.Count(x => x.LLCOCEJHKLD != null) == dependents.Count);

        var areaStars = firstArea.ProgressBonusList.Sum(x => x.AddStar);
        check("completing the area reports its stars and the build level they reach",
            last?.OOABOBHEKEH.Select(x => x.FHBMGFFBOLP).LastOrDefault(x => x != null) is { } starUpdate &&
            starUpdate.StarNum == areaStars && starUpdate.BHEJCEJHLIB == TrainPartyBuildCatalog.GetBuildLevel(areaStars) &&
            starUpdate.BHEJCEJHLIB > 0);
        manager.ApplyFloorSavedValues();
        check("the finished room reaches 100 and its Build_<Room>_Finished while the car is not complete",
            player.SceneData.GetFloorSavedValue(floor.FloorID, ProgressName(areaId)) == 100 &&
            player.SceneData.GetFloorSavedValue(floor.FloorID, FinishedName(floor, areaId)) == 1 &&
            player.SceneData.GetFloorSavedValue(floor.FloorID, GlobalProgress) == steps.Count * 100 / totalSteps &&
            player.SceneData.GetFloorSavedValue(floor.FloorID, hasFlag) == hasFlagBefore);

        var info = manager.ToPartyInfo();
        var area = info.AreaList.Single(x => x.AreaId == areaId);
        check("the built area resyncs with its static props, finished steps and progress 100",
            area.Progress == 100 &&
            area.StepIdList.ToHashSet().SetEquals(steps.SelectMany(step => step.StaticPropIDList).Select(id => (uint)id)) &&
            area.StaticPropIdList.ToHashSet().SetEquals(steps.Select(step => (uint)step.ID)) &&
            dependents.All(id => info.AreaList.Any(x => x.AreaId == id && x.Progress == 0 && x.StepIdList.Count == 0)));
    }

    private static async Task DiyPlacement(Action<string, bool> check)
    {
        var player = await NewPlayer(60036);
        var manager = player.TrainPartyManager!;
        manager.EnsureDefaults();
        var areaId = manager.Data.Areas.Keys.Min();
        var otherArea = manager.Data.Areas.Keys.First(x => x != areaId);
        var prop = (uint)GameData.TrainPartyDynamicConfigData.Keys.Min();
        var slotA = (uint)(areaId * 100000 + 101);
        var slotB = (uint)(areaId * 100000 + 201);

        var placed = manager.BuildDiy(new TrainPartyBuildDiyCsReq
            { AreaId = (uint)areaId, DiceSlotId = slotA, DiyDynamicId = prop });
        check("placing an owned prop answers with the changed slot",
            placed.Retcode == 0 && placed.JKPJFKCBIEM.Count == 1 &&
            placed.JKPJFKCBIEM[0] is { AreaId: var a, OOFPPPCEIDP: { DiceSlotId: var s, DiyDynamicId: var d } } &&
            a == areaId && s == slotA && d == prop && manager.Data.Areas[areaId].DynamicInfo[(int)slotA] == prop);

        var moved = manager.BuildDiy(new TrainPartyBuildDiyCsReq
            { AreaId = (uint)areaId, DiceSlotId = slotB, DiyDynamicId = prop });
        check("placing the prop elsewhere empties its old slot",
            moved.Retcode == 0 && moved.JKPJFKCBIEM.Count == 2 &&
            moved.JKPJFKCBIEM.Any(x => x.OOFPPPCEIDP.DiceSlotId == slotA && x.OOFPPPCEIDP.DiyDynamicId == 0) &&
            !manager.Data.Areas[areaId].DynamicInfo.ContainsKey((int)slotA) &&
            manager.Data.Areas[areaId].DynamicInfo[(int)slotB] == prop);

        var unowned = manager.BuildDiy(new TrainPartyBuildDiyCsReq
            { AreaId = (uint)areaId, DiceSlotId = slotA, DiyDynamicId = 990001 });
        var foreignSlot = manager.BuildDiy(new TrainPartyBuildDiyCsReq
            { AreaId = (uint)areaId, DiceSlotId = (uint)(otherArea * 100000 + 101), DiyDynamicId = prop });
        check("an unowned prop or another area's slot is rejected without changes",
            unowned.Retcode == (uint)Retcode.RetReqParaInvalid && foreignSlot.Retcode == (uint)Retcode.RetReqParaInvalid &&
            unowned.JKPJFKCBIEM.Count == 0 && foreignSlot.JKPJFKCBIEM.Count == 0 &&
            manager.Data.Areas[areaId].DynamicInfo.Count == 1);

        var cleared = manager.BuildDiy(new TrainPartyBuildDiyCsReq { AreaId = (uint)areaId, DiceSlotId = slotB });
        check("an empty prop clears the slot",
            cleared.Retcode == 0 && manager.Data.Areas[areaId].DynamicInfo.Count == 0 &&
            manager.ToPartyInfo().AreaList.Single(x => x.AreaId == areaId).DynamicInfo.Count == 0);
    }
}
