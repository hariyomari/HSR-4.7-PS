using System.Reflection;
using Google.Protobuf;
using March7thHoney.Data;
using March7thHoney.Enums.GridFight;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.GridFight.Battle;
using March7thHoney.Proto;

static class EconomyEffectsRegression
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static object? Call(GridFightSession run, string method, params object[] args) =>
        typeof(GridFightSession).GetMethod(method, Hidden)!.Invoke(run, args);

    private static void Set(GridFightSession run, string property, object value) =>
        typeof(GridFightSession).GetProperty(property)!.SetValue(run, value);

    public static void Run(Func<uint?, GridFightManager> newRun, GridFightResourceCatalog catalog,
        Action<string, bool> check)
    {
        GridFightManager With(uint augment)
        {
            var manager = newRun(101);
            var run = manager.Session!;
            run.ActiveInvestmentIds.Add(augment);
            Call(run, "PrepareInvestmentSelectionReward", augment);
            Call(run, "ApplyAugmentSelectionEffects", augment);
            return manager;
        }
        uint Role(uint cost) => catalog.RolePool.First(role => role.Rarity == cost).ID;
        void Board(GridFightSession run, uint cost = 1)
        {
            run.Roles.Clear();
            run.TryAddRole(Role(cost), 1, out _);
            run.Roles.Single().Position = 1;
        }
        void Shop(GridFightSession run, uint cost, int count)
        {
            run.Shop.Clear();
            var ids = catalog.RolePool.Where(role => role.Rarity == cost).Select(role => role.ID).ToList();
            for (var index = 0; index < count; index++) run.Shop.Add(new GridFightShopSlot
            { Index = (uint)index, RoleId = ids[index % ids.Count], Star = 1, Price = cost });
        }
        var statistics = new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>());

        {
            var manager = With(301301);
            var run = manager.Session!;
            Set(run, nameof(run.PlayerLevel), 5u);
            Set(run, nameof(run.PlayerExp), 0u);
            Shop(run, 1, 3);
            run.Shop[1].SoldOut = true;
            run.Shop[2] = new GridFightShopSlot { Index = 2, SpecialGoodsId = 311, Price = 2 };
            run.AdjustGold(100);
            var original = run.Shop[0].RoleId;
            var gold = run.Gold;
            var exp = run.PlayerExp;
            var purchased = run.BuyExp();
            var promoted = run.Shop[0];
            check("晋升名额: a successful XP purchase upgrades a shop role by exactly one cost", purchased &&
                promoted.RoleId != original && GameData.GridFightRoleBasicInfoData[promoted.RoleId].Rarity == 2 &&
                run.Gold == gold - run.CurrentBuyExpCost && run.PlayerExp == exp + catalog.BuyExpAmount);
            check("晋升名额: recomputes price and preserves slot/star", promoted.Index == 0 && promoted.Star == 1 &&
                promoted.Price == catalog.GetBuyPrice(promoted.RoleId, promoted.Star));
            check("晋升名额: sold-out and special goods stay untouched", run.Shop[1].SoldOut &&
                GameData.GridFightRoleBasicInfoData[run.Shop[1].RoleId].Rarity == 1 && run.Shop[2].SpecialGoodsId == 311);
            var notify = GridFightSyncUpdateResultScNotify.Parser.ParseFrom(manager.BuildSync(
                GridFightUpdateSrcType.KgridFightSrcBuyExp, includeGold: true, includeShop: true, includeLevel: true).ToByteArray());
            var goods = notify.ResultList.SelectMany(result => result.UpdateDynamicList)
                .Single(update => update.ShopInfo != null).ShopInfo.GoodsList[0];
            check("晋升名额: XP response synchronizes the promoted role and price", goods.RoleGoodsInfo.RoleId == promoted.RoleId &&
                goods.RoleGoodsInfo.RoleStar == promoted.Star && goods.Price == promoted.Price);
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            check("晋升名额: promoted goods survive reload", restored.Shop[0].RoleId == promoted.RoleId && restored.Shop[0].Price == promoted.Price);
            var boughtAgain = restored.BuyExp();
            check("晋升名额: subsequent XP purchase also promotes", boughtAgain && GameData.GridFightRoleBasicInfoData[restored.Shop[0].RoleId].Rarity == 3);

            Shop(run, 1, 5);
            var before = run.Shop.Select(slot => slot.RoleId).ToArray();
            run.BuyExp();
            check("晋升名额: changes only one of several eligible goods", run.Shop.Where((slot, index) => slot.RoleId != before[index]).Count() == 1);
            Shop(run, 5, 5);
            before = run.Shop.Select(slot => slot.RoleId).ToArray();
            check("晋升名额: top-cost shop still allows XP purchase without invalid promotion", run.BuyExp() &&
                run.Shop.Select(slot => slot.RoleId).SequenceEqual(before));
            run.Shop.Clear();
            check("晋升名额: empty shop still allows XP purchase", run.BuyExp() && run.Shop.Count == 0);

            Shop(run, 1, 1);
            original = run.Shop[0].RoleId;
            run.AdjustGold(-(int)run.Gold);
            check("晋升名额: insufficient gold does not promote", !run.BuyExp() && run.Shop[0].RoleId == original);
            run.AdjustGold(100);
            Set(run, nameof(run.Phase), GridFightSessionPhase.InBattle);
            check("晋升名额: rejected purchase phase does not promote", !run.BuyExp() && run.Shop[0].RoleId == original);
            Set(run, nameof(run.Phase), GridFightSessionPhase.Preparing);
            Set(run, nameof(run.PlayerLevel), catalog.MaxPlayerLevel);
            check("晋升名额: level cap does not permit free promotion", !run.BuyExp() && run.Shop[0].RoleId == original);

            var ordinary = newRun(101).Session!;
            Shop(ordinary, 1, 2);
            ordinary.AdjustGold(100);
            before = ordinary.Shop.Select(slot => slot.RoleId).ToArray();
            check("晋升名额: ordinary XP purchases keep the shop unchanged", ordinary.BuyExp() &&
                ordinary.Shop.Select(slot => slot.RoleId).SequenceEqual(before));
        }

        uint DisplayedCap(GridFightCurrentInfo current) => GridFightCurrentInfo.Parser.ParseFrom(current.ToByteArray())
            .GameInfoList.Single(info => info.BasicInfo != null).BasicInfo.MaxInterestGold;
        foreach (var (augments, expected) in new (uint[], uint)[]
                 { ([], catalog.MaxInterestGold), ([300101], 9), ([300801], 10), ([300101, 300801], 10) })
        {
            var manager = newRun(101);
            var run = manager.Session!;
            var previousCap = run.CurrentMaxInterestGold;
            foreach (var id in augments) run.ActiveInvestmentIds.Add(id);
            run.AdjustGold(85 - (int)run.Gold);
            check($"interest display [{string.Join(',', augments)}]: current-info cap matches settlement cap",
                DisplayedCap(manager.BuildCurrentInfo()!) == expected && run.CurrentMaxInterestGold == expected);
            var refresh = manager.BuildInterestDisplayRefresh(run, previousCap);
            check($"interest display [{string.Join(',', augments)}]: refresh only when cap changes",
                expected == previousCap ? refresh == null : refresh?.SystemInfo != null &&
                    DisplayedCap(refresh.CurrentInfo) == expected && refresh.CurrentInfo.GameInfoList
                        .Single(info => info.BasicInfo != null).BasicInfo.Gold == 85);
            check($"interest display [{string.Join(',', augments)}]: repeated unchanged request needs no refresh",
                manager.BuildInterestDisplayRefresh(run, expected) == null);
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            check($"interest display [{string.Join(',', augments)}]: cap survives reconnect", restored.CurrentMaxInterestGold == expected);
            Board(run);
            var entered = run.EnterBattle();
            var settled = run.ResolveBattle(true, statistics);
            check($"interest display [{string.Join(',', augments)}]: 85 gold pays the previewed interest",
                entered && settled.Success && settled.InterestGold == Math.Min(85u / catalog.DepositPerInterest, expected));
        }

        foreach (var (id, interest) in new[] { (300801u, 10u), (300101u, 9u) })
        {
            var manager = With(id);
            var run = manager.Session!;
            Board(run);
            run.AdjustGold(100 - (int)run.Gold);
            var entered = run.EnterBattle();
            var settled = run.ResolveBattle(true, statistics);
            check($"economy {id}: battle settlement applies its interest cap", entered && settled.Success && settled.InterestGold == interest);
        }
        foreach (var spend in new[] { false, true })
        {
            var run = With(300101).Session!;
            run.AdjustGold(100);
            if (spend) run.BuyExp();
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            var before = restored.Gold;
            Call(restored, "OnEconomyNodeEntered");
            check($"开源节流: persisted spending state, spent={spend}", restored.Gold - before == (spend ? 0u : 9u));
        }
        foreach (var (id, interval, cost) in new[] { (201201u, 7, 2u), (102201u, 4, 3u) })
        {
            var manager = With(id);
            var run = manager.Session!;
            Board(run, 2);
            run.Roles.Single().Position = 14;
            run.AdjustGold(200);
            for (var index = 1; index <= interval; index++)
            {
                var success = run.RefreshShop(true);
                if (id == 102201 && index == 1)
                    check("市场干预: the first refresh is all 3-cost", success && run.Shop.All(slot => GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity == 3));
            }
            check($"economy {id}: scheduled refresh has the required rarity", run.Shop.Count == 5 &&
                run.Shop.All(slot => slot.SpecialGoodsId == 0 && GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity == cost));
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            restored.AdjustGold(-checked((int)restored.Gold));
            restored.AdjustFreeRefresh(-checked((int)restored.FreeShopRefreshCount));
            check($"economy {id}: failed refresh is rejected", !restored.RefreshShop(true));
        }
        {
            var run = With(202201).Session!;
            run.Roles.Clear();
            run.AdjustGold(100);
            Shop(run, 4, 2);
            var first = run.BuyGoods([0]);
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            var second = restored.BuyGoods([1]);
            check("四费晋升: next 4-cost is 2-star exactly once across restore", first.Success && first.AcquiredRoles.Single().Star == 2 &&
                second.Success && second.AcquiredRoles.Single().Star == 1);
        }
        {
            var manager = With(101201);
            var run = manager.Session!;
            run.Roles.Clear();
            run.AdjustGold(100);
            Shop(run, 5, 3);
            run.BuyGoods([0, 1]);
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            var before = restored.Gold;
            var purchase = restored.BuyGoods([2]);
            check("返利: partial progress survives restore and third purchase rebates 3", purchase.Success && restored.Gold == before - 5 + 3);
            check("返利: replay cannot rebate twice", !restored.BuyGoods([2]).Success && restored.Gold == before - 5 + 3);
        }
        {
            var manager = With(301601);
            var run = manager.Session!;
            run.AdjustGold(100);
            var previous = run.PlayerExp;
            run.RefreshShop(true);
            var notify = manager.BuildRefreshShopSync();
            check("淘金客: paid refresh grants and synchronizes 2 XP", run.PlayerExp == previous + 2 && notify.ResultList
                .SelectMany(result => result.UpdateDynamicList).Any(data => data.PlayerLevel?.Exp == run.PlayerExp));
            run.AdjustFreeRefresh(1);
            previous = run.PlayerExp;
            run.RefreshShop(true);
            check("淘金客: free and automatic refreshes grant no XP", run.PlayerExp == previous);
        }
        {
            var manager = With(300201);
            var run = manager.Session!;
            run.AdjustGold(1000);
            var before = run.Gold;
            run.BuyExp();
            check("商业间谍: XP purchase is discounted by 1", before - run.Gold == catalog.BuyExpCost - 1);
            run.TakeEconomyEffects();
            var level = run.PlayerLevel;
            while (run.PlayerLevel == level) run.BuyExp();
            var stolen = run.TakeEconomyEffects().Where(effect => effect.AugmentId == 300201)
                .SelectMany(effect => effect.Application.RoleAcquisitions).ToList();
            check("商业间谍: level-up rerolls the shop and steals three roles", stolen.Count == 3 && run.Shop.Count(slot => slot.SoldOut) == 3);
        }
        {
            var run = With(101701).Session!;
            run.AdjustGold(1000);
            var before = run.Gold;
            var purchases = 0;
            while (run.PlayerLevel < 9) { run.BuyExp(); purchases++; }
            check("成长基金: reaching level 9 grants 40 gold", run.Gold == before - purchases * catalog.BuyExpCost + 40);
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            before = restored.Gold;
            Call(restored, "GrantGrowthFund");
            check("成长基金: reward is one-shot across restore", restored.Gold == before);
        }
        {
            var run = With(101701).Session!;
            var checkpoint = Call(run, "CaptureRewardState")!;
            var before = run.Gold;
            Call(run, "AddPlayerExp", 1000u);
            Call(run, "RestoreRewardState", checkpoint);
            check("economy: reward rollback restores claim flags and pending payouts", run.Gold == before && run.TakeEconomyEffects().Count == 0);
            Call(run, "AddPlayerExp", 1000u);
            check("economy: a rolled-back growth fund can still pay once", run.Gold == before + 40);
        }
        {
            var manager = newRun(101);
            var run = manager.Session!;
            Set(run, nameof(run.Phase), GridFightSessionPhase.SelectingInvestment);
            Set(run, nameof(run.InvestmentOffer), new List<uint> { 203201 });
            var selected = run.SelectInvestment(run.PendingQueuePosition, 203201);
            check("白银投资: selection immediately opens a silver choice with 3 rerolls", selected &&
                run.Phase == GridFightSessionPhase.SelectingInvestment && run.InvestmentFreeRerollCount == 3 &&
                run.InvestmentOffer.Count == 3 && run.InvestmentOffer.All(id => GameData.GridFightAugmentData[id].Quality == GridFightAugmentQualityEnum.Silver));
            // The client keeps refreshes per card (MaxRollTimes/RollTimes): each card gets its own three.
            run.RerollInvestment(run.PendingQueuePosition, run.InvestmentOffer[0]);
            var wire = manager.BuildPendingAction()!.InvestmentAction.PendingInvestmentList;
            check("白银投资: a refresh counts on that card only and never exceeds its allowance on the wire",
                wire.Count == 3 && wire[0].RerollCount == 1 && wire.Skip(1).All(card => card.RerollCount == 0) &&
                wire.All(card => card.FreeRerollCount == 3 && card.RerollCount <= card.FreeRerollCount));
            var restored = SnapshotProbe.RoundTrip(catalog, run);
            var refreshed = true;
            for (var index = 0; index < 2; index++) refreshed &= restored.RerollInvestment(restored.PendingQueuePosition, restored.InvestmentOffer[0]);
            check("白银投资: a card refreshes exactly three times across save/restore", refreshed &&
                restored.GetInvestmentRerollCount(restored.InvestmentOffer[0]) == 3 &&
                !restored.RerollInvestment(restored.PendingQueuePosition, restored.InvestmentOffer[0]));
            var others = true;
            for (var index = 0; index < 3; index++) others &= restored.RerollInvestment(restored.PendingQueuePosition, restored.InvestmentOffer[1]);
            check("白银投资: refreshing a card a second time works on every card (used to share one pool of 3)", others &&
                restored.GetInvestmentRerollCount(restored.InvestmentOffer[1]) == 3 && restored.RerollInvestment(restored.PendingQueuePosition, restored.InvestmentOffer[2]));
            restored.SelectInvestment(restored.PendingQueuePosition, restored.InvestmentOffer[0]);
            check("白银投资: child selection resumes the original flow", restored.Phase == GridFightSessionPhase.AwaitingBattleReturn);
            for (var index = 0; index < 3; index++) Call(restored, "OnEconomyNodeEntered");
            var due = (bool)Call(restored, "TryBeginBonusInvestment", GridFightSessionPhase.AwaitingBattleReturn)!;
            check("白银投资: another silver choice becomes due after 3 nodes", due && restored.InvestmentOffer.Count == 3);
        }
        if (catalog.PortalPool.Contains(137u))
        {
            var run = newRun(137).Session!;
            var growing = run.Roles.Where(role => role.HasPowerGrowth).ToList();
            Call(run, "GrowPowerPortalRoles");
            check("战力飙升: its opening role continues growing", growing.Count == 1 && growing[0].PermanentPropertyBonuses.Values.Sum(value => (long)value) == 50);
        }
        if (catalog.CurrentSeason != 2) return;
        {
            var manager = newRun(101);
            var run = manager.Session!;
            run.Roles.Clear();
            foreach (var basic in catalog.RolePool.Where(role => role.TraitList.Contains(GridFightSession.TrailblazeTraitId)).Take(3))
            {
                run.TryAddRole(basic.ID, 1, out _);
                run.Roles.Last().Position = (uint)run.Roles.Count;
            }
            typeof(GridFightSession).GetField("_trailblazeCoin", Hidden)!.SetValue(run, 100u);
            var goods = (List<GridFightTrailblazeGoodState>)typeof(GridFightSession).GetField("_trailblazeGoods", Hidden)!.GetValue(run)!;
            goods.Clear();
            goods.Add(new(402, 15));
            var application = run.BuyTrailblazeGood(0);
            var notify = manager.BuildSeasonTwoSelectionSync(GridFightSession.TrailblazeTraitId, GridFightSession.TrailblazeEffectId, 0, application);
            var battleRole = GridFightBattleProtoBuilder.BuildRoleInfo(run, run.Roles[0], Gender.Man);
            check("开拓商品: actual purchase deducts currency and synchronizes the battle property", application.Success && run.TrailblazeCoin == 85 &&
                battleRole.ConvertPropertyToFixpoint.GetValueOrDefault(1027u) == 50 && notify.ResultList.SelectMany(result => result.UpdateDynamicList)
                    .Any(data => data.UpdateRoleInfo?.ConvertPropertyToFixpoint.GetValueOrDefault(1027u) == 50));
        }
        {
            var run = newRun(101).Session!;
            var bought = (Dictionary<uint, uint>)typeof(GridFightSession).GetField("_trailblazeBought", Hidden)!.GetValue(run)!;
            foreach (var row in GridFightSeasonTwoTables.TrailblazeGoods.Where(row => row.Type == "Property"))
            {
                bought.Clear();
                bought[row.Id] = 2;
                var restored = SnapshotProbe.RoundTrip(catalog, run);
                var values = new Dictionary<uint, uint>();
                restored.ApplyTrailblazeProperties(values);
                check($"开拓属性商品 {row.Id}: purchases stack and survive restore", values[row.Params[0]] == row.Params[1] * 2);
            }
        }
        {
            var run = newRun(101).Session!;
            run.Roles.Clear();
            // The Aha the shop sells: 151102 (3-cost) in the 4.6.5x tables, 151112 (4-cost) from OSBETAWin4.7.51.
            run.TryAddRole(run.AhaBaseRoleId, 1, out _);
            run.TryAddRole(run.AhaBaseRoleId, 1, out _);
            var absorbed = run.AbsorbAhaCopies();
            var aha = run.Roles.Single();
            var roleInfo = GridFightBattleProtoBuilder.BuildRoleInfo(run, aha, Gender.Man);
            check("阿哈: growth reaches the battle role's saved values", run.AhaGrowth > 0 &&
                roleInfo.BABNHPDGNGB[GridFightSession.AhaGrowthSavedValueKey] == run.AhaGrowth);
            check("阿哈: absorption updates the role even before the next tier", absorbed.Changed.Contains(aha));
        }
        check("highest S2 rank uses S1 Wealth Creator 50 combat scaling", catalog.GetEnemyDifficultyLevel(catalog.HighestDivision.ID) == 120 &&
            catalog.GetBattleDivisionId(catalog.HighestDivision.ID) == 10950);
    }
}
