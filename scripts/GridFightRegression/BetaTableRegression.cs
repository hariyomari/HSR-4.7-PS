using Google.Protobuf;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Data.GridFight;
using March7thHoney.Enums.GridFight;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.Proto;

static class BetaTableRegression
{
    public static void Run(GridFightResourceCatalog catalog,
        Func<uint?, GridFightManager> newRun, Action<string, bool> check)
    {
        Console.WriteLine($"Native season {catalog.CurrentSeason}: {catalog.RolePool.Count} roles, {catalog.CampPool.Count} camps.");
        check("native roles resolve without the removed saved-value column", catalog.RolePool.Count > 5 &&
            catalog.RolePool.All(role => role.SeasonID == catalog.CurrentSeason && role.UsesNativeSkillOverrides && role.RoleSavedValueList.Count == 0));
        check("current season includes the dumped 1511 role", catalog.CurrentSeason != 2 || catalog.RolePool.Any(role => role.AvatarID == 1511));
        check("active camp IDs resolve to the current season", catalog.CampPool.All(camp => GameData.GridFightCampData[camp.ID].SeasonID == catalog.CurrentSeason));
        check("inactive camps retain distinct cache keys", GameData.GridFightCampData.Values.Any(camp => camp.SeasonID != catalog.CurrentSeason && camp.SeasonID != 0));
        // Season 2 repurposes talent 2011; no final-turn healing source is confirmed for it yet.
        check("season two does not heal from repurposed 2011", catalog.IsSeasonOne || catalog.GetFinalBattleTurnHealing(0) == 0);
        var manager = newRun(null);
        var session = manager.Session!;
        var other = GameData.GridFightRoleBasicInfoData.Values.First(role => role.SeasonID != catalog.CurrentSeason);
        var beforeCount = session.Roles.Count;
        check("cross-season grant fails without changing inventory", !session.TryAddRole(other.ID, 1, out _) && session.Roles.Count == beforeCount);
        // Fake avatars (season-2 帕姆王 604202) are granted by rewards, so only the shop pool excludes them.
        check("fake avatars stay out of the role pool", catalog.RolePool.All(role => !role.IsFakeAvatar));
        var scopedRewards = true;
        for (var attempt = 0; attempt < 100; attempt++)
            scopedRewards &= GridFightSpecialFeatures.TryResolveOrb(200002, out var rewards) &&
                rewards.Where(reward => reward.Type == GridFightBonusTypeEnum.SpecificAvatar).All(reward =>
                    GameData.GridFightRoleBasicInfoData[reward.ItemId].SeasonID == catalog.CurrentSeason);
        check("random role rewards do not mix seasons", scopedRewards);
        check("native role can be added at max star", session.TryAddRole(catalog.RolePool.First().ID, 3, out _));
        check("fully excluded shop is empty", catalog.RollShop(catalog.InitialPlayerLevel,
            excludedRoleIds: GameData.GridFightRoleBasicInfoData.Keys.ToHashSet()).Count == 0);
        var system = manager.BuildSystemInfo();
        check("system info survives protobuf round trip", GridFightSystemInfo.Parser.ParseFrom(system.ToByteArray()).Equals(system));
        VerifyTableOverlay(catalog, check);
        VerifyRoute(catalog, newRun, check);
    }

    private static void VerifyTableOverlay(GridFightResourceCatalog catalog, Action<string, bool> check)
    {
        var original = GameData.GridFightCampData.ToDictionary();
        var monsters = GameData.GridFightMonsterData.Keys.Take(2).ToArray();
        var fixture = new GridFightCampExcel { ID = 900000, IfRandomEnabled = 1, MonsterList = [monsters[0]] };
        try
        {
            GridFightTableCompatibility.Apply([fixture,
                new GridFightCampSeasonExcel { CampID = fixture.ID, SeasonID = catalog.CurrentSeason, MonsterList = [monsters[1]] },
                new GridFightCampSeasonExcel { CampID = fixture.ID, SeasonID = catalog.CurrentSeason + 1, MonsterList = [monsters[0]] }]);
            check("season monsters replace base monsters without losing other season", GameData.GridFightCampData.Count == 2 &&
                GameData.GridFightCampData[fixture.ID].MonsterList.SequenceEqual([monsters[1]]) &&
                GameData.GridFightCampData.Values.Any(camp => camp.SeasonID == catalog.CurrentSeason + 1 && camp.MonsterList.SequenceEqual([monsters[0]])));
            var before = GameData.GridFightCampData.ToDictionary();
            var rejected = false;
            try { GridFightTableCompatibility.Apply([fixture, new GridFightCampSeasonExcel { CampID = fixture.ID + 1, SeasonID = catalog.CurrentSeason }]); }
            catch (InvalidDataException) { rejected = true; }
            check("missing base camp is rejected atomically", rejected && GameData.GridFightCampData.Count == before.Count &&
                before.All(pair => ReferenceEquals(GameData.GridFightCampData[pair.Key], pair.Value)));
            rejected = false;
            try
            {
                GridFightTableCompatibility.Apply([fixture,
                    new GridFightCampSeasonExcel { CampID = fixture.ID, SeasonID = catalog.CurrentSeason, MonsterList = [monsters[0]] },
                    new GridFightCampSeasonExcel { CampID = fixture.ID, SeasonID = catalog.CurrentSeason, MonsterList = [monsters[1]] }]);
            }
            catch (InvalidDataException) { rejected = true; }
            check("duplicate season camp is rejected atomically", rejected && before.All(pair =>
                ReferenceEquals(GameData.GridFightCampData[pair.Key], pair.Value)));
            rejected = false;
            try
            {
                GridFightTableCompatibility.Apply([fixture,
                    new GridFightCampSeasonExcel { CampID = fixture.ID, SeasonID = catalog.CurrentSeason, MonsterList = [uint.MaxValue] }]);
            }
            catch (InvalidDataException) { rejected = true; }
            check("missing seasonal monster is rejected atomically", rejected && before.All(pair =>
                ReferenceEquals(GameData.GridFightCampData[pair.Key], pair.Value)));
            GridFightTableCompatibility.Apply([fixture]);
            check("legacy combined tables remain unchanged", before.All(pair => ReferenceEquals(GameData.GridFightCampData[pair.Key], pair.Value)));
        }
        finally
        {
            GameData.GridFightCampData.Clear();
            foreach (var pair in original) GameData.GridFightCampData.Add(pair.Key, pair.Value);
        }
    }

