using System.Reflection;
using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Enums.GridFight;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.GridFight.Battle;
using March7thHoney.GameServer.Game.GridFight.Persistence;
using March7thHoney.Proto;

static class ThreeEffectsRegression
{
    public static void Run(Func<uint?, GridFightManager> newRun, GridFightResourceCatalog catalog,
        Action<string, bool> check)
    {
        var roleId = catalog.RolePool.First(role => role.Rarity == 1).ID;
        var statistics = new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>());

        if (catalog.PortalPool.Contains(GridFightSession.PowerGrowthPortalId))
        {
            var manager = newRun(GridFightSession.PowerGrowthPortalId);
            var run = manager.Session!;
            var target = run.Roles.Single(role => role.HasPowerGrowth);
            check("战力提升: marks exactly its opening 2-star 1-cost role", target.Star == 2 &&
                GameData.GridFightRoleBasicInfoData[target.RoleId].Rarity == 1 && target.PermanentPropertyBonuses.Count == 0);
            run.Roles.RemoveAll(role => role != target);
            target.Position = 1;
            var entered = run.EnterBattle();
            var resolved = run.ResolveBattle(true, statistics);
            var bonuses = target.PermanentPropertyBonuses.Values.Sum(value => (long)value);
            check("战力提升: entering the next node grants one permanent 5% bonus",
                entered && resolved.Success && bonuses == 50);
            check("战力提升: repeated battle result does not grant growth twice",
                !run.ResolveBattle(true, statistics).Success && target.PermanentPropertyBonuses.Values.Sum(value => (long)value) == 50);
            var sync = manager.BuildBattleRoundSync(run.PendingQueuePosition, []);
            var synced = sync.ResultList.SelectMany(result => result.UpdateDynamicList)
                .FirstOrDefault(data => data.UpdateRoleInfo?.UniqueId == target.UniqueId)?.UpdateRoleInfo;
            var battle = GridFightBattleProtoBuilder.BuildRoleInfo(run, target, Gender.Man);
            check("战力提升: lobby and battle both carry the grown property", synced != null &&
                target.PermanentPropertyBonuses.All(pair => synced.ConvertPropertyToFixpoint[pair.Key] ==
                    battle.ConvertPropertyToFixpoint[pair.Key] && synced.ConvertPropertyToFixpoint[pair.Key] >= pair.Value));
            check("战力提升: sync is drained once", run.TakePowerGrowthRoleUpdates().Count == 0);
            var restored = SnapshotProbe.RoundTrip(manager.Catalog, run);
            var restoredTarget = restored.Roles.Single();
            check("战力提升: save/restore preserves the target and accumulated bonuses",
                restoredTarget.HasPowerGrowth && restoredTarget.PermanentPropertyBonuses.OrderBy(pair => pair.Key)
                    .SequenceEqual(target.PermanentPropertyBonuses.OrderBy(pair => pair.Key)));
            restored.TryAddRole(target.RoleId, 2, out _);
            restored.TryAddRole(target.RoleId, 2, out _);
            var merged = restored.Roles.Single();
            check("战力提升: a 3-star merge inherits its growth", merged.Star == 3 && merged.HasPowerGrowth &&
                merged.PermanentPropertyBonuses.Values.Sum(value => (long)value) == 50);
            typeof(GridFightSession).GetMethod("GrowPowerPortalRoles", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(restored, null);
            check("战力提升: the merged role continues growing and other copies receive no growth",
                merged.PermanentPropertyBonuses.Values.Sum(value => (long)value) == 100);
            restored.TryAddRole(roleId, 1, out _);
            check("战力提升: buying another copy does not copy the portal effect",
                restored.Roles.Where(role => role != merged).All(role => !role.HasPowerGrowth && role.PermanentPropertyBonuses.Count == 0));
        }

        // Binary fixture with the five original role members verifies old saves default new members correctly.
        var oldRole = MemoryPackSerializer.Deserialize<GridFightRoleSnapshot>(MemoryPackSerializer.Serialize(
            new LegacyGridFightRoleSnapshot { UniqueId = 7, RoleId = roleId, Star = 2, Position = 14,
                EquippedEquipmentUniqueIds = [9] }))!;
        check("old role snapshot remains readable after appending growth members", oldRole.UniqueId == 7 &&
            oldRole.EquippedEquipmentUniqueIds.SequenceEqual(new uint[] { 9 }) && !oldRole.HasPowerGrowth &&
            oldRole.PermanentPropertyBonuses is null or { Count: 0 });
        var legacyManager = newRun(101);
        var legacyRun = legacyManager.Session!;
        legacyRun.Roles.Clear();
        legacyRun.TryAddRole(roleId, 1, out _);
        var legacySnapshot = legacyRun.CaptureSnapshot();
        oldRole.UniqueId = legacySnapshot.Roles[0].UniqueId;
        legacySnapshot.Roles[0] = oldRole;
        var legacyRestored = GridFightSession.Restore(legacyManager.Catalog, legacySnapshot);
        check("old role snapshot restores missing growth properties as an empty dictionary",
            legacyRestored.Roles.Single().PermanentPropertyBonuses.Count == 0);

        if (catalog.AugmentPool.Contains(GridFightSession.PrismaticProcurementAugmentId))
        {
            var manager = newRun(101);
            var run = manager.Session!;
            run.Roles.Clear();
            run.TryAddRole(roleId, 1, out _);
            var left = run.Roles.Single();
            left.Position = 14;
            var otherId = catalog.RolePool.First(role => role.Rarity == 2).ID;
            run.TryAddRole(otherId, 1, out _);
            run.Roles.Single(role => role.RoleId == otherId).Position = 16;
            run.ActiveInvestmentIds.Add(GridFightSession.PrismaticProcurementAugmentId);
            run.AdjustGold(200);
            for (var count = 1; count <= 4; count++) run.RefreshShop(true);
            check("采购专员·彩: manual refreshes count from acquisition", run.ProcurementRefreshCount == 4);
            run.RefreshShop(false);
            check("采购专员·彩: automatic node shop refresh does not advance its counter", run.ProcurementRefreshCount == 4);
            var full = manager.BuildCurrentInfo()!;
            check("采购专员·彩: reconnect snapshot carries the refresh counter", full.GameInfoList
                .Any(info => info.ContextValueInfo?.ContextValueMap.GetValueOrDefault(GridFightSession.ProcurementSavedValueKey) == 4));
            var restored = SnapshotProbe.RoundTrip(manager.Catalog, run);
            var fifth = restored.RefreshShop(true);
            check("采购专员·彩: the fifth refresh after restore offers five roles of the leftmost bench cost",
                fifth && restored.ProcurementRefreshCount == 0 && restored.Shop.Count == 5 &&
                restored.Shop.All(slot => slot.SpecialGoodsId == 0 && GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity == 1));
            run.RefreshShop(true);
            var refreshSync = manager.BuildRefreshShopSync();
            check("采购专员·彩: refresh sends the counter reset", refreshSync.ResultList.SelectMany(result => result.UpdateDynamicList)
                .Any(data => data.SavedValueUpdate?.SavedValues.ContainsKey(GridFightSession.ProcurementSavedValueKey) == true &&
                    data.SavedValueUpdate.SavedValues[GridFightSession.ProcurementSavedValueKey] == 0));
            run.Roles.RemoveAll(role => role != left);
            left.RoleId = otherId;
            run.AdjustFreeRefresh(5);
            for (var count = 0; count < 5; count++) run.RefreshShop(true);
            check("采购专员·彩: free refreshes count and read the current bench role at the next trigger",
                run.ProcurementRefreshCount == 0 && run.Shop.Count == 5 &&
                run.Shop.All(slot => GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity == 2));
            run.AdjustGold(-checked((int)run.Gold));
            check("采购专员·彩: failed refresh does not advance the counter", !run.RefreshShop(true) && run.ProcurementRefreshCount == 0);
            run.Roles.Clear();
            run.AdjustGold(100);
            for (var count = 0; count < 5; count++) run.RefreshShop(true);
            check("采购专员·彩: an empty bench falls back to the normal shop", run.Shop.Count > 0 && run.ProcurementRefreshCount == 0);
        }

        if (catalog.CurrentSeason != 2) return;
        var eliteNode = catalog.StandardRoute.First(node => node.NodeType == GridFightNodeTypeEnum.EliteBranch);
        foreach (var state in new[] { "active", "lost", "sold", "upgraded" })
        {
            var manager = newRun(101);
            var run = manager.Session!;
            run.Roles.Clear();
            run.TryAddRole(roleId, 1, out _);
            var role = run.Roles.Single();
            role.Position = 1;
            var lights = (List<GridFightSpotLightState>)typeof(GridFightSession)
                .GetField("_spotLights", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(run)!;
            var crown = new GridFightSpotLightState { UniqueId = 9001, RoleUniqueId = role.UniqueId,
                RoleId = role.RoleId, SpotLightId = GridFightSession.HardBattleTreasureSpotLightId, IsLoss = state == "lost" };
            lights.Add(crown);
            var battleRole = GridFightBattleProtoBuilder.BuildRoleInfo(run, role, Gender.Man);
            var lobbyRole = manager.BuildCurrentInfo()!.GameInfoList.First(info => info.TeamInfo != null).TeamInfo.GridGameRoleList.Single();
            var expected = state == "lost" ? 100u : 300u;
            check($"苦战寻宝 {state}: lobby and battle apply 20% healing and shield only to the bound active role",
                lobbyRole.ConvertPropertyToFixpoint[1013] == expected && lobbyRole.ConvertPropertyToFixpoint[1019] == expected &&
                battleRole.ConvertPropertyToFixpoint[1013] == expected && battleRole.ConvertPropertyToFixpoint[1019] == expected);
            if (state == "sold") crown.RoleUniqueId += 1000;
            if (state == "upgraded") run.ActiveInvestmentIds.Add(GridFightSession.FreeUpgradeAugmentId);
            run.TryMoveToSection(eliteNode.ChapterId, eliteNode.SectionId, out _);
            run.HandlePending(run.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
            var option = run.EncounterOptions.OrderByDescending(option => option.Quality).First();
            run.SelectEncounter(run.PendingQueuePosition, option.EliteBranchId);
            run.HandlePending(run.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
            if (run.Phase == GridFightSessionPhase.AwaitingBackToPrepare) run.BackToPrepare();
            run.TakeAugmentNodeEffects();
            var entered = run.EnterBattle();
            var settled = run.ResolveBattle(true, statistics);
            var paid = run.TakeAugmentNodeEffects();
            var drops = paid.SelectMany(effect => effect.Application.Drops).ToList();
            var eligible = state is "active" or "upgraded";
            check($"苦战寻宝 {state}: encounter pays its original preview plus one extra reward only while owned",
                entered && settled.Success && paid.All(effect => effect.Application.Success) &&
                (eligible ? drops.Count > option.Rewards!.Count : drops.Count == option.Rewards!.Count));
            check($"苦战寻宝 {state}: repeated settlement cannot pay twice", !run.ResolveBattle(true, statistics).Success &&
                run.TakeAugmentNodeEffects().Count == 0);
        }
    }
}

[MemoryPackable]
public partial class LegacyGridFightRoleSnapshot
{
    public uint UniqueId { get; set; }
    public uint RoleId { get; set; }
    public uint Star { get; set; }
    public uint Position { get; set; }
    public List<uint> EquippedEquipmentUniqueIds { get; set; } = [];
}