    private static void VerifyRoute(GridFightResourceCatalog catalog, Func<uint?, GridFightManager> newRun, Action<string, bool> check)
    {
        var manager = newRun(null);
        var route = manager.Session!;
        route.Roles.Clear();
        if (!route.TryAddRole(catalog.RolePool.First(role => role.AvatarID < 2000).ID, 1, out var acquisition))
            throw new InvalidOperationException("Native route fighter could not be added.");
        acquisition.AcquiredRoles.Single().Position = 1;
        var battles = 0;
        var finished = false;
        for (var transition = 0; transition < 300 && !finished; transition++)
        {
            var queue = route.PendingQueuePosition;
            switch (route.Phase)
            {
                case GridFightSessionPhase.Preparing:
                    if (!route.EnterBattle()) throw new InvalidOperationException($"Cannot enter native battle {route.ChapterId}/{route.SectionId}.");
                    check($"native battle {++battles}: scoped monsters and nonzero formation", route.BattlePlan != null &&
                        route.BattlePlan.Waves.Count > 0 && route.BattlePlan.Waves.All(wave => wave.FormationWaveId != 0 && wave.Monsters.Count > 0) &&
                        GameData.GridFightCampData[route.BattlePlan.CampId].SeasonID == catalog.CurrentSeason);
                    var stats = new GridFightBattleStatistics(100, [], [], [], [], 0, new Dictionary<string, uint>());
                    var settlement = route.ResolveBattle(true, stats);
                    check($"native battle {battles}: settles once", settlement.Success && !route.ResolveBattle(true, stats).Success);
                    finished = !settlement.RouteAdvanced;
                    break;
                case GridFightSessionPhase.AwaitingBattleRound:
                case GridFightSessionPhase.AwaitingSupplyRound:
                    if (!route.HandlePending(queue, GridFightPendingRequestKind.RoundBegin)) throw new InvalidOperationException("Native round failed.");
                    manager.BuildBattleRoundSync(queue, []);
                    break;
                case GridFightSessionPhase.SelectingInvestment:
                    var investment = route.InvestmentOffer.First();
                    if (!route.SelectInvestment(queue, investment)) throw new InvalidOperationException("Native investment failed.");
                    manager.BuildInvestmentSelectionSync(queue, investment);
                    break;
                case GridFightSessionPhase.SelectingSupply:
                    var supply = route.SelectSupply(queue, [1]);
                    if (!supply.Success) throw new InvalidOperationException("Native supply failed.");
                    manager.BuildSupplySelectionSync(queue, 1, supply);
                    break;
                case GridFightSessionPhase.SelectingEncounter:
                    if (!route.SelectEncounter(queue, route.EncounterOptions.First().EliteBranchId)) throw new InvalidOperationException("Native encounter failed.");
                    break;
                case GridFightSessionPhase.AwaitingBattleReturn:
                    if (!route.HandlePending(queue, GridFightPendingRequestKind.ReturnPreparation)) throw new InvalidOperationException("Native return failed.");
                    break;
                case GridFightSessionPhase.AwaitingBackToPrepare:
                    if (!route.BackToPrepare()) throw new InvalidOperationException("Native preparation failed.");
                    manager.BuildPreparationSync();
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected native state: {route.Phase}.");
            }
        }
        check("native full route reaches final settlement", finished && battles > 10);
    }
}
