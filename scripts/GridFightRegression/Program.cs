using Google.Protobuf;
using MemoryPack;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.GridFight;
using March7thHoney.Database.Player;
using March7thHoney.Enums.GridFight;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.GridFight.Persistence;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Internationalization;
using March7thHoney.Proto;
using March7thHoney.Util;

// Usage: GridFightRegression [--cache <Resource.bin>]
// Reads the checked-out cache (or a copy of --cache); all gameplay and fixture changes remain in process memory.
// Checks shared by every season run first; the capture-backed season-1 mechanics (Silver Wolf, Fate Diviner,
// DoT contract) run only when the cache's GridFight_Season_ID is 1.
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string? Arg(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
var cacheArgument = Arg("--cache");
var configPath = Path.Combine(repository, "Config");
if (cacheArgument != null)
{
    configPath = Path.Combine(repository, "tmp/GridFightRegression/Cache");
    Directory.CreateDirectory(configPath);
    File.Copy(Path.GetFullPath(cacheArgument, repository), Path.Combine(configPath, "Resource.bin"), true);
}
ConfigManager.Config.Path.ConfigPath = configPath;
ConfigManager.Config.Path.LanguagePath = Path.Combine(repository, "Config/Languages");
ConfigManager.Config.Path.LogPath = Path.Combine(repository, "tmp/GridFightRegression/Logs");
I18NManager.LoadLanguage();
if (!ResourceCache.LoadCache()) throw new InvalidDataException("Current resource cache failed validation.");

var failures = new List<string>();
var checks = 0;
void Check(string name, bool success)
{
    checks++;
    Console.WriteLine($"{(success ? "PASS" : "FAIL")} {name}");
    if (!success) failures.Add(name);
}

var catalog = new GridFightResourceCatalog(new Random(731));
var errors = catalog.Validate();
Check("GridFight catalog validates against current cache", errors.Count == 0);
foreach (var error in errors) Console.WriteLine(error);
if (errors.Count > 0) return 1;
var season = catalog.CurrentSeason;
Console.WriteLine($"GridFight season {season}.{catalog.CurrentSubSeason}, role pool {catalog.RolePool.Count}, " +
                  $"portals {catalog.PortalPool.Count}, augments {catalog.AugmentPool.Count}");

GridFightManager NewRun(uint? portalId = null)
{
    // Seeded offers permit selecting every portal without changing production visibility.
    for (var seed = 0; seed < 500; seed++)
    {
        var manager = new GridFightManager(new PlayerInstance(new PlayerData { Uid = 1 }), new Random(seed));
        if (!manager.Start(catalog.CurrentSeason, catalog.HighestDivision.ID, false))
            throw new InvalidOperationException("Cannot start GridFight.");
        var session = manager.Session!;
        var portal = portalId ?? session.PortalOffer.First(id => id != GridFightSession.BlueSeaPortalId);
        if (!session.PortalOffer.Contains(portal)) continue;
        if (!session.HandlePending(session.PendingQueuePosition, GridFightPendingRequestKind.PortalSelect, portal))
            throw new InvalidOperationException("Cannot select portal.");
        if (session.Phase == GridFightSessionPhase.SelectingNestedPortal)
        {
            Check("Blue Sea exposes nested portal", session.PortalOffer.Count == 1);
            Check("Blue Sea keeps its active effect", session.ActivePortalIds.Contains(GridFightSession.BlueSeaPortalId));
            session.HandlePending(session.PendingQueuePosition, GridFightPendingRequestKind.PortalSelect, session.PortalOffer.Single());
        }
        session.HandlePending(session.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
        session.GrantOfficialInitialRoundRewards(out _);
        session.HandlePending(session.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
        return manager;
    }
    throw new InvalidOperationException($"Portal {portalId} was never offered.");
}

var opening = NewRun(101);
var openingSession = opening.Session!;
Check("opening reaches preparation", openingSession.Phase == GridFightSessionPhase.Preparing);
Check("ordinary opening has four roles costing six in total", openingSession.Roles.Count == 4 &&
    openingSession.Roles.Sum(role => GameData.GridFightRoleBasicInfoData[role.RoleId].Rarity) == 6);
Check("opening IDs do not collide", openingSession.Roles.Select(role => role.UniqueId)
    .Concat(openingSession.ForgeItems.Select(item => item.UniqueId))
    .Concat(openingSession.Equipments.Select(item => item.UniqueId))
    .Concat(openingSession.Orbs.Select(item => item.UniqueId)).Distinct().Count() ==
    openingSession.Roles.Count + openingSession.ForgeItems.Count + openingSession.Equipments.Count + openingSession.Orbs.Count);
var orbCount = openingSession.Orbs.Count;
Check("initial reward cannot be granted twice", openingSession.GrantOfficialInitialRoundRewards(out _).Count == 0 &&
    openingSession.Orbs.Count == orbCount);
Check("stale pending request does not advance state", !openingSession.HandlePending(1, GridFightPendingRequestKind.RoundBegin));

// Test roles: unrestricted current-season shop roles with every star row (season 1 keeps its historical 1001/1004).
var testRoles = catalog.RolePool
    .Where(role => openingSession.IsRestrictedRoleUnlockedForRun(role.ID))
    .Where(role => Enumerable.Range(1, 3).All(star => GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | (uint)star)))
    .OrderBy(role => role.ID is 1001 or 1004 ? 0 : 1)
    .ThenBy(role => role.ID)
    .Select(role => role.ID)
    .Take(4)
    .ToArray();
if (testRoles.Length < 4) throw new InvalidDataException("GridFight role pool has fewer than four test roles.");
var (roleA, roleB, roleC, roleD) = (testRoles[0], testRoles[1], testRoles[2], testRoles[3]);

// Battle-entry maze skills are sent as MazeBuffs; a skill id without a MazeBuff row (e.g. 121002 Guinaifen) makes the
// client NRE while the battle loads, so every role star must resolve to nothing or to an existing MazeBuff.
var invalidEntryBuffs = GameData.GridFightRoleStarData.Values
    .Select(star => new GridFightRoleState { RoleId = star.ID, Star = star.Star })
    .Where(role => March7thHoney.GameServer.Game.GridFight.Battle.GridFightBattleProtoBuilder
                       .TryResolveEntryMazeSkill(role, out var buffId) &&
                   !GameData.MazeBuffData.ContainsKey(buffId * 10 + 1))
    .Select(role => $"{role.RoleId}/{role.Star}")
    .ToList();
Check($"battle-entry maze buffs exist for every role star (invalid: {string.Join(", ", invalidEntryBuffs)})",
    invalidEntryBuffs.Count == 0);
if (GameData.GridFightRoleStarData.ContainsKey(121002u << 4 | 1))
    Check("battle-entry maze buff: Guinaifen's overridden skill 121002 is not sent",
        !March7thHoney.GameServer.Game.GridFight.Battle.GridFightBattleProtoBuilder.TryResolveEntryMazeSkill(
            new GridFightRoleState { RoleId = 121002, Star = 1 }, out _));

foreach (var portalId in catalog.PortalPool)
{
    var manager = NewRun(portalId);
    var session = manager.Session!;
    var snapshot = manager.BuildCurrentInfo()!;
    var bytes = snapshot.ToByteArray();
    Check($"portal {portalId}: opening and protobuf round trip", session.Phase == GridFightSessionPhase.Preparing &&
        GridFightCurrentInfo.Parser.ParseFrom(bytes).Equals(snapshot));
}

// Investment environment: one free reroll, reported back as reroll_count; a second reroll is refused.
{
    var rerollManager = new GridFightManager(new PlayerInstance(new PlayerData { Uid = 1 }), new Random(7));
    rerollManager.Start(catalog.CurrentSeason, catalog.HighestDivision.ID, false);
    var rerollSession = rerollManager.Session!;
    var queue = rerollSession.PendingQueuePosition;
    var firstReroll = rerollSession.HandlePending(queue, GridFightPendingRequestKind.PortalReroll);
    var afterFirst = rerollSession.PortalOffer.ToArray();
    var pendingAfterReroll = rerollManager.BuildCurrentInfo()!.PendingAction.PortalAction;
    Check("portal reroll: first free reroll accepted and reported",
        firstReroll && rerollSession.PortalRerollCount == 1 &&
        pendingAfterReroll.FreeRerollCount == 1 && pendingAfterReroll.RerollCount == 1);
    Check("portal reroll: second reroll refused and offer kept",
        !rerollSession.HandlePending(queue, GridFightPendingRequestKind.PortalReroll) &&
        rerollSession.PortalOffer.SequenceEqual(afterFirst) &&
        rerollSession.HandlePending(queue, GridFightPendingRequestKind.PortalSelect, afterFirst.First(id => id != GridFightSession.BlueSeaPortalId)));
}

Check("investment quality roll tolerates an exhausted pool",
    catalog.RollInvestmentQuality(1, 3, catalog.AugmentPool) == GridFightAugmentQualityEnum.None);

var shopManager = NewRun(101);
var shop = shopManager.Session!;
shop.AdjustGold(1000);
var previousShop = shop.Shop.Select(slot => (slot.RoleId, slot.Star, slot.SoldOut)).ToArray();
Check("shop locks", shop.SetShopLocked(true));
Check("locked free refresh retains slots", shop.RefreshShop(false) && previousShop.SequenceEqual(
    shop.Shop.Select(slot => (slot.RoleId, slot.Star, slot.SoldOut))));
var previousGold = shop.Gold;
Check("paid refresh unlocks and charges once", shop.RefreshShop(true) && !shop.ShopLocked &&
    shop.Gold == previousGold - catalog.RefreshCost);
// Split-table (GridFightCampSeason, 4.6.52+ and 4.7 beta) caches resolve role skills natively; they get their own
// checks and then the full suite, which holds for them as well.
if (catalog.RolePool.Any(role => role.UsesNativeSkillOverrides))
    BetaTableRegression.Run(catalog, NewRun, Check);
var maxStarRole = catalog.RolePool.First(role => role.ID == roleA);
shop.TryAddRole(maxStarRole.ID, 3, out _);
var switchGroup = catalog.RoleSwitchGroups.FirstOrDefault(group => group.RoleIdList.Contains(maxStarRole.ID));
var excluded = switchGroup?.RoleIdList.ToHashSet() ?? [maxStarRole.ID];
var maxStarFiltered = true;
for (var i = 0; i < 200; i++)
{
    shop.RefreshShop(false);
    maxStarFiltered &= shop.Shop.All(slot => !excluded.Contains(slot.RoleId));
}
Check("shop excludes owned max-star roles and switch variants", maxStarFiltered);
var allExcluded = GameData.GridFightRoleBasicInfoData.Keys.ToHashSet();
Check("fully excluded shop returns empty without throwing", catalog.RollShop(catalog.InitialPlayerLevel,
    excludedRoleIds: allExcluded).Count == 0);

// --- Season separation ----------------------------------------------------------------------------------------
// From 4.6.51 the role/trait/equipment/forge/craft tables hold several seasons; nothing from another season may
// reach a run. On a single-season cache these checks still hold trivially.
bool RoleInSeason(uint roleId) => GridFightSeasonScope.IsRoleInSeason(roleId, season);
bool ItemInSeason(uint itemId) => GridFightSeasonScope.IsItemInSeason(itemId, season);
bool RewardInSeason(GridFightRewardEntry reward) => reward.Type switch
{
    GridFightBonusTypeEnum.SpecificAvatar => RoleInSeason(reward.ItemId),
    GridFightBonusTypeEnum.Item => ItemInSeason(reward.ItemId),
    _ => true,
};
var hasOtherSeasonRows = GameData.GridFightRoleBasicInfoData.Values.Any(role => role.SeasonID != season);
Console.WriteLine($"cache holds other-season GridFight rows: {hasOtherSeasonRows}");
Check("role pool holds only current-season roles", catalog.RolePool.All(role => role.SeasonID == season));
Check("role pool excludes upgrade-only cost tiers", catalog.RolePool.All(role => !catalog.UpgradeOnlyRoleIds.Contains(role.ID)));
Check("portal and augment pools belong to the current season",
    catalog.PortalPool.All(id => GridFightSeasonScope.IsPortalInSeason(id, season)) &&
    catalog.AugmentPool.All(id => GridFightSeasonScope.IsAugmentInSeason(id, season)));
Check("basic equipment pool belongs to the current season", catalog.BasicEquipmentPool.Count > 0 &&
    catalog.BasicEquipmentPool.All(ItemInSeason));
var seasonShop = NewRun(101).Session!;
seasonShop.AdjustGold(100000);
var shopRolesInSeason = true;
for (var i = 0; i < 300; i++)
{
    seasonShop.RefreshShop(false);
    shopRolesInSeason &= seasonShop.Shop.All(slot => slot.SpecialGoodsId != 0 || RoleInSeason(slot.RoleId));
}
Check("shop rolls only current-season roles", shopRolesInSeason);
var supplyInSeason = Enumerable.Range(0, 100).All(_ => catalog.RollSupplyOffer(5)
    .All(offer => RoleInSeason(offer.RoleId) && ItemInSeason(offer.EquipmentId)));
Check("supply offers stay in the current season", supplyInSeason);
var orbsChecked = 0;
var orbsInSeason = true;
foreach (var orbId in GameData.GridFightOrbData.Keys.Order())
for (var sample = 0; sample < 8; sample++)
{
    if (!GridFightSpecialFeatures.TryResolveOrb(orbId, out var rewards)) break;
    orbsChecked++;
    if (rewards.All(RewardInSeason)) continue;
    orbsInSeason = false;
    Console.WriteLine($"orb {orbId} opened other-season rewards: " +
                      string.Join(", ", rewards.Select(reward => $"{reward.Type}:{reward.ItemId}")));
    break;
}
Check($"every resolvable orb opens current-season roles and items ({orbsChecked} samples)", orbsChecked > 0 && orbsInSeason);
// Random role rewards leave out the roles a season-2 run never hands out on their own, the same list the shop pool
// leaves out: GridFight_InitialExceptRoleIdList. Tenglong (丹恒•腾荒, 141402) is only the Dan Heng trait (双龙吟) summon
// and the Aha tiers above the sold one are upgrade products; without the list a random 5-cost reward could be a
// 7-cost Aha or the summon.
var neverGranted = catalog.IsSeasonOne ? new HashSet<uint>() : catalog.InitialExcludedRoleIds.ToHashSet();
if (!catalog.IsSeasonOne)
{
    Check("upgrade-only cost tiers are on the list of roles that are never handed out",
        catalog.UpgradeOnlyRoleIds.All(neverGranted.Contains));
    var leakedRoles = new SortedSet<uint>();
    var randomRoleDraws = 0;
    foreach (var row in GameData.GridFightBasicBonusPoolV2Data.Values
                 .Where(row => row.BonusType == GridFightBonusTypeEnum.RandomAvatar).OrderBy(row => row.BonusID))
    for (var sample = 0; sample < 80; sample++)
    {
        if (!GridFightSpecialFeatures.TryResolveBonus(row.BonusID, out var rewards)) break;
        randomRoleDraws++;
        leakedRoles.UnionWith(rewards.Select(reward => reward.ItemId).Where(neverGranted.Contains));
    }
    Check($"random role rewards never hand out a role of GridFight_InitialExceptRoleIdList ({randomRoleDraws} draws)" +
          (leakedRoles.Count == 0 ? "" : $" (handed out: {string.Join(", ", leakedRoles)})"),
        randomRoleDraws > 0 && leakedRoles.Count == 0);
}
// OSBETAWin4.7.51 adds BonusType AllAvatarByRarity (BasicBonusPoolV2 116-130, [rarity, star] like the RandomAvatar
// rows before them); older caches have no such row.
var allByRarityRows = GameData.GridFightBasicBonusPoolV2Data.Values
    .Where(row => row.BonusType == GridFightBonusTypeEnum.AllAvatarByRarity)
    .OrderBy(row => row.BonusID)
    .ToList();
if (allByRarityRows.Count > 0)
{
    var wrongRows = allByRarityRows.Where(row =>
    {
        var (rarity, star) = (row.BonusTypeParamList[0], row.BonusTypeParamList[1]);
        var expected = GameData.GridFightRoleBasicInfoData.Values
            .Where(role => role.SeasonID == season && role.IsInPool && role.Rarity == rarity &&
                           !neverGranted.Contains(role.ID) &&
                           GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | star))
            .Select(role => role.ID).Order().ToList();
        var resolved = GridFightSpecialFeatures.TryResolveBonus(row.BonusID, out var rewards);
        return resolved != expected.Count > 0 || !rewards.All(reward =>
                   reward.Type == GridFightBonusTypeEnum.SpecificAvatar && reward.RoleStar == star && reward.Count == 1) ||
               !rewards.Select(reward => reward.ItemId).Order().SequenceEqual(expected);
    }).Select(row => row.BonusID).ToList();
    Check($"AllAvatarByRarity: each of the {allByRarityRows.Count} rows hands out every pool role of its rarity at its star" +
          (wrongRows.Count == 0 ? "" : $" (wrong: {string.Join(", ", wrongRows)})"), wrongRows.Count == 0);
}
var forgesInSeason = GameData.GridFightForgeData.Keys
    .Where(id => ItemInSeason(id))
    .All(id => Enumerable.Range(0, 20).All(_ => catalog.RollForgeEquipment(id).All(ItemInSeason)));
Check("forge offers stay in the current season", forgesInSeason);
var categoriesInSeason = Enum.GetValues<GridFightEquipCategoryEnum>()
    .All(category => Enumerable.Range(0, 20).All(_ => catalog.RollEquipment(category, 0) is var id && (id == 0 || ItemInSeason(id))));
Check("random equipment by category stays in the current season", categoriesInSeason &&
    Enumerable.Range(0, 20).All(_ => catalog.RollPepeEmblems(3).All(ItemInSeason)));
var craftSession = NewRun(101).Session!;
var basicRecipe = GridFightSeasonScope.Crafts(season)
    .Where(recipe => recipe.CostEquipList.Count == 2 && recipe.CostEquipList.All(id =>
        GameData.GridFightEquipmentData.TryGetValue(id, out var item) && item.EquipCategory == GridFightEquipCategoryEnum.Basic))
    .Where(recipe => GameData.GridFightEquipmentData.ContainsKey(recipe.CraftEquipID))
    .OrderBy(recipe => recipe.CraftID)
    .First();
craftSession.TryAddEquipment(basicRecipe.CostEquipList[0], out var craftLeft);
craftSession.TryAddEquipment(basicRecipe.CostEquipList[1], out var craftRight);
var seasonalCraft = craftSession.CraftEquipment(0, [craftLeft.UniqueId, craftRight.UniqueId], 0, 0);
Check($"basic pair {basicRecipe.CostEquipList[0]}+{basicRecipe.CostEquipList[1]} crafts the current-season item",
    seasonalCraft.Success && seasonalCraft.AddedEquipment!.EquipmentId == basicRecipe.CraftEquipID &&
    ItemInSeason(seasonalCraft.AddedEquipment.EquipmentId));
var systemInfo = opening.BuildSystemInfo();
if (season == 2)
{
    var seasonModule = GameData.GridFightSeasonModuleData.Values.Single(row =>
        row.SeasonID == season && row.SubSeasonID == catalog.CurrentSubSeason).ActivityModuleID;
    Check("S2 visibility uses the current season module instead of an unrelated ban-table module",
        catalog.ContentModuleId == seasonModule);
    var clientView = GridFightCurrentInfo.Parser.ParseFrom(opening.BuildCurrentInfo()!.ToByteArray());
    var modules = clientView.GameInfoList.Single(info => info.BasicInfo != null).BasicInfo.ModuleIdList;
    Check("S2 current-info wire carries the season module for client trait role filtering",
        modules.SequenceEqual(new[] { seasonModule }));
}
var tutorialStages = GameData.GridFightConstValueData.GetValueOrDefault("GridFight_TutorialStageList")?.AsUInt32List() ?? [];
Check("permanent info reports every tutorial stage as finished", tutorialStages.Count > 0 &&
    tutorialStages.All(stage => systemInfo.PermanentInfo.FBJMBJKMAJA.TryGetValue(stage, out var progress) &&
                                progress.EABKMELHIML == stage && progress.GDAKCOICIDA > 0 &&
                                (progress.MPJPLKKJNGE > 0 || GameData.GridFightTutorialStageNodeData.All(node => node.UnlockTutorialTask == 0))));
var handbook = systemInfo.StaticInfo.Handbook;
Check("handbook lists only current-season roles, items and portals",
    handbook.RoleInfo.RoleIdList.All(RoleInSeason) &&
    handbook.EquipInfo.GridFightItemList.All(ItemInSeason) &&
    handbook.PortalInfo.SeenPortalIdList.All(id => GridFightSeasonScope.IsPortalInSeason(id, season)));
if (season >= 2)
    Check("S2 handbook unlocks every 主角光环",
        handbook.KJIJEGAHLGB is { } spotLightBook &&
        spotLightBook.JHGCJKCHNCO.Count == GridFightSeasonTwoTables.SpotLights.Length &&
        spotLightBook.MOOIOOBHGKB.Count == GridFightSeasonTwoTables.SpotLights.Length);
var avatarOrb = GameData.GridFightOrbData.Values
    .Select(orb => (Orb: orb, Bonus: GameData.GridFightBasicBonusPoolV2Data.GetValueOrDefault(orb.BonusID)))
    .Where(entry => entry.Bonus?.BonusType == GridFightBonusTypeEnum.SpecificAvatar)
    .Select(entry => (entry.Orb.OrbID, AvatarId: entry.Bonus!.BonusTypeParamList.FirstOrDefault()))
    .FirstOrDefault(entry => GridFightSeasonScope.ResolveRoleId(entry.AvatarId, season) is var id && id != 0 &&
                             GameData.GridFightRoleBasicInfoData[id].AvatarID == entry.AvatarId);
Check("avatar orbs resolve to the current season's role for that avatar", avatarOrb.OrbID != 0 &&
    GridFightSpecialFeatures.TryResolveOrb(avatarOrb.OrbID, out var avatarRewards) &&
    avatarRewards.All(reward => reward.Type != GridFightBonusTypeEnum.SpecificAvatar ||
                                RoleInSeason(reward.ItemId) &&
                                GameData.GridFightRoleBasicInfoData[reward.ItemId].AvatarID == avatarOrb.AvatarId));
foreach (var tieredRoleId in catalog.UpgradeOnlyRoleIds.Where(id =>
             GameData.GridFightConstValueData.GetValueOrDefault("GridFight_SevenPriceRoleIdList")?.AsUInt32List().Contains(id) == true))
    Check($"seven-price role {tieredRoleId} uses ShopPrice row 7",
        catalog.GetBuyPrice(tieredRoleId) == GameData.GridFightShopPriceData[7].BuyGoldList[0]);
Check("shop weights come from the current season", catalog.IsSeasonOne ||
    catalog.GetShopRarityWeights(3).SequenceEqual(GameData.GridFightPlayerLevelData[3].RarityWeights));
// Season 2 runs carry one partner; the client flow reads its first effect when the run opens. Season 1 has none.
var partnerRun = NewRun();
var partnerSession = partnerRun.Session!;
var partnerSync = partnerRun.BuildCurrentInfo()!.GameInfoList
    .Where(info => info.OGKMAKNOPOD != null)
    .SelectMany(info => info.OGKMAKNOPOD.BBEEGMDAMIB)
    .ToList();
Check("season 2 run syncs one partner effect; season 1 syncs none", catalog.IsSeasonOne
    ? partnerSession.PetPalId == 0 && partnerSync.Count == 0
    : GameData.GridFightPetPalData.TryGetValue(partnerSession.PetPalId, out var partnerRow) &&
      partnerRow.EffIDList.Contains(partnerSession.PetPalEffectId) && partnerSync.Count == 1 &&
      partnerSync[0].NDAHDHKGNBG == partnerSession.PetPalId && partnerSync[0].OKGGFNJJLNG == partnerSession.PetPalEffectId);

// The 4.7 client builds its stage nodes only from the synced route nodes; without them the current node is null.
var levelInfo = partnerRun.BuildCurrentInfo()!.GameInfoList.Single(info => info.LevelInfo != null).LevelInfo;
Check("current info syncs every node of a current-season route", GridFightSeasonScope.IsRouteInSeason(catalog.StandardRouteId, season) &&
    levelInfo.RouteId == catalog.StandardRouteId && levelInfo.OOMMIIKPHBC.Count == catalog.StandardRoute.Count &&
    levelInfo.OOMMIIKPHBC.Zip(catalog.StandardRoute).All(pair =>
        pair.First.DCAHJELNJNP == pair.Second.ChapterId && pair.First.SectionId == pair.Second.SectionId &&
        pair.First.Param.StageId == pair.Second.StageId && pair.First.Param.NodeType == (uint)pair.Second.NodeType &&
        pair.First.Param.PFEMDGNFHNH == pair.Second.Template.IsAugment &&
        pair.First.Param.ParamList.SequenceEqual(pair.Second.Parameters)) &&
    levelInfo.OOMMIIKPHBC.Any(node => node.DCAHJELNJNP == levelInfo.ChapterId && node.SectionId == levelInfo.SectionId));

// The 4.7 client reads consumable updates and removals only through the nested consumable info (field 13).
var consumableItem = GameData.GridFightConsumablesData.Keys.Order()
    .First(id => partnerSession.Consumables.All(held => held.ItemId != id));
var consumableUpdates = new[] { true, false }.Select(_ =>
        partnerSession.TryAddConsumable(consumableItem, out var consumableChange)
            ? partnerRun.BuildConsumableSync(consumableChange).ResultList.Single().UpdateDynamicList.Single()
            : null)
    .ToList();
Check("consumable add and stack updates carry the nested consumable info", consumableUpdates.All(update =>
    (update?.AddGameItemInfo ?? update?.UpdateGameItemInfo)?.UpdateGridFightConsumableList.Single() is { } entry &&
    entry.MCKHGIEJLHM is { } nested && nested.GroupId == entry.GroupId && nested.ItemId == consumableItem &&
    nested.Num == entry.Num) && consumableUpdates[0]?.AddGameItemInfo != null && consumableUpdates[1]?.UpdateGameItemInfo != null);

// Synthetic items isolate dress restrictions from resource balancing changes.
const uint FreeItem = 990001, UniqueItem = 990002, LeaderItem = 990003, RoleItem = 990004;
foreach (var (id, rule) in new[] { (FreeItem, GridFightEquipDressTypeEnum.DressRuleNotUnique),
             (UniqueItem, GridFightEquipDressTypeEnum.DressRuleUnique),
             (LeaderItem, GridFightEquipDressTypeEnum.DressRuleLeader),
             (RoleItem, GridFightEquipDressTypeEnum.DressRuleRoleOnly) })
    GameData.GridFightEquipmentData[id] = new GridFightEquipmentExcel
    {
        ID = id, DressRule = rule, DressRuleParamList = [roleA],
    };
GridFightRoleState AddRole(GridFightSession session, uint roleId)
{
    if (!session.TryAddRole(roleId, 1, out var acquisition)) throw new InvalidOperationException("Cannot add role.");
    return acquisition.AcquiredRoles.Single();
}
GridFightEquipmentState AddEquipment(GridFightSession session, uint equipmentId)
{
    if (!session.TryAddEquipment(equipmentId, out var equipment)) throw new InvalidOperationException("Cannot add equipment.");
    return equipment;
}

// Implants (Pom-Pom modules, Express components, hack/Fate items): a role has one implant slot, so a second implant
// replaces the worn one, which goes back to the bag instead of staying on the role invisibly.
const uint ImplantA = 990011, ImplantB = 990012;
foreach (var id in new[] { ImplantA, ImplantB })
    GameData.GridFightEquipmentData[id] = new GridFightEquipmentExcel
    {
        ID = id, EquipType = GridFightEquipmentTypeEnum.Implants,
        DressRule = GridFightEquipDressTypeEnum.DressRuleRoleOnly, DressRuleParamList = [roleA],
    };
var implantSession = NewRun(101).Session!;
implantSession.Roles.Clear();
var implantRole = AddRole(implantSession, roleA);
implantRole.Position = 1;
var oldImplant = AddEquipment(implantSession, ImplantA);
var newImplant = AddEquipment(implantSession, ImplantB);
var firstImplantDress = implantSession.DressEquipment(implantRole.UniqueId, oldImplant.UniqueId);
var implantSwap = implantSession.DressEquipment(implantRole.UniqueId, newImplant.UniqueId);
Check("a second implant replaces the worn one, which returns to the bag",
    firstImplantDress.Success && implantSwap.Success && implantSwap.RemovedEquipments.Count == 0 &&
    implantRole.EquippedEquipmentUniqueIds.SequenceEqual([newImplant.UniqueId]) &&
    implantSession.Equipments.Contains(oldImplant));
var benchImplantRole = AddRole(implantSession, roleA);
benchImplantRole.Position = 14;
Check("implants cannot be dressed on a bench role",
    !implantSession.DressEquipment(benchImplantRole.UniqueId, oldImplant.UniqueId).Success);

// Copy consumables with a trait parameter (350113/350114 开拓·员工/完美投影仪 [3|5, 1, 100502]) only copy that trait's members.
const uint TraitProjector = 990101;
var roleATraits = GameData.GridFightRoleBasicInfoData[roleA].TraitList;
var foreignTrait = GameData.GridFightRoleBasicInfoData.Values.SelectMany(row => row.TraitList)
    .First(trait => !roleATraits.Contains(trait));
GameData.GridFightConsumablesData[TraitProjector] = new GridFightConsumablesExcel
{
    ID = TraitProjector, ConsumableRule = GridFightConsumeTypeEnum.Copy, IfConsume = true,
    ConsumableParamList = [5, 1, foreignTrait],
};
var copySession = NewRun(101).Session!;
copySession.Roles.Clear();
var copySource = AddRole(copySession, roleA);
copySession.TryAddConsumable(TraitProjector, out var projector);
Check("a trait projector refuses a role outside its trait",
    !copySession.UseConsumable(projector.Consumable.GroupId, copySource.UniqueId, 0).Success);
GameData.GridFightConsumablesData[TraitProjector].ConsumableParamList = [5, 1, roleATraits[0]];
Check("a trait projector copies a member of its trait",
    copySession.UseConsumable(projector.Consumable.GroupId, copySource.UniqueId, 0).Success &&
    copySession.Roles.Count(role => role.RoleId == roleA) == 2);
HLHHLIGPNOM Track(uint roleId, uint equipmentId, uint priority = 1)
{
    var update = new HLHHLIGPNOM { BGFFICLCKHE = roleId, BJDOINAHDLN = true, Priority = priority };
    update.GridFightItemList.Add(equipmentId);
    return update;
}
var tracksManager = NewRun(101);
var tracks = tracksManager.Session!;
tracks.Roles.Clear();
var firstRole = AddRole(tracks, roleA);
var secondRole = AddRole(tracks, roleB);
var request = Track(roleA, FreeItem);
request.MCJMHBIECMN.Add(FreeItem);
Check("track accepts valid request", tracks.UpdateEquipmentTracks([request], 0) == 0);
Check("unowned client completion is ignored", tracks.EquipmentTracks.Single().CompletedEquipmentIds.Count == 0);
Check("invalid batch is atomic", tracks.UpdateEquipmentTracks([Track(roleB, FreeItem), Track(0, FreeItem)], 0) != 0 &&
    tracks.EquipmentTracks.Count == 1);
Check("track cap rejects batch without partial writes", tracks.UpdateEquipmentTracks(
    [Track(roleB, FreeItem), Track(roleC, FreeItem), Track(roleD, FreeItem)], 0) == GridFightSession.EquipmentTrackMaxRetcode &&
    tracks.EquipmentTracks.Count == 1);

// Recommendation/craft APIs call the automatic dress path, exercised through tracked craft.
GameData.GridFightCraftConfigData[990001] = new GridFightCraftConfigExcel
    { CraftID = 990001, CostEquipList = [FreeItem, FreeItem], CraftEquipID = LeaderItem };
GridFightEquipCraftResult CraftTracked(GridFightSession session, uint itemId, uint target = 0)
{
    var left = AddEquipment(session, FreeItem);
    var right = AddEquipment(session, FreeItem);
    return session.CraftEquipment(itemId, [left.UniqueId, right.UniqueId],
        GridFightEquipCraftSource.KgridFightEquipCraftSourceTrack, target);
}
tracks.UpdateEquipmentTracks([Track(roleA, LeaderItem), Track(roleB, LeaderItem, 2)], 0);
secondRole.Position = 1;
var leaderCraft = CraftTracked(tracks, LeaderItem);
Check("automatic dress skips ineligible higher-priority role", leaderCraft.Success &&
    !firstRole.EquippedEquipmentUniqueIds.Contains(leaderCraft.AddedEquipment!.UniqueId) &&
    secondRole.EquippedEquipmentUniqueIds.Contains(leaderCraft.AddedEquipment.UniqueId));
var explicitCraft = CraftTracked(tracks, LeaderItem, firstRole.UniqueId);
Check("explicit ineligible target leaves item in inventory", explicitCraft.Success &&
    tracks.Roles.All(role => !role.EquippedEquipmentUniqueIds.Contains(explicitCraft.AddedEquipment!.UniqueId)));
var unknownCraft = CraftTracked(tracks, LeaderItem, uint.MaxValue);
Check("unknown explicit target does not fall back to another role", unknownCraft.Success &&
    tracks.Roles.All(role => !role.EquippedEquipmentUniqueIds.Contains(unknownCraft.AddedEquipment!.UniqueId)));
tracks.TryAddEquipment(LeaderItem, out var lockedEquipment, isLocked: true);
Check("NPC equipment is locked before automatic tracking can claim it", lockedEquipment.IsLocked &&
    tracks.Roles.All(role => !role.EquippedEquipmentUniqueIds.Contains(lockedEquipment.UniqueId)));

foreach (var pair in new[] { (350601u, 350601u), (350601u, 350602u), (350602u, 350602u) })
{
    var manager = NewRun(101);
    var session = manager.Session!;
    session.Roles.Clear();
    var role = AddRole(session, roleA);
    var a = AddEquipment(session, pair.Item1);
    var b = AddEquipment(session, pair.Item2);
    var before = session.MaxActiveRoles;
    var crafted = session.CraftEquipment(GridFightSession.WealthDiamondEquipmentId, [a.UniqueId, b.UniqueId], 0, 0);
    Check($"diamond {pair}: manual recipe and capacity", crafted.Success && session.MaxActiveRoles == before + 1);
    a = AddEquipment(session, pair.Item1);
    b = AddEquipment(session, pair.Item2);
    session.DressEquipment(role.UniqueId, a.UniqueId);
    var dressed = session.DressEquipment(role.UniqueId, b.UniqueId);
    Check($"diamond {pair}: on-role recipe agrees", dressed.Success && dressed.AutoCrafted &&
        dressed.AddedEquipments.Single().EquipmentId == GridFightSession.WealthDiamondEquipmentId);
}

var diceManager = NewRun(101);
var diceSession = diceManager.Session!;
diceSession.Roles.Clear();
var diceRole = AddRole(diceSession, roleA);
var otherRole = AddRole(diceSession, roleB);
var dice = AddEquipment(diceSession, 35030404);
var dress = diceSession.DressEquipment(diceRole.UniqueId, dice.UniqueId);
var diceSync = diceManager.BuildEquipDressSync(dress);
var filled = diceSession.GetEquippedEquipment(diceRole).Where(GridFightSession.IsDiceFilledEquipment).ToArray();
Check("dice creates two locked temporary items", dress.Success && filled.Length == 2 && filled.All(item => item.IsLocked));
Check("dice fill stays in the current season", filled.All(item => ItemInSeason(item.EquipmentId)));
Check("dice fill is synchronized", diceSync.ResultList.SelectMany(result => result.UpdateDynamicList)
    .Where(data => data.AddGameItemInfo != null).SelectMany(data => data.AddGameItemInfo.GridFightEquipmentList)
    .Count(item => filled.Any(fill => fill.UniqueId == item.UniqueId)) == 2);
Check("temporary item cannot be moved independently", !diceSession.DressEquipment(otherRole.UniqueId, filled[0].UniqueId).Success);
var binding = filled.Select(item => item.EquipmentId).ToArray();
var transfer = diceSession.DressEquipment(otherRole.UniqueId, dice.UniqueId);
diceManager.BuildEquipDressSync(transfer);
Check("moving dice keeps its roll and deletes old instances", transfer.Success && diceRole.EquippedEquipmentUniqueIds.Count == 0 &&
    filled.All(item => !diceSession.Equipments.Contains(item)) && binding.SequenceEqual(
        diceSession.GetEquippedEquipment(otherRole).Where(GridFightSession.IsDiceFilledEquipment).Select(item => item.EquipmentId)));
diceSession.ApplyDiceEquipmentRefills();
var changes = diceSession.TakeDiceEquipmentChanges();
Check("dice reroll reports removals and additions", changes.RemovedEquipments.Count == 2 && changes.AddedEquipments.Count == 2);
Check("dice sync is consumed once", diceSession.TakeDiceEquipmentChanges().AddedEquipments.Count == 0);
diceSession.RecycleRole(otherRole.UniqueId);
Check("selling dice wearer does not leak temporary inventory", diceSession.Equipments.All(item => !GridFightSession.IsDiceFilledEquipment(item)) &&
    diceSession.Equipments.Contains(dice));

if (catalog.IsSeasonOne)
{
    var pendingManager = NewRun(101);
    var pendingSession = pendingManager.Session!;
    pendingSession.Roles.Clear();
    pendingSession.TryAddRole(GridFightSpecialFeatures.SilverWolfThreeCostRoleId, 2, out var silverWolfAcquisition);
    var silverWolf = silverWolfAcquisition.AcquiredRoles.Single();
    var blackSwan = AddRole(pendingSession, GridFightSession.BlackSwanRoleId);
    var deployment = pendingSession.UpdatePositions([(silverWolf.UniqueId, 1), (blackSwan.UniqueId, 2)]);
    pendingManager.BuildPositionSync(deployment);
    Check("simultaneous trait activations share one pending queue", deployment.Success &&
        pendingSession.HeadPlayerPending != null && pendingSession.FateDivinerPending == null);
    Check("pending role cannot be moved, sold or sent into battle", !pendingSession.UpdatePositions([(silverWolf.UniqueId, 14)]).Success &&
        !pendingSession.RecycleRole(silverWolf.UniqueId).Success && !pendingSession.EnterBattle());
    var head = pendingSession.HeadPlayerPending!;
    Check("invalid enhancement preserves pending state", !pendingSession.ResolveHeadPlayerEnhance(head.QueuePosition, uint.MaxValue).Success &&
        ReferenceEquals(head, pendingSession.HeadPlayerPending));
    var headResult = pendingSession.ResolveHeadPlayerEnhance(head.QueuePosition, GridFightSpecialFeatures.SilverWolfUpgradeToFourCostOptionId);
    pendingManager.BuildHeadPlayerEnhanceSync(headResult);
    Check("cost upgrade retains entity and advances pending queue", headResult.Success &&
        silverWolf.RoleId == GridFightSpecialFeatures.SilverWolfFourCostRoleId && silverWolf.Star == 1 && silverWolf.Position == 1);
    Check("queued Fate enhancement follows Head Player", pendingSession.HeadPlayerPending == null &&
        pendingSession.FateDivinerPending?.QueuePosition > head.QueuePosition);
    var fate = pendingSession.FateDivinerPending!;
    var fateResult = pendingSession.ResolveFateDivinerEnhance(fate.QueuePosition, 6);
    pendingManager.BuildFateDivinerEnhanceSync(fateResult);
    Check("Fate enhancement completes and releases selection", fateResult.Success && !pendingSession.HasPendingSelection);
    Check("duplicate enhancement cannot grant twice", !pendingSession.ResolveFateDivinerEnhance(fate.QueuePosition, 6).Success);
}

// A full route exercises battles, supply nodes, encounters and investment selections.
var routePortal = catalog.PortalPool.Contains(GridFightSession.DotContractPortalId)
    ? GridFightSession.DotContractPortalId
    : 101u;
var routeManager = NewRun(routePortal);
var route = routeManager.Session!;
route.Roles.Clear();
route.ActiveInvestmentIds.Add(GridFightSession.PeriodicBenefitsAugmentId);
route.ActiveInvestmentIds.Add(GridFightSession.GoldFuturesPlusAugmentId);
var luckyTokenInPool = catalog.AugmentPool.Contains(GridFightSession.LuckyTokenSilverAugmentId);
if (luckyTokenInPool) route.ActiveInvestmentIds.Add(GridFightSession.LuckyTokenSilverAugmentId);
var investmentNodesOffered = new HashSet<(uint, uint)>();
var investmentRerollChecked = false;
var fighter = AddRole(route, roleA);
fighter.Position = 1;
const uint PeriodicGoldItem = 990005;
GameData.GridFightEquipmentData[PeriodicGoldItem] = new GridFightEquipmentExcel
{
    ID = PeriodicGoldItem, DressRule = GridFightEquipDressTypeEnum.DressRuleNotUnique,
    EquipFunc = GridFightEquipFuncTypeEnum.AvatarMaxNumberAdd,
    ParamList = [new() { Value = 1 }, new() { Value = 2 }],
};
var periodicItem = AddEquipment(route, PeriodicGoldItem);
route.DressEquipment(fighter.UniqueId, periodicItem.UniqueId);
var battleCount = 0;
var futuresSelections = 0;
var lastFuturesNode = 0;
var periodicRewardsObserved = 0;
var finished = false;
var routeStaysInSeason = true;
var routeSnapshotDifferences = new List<string>();
for (var transition = 0; transition < 300 && !finished; transition++)
{
    // Every state the route passes through must survive the save a request ends with.
    if (SnapshotProbe.CompareSessions(route, SnapshotProbe.RoundTrip(routeManager.Catalog, route)) is { } routeDifference)
        routeSnapshotDifferences.Add($"{route.Phase} at {route.ChapterId}/{route.SectionId}: {routeDifference}");
    var queue = route.PendingQueuePosition;
    switch (route.Phase)
    {
        case GridFightSessionPhase.Preparing:
            if (!route.EnterBattle()) throw new InvalidOperationException($"Cannot battle at {route.ChapterId}/{route.SectionId}.");
            var statistics = new GridFightBattleStatistics(100, [], [], [], [], 1,
                new Dictionary<string, uint> { [GridFightSession.DotContractSavedValueKey] = 180 });
            var settlement = route.ResolveBattle(true, statistics);
            if (!settlement.Success) throw new InvalidOperationException("Battle settlement failed.");
            battleCount++;
            Check($"battle {battleCount}: duplicate settlement rejected", !route.ResolveBattle(true, statistics).Success);
            finished = !settlement.RouteAdvanced;
            break;
        case GridFightSessionPhase.AwaitingBattleRound:
        case GridFightSessionPhase.AwaitingSupplyRound:
            if (!route.HandlePending(queue, GridFightPendingRequestKind.RoundBegin)) throw new InvalidOperationException("Round failed.");
            var roundSync = routeManager.BuildBattleRoundSync(queue, []);
            periodicRewardsObserved += roundSync.ResultList.Count(result =>
                result.EffectParamList.Contains(GridFightSession.PeriodicBenefitsAugmentId));
            break;
        case GridFightSessionPhase.SelectingInvestment:
            routeStaysInSeason &= route.InvestmentOffer.All(id => GridFightSeasonScope.IsAugmentInSeason(id, season));
            if (route.IsGoldFuturesInvestmentPending)
            {
                var nodeIndex = catalog.StandardRoute.ToList().FindIndex(node =>
                    node.ChapterId == route.ChapterId && node.SectionId == route.SectionId);
                Check($"gold futures selection {++futuresSelections}: waits at least four nodes", nodeIndex - lastFuturesNode >= 4);
                lastFuturesNode = nodeIndex;
            }
            else
            {
                investmentNodesOffered.Add((route.ChapterId, route.SectionId));
                if (!investmentRerollChecked)
                {
                    investmentRerollChecked = true;
                    var rerolledAway = new List<uint>();
                    var rerollsOk = true;
                    foreach (var slot in route.InvestmentOffer.ToArray())
                    {
                        rerollsOk &= route.RerollInvestment(queue, slot);
                        rerolledAway.Add(slot);
                        rerollsOk &= !route.InvestmentOffer.Any(rerolledAway.Contains);
                    }
                    var replaced = route.InvestmentOffer.First();
                    Check("investment reroll: replaced offers never return, each slot rerolls once",
                        rerollsOk && !route.RerollInvestment(queue, replaced) &&
                        route.InvestmentOffer.Distinct().Count() == route.InvestmentOffer.Count);
                }
            }
            var investment = route.InvestmentOffer.First();
            if (!route.SelectInvestment(queue, investment)) throw new InvalidOperationException("Investment failed.");
            routeManager.BuildInvestmentSelectionSync(queue, investment);
            break;
        case GridFightSessionPhase.SelectingSupply:
            routeStaysInSeason &= route.SupplyOffer.All(offer => RoleInSeason(offer.RoleId));
            // The 4.6.51 client builds every draft card from the top drop (field 13) and NREs
            // without it, leaving the run stuck on the post-battle settlement.
            var supplyCards = routeManager.BuildPendingAction()?.SupplyAction?.SupplyRoleInfoList;
            Check($"supply at {route.ChapterId}/{route.SectionId}: every card carries a role top drop and its equipment drop",
                supplyCards is { Count: > 0 } && supplyCards.All(card =>
                    card.OPPFPECOBEF is { DropType: GridFightDropType.Role } top &&
                    top.ItemId == card.RoleId && top.Param?.RoleStar >= 1 && card.GridFightItemList.Count >= 1 &&
                    card.DHLBEFLOOPM.Select(drop => drop.ItemId).SequenceEqual(card.GridFightItemList)));
            var supply = route.SelectSupply(queue, [1]);
            if (!supply.Success) throw new InvalidOperationException("Supply failed.");
            routeManager.BuildSupplySelectionSync(queue, 1, supply);
            break;
        case GridFightSessionPhase.SelectingEncounter:
            if (!route.SelectEncounter(queue, route.EncounterOptions.First().EliteBranchId)) throw new InvalidOperationException("Encounter failed.");
            break;
        case GridFightSessionPhase.AwaitingBattleReturn:
            if (!route.HandlePending(queue, GridFightPendingRequestKind.ReturnPreparation)) throw new InvalidOperationException("Return failed.");
            break;
        case GridFightSessionPhase.AwaitingBackToPrepare:
            var beforePreparationGold = route.Gold;
            if (!route.BackToPrepare()) throw new InvalidOperationException("Preparation failed.");
            routeManager.BuildPreparationSync();
            Check($"node {route.ChapterId}/{route.SectionId}: periodic equipment pays once", route.Gold == beforePreparationGold + 2 &&
                !route.BackToPrepare() && route.Gold == beforePreparationGold + 2);
            break;
        default:
            throw new InvalidOperationException($"Unexpected route state {route.Phase}.");
    }
    routeStaysInSeason &= route.Roles.All(role => RoleInSeason(role.RoleId)) &&
                          route.Shop.All(slot => slot.SpecialGoodsId != 0 || RoleInSeason(slot.RoleId)) &&
                          route.Equipments.All(item => item.EquipmentId >= 990000 || ItemInSeason(item.EquipmentId));
}
Check("full route reaches final boss settlement", finished && battleCount > 10);
foreach (var difference in routeSnapshotDifferences.Take(5)) Console.WriteLine(difference);
Check("full route: the run saved before every step restores field for field", routeSnapshotDifferences.Count == 0);
var routeNodes = catalog.StandardRoute.ToList();
var augmentNodes = routeNodes.Where(node => node.Template.IsAugment != 0)
    .Select(node => (node.ChapterId, node.SectionId)).ToHashSet();
var augmentAfterSupply = routeNodes.Skip(1).Where((node, index) =>
    node.Template.IsAugment != 0 && routeNodes[index].NodeType == GridFightNodeTypeEnum.Supply).Count();
Console.WriteLine($"route: {augmentNodes.Count} investment nodes, {augmentAfterSupply} directly after a supply node");
Check("every investment node offers an investment, including nodes right after supply",
    investmentRerollChecked && augmentNodes.SetEquals(investmentNodesOffered));
if (luckyTokenInPool)
    Check("lucky token (silver) grants its consumable (350107) during the route",
        route.Consumables.Any(item => item.ItemId == GridFightSession.LuckyTokenConsumableId && item.Count >= 1));
Check("full route keeps roles, shop, items and offers in the current season", routeStaysInSeason);

// Encounters (遭遇): every option card carries a reward rolled from its tier, and clearing it pays exactly that card.
var eliteNode = catalog.StandardRoute.FirstOrDefault(node => node.NodeType == GridFightNodeTypeEnum.EliteBranch);
if (eliteNode != null)
{
    GridFightSession EncounterRun(bool freeUpgrade)
    {
        var run = NewRun(routePortal).Session!;
        run.Roles.Clear();
        AddRole(run, roleA).Position = 1;
        if (freeUpgrade) run.ActiveInvestmentIds.Add(GridFightSession.FreeUpgradeAugmentId);
        run.TryMoveToSection(eliteNode.ChapterId, eliteNode.SectionId, out _);
        run.HandlePending(run.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
        return run;
    }

    var encounterRun = EncounterRun(false);
    if (encounterRun.Phase == GridFightSessionPhase.SelectingEncounter && encounterRun.EncounterOptions.Count == 2)
    {
        Check("encounter: both option cards carry a reward",
            encounterRun.EncounterOptions.All(option => option.Rewards is { Count: > 0 }));
        var option = encounterRun.EncounterOptions[0];
        encounterRun.SelectEncounter(encounterRun.PendingQueuePosition, option.EliteBranchId);
        encounterRun.HandlePending(encounterRun.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
        if (encounterRun.Phase == GridFightSessionPhase.AwaitingBackToPrepare) encounterRun.BackToPrepare();
        var refreshBefore = encounterRun.FreeRefreshCount;
        var rolesBefore = encounterRun.Roles.Count;
        var itemsBefore = encounterRun.Equipments.Count + encounterRun.ForgeItems.Count + encounterRun.Consumables.Count;
        var encounterWon = encounterRun.EnterBattle() && encounterRun.ResolveBattle(true,
            new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>())).Success;
        var paid = encounterRun.TakeAugmentNodeEffects();
        var drop = option.Rewards![0];
        var matches = drop.Type switch
        {
            GridFightDropType.Refresh => encounterRun.FreeRefreshCount == refreshBefore + drop.Num,
            GridFightDropType.Role => encounterRun.Roles.Count > rolesBefore,
            GridFightDropType.Item => encounterRun.Equipments.Count + encounterRun.ForgeItems.Count + encounterRun.Consumables.Count > itemsBefore,
            _ => paid.Any(effect => effect.Application.GoldChanged || effect.Application.PlayerExpChanged || effect.Application.PlayerLevelChanged),
        };
        Check($"encounter: clearing quality {option.Quality} pays its card reward ({drop.Type} {drop.ItemId}x{drop.Num})",
            encounterWon && paid.Count == 1 && paid[0].Application.Success && matches);
    }
    else Console.WriteLine($"encounter reward check skipped (phase {encounterRun.Phase})");

    var upgradedRun = EncounterRun(true);
    var topOption = upgradedRun.EncounterOptions.OrderByDescending(option => option.Quality).FirstOrDefault();
    if (topOption != null)
        Check("免费升舱: a tier-4 encounter carries an extra reward, lower tiers roll from the next tier",
            topOption.Quality < 4 ? topOption.Rewards is { Count: > 0 } : topOption.Rewards is { Count: 2 });
}

// Chimera upgrade consumable (奇巧零食, 350119, UpgradeChimera): adds one star to a role-exclusive chimera and is consumed.
var exclusiveChimera = GridFightSeasonTwoTables.Chimera.FirstOrDefault(row => row.RoleId != 0 &&
    GridFightSeasonTwoTables.Chimera.Where(other => other.Id == row.Id).Max(other => other.Star) > 1);
if (exclusiveChimera != null && GameData.GridFightConsumablesData.ContainsKey(350119))
{
    var snackRun = NewRun(routePortal).Session!;
    var chimeras = (List<GridFightChimeraState>)typeof(GridFightSession)
        .GetField("_chimeras", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(snackRun)!;
    var pet = new GridFightChimeraState { UniqueId = 9001, ChimeraId = exclusiveChimera.Id };
    chimeras.Add(pet);
    snackRun.TryAddConsumable(350119, out var snack);
    var snackGroup = snackRun.Consumables.First(item => item.ItemId == 350119).GroupId;
    var snackUsed = snackRun.UseConsumable(snackGroup, 0, 0, pet.UniqueId).Success;
    Check("奇巧零食: upgrades the chosen role-exclusive chimera by one star and is consumed",
        snackUsed && pet.Star == 2 && snackRun.Consumables.All(item => item.ItemId != 350119 || item.Count == 0) &&
        !snackRun.UseConsumable(snackGroup, 0, 0, pet.UniqueId).Success);
}

// A spotlight (主角光环) follows a star-up merge, and Refresh Vision (刷新视界, 100100) grants a refresh after a battle
// and stacks strength per refresh.
{
    var mergeRun = NewRun(routePortal).Session!;
    mergeRun.Roles.Clear();
    var bound = AddRole(mergeRun, roleA);
    bound.Position = 1;
    var spotLights = (List<GridFightSpotLightState>)typeof(GridFightSession)
        .GetField("_spotLights", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(mergeRun)!;
    var crown = new GridFightSpotLightState
    {
        UniqueId = 9100, SpotLightId = GridFightSession.RefreshVisionSpotLightId, RoleId = roleA, RoleUniqueId = bound.UniqueId,
    };
    spotLights.Add(crown);
    mergeRun.TryAddRole(roleA, 1, out _);
    mergeRun.TryAddRole(roleA, 1, out _);
    var merged = mergeRun.Roles.FirstOrDefault(role => role.RoleId == roleA && role.Star == 2);
    Check("主角光环: a star-up merge moves the crown to the merged unit instead of losing it",
        merged != null && crown.RoleUniqueId == merged.UniqueId && !crown.IsLoss &&
        mergeRun.GetSpotLightIdForRole(merged.UniqueId) == GridFightSession.RefreshVisionSpotLightId);

    if (merged != null)
    {
        merged.Position = 1;
        var refreshBefore = mergeRun.FreeRefreshCount;
        var visionBattle = mergeRun.EnterBattle() && mergeRun.ResolveBattle(true,
            new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>())).Success;
        var visionRefresh = mergeRun.FreeRefreshCount == refreshBefore + 1;
        for (var step = 0; step < 10 && mergeRun.Phase != GridFightSessionPhase.Preparing; step++)
        {
            if (mergeRun.Phase == GridFightSessionPhase.SelectingInvestment)
                mergeRun.SelectInvestment(mergeRun.PendingQueuePosition, mergeRun.InvestmentOffer.First());
            else if (mergeRun.Phase == GridFightSessionPhase.SelectingSupply)
                mergeRun.SelectSupply(mergeRun.PendingQueuePosition, [1]);
            else if (mergeRun.Phase == GridFightSessionPhase.SelectingEncounter)
                mergeRun.SelectEncounter(mergeRun.PendingQueuePosition, mergeRun.EncounterOptions.First().EliteBranchId);
            else if (mergeRun.Phase == GridFightSessionPhase.AwaitingBackToPrepare)
                mergeRun.BackToPrepare();
            else if (!mergeRun.HandlePending(mergeRun.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin))
                mergeRun.HandlePending(mergeRun.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
        }
        mergeRun.AdjustGold(100);
        var refreshed = mergeRun.RefreshShop(true) && mergeRun.RefreshShop(true);
        Check("刷新视界: a battle with the crowned unit grants 1 free refresh and each refresh adds 1.5% strength",
            visionBattle && visionRefresh && refreshed && mergeRun.SpotLightStrengthBonusFixpoint == 30);
    }
}

// Spotlight charges: the MoonGame (幻月游戏) golden orb bonus (23156) pays 1 replace (重选主角) and 8 free spotlight
// rerolls.
if (GridFightSpecialFeatures.TryResolveBonus(23156, out var chargeRewards))
{
    var chargeRun = NewRun(routePortal).Session!;
    var applyRewards = typeof(GridFightSession).GetMethod("ApplyRewards",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    var charged = (GridFightRewardApplication)applyRewards.Invoke(chargeRun, [chargeRewards.Where(reward => reward.Type is
        GridFightBonusTypeEnum.SpotLightReplaceCnt or GridFightBonusTypeEnum.SpotLightFreeRefreshCnt).ToList(), 0u, 0u])!;
    Check("主角光环: bonus 23156 grants 1 replace and 8 free spotlight rerolls",
        charged.Success && chargeRun.SpotLightReplaceTotal == 1 && chargeRun.SpotLightFreeRefreshTotal == 8);
}
else Check("主角光环: bonus 23156 resolves", false);

// Battle-reported role growth (飞霄 军衔, GP_Avatar_S2_Feixiao_00) survives into the next sync and battle.
{
    var rankRun = NewRun(routePortal).Session!;
    rankRun.Roles.Clear();
    AddRole(rankRun, roleA).Position = 1;
    var rankBattle = rankRun.EnterBattle() && rankRun.ResolveBattle(true, new GridFightBattleStatistics(100, [], [], [], [], 1,
        new Dictionary<string, uint> { ["GP_Avatar_S2_Feixiao_00"] = 4, ["SomeOtherKey"] = 9 })).Success;
    rankRun.RefreshRoleSavedValues();
    Check("role saved values: battle-reported 军衔 growth is kept for the next battle",
        rankBattle && rankRun.RoleSavedValues.GetValueOrDefault("GP_Avatar_S2_Feixiao_00") == 4 &&
        !rankRun.RoleSavedValues.ContainsKey("SomeOtherKey"));
}

// A role forge (聘用书) is redeemed for a role of its cost; the loot orb augments (爆晶矿) drop loot orbs on kills.
{
    var hireRun = NewRun(routePortal).Session!;
    hireRun.Roles.Clear();
    hireRun.ActiveInvestmentIds.Add(203302); // 专家招募+: 2x 5费聘用书 + 12 gold
    typeof(GridFightSession).GetMethod("ApplyAugmentSelectionEffects",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(hireRun, [203302u]);
    var hireEffects = hireRun.TakeAugmentSelectionEffects();
    var hiredRoles = hireEffects.SelectMany(effect => effect.Application.RoleAcquisitions).SelectMany(result => result.AcquiredRoles).ToList();
    Check("专家招募+: two 5费聘用书 become two 5-cost roles",
        hiredRoles.Count == 2 && hiredRoles.All(role => GameData.GridFightRoleBasicInfoData[role.RoleId].Rarity == 5));

    var lootRun = NewRun(routePortal).Session!;
    lootRun.Roles.Clear();
    AddRole(lootRun, roleA).Position = 1;
    lootRun.ActiveInvestmentIds.Add(301501);
    var orbsBefore = lootRun.Orbs.Count;
    var kills = Enumerable.Range(1, 20).Select(index => new GridFightKilledMonsterInstance(1, (uint)index, 0)).ToList();
    var lootBattle = lootRun.EnterBattle() && lootRun.ResolveBattle(true,
        new GridFightBattleStatistics(100, [], [], [], kills, 1, new Dictionary<string, uint>())).Success;
    Check("爆晶矿·彩: kills drop loot orbs (121006-121008)",
        lootBattle && lootRun.Orbs.Count > orbsBefore &&
        lootRun.Orbs.Skip(orbsBefore).All(orb => orb.OrbItemId is 121006 or 121007 or 121008));
    // Their bonus rows (21006-21008) are not shipped; opening them used to fail ("消息获取失败") and block "open all".
    Check("爆晶矿: every loot orb opens and \"open all\" takes them",
        new uint[] { 121006, 121007, 121008 }.All(id => GridFightSpecialFeatures.TryResolveOrb(id, out var opened) && opened.Count > 0) &&
        lootRun.OpenOrbs([], true).Count > 0 && lootRun.Orbs.All(orb => orb.OrbItemId is not (121006 or 121007 or 121008)));

    // Trotters spawned inside a battle (season talent 4061, Trotter Virus) drop orbs that only the client knows about;
    // it reports them in GridFightBattleResultStatistics field 5.
    var trotterRun = NewRun(routePortal).Session!;
    trotterRun.Roles.Clear();
    AddRole(trotterRun, roleA).Position = 1;
    var trotterOrbs = trotterRun.Orbs.Count;
    var trotterKills = new List<GridFightKilledMonsterInstance> { new(1, 1, 0), new(1, 2, 0) };
    var trotterBattle = trotterRun.EnterBattle() && trotterRun.ResolveBattle(true,
        new GridFightBattleStatistics(100, [], [], [], trotterKills, 1, new Dictionary<string, uint>()),
        [new(GridFightBonusTypeEnum.Orb, 199, 5), new(GridFightBonusTypeEnum.Orb, 999999, 1)]).Success;
    Check("in-battle trotter orbs are granted: openable ones only, at most one per kill",
        trotterBattle && trotterRun.Orbs.Count - trotterOrbs == 2 &&
        trotterRun.Orbs.Skip(trotterOrbs).All(orb => orb.OrbItemId == 199));
}

// Pepe Cameo (佩佩客串): Pepe arrives on pick, wears an advanced item per perfect clear and leaves after 5 nodes with
// it unlocked.
var cameoRun = NewRun(routePortal).Session!;
cameoRun.Roles.Clear();
AddRole(cameoRun, roleA).Position = 1;
cameoRun.ActiveInvestmentIds.Add(GridFightSession.PepeCameoAugmentId);
typeof(GridFightSession).GetMethod("ApplyAugmentSelectionEffects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
    .Invoke(cameoRun, [GridFightSession.PepeCameoAugmentId]);
var cameoArrived = cameoRun.PepeNpc != null;
var cameoBattle = cameoRun.EnterBattle() && cameoRun.ResolveBattle(true,
    new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>())).Success;
var cameoWorn = cameoRun.PepeNpc?.EquippedEquipmentUniqueIds.ToList() ?? [];
var cameoEvents = cameoRun.TakePepeCameoEvents();
var invoke = typeof(GridFightSession).GetMethod("OnPepeCameoNodeEntered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
for (var node = 0; node < 6 && cameoRun.PepeNpc != null; node++) invoke.Invoke(cameoRun, []);
var cameoLeft = cameoRun.PepeNpc == null && cameoRun.TakePepeCameoEvents().Any(cameo => cameo.Kind == GridFightPepeCameoEventKind.Leave);
Check("佩佩客串: Pepe arrives, wears a locked advanced item after a perfect clear and leaves it unlocked",
    cameoArrived && cameoBattle && cameoWorn.Count == 1 &&
    cameoEvents.Select(cameo => cameo.Kind).SequenceEqual([GridFightPepeCameoEventKind.Arrive, GridFightPepeCameoEventKind.Equip]) &&
    cameoLeft && cameoRun.Equipments.Any(item => item.UniqueId == cameoWorn[0] && !item.IsLocked));

// Season 2 adds an effect-only level 11 after the level-10 cap; normal exp must stop at the cap.
var levelRun = NewRun(routePortal).Session!;
levelRun.AdjustGold(10000);
while (levelRun.BuyExp()) { }
var boughtLevel = levelRun.PlayerLevel;
levelRun.Roles.Clear();
AddRole(levelRun, roleA).Position = 1;
var cappedBattleWon = levelRun.EnterBattle() &&
    levelRun.ResolveBattle(true, new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>())).Success;
Check("player level stops at the level cap, also when a battle grants exp",
    boughtLevel == catalog.MaxPlayerLevel && cappedBattleWon && levelRun.PlayerLevel == catalog.MaxPlayerLevel);
Check("periodic benefits are published on node entry", periodicRewardsObserved > 10);
Check("gold futures offers recur through full route", futuresSelections >= 2);
if (routePortal == GridFightSession.DotContractPortalId)
    Check("DoT threshold grants Black Swan once across repeated settlements", route.Orbs.Count(orb =>
        orb.OrbItemId == GridFightSession.DotContractBlackSwanOrbId) == 1);

// ---- Data-driven investment effects (GridFightAugmentEffects)
{
    var grantTables = GridFightSession.AugmentSelectionGrants
        .Select(pair => (pair.Key, pair.Value))
        .Concat(GridFightSession.AugmentRecurringEffects.SelectMany(pair => pair.Value.Select(effect => (pair.Key, effect.Grant))))
        .ToList();
    // The effect tables are shared by both seasons, so an entry has to be an investment of some season's list
    // rather than of the running one: OSBETAWin4.7.51 moved 103601 (经验就是财富) out of season 2 and kept it in
    // season 1. What a run can offer is still decided by the running season's pool alone.
    var listedAugments = GameData.GridFightSeasonAugmentData.Values.SelectMany(ids => ids).ToHashSet();
    var unlistedGrants = grantTables.Select(entry => entry.Key).Where(id => !listedAugments.Contains(id))
        .Distinct().Order().ToList();
    Check($"augment effects: every configured investment is in a season's investment list{(unlistedGrants.Count == 0 ? "" : " (missing: " + string.Join(", ", unlistedGrants) + ")")}",
        unlistedGrants.Count == 0);
    Check("augment effects: investments are only offered from the running season's list",
        catalog.AugmentPool.All(GameData.GridFightSeasonAugmentData[season].Contains));
    var invalidGrantItems = grantTables.SelectMany(entry => entry.Item2.Items.Select(item => (entry.Key, item.ItemId)))
        .Where(pair => !(GameData.GridFightEquipmentData.ContainsKey(pair.ItemId) ||
                         GameData.GridFightConsumablesData.ContainsKey(pair.ItemId) ||
                         GameData.GridFightForgeData.ContainsKey(pair.ItemId)) || !ItemInSeason(pair.ItemId))
        .Distinct().ToList();
    Check($"augment effects: every granted item exists and belongs to the current season{(invalidGrantItems.Count == 0 ? "" : " (invalid: " + string.Join(", ", invalidGrantItems.Select(pair => $"{pair.Key}->{pair.ItemId}")) + ")")}",
        invalidGrantItems.Count == 0);

    var applySelection = typeof(GridFightSession).GetMethod("ApplyAugmentSelectionEffects",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
    GridFightAugmentEffectResult Select(GridFightSession target, uint augmentId)
    {
        target.ActiveInvestmentIds.Add(augmentId);
        applySelection.Invoke(target, [augmentId]);
        return target.TakeAugmentSelectionEffects().Single();
    }

    var effectSession = NewRun(101).Session!;
    effectSession.Roles.Clear();
    var rolesBefore = effectSession.Roles.Count;
    var twoCost = Select(effectSession, 100401); // 二费援军: 4 random 2-cost roles
    var addedRoleIds = twoCost.Application.RoleAcquisitions.SelectMany(acquisition => acquisition.AcquiredRoles)
        .Select(role => role.RoleId).ToList();
    Check("二费援军 grants four 2-cost roles of the current season",
        addedRoleIds.Count == 4 && addedRoleIds.All(id => GameData.GridFightRoleBasicInfoData[id].Rarity == 2 && RoleInSeason(id)) &&
        effectSession.Roles.Count >= rolesBefore + 1);

    var hpBefore = (effectSession.LineupHp, effectSession.MaxLineupHp);
    var health = Select(effectSession, 103301); // 健康充值: +30 HP and +30 max HP
    Check("健康充值 raises lineup HP and max HP by 30",
        health.Application.LineupHpChanged && effectSession.MaxLineupHp == hpBefore.MaxLineupHp + 30 &&
        effectSession.LineupHp == Math.Min(hpBefore.LineupHp + 30, effectSession.MaxLineupHp));

    Select(effectSession, 100301); // 免费午餐: 11 free refreshes
    var goldBeforeRefresh = effectSession.Gold;
    Check("免费午餐 shows a free refresh and refreshing does not spend gold",
        effectSession.FreeRefreshCount == 11 && effectSession.CurrentRefreshCost == 0 &&
        effectSession.RefreshShop(true) && effectSession.Gold == goldBeforeRefresh && effectSession.FreeRefreshCount == 10);

    var forgeBefore = effectSession.ForgeItems.Count;
    var goldBeforeTrade = effectSession.Gold;
    var trade = Select(effectSession, 101402); // 军火贸易+: armament box 99999 and 8 gold
    Check("军火贸易+ grants its armament box and 8 gold",
        trade.Application.AddedForgeItems.Count == 1 && effectSession.ForgeItems.Count == forgeBefore + 1 &&
        effectSession.Gold == goldBeforeTrade + 8);

    // The selection sync carries the effect next to the augment add.
    var effectManager = NewRun(101);
    var syncSession = effectManager.Session!;
    syncSession.ActiveInvestmentIds.Add(101402);
    applySelection.Invoke(syncSession, [101402u]);
    var selectionSync = effectManager.BuildInvestmentSelectionSync(syncSession.PendingQueuePosition, 101402);
    Check("investment selection sync carries the effect rewards",
        selectionSync.ResultList.Any(result => result.EffectParamList.FirstOrDefault() == 101402 &&
            result.UpdateDynamicList.Any(data => data.AddForgeItemInfo != null) &&
            result.UpdateDynamicList.Any(data => data.ItemValue == syncSession.Gold)));

    // Node triggers: 201301 (长期主义) pays on the next 3 node entries only, 101602 (人才激励+) pays once after 3 nodes.
    var nodeManager = NewRun(101);
    var nodeSession = nodeManager.Session!;
    nodeSession.Roles.Clear();
    AddRole(nodeSession, roleA).Position = 1;
    nodeSession.ActiveInvestmentIds.Add(201301);
    nodeSession.ActiveInvestmentIds.Add(101602);
    var nodeEffects = new List<uint>();
    var nodesEntered = 0;
    for (var step = 0; step < 200 && nodesEntered < 5; step++)
    {
        var queue = nodeSession.PendingQueuePosition;
        switch (nodeSession.Phase)
        {
            case GridFightSessionPhase.Preparing:
                if (!nodeSession.EnterBattle()) throw new InvalidOperationException("Cannot battle in node test.");
                if (nodeSession.ResolveBattle(true, new GridFightBattleStatistics(100, [], [], [], [], 1,
                        new Dictionary<string, uint>())).RouteAdvanced) nodesEntered++;
                break;
            case GridFightSessionPhase.AwaitingBattleRound:
            case GridFightSessionPhase.AwaitingSupplyRound:
                nodeSession.HandlePending(queue, GridFightPendingRequestKind.RoundBegin);
                nodeEffects.AddRange(nodeManager.BuildBattleRoundSync(queue, []).ResultList
                    .Where(result => result.UpdateSource == GridFightUpdateSrcType.KgridFightSrcAugment)
                    .Select(result => result.EffectParamList.FirstOrDefault()));
                break;
            case GridFightSessionPhase.SelectingInvestment:
                nodeSession.SelectInvestment(queue, nodeSession.InvestmentOffer.First());
                break;
            case GridFightSessionPhase.SelectingSupply:
                if (nodeSession.SelectSupply(queue, [1]).Success) nodesEntered++;
                break;
            case GridFightSessionPhase.SelectingEncounter:
                nodeSession.SelectEncounter(queue, nodeSession.EncounterOptions.First().EliteBranchId);
                break;
            case GridFightSessionPhase.AwaitingBattleReturn:
                nodeSession.HandlePending(queue, GridFightPendingRequestKind.ReturnPreparation);
                break;
            case GridFightSessionPhase.AwaitingBackToPrepare:
                nodeSession.BackToPrepare();
                break;
            default:
                throw new InvalidOperationException($"Unexpected node test state {nodeSession.Phase}.");
        }
    }
    Check("长期主义 pays on exactly the next three node entries",
        nodesEntered >= 4 && nodeEffects.Count(id => id == 201301) == 3);
    Check("人才激励+ pays its projector once after three nodes",
        nodeEffects.Count(id => id == 101602) == 1 &&
        nodeSession.Consumables.Any(item => item.ItemId == 350105));
}

// ---- Season-2-only investments (GridFightSeasonTwoAugmentEffects)
if (season == 2)
{
    const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var applySelection = typeof(GridFightSession).GetMethod("ApplyAugmentSelectionEffects", Private)!;
    IReadOnlyList<GridFightAugmentEffectResult> Pick(GridFightSession target, uint augmentId)
    {
        target.ActiveInvestmentIds.Add(augmentId);
        applySelection.Invoke(target, [augmentId]);
        return target.TakeAugmentSelectionEffects();
    }
    IEnumerable<GridFightRoleState> Acquired(IEnumerable<GridFightAugmentEffectResult> effects) =>
        effects.SelectMany(effect => effect.Application.RoleAcquisitions).SelectMany(acquisition => acquisition.AcquiredRoles);

    var setRun = NewRun(101).Session!;
    setRun.Roles.Clear();
    var prismaticSet = Pick(setRun, 311501); // 仙舟舰队星徽套组(彩): emblem + 桂乃芬 + 星航履
    var setItems = prismaticSet.SelectMany(effect => effect.Application.AddedEquipments).Select(item => item.EquipmentId).ToList();
    Check("prismatic emblem set grants the season-2 emblem, the featured role and its equipment",
        setItems.Contains(355100102) && setItems.Contains(47030108) &&
        Acquired(prismaticSet).Any(role => GameData.GridFightRoleBasicInfoData[role.RoleId].AvatarID == 1210 && RoleInSeason(role.RoleId)));

    var budgetRun = NewRun(101).Session!;
    budgetRun.Roles.Clear();
    var budgetRoles = Acquired(Pick(budgetRun, 204802)).ToList(); // 人才大礼包+: 18 gold incl. one 4-cost
    var budgetCost = budgetRoles.Sum(role => (int)GameData.GridFightRoleBasicInfoData[role.RoleId].Rarity);
    Check("人才大礼包+ hands out roles worth 18 gold including a 4-cost",
        budgetCost == 18 && budgetRoles.Any(role => GameData.GridFightRoleBasicInfoData[role.RoleId].Rarity == 4));

    var moonRun = NewRun(101).Session!;
    moonRun.Roles.Clear();
    var moonMembers = catalog.GetRolesByTrait(100302).Select(role => role.ID).ToHashSet();
    var firstHalf = Acquired(Pick(moonRun, GridFightSession.MoonGatheringAugmentId)).Select(role => role.RoleId).ToHashSet();
    var take = typeof(GridFightSession).GetMethod("TakeMoonGatheringSecondHalf", Private)!;
    var secondHalf = ((IReadOnlyList<uint>)take.Invoke(moonRun, [])!).ToHashSet();
    Check("幻月集结 splits the 幻月游戏 members into two halves that together cover the trait",
        moonMembers.Count > 1 && firstHalf.Count == (moonMembers.Count + 1) / 2 &&
        firstHalf.Union(secondHalf).ToHashSet().SetEquals(moonMembers) && !firstHalf.Overlaps(secondHalf));

    var upgradeRun = NewRun(GridFightSession.SilverEraPortalId).Session!; // all offers silver unless upgraded
    Pick(upgradeRun, GridFightSession.InvestmentUpgradeAugmentId);
    typeof(GridFightSession).GetMethod("PrepareInvestmentOffer", Private)!.Invoke(upgradeRun, []);
    var upgradedOffer = upgradeRun.InvestmentOffer.Select(id => GameData.GridFightAugmentData[id].Quality).Distinct().ToList();
    typeof(GridFightSession).GetMethod("PrepareInvestmentOffer", Private)!.Invoke(upgradeRun, []);
    var nextOffer = upgradeRun.InvestmentOffer.Select(id => GameData.GridFightAugmentData[id].Quality).Distinct().ToList();
    Check("投资升级 raises only the next offer by one quality (silver era -> gold, then silver again)",
        upgradedOffer.SequenceEqual([GridFightAugmentQualityEnum.Gold]) && nextOffer.SequenceEqual([GridFightAugmentQualityEnum.Silver]));

    var foresightRun = NewRun(101).Session!;
    var goldBefore = foresightRun.Gold;
    var foresightEffects = Pick(foresightRun, GridFightSession.ForesightAugmentId);
    var foresightBonus = foresightRun.TakeForesightBonusAugments();
    typeof(GridFightSession).GetMethod("PrepareInvestmentOffer", Private)!.Invoke(foresightRun, []);
    var foresightOffer = foresightRun.InvestmentOffer.ToList();
    Check("远见 grants 15 gold and one random gold investment",
        foresightRun.Gold == goldBefore + 15 && foresightBonus.Count == 1 &&
        GameData.GridFightAugmentData[foresightBonus[0]].Quality == GridFightAugmentQualityEnum.Gold &&
        foresightRun.ActiveInvestmentIds.Contains(foresightBonus[0]) && foresightEffects.Count > 0);
    Check("远见 turns later investment nodes into a single prismatic pick",
        foresightOffer.Count == 1 && GameData.GridFightAugmentData[foresightOffer[0]].Quality == GridFightAugmentQualityEnum.Prismatic &&
        foresightRun.IsForesightActive);

    // /grid console command entry points.
    var commandRun = NewRun(101).Session!;
    var commandHp = commandRun.LineupHp;
    var commandRefresh = commandRun.FreeRefreshCount;
    var commandLoss = commandRun.ApplyCommandGrant(new GridFightAugmentGrant { HpLoss = 5 });
    var commandRefreshGrant = commandRun.ApplyCommandGrant(new GridFightAugmentGrant { FreeRefresh = 2 });
    var commandAugment = commandRun.TryGrantInvestmentByCommand(GridFightSession.ForesightAugmentId);
    var commandAugmentTwice = commandRun.TryGrantInvestmentByCommand(GridFightSession.ForesightAugmentId);
    var commandInfo = commandRun.DescribeForCommand();
    Check("/grid commands: hp loss, free refresh and a granted investment apply once and show in info",
        commandLoss.Application.Success && commandRun.LineupHp == commandHp - 5 &&
        commandRefreshGrant.Application.Success && commandRun.FreeRefreshCount == commandRefresh + 2 &&
        commandAugment && !commandAugmentTwice && commandRun.TakeForesightBonusAugments().Count == 1 &&
        commandInfo.Contains(GridFightSession.ForesightAugmentId.ToString()) &&
        !commandRun.TryGrantInvestmentByCommand(1));

    Check("分批扩招 has both its node and boss triggers",
        GridFightSession.AugmentRecurringEffects[305101].Select(effect => effect.Trigger).OrderBy(trigger => trigger)
            .SequenceEqual(new[] { GridFightAugmentTrigger.AfterNodes, GridFightAugmentTrigger.BossNode }.OrderBy(trigger => trigger)));
}

// ---- Investment environment (portal) effects (GridFightPortalEffects)
{
    const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    void Invoke(GridFightSession target, string method) => typeof(GridFightSession).GetMethod(method, Private)!.Invoke(target, []);

    if (catalog.PortalPool.Contains(131u))
    {
        var red = NewRun(131u).Session!;
        Check("红钻贵族 opens with the red diamond and a simple armament box",
            red.Equipments.Any(item => item.EquipmentId == 350601) &&
            red.ForgeItems.Any(item => item.ForgeItemId == GridFightSession.SimpleArmamentBoxForgeId));
    }
    if (catalog.PortalPool.Contains(GridFightSession.TalentReservePortalId))
    {
        var reserve = NewRun(GridFightSession.TalentReservePortalId).Session!;
        Check("人才储备 replaces the opening roles with three 3-cost roles",
            reserve.Roles.Count == 3 && reserve.Roles.All(role => GameData.GridFightRoleBasicInfoData[role.RoleId].Rarity == 3));
    }
    foreach (var (portalId, expected, label) in new[]
             {
                 (GridFightSession.PrismaticEraPortalId, GridFightAugmentQualityEnum.Prismatic, "彩虹时代"),
                 (GridFightSession.FirstPrizePortalId, GridFightAugmentQualityEnum.Prismatic, "头彩"),
             })
    {
        if (!catalog.PortalPool.Contains(portalId)) continue;
        var run = NewRun(portalId).Session!;
        Invoke(run, "PrepareInvestmentOffer");
        Check($"{label} makes the first investment offer {expected}",
            run.InvestmentOffer.Count == 3 &&
            run.InvestmentOffer.All(id => GameData.GridFightAugmentData[id].Quality == expected));
    }
    if (catalog.PortalPool.Contains(GridFightSession.SecondHandMarketPortalId))
    {
        var market = NewRun(GridFightSession.SecondHandMarketPortalId).Session!;
        market.AdjustGold(1000);
        for (var i = 0; i < 19; i++) market.RefreshShop(true);
        var before20 = market.TakePortalRefreshEffects().Count;
        var goldBefore = market.Gold;
        market.RefreshShop(true);
        var effects = market.TakePortalRefreshEffects();
        Check("二手市场 pays 30 gold and a projector on the 20th refresh only",
            before20 == 0 && effects.Count == 1 && market.Gold == goldBefore - catalog.RefreshCost + 30 &&
            market.Consumables.Any(item => item.ItemId == 350105));
    }
    if (catalog.PortalPool.Contains(GridFightSession.LongTermBullPortalId))
    {
        var bull = NewRun(GridFightSession.LongTermBullPortalId).Session!;
        bull.AdjustGold(1000);
        for (var i = 0; i < 30; i++) bull.RefreshShop(true);
        var bullEffects = bull.TakePortalRefreshEffects();
        Check("长线利好 pays 20 gold after 30 paid refreshes and then refreshes cost 1 gold",
            bullEffects.Count == 1 && bullEffects[0].ShopChanged && bull.CurrentRefreshCost == Math.Min(1u, catalog.RefreshCost));
    }
    var invitationPortals = catalog.PortalPool.Where(id => id is >= 2101 and <= 2119).ToList();
    var invitationOk = invitationPortals.All(id =>
    {
        var run = NewRun(id).Session!;
        return run.InvitationPortalEquipment is { } emblem &&
               GameData.GridFightEquipmentData[emblem.EquipmentId].EquipCategory == GridFightEquipCategoryEnum.Emblem &&
               ItemInSeason(emblem.EquipmentId);
    });
    if (invitationPortals.Count > 0)
        Check($"season-2 invitations ({invitationPortals.Count}) each open with their season emblem", invitationOk);
    var conceptPortals = catalog.PortalPool.Where(id => id is >= 2201 and <= 2214).ToList();
    var conceptOk = conceptPortals.All(id =>
    {
        var run = NewRun(id).Session!;
        var config = run.ConceptStockConfig!;
        var featured = new[] { config.FirstRoleId, config.SecondRoleId }
            .Select(avatarId => GridFightSeasonScope.ResolveRoleId(avatarId, season)).ToList();
        return featured.All(roleId => roleId != 0 && run.Roles.Any(role => role.RoleId == roleId)) &&
               run.ConceptStockInitialEquipment?.EquipmentId == config.InitialEquipmentId &&
               run.ConceptStockBoostedTraitId == config.TraitId &&
               run.Roles.All(role => RoleInSeason(role.RoleId));
    });
    if (conceptPortals.Count > 0)
        Check($"season-2 concept stocks ({conceptPortals.Count}) open with their featured roles and equipment", conceptOk);
    if (catalog.PortalPool.Contains(GridFightSession.StrategyMasterPortalId))
    {
        var master = NewRun(GridFightSession.StrategyMasterPortalId).Session!;
        master.ActiveInvestmentIds.AddRange([catalog.AugmentPool[0], catalog.AugmentPool[1]]);
        var goldBefore = master.Gold;
        Invoke(master, "OnPortalInvestmentSelected");
        Check("策略大师 pays 2 gold per owned investment", master.Gold == goldBefore + 4 &&
            master.TakeAugmentSelectionEffects().Single().IsPortal);
    }
}

// Runs moved to the first camp node whose penalty rule costs HP (the opening nodes cost none).
(GridFightManager Manager, GridFightSession Session) PenalizedRun()
{
    var manager = NewRun(101);
    MoveToPenalizedNode(manager.Session!);
    return (manager, manager.Session!);
}

void MoveToPenalizedNode(GridFightSession session)
{
    var node = catalog.StandardRoute.First(candidate => candidate.NodeType == GridFightNodeTypeEnum.CampMonster &&
        catalog.GetPenaltyRule(candidate.PenaltyBonusRuleId).ProgressPenaltyCoefficient > 0);
    if (!session.TryMoveToSection(node.ChapterId, node.SectionId, out _)) throw new InvalidOperationException("Cannot move.");
    for (var step = 0; step < 20 && session.Phase != GridFightSessionPhase.Preparing; step++)
    {
        var queue = session.PendingQueuePosition;
        var advanced = session.Phase switch
        {
            GridFightSessionPhase.AwaitingBattleRound or GridFightSessionPhase.AwaitingSupplyRound =>
                session.HandlePending(queue, GridFightPendingRequestKind.RoundBegin),
            GridFightSessionPhase.SelectingInvestment => session.SelectInvestment(queue, session.InvestmentOffer.First()),
            GridFightSessionPhase.SelectingSupply => session.SelectSupply(queue, [1]).Success,
            GridFightSessionPhase.SelectingEncounter =>
                session.SelectEncounter(queue, session.EncounterOptions.First().EliteBranchId),
            GridFightSessionPhase.AwaitingBattleReturn => session.HandlePending(queue, GridFightPendingRequestKind.ReturnPreparation),
            GridFightSessionPhase.AwaitingBackToPrepare => session.BackToPrepare(),
            _ => false,
        };
        if (!advanced) throw new InvalidOperationException($"Cannot reach preparation from {session.Phase}.");
    }
    session.Roles.Clear();
    AddRole(session, roleA).Position = 1;
}

// Retiring a battle settles it as a lost one: the penalties apply and the run moves on instead of returning to the same
// preparation, where the team could be rebuilt and the battle replayed for free.
var retire = PenalizedRun().Session;
var retireNode = (retire.ChapterId, retire.SectionId);
var retired = retire.EnterBattle() ? retire.ResolveBattle(false, GridFightBattleStatistics.Empty) : GridFightBattleSettlementResult.Failed;
Check("retired battle settles as a loss and does not return to the same preparation",
    retired.Success && !retired.IsPerfectFinish && retired.ProgressPercent < 100 &&
    retired.HpChanges.Any(change => change.Kind == GridFightHpChangeKind.ProgressPenalty && change.Amount > 0) &&
    retire.Phase != GridFightSessionPhase.Preparing && (retire.ChapterId, retire.SectionId) != retireNode &&
    !retire.ResolveBattle(false, GridFightBattleStatistics.Empty).Success);

// Squad HP 0 ends the run: the post-battle heal (长线作战 +2) must not revive it.
var (doomedRun, doomed) = PenalizedRun();
typeof(GridFightSession).GetProperty(nameof(GridFightSession.LineupHp))!.SetValue(doomed, 1u);
var doomedBattle = doomed.EnterBattle()
    ? doomed.ResolveBattle(true, new GridFightBattleStatistics(0, [], [], [], [], 1, new Dictionary<string, uint>()))
    : GridFightBattleSettlementResult.Failed;
Check("a lost battle at 1 HP defeats the run without a revive heal",
    doomedBattle.Success && doomedBattle.Defeated && !doomedBattle.RouteAdvanced && doomed.LineupHp == 0 && doomedBattle.LineupHpAfter == 0 &&
    doomedBattle.HpChanges.All(change => change.Kind != GridFightHpChangeKind.TalentHealing) &&
    doomed.Phase == GridFightSessionPhase.Defeated && !doomed.EnterBattle() && !doomed.BuyExp() && !doomed.RefreshShop(true));
Check("a defeat settles with reason Defeat",
    doomedRun.BuildSettleNotify(GridFightSettleReason.Defeat).FinishInfo.Reason == GridFightSettleReason.Defeat);

// The settle's division_id becomes the player's division on the client; it must not drop to the played difficulty.
var easiestDivision = GameData.GridFightDivisionInfoData.Values
    .Where(row => row.SeasonID == catalog.CurrentSeason)
    .OrderBy(row => row.Progress).ThenBy(row => row.ID).First();
var easyRun = new GridFightManager(new PlayerInstance(new PlayerData { Uid = 1 }), new Random(1));
easyRun.Start(catalog.CurrentSeason, easiestDivision.ID, false);
var easySettle = easyRun.BuildSettleNotify(GridFightSettleReason.Quit);
Check("settle keeps the unlocked division and reports the played one in the finish info",
    easySettle.DivisionId == catalog.HighestDivision.ID && easySettle.FinishInfo.Basic.DivisionId == easiestDivision.ID);

// QuitBattleCsReq clears a GridFight battle without PVEBattleResultCsReq; it settles like a retired battle.
var (quitManager, quit) = PenalizedRun();
var quitNode = (quit.ChapterId, quit.SectionId);
var quitBattlesFinished = quit.BattlesFinished;
var quitEntered = quit.EnterBattle();
quit.PrepareMortenaxBladeBattleEffects();
var quitSettled = quitManager.QuitBattle(quit).AsTask().GetAwaiter().GetResult();
Check("QuitBattle settles a GridFight battle as a lost one and moves on",
    quitEntered && quitSettled && quit.Phase is not (GridFightSessionPhase.InBattle or GridFightSessionPhase.Preparing) &&
    (quit.ChapterId, quit.SectionId) != quitNode && quit.KeepWinCount == 0 &&
    quit.BattlesFinished == quitBattlesFinished + 1 && quit.MortenaxBladeBattleEffects.Count == 0);
Check("QuitBattle is ignored when repeated or after settlement",
    !quitManager.QuitBattle(quit).AsTask().GetAwaiter().GetResult() &&
    quit.BattlesFinished == quitBattlesFinished + 1);
var (staleQuitManager, staleQuit) = PenalizedRun();
Check("QuitBattle ignores a stale session context",
    staleQuit.EnterBattle() && !staleQuitManager.QuitBattle(NewRun(101).Session!).AsTask().GetAwaiter().GetResult() &&
    staleQuit.Phase == GridFightSessionPhase.InBattle);
staleQuit.AbortBattleStart();
Check("battle can be entered again after an ignored QuitBattle",
    staleQuit.Phase == GridFightSessionPhase.Preparing && staleQuit.EnterBattle());
var (doomedQuitManager, doomedQuit) = PenalizedRun();
typeof(GridFightSession).GetProperty(nameof(GridFightSession.LineupHp))!.SetValue(doomedQuit, 1u);
var doomedQuitSettled = doomedQuit.EnterBattle() &&
                        doomedQuitManager.QuitBattle(doomedQuit).AsTask().GetAwaiter().GetResult();
Check("QuitBattle at 1 HP ends the run",
    doomedQuitSettled && doomedQuitManager.Session == null && doomedQuitManager.HasSettledSession);

// --- Season-2 unique / origin trait effects ---------------------------------------------------------------------
// Wire layouts follow the 4.6.51 client; these checks pin the server-side rules.
if (!catalog.IsSeasonOne)
{
    (GridFightManager Manager, GridFightSession Session) Board(params (uint RoleId, uint Position)[] roles)
    {
        var manager = NewRun(101);
        var session = manager.Session!;
        session.Roles.Clear();
        session.AdjustGold(500);
        foreach (var (roleId, position) in roles) AddRole(session, roleId).Position = position;
        return (manager, session);
    }
    GridFightTraitEffectInfo? EffectInfo(GridFightManager manager, uint traitId, uint effectId) =>
        manager.BuildCurrentInfo()!.GameInfoList
            .Where(info => info.TraitInfo != null)
            .SelectMany(info => info.TraitInfo.GridFightTraitInfo)
            .Where(trait => trait.TraitId == traitId)
            .SelectMany(trait => trait.TraitEffectList)
            .FirstOrDefault(effect => effect.EffectId == effectId);

    // Dan Heng trait (双龙吟): Dan Heng IL summons a same-star Tenglong in the back area and asks for a trait.
    var (danHengRun, danHeng) = Board((GridFightSession.DanHengMainRoleId, 1), (roleA, 2));
    var danHengSync = danHengRun.BuildSeasonTwoFollowUpSync();
    var sub = danHeng.Roles.FirstOrDefault(role => role.RoleId == GridFightSession.DanHengSubRoleId);
    Check("S2 双龙吟: Tenglong is summoned in the back area with the main's star",
        sub != null && sub.Position is >= 5 and <= 13 && sub.Star == 1 && danHeng.DanHengSubUniqueId == sub.UniqueId &&
        danHengSync!.ResultList.SelectMany(result => result.UpdateDynamicList).Any(data => data.AddRoleInfo?.UniqueId == sub.UniqueId));
    var danHengPending = danHengRun.BuildPendingAction();
    Check("S2 双龙吟: trait choice pending action carries the four candidate traits",
        danHengPending?.GEPGMIEPBMB is { } choose && choose.RoleId == GridFightSession.DanHengMainRoleId &&
        choose.DAHOEGKELCI.OrderBy(id => id).SequenceEqual(new uint[] { 200402, 200602, 200802, 201002 }));
    Check("S2 双龙吟: Tenglong cannot be sold or moved to the front",
        !danHeng.RecycleRole(sub!.UniqueId).Success && !danHeng.UpdatePositions([(sub.UniqueId, 3)]).Success);
    var finishedChoice = danHeng.SelectDanHengTrait(danHengPending!.QueuePosition, 200402, out var choseTrait);
    var main = danHeng.Roles.First(role => role.RoleId == GridFightSession.DanHengMainRoleId);
    Check("S2 双龙吟: the chosen trait goes to Dan Heng IL and the prompt finishes",
        choseTrait && finishedChoice == danHengPending.QueuePosition && danHeng.SeasonTwoPending == null &&
        danHeng.ResolveRoleTraitIds(main).Contains(200402u) &&
        EffectInfo(danHengRun, GridFightSession.DanHengTraitId, GridFightSession.DanHengChooseTraitEffectId)
            ?.GEPGMIEPBMB.CKHHAPGAAOH.SequenceEqual(new uint[] { 200402 }) == true &&
        EffectInfo(danHengRun, GridFightSession.DanHengTraitId, GridFightSession.DanHengTwoInOneEffectId)
            ?.AMOIMOHMBEM is { } twoInOne && twoInOne.CAAKCKEPMEK == main.UniqueId && twoInOne.OKFEMIMJLME == sub.UniqueId);
    Check("S2 双龙吟: the summon does not use population", danHeng.EnterBattle());
    danHeng.AbortBattleStart();
    main.Position = 15;
    danHengRun.BuildSeasonTwoFollowUpSync();
    Check("S2 双龙吟: Tenglong leaves with Dan Heng IL", danHeng.Roles.All(role => role.RoleId != GridFightSession.DanHengSubRoleId));

    // The summon brings its own back cell: with every unlocked back cell taken it still lands in an unlocked one, and
    // the client hears about the extra cell (833) before the summon's add-role entry.
    var fillers = catalog.RolePool.Select(role => role.ID)
        .Where(id => id != GridFightSession.DanHengMainRoleId && GameData.GridFightRoleBasicInfoData[id].Rarity <= 4)
        .Take(6).ToArray();
    var (cellRun, cell) = Board([(GridFightSession.DanHengMainRoleId, 1), .. fillers.Select((id, index) => (id, (uint)(5 + index)))]);
    cellRun.BuildCurrentInfo();
    var backCellsBefore = cell.OffFieldMaxCount;
    var cellSync = cellRun.BuildSeasonTwoFollowUpSync()!.ResultList.SelectMany(result => result.UpdateDynamicList).ToList();
    var cellSub = cell.Roles.FirstOrDefault(role => role.RoleId == GridFightSession.DanHengSubRoleId);
    var cellIndex = cellSync.FindIndex(data => data.JLOCMDIDPCI == backCellsBefore + 1);
    var subIndex = cellSync.FindIndex(data => data.AddRoleInfo?.UniqueId == cellSub?.UniqueId);
    Check("S2 双龙吟: with the back cells full the summon opens its own cell and stands inside the unlocked cells",
        cellSub != null && cell.OffFieldMaxCount == backCellsBefore + 1 && cellSub.Position <= 4 + cell.OffFieldMaxCount &&
        cellIndex >= 0 && subIndex > cellIndex);
    cell.Roles.First(role => role.RoleId == GridFightSession.DanHengMainRoleId).Position = 15;
    var closeSync = cellRun.BuildSeasonTwoFollowUpSync()!.ResultList.SelectMany(result => result.UpdateDynamicList).ToList();
    Check("S2 双龙吟: the summon's cell closes again when it leaves",
        cell.OffFieldMaxCount == backCellsBefore && closeSync.Any(data => data.JLOCMDIDPCI == backCellsBefore) &&
        cell.Roles.All(role => role.RoleId != GridFightSession.DanHengSubRoleId));
    Check("S2 双龙吟: 丹恒•腾荒 is never in the role pool (shop, 我5费呢？！, supply)",
        catalog.RolePool.All(role => role.ID != GridFightSession.DanHengSubRoleId));

    // A Wealth Diamond (财富宝钻) from any source: the next sync raises the team size and opens a back cell without a
    // level-up.
    var (crownRun, crown) = Board((roleA, 1));
    crownRun.BuildCurrentInfo();
    var (rolesBefore, cellsBefore) = (crown.MaxActiveRoles, crown.OffFieldMaxCount);
    crown.TryAddEquipment(GridFightSession.WealthDiamondEquipmentId, out _);
    var crownSync = crownRun.BuildSeasonTwoFollowUpSync()?.ResultList.SelectMany(result => result.UpdateDynamicList).ToList() ?? [];
    Check("S2 财富宝钻: the team size and the back cells reach the client at once",
        crown.MaxActiveRoles == rolesBefore + 1 && crown.OffFieldMaxCount == cellsBefore + 1 &&
        crownSync.Any(data => data.MaxActiveRoleCount == rolesBefore + 1) && crownSync.Any(data => data.JLOCMDIDPCI == cellsBefore + 1));
    var crownBasic = crownRun.BuildCurrentInfo()!.GameInfoList.First(info => info.BasicInfo != null).BasicInfo;
    Check("S2 re-entry snapshot: field 11 carries the team size and field 7 the level (client GridFightPlayer.SetData)",
        crownBasic.MaxActiveRoleCount == crown.MaxActiveRoles && crownBasic.PlayerLevel == crown.PlayerLevel &&
        crownBasic.KeepWinCount == crown.KeepWinCount && crownBasic.MaxLineupHp == crown.MaxLineupHp);

    // Phainon trait (盗火行者): dragging a role onto Phainon turns it into kindling (薪火, a role saved value).
    var (phainonRun, phainon) = Board((GridFightSession.PhainonRoleId, 1), (roleA, 2));
    phainonRun.BuildSeasonTwoFollowUpSync();
    var food = phainon.Roles.First(role => role.RoleId == roleA);
    var expectedKindling = GridFightSession.GetPhainonKindlingGain(roleA, 1);
    var consumed = phainon.ConsumeRoleForPhainon(food.UniqueId, out var kindling);
    Check("S2 盗火行者: consuming a role removes it and stores the kindling in Phainon's saved value",
        consumed.Success && kindling == expectedKindling && kindling > 0 && phainon.Roles.All(role => role.UniqueId != food.UniqueId) &&
        phainon.RefreshRoleSavedValues().GetValueOrDefault(GridFightSession.PhainonSavedValueKey) == kindling);
    var phainonRole = phainon.Roles.First(role => role.RoleId == GridFightSession.PhainonRoleId);
    var phainonBattleRole = March7thHoney.GameServer.Game.GridFight.Battle.GridFightBattleProtoBuilder
        .BuildRoleInfo(phainon, phainonRole, Gender.Man);
    Check("S2 盗火行者: the battle role info carries Phainon's 薪火 in the role-scoped custom values (field 14)",
        phainonBattleRole.BABNHPDGNGB.GetValueOrDefault(GridFightSession.PhainonSavedValueKey) == kindling);
    if (GameData.GridFightRoleBasicInfoData.GetValueOrDefault(140202u)?.Rarity == 5 &&
        GameData.GridFightRoleBasicInfoData.GetValueOrDefault(110702u)?.Rarity == 5)
        Check("S2 盗火行者: a 3★ 5-cost gives 3355 薪火 and a 黄金裔 30% more (Clara 3355, Aglaea 4362)",
            GridFightSession.GetPhainonKindlingGain(110702, 3) == 3355 &&
            GridFightSession.GetPhainonKindlingGain(140202, 3) == 4362);
    Check("S2 盗火行者: Phainon cannot eat himself",
        !phainon.ConsumeRoleForPhainon(phainon.Roles.First(role => role.RoleId == GridFightSession.PhainonRoleId).UniqueId, out _).Success);

    // Ranger trait (巡海游侠): Mortenax asks to appoint a front role, which then carries trait 300302.
    var (rangerRun, ranger) = Board((150402, 5), (roleA, 1));
    var rangerSync = rangerRun.BuildSeasonTwoFollowUpSync();
    var rangerPending = rangerRun.BuildPendingAction();
    Check("S2 巡海游侠: appointment prompt lists the front roles",
        rangerPending?.DPKFJEEJABC?.PNHBEHLICFL.SequenceEqual(new[] { roleA }) == true && rangerSync != null);
    ranger.AppointRanger(rangerPending!.QueuePosition, roleA, out var appointedRanger);
    Check("S2 巡海游侠: the appointed role gains the ranger trait",
        appointedRanger && ranger.ResolveRoleTraitIds(ranger.Roles.First(role => role.RoleId == roleA))
            .Contains(GridFightSession.RangerTraitId));

    // Jade trait (黑市主理人): the signature good sits in slot 1 and never sells out.
    var (jadeRun, jade) = Board((GridFightSession.JadeRoleId, 1));
    jadeRun.BuildSeasonTwoFollowUpSync();
    var jadeInfo = EffectInfo(jadeRun, GridFightSession.JadeTraitId, GridFightSession.JadeEffectId)?.GDAMHENIIDJ;
    Check("S2 黑市主理人: shop has a signature good in slot 1 and rotating goods from slot 2, each with drops",
        jadeInfo != null && jadeInfo.OGDKJPDFFAI.Any(good => good.SlotId == 1 && good.DNJCAMKKLPB != 0) &&
        jadeInfo.OGDKJPDFFAI.Where(good => good.DNJCAMKKLPB != 0).All(good => good.NMGIABHIJJB?.DropList.Count > 0));
    var hpBeforeJade = jade.LineupHp;
    var goldBeforeJade = jade.Gold;
    var signature = jade.BuyJadeGood(1);
    Check("S2 黑市主理人: buying the signature good pays HP, grants gold and keeps the slot",
        signature.Success && jade.LineupHp == hpBeforeJade - 10 && jade.Gold > goldBeforeJade &&
        jade.JadeGoods.Any(good => good.SlotId == 1 && good.GoodId != 0));

    // Trailblaze (开拓银轨), Chimera (奇兽花园), Member Link (梦境交织), Counter (反击), Stellaron Hunter (星核猎手) and
    // Clara (机心巧匠): stateful effects round-trip through GetData.
    var trailRoles = GameData.GridFightRoleBasicInfoData.Values
        .Where(role => role.SeasonID == season && role.TraitList.Contains(GridFightSession.TrailblazeTraitId) && role.IsInPool)
        .Select(role => role.ID).Take(2).ToArray();
    if (trailRoles.Length == 2)
    {
        var (trailRun, trail) = Board((trailRoles[0], 1), (trailRoles[1], 2));
        trailRun.BuildSeasonTwoFollowUpSync();
        var trailInfo = EffectInfo(trailRun, GridFightSession.TrailblazeTraitId, GridFightSession.TrailblazeEffectId)?.KDBKIPPHMFI;
        Check("S2 开拓银轨: goods list ends with the refresh good and never contains id 0",
            trailInfo != null && trailInfo.OGDKJPDFFAI.Count > 1 && trailInfo.OGDKJPDFFAI.All(good => good.DNJCAMKKLPB != 0) &&
            trailInfo.OGDKJPDFFAI[^1].DNJCAMKKLPB == GridFightSession.TrailblazeRefreshGoodId);
        trail.EnterBattle();
        trail.ResolveBattle(true, new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>()));
        Check("S2 开拓银轨: a battle stage grants 开拓力", trail.TrailblazeCoin > 0);

        // Pam King (帕姆王) "累计消耗解锁": spending Trailblaze coins (开拓力) fills CostedCoin of the oneof 1466 modifier;
        // 500 grants 604202 once.
        var (pamRun, pam) = Board((trailRoles[0], 1), (trailRoles[1], 2));
        pamRun.BuildSeasonTwoFollowUpSync();
        var pamStart = pamRun.BuildCurrentInfo()!.GameData.GameItemList
            .FirstOrDefault(item => item.ONKMJAGNECC != null)?.ONKMJAGNECC;
        Check("S2 帕姆王: GetData carries the progress modifier at 0/500",
            pamStart != null && pamStart.IANFMIJBKAF == 0 && pamStart.PLAIPMBIHGJ == GridFightSession.PamKingCostedThreshold &&
            pam.PamKingModifierUniqueId != 0);
        typeof(GridFightSession).GetField("_trailblazeCoin", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!.SetValue(pam, 2000u);
        var pamFirstCost = pam.TrailblazeGoods[^1].Cost;
        var pamFirstBuy = pam.BuyTrailblazeGood((uint)pam.TrailblazeGoods.Count - 1).Success;
        var pamDiff = pamRun.BuildSeasonTwoFollowUpSync()?.ResultList.SelectMany(result => result.UpdateDynamicList)
            .Select(data => data.GameDataItemInfo?.ONKMJAGNECC).LastOrDefault(info => info != null);
        Check("S2 帕姆王: a Trailblaze purchase counts its cost and syncs the modifier",
            pamFirstBuy && pam.TrailblazeCostedCoin == pamFirstCost && pamDiff?.IANFMIJBKAF == pamFirstCost);
        GridFightSyncUpdateResultScNotify? pamUnlock = null;
        for (var buy = 0; buy < 1000 && pamUnlock == null; buy++)
        {
            if (!pam.BuyTrailblazeGood((uint)pam.TrailblazeGoods.Count - 1).Success) break;
            var notify = new GridFightSyncUpdateResultScNotify();
            pamRun.AppendPamKingUnlock(notify);
            if (notify.ResultList.Count > 0) pamUnlock = notify;
        }
        var pamResult = pamUnlock?.ResultList[0];
        Check("S2 帕姆王: reaching 500 grants 帕姆王 with update source 42 and the modifier UID as params[1]",
            pam.TrailblazeCostedCoin >= GridFightSession.PamKingCostedThreshold && pam.PamKingUnlocked &&
            pam.Roles.Any(role => role.RoleId == GridFightSession.PamKingRoleId) &&
            pamResult?.UpdateSource == GridFightUpdateSrcType.Hlhfimghhno &&
            pamResult.EffectParamList.SequenceEqual(new[] { GridFightSession.TrailblazeTraitId, pam.PamKingModifierUniqueId }) &&
            pamResult.UpdateDynamicList.Any(data => data.AddRoleInfo?.Id == GridFightSession.PamKingRoleId) &&
            pamResult.UpdateDynamicList.Any(data => data.GameDataItemInfo?.ONKMJAGNECC?.IANFMIJBKAF >= GridFightSession.PamKingCostedThreshold));
        var pamCount = pam.Roles.Count(role => role.RoleId == GridFightSession.PamKingRoleId);
        pam.BuyTrailblazeGood((uint)pam.TrailblazeGoods.Count - 1);
        var pamAgain = new GridFightSyncUpdateResultScNotify();
        pamRun.AppendPamKingUnlock(pamAgain);
        Check("S2 帕姆王: the unlock is granted only once",
            pamAgain.ResultList.Count == 0 && pam.Roles.Count(role => role.RoleId == GridFightSession.PamKingRoleId) == pamCount);

        // The client keeps Trailblaze goods by list index and redraws only the indexes that changed, so the shop keeps
        // one shape: five distinct goods, then the refresh good at index 5 — also at the lowest layer, after a
        // purchase (restocked in place) and after a refresh.
        var (shapeRun, shape) = Board((trailRoles[0], 1), (trailRoles[1], 2));
        shapeRun.BuildSeasonTwoFollowUpSync();
        typeof(GridFightSession).GetField("_trailblazeCoin", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!.SetValue(shape, 5000u);
        bool HasShopShape(GridFightSession session) =>
            session.TrailblazeGoods.Count == 6 &&
            session.TrailblazeGoods[5].GoodId == GridFightSession.TrailblazeRefreshGoodId &&
            session.TrailblazeGoods.Take(5).Select(good => good.GoodId).Distinct().Count() == 5 &&
            session.TrailblazeGoods.All(good => good.GoodId != 0);
        var shapeAtStart = HasShopShape(shape);
        var shapeRefreshes = new HashSet<string>();
        var shapeKeptOnBuy = true;
        for (var round = 0; round < 6; round++)
        {
            var bought = shape.TrailblazeGoods[0].GoodId;
            shapeKeptOnBuy &= shape.BuyTrailblazeGood(0).Success && HasShopShape(shape) &&
                              (bought is 201 or 203 or 204 or 205 ? shape.TrailblazeGoods.Take(5).All(good => good.GoodId != bought) : true);
            shapeKeptOnBuy &= shape.BuyTrailblazeGood(5).Success && HasShopShape(shape);
            shapeRefreshes.Add(string.Join(',', shape.TrailblazeGoods.Take(5).Select(good => good.GoodId).Order()));
        }
        Check("S2 开拓银轨: the shop keeps five goods and the refresh good at index 5 through purchases and refreshes",
            shapeAtStart && shapeKeptOnBuy);
        Check("S2 开拓银轨: refreshing at the lowest layer offers different goods, not one reshuffled set",
            shapeRefreshes.Count > 1);
    }
    var (claraRun, clara) = Board((GridFightSession.ClaraRoleId, 1));
    claraRun.BuildSeasonTwoFollowUpSync();
    var claraPending = claraRun.BuildPendingAction();
    Check("S2 机心巧匠: Clara's first upgrade offers the three modules",
        claraPending?.PFEBKFJOGKO?.EGJDILDEAKP.OrderBy(id => id).SequenceEqual(new uint[] { 1001, 2001, 3001 }) == true);
    clara.SelectClaraEnhance(claraPending!.QueuePosition, 2001, out var enhanced);
    Check("S2 机心巧匠: the chosen module is recorded and synced",
        enhanced && EffectInfo(claraRun, GridFightSession.ClaraTraitId, GridFightSession.ClaraEffectId)
            ?.PFEBKFJOGKO.EMAGFIBLNCJ.Any(entry => entry.IGOHOJEJIGP == 2001 && entry.Num == 1) == true);

    var chimeraRoles = GameData.GridFightRoleBasicInfoData.Values
        .Where(role => role.SeasonID == season && role.TraitList.Contains(GridFightSession.ChimeraTraitId) && role.IsInPool)
        .Select(role => role.ID).Distinct().Take(3).ToArray();
    if (chimeraRoles.Length == 3)
    {
        var (chimeraRun, chimera) = Board((chimeraRoles[0], 1), (chimeraRoles[1], 2), (chimeraRoles[2], 3));
        chimeraRun.BuildSeasonTwoFollowUpSync();
        var chimeraPending = chimeraRun.BuildPendingAction()?.JMJOAOCJAAG;
        Check("S2 奇兽花园: first activation grants a free recruit with goods", chimeraPending is { OGDKJPDFFAI.Count: > 0 });
        var pickIndex = chimera.ChimeraGoods.ToList().FindIndex(good => good.Type == GridFightSession.ChimeraGoodChimera);
        if (pickIndex >= 0)
        {
            chimera.SelectChimeraGood(chimera.SeasonTwoPending!.QueuePosition, (uint)pickIndex, out var finishedChimera);
            Check("S2 奇兽花园: a recruited chimera appears in the garden",
                finishedChimera != 0 && chimera.Chimeras.Count == 1 &&
                EffectInfo(chimeraRun, GridFightSession.ChimeraTraitId, GridFightSession.ChimeraEffectId)
                    ?.GIKNCJMJLFE.BCGOAIPKGIK.Count == 1);
        }
    }

    // Jade's auto-open flag must not arrive together with a server prompt (the client then never closes the prompt).
    var (mixedRun, mixed) = Board((GridFightSession.JadeRoleId, 1), (GridFightSession.ClaraRoleId, 2));
    bool? AutoOpen(GridFightSyncUpdateResultScNotify? sync) => sync?.ResultList
        .SelectMany(result => result.UpdateDynamicList)
        .Where(data => data.TraitInfo?.TraitEffectInfo?.GDAMHENIIDJ != null)
        .Select(data => (bool?)data.TraitInfo.TraitEffectInfo.GDAMHENIIDJ.KPMKFGCFMHC)
        .LastOrDefault();
    var firstFlush = mixedRun.BuildSeasonTwoFollowUpSync();
    var claraQueue = mixed.SeasonTwoPending?.QueuePosition ?? 0;
    Check("S2 黑市主理人: shop auto-open waits while Clara's prompt is open",
        claraQueue != 0 && AutoOpen(firstFlush) == false);
    mixed.SelectClaraEnhance(claraQueue, 1001, out _);
    Check("S2 黑市主理人: shop auto-open follows once the prompt is answered",
        AutoOpen(mixedRun.BuildSeasonTwoFollowUpSync()) == true);
    Check("S2 pending: a resent answer for a finished prompt is recognised",
        mixed.IsFinishedSeasonTwoPending(claraQueue) && !mixed.IsFinishedSeasonTwoPending(claraQueue + 100));

    // Aha trait (幻乐之主): an Aha on the board may put an Aha good into a shop roll; copies turn into growth. Which
    // role ids are Aha's cost tiers, and what the tier the shop sells costs, comes from the tables: 3-cost 151102
    // below 151112 / 151122 / 151132 in 4.6.5x, 4-cost 151112 below 151122 / 151132 from OSBETAWin4.7.51 (151102
    // was removed).
    var ahaTiers = catalog.AhaGrowthRoleIds;
    var ahaSevenId = catalog.AhaSevenCostRoleId;
    Check("S2 幻乐之主: the tables name the star-less Aha tiers in cost order and one seven-price tier",
        ahaTiers.Count >= 2 && ahaSevenId != 0 &&
        ahaTiers.Append(ahaSevenId).Select(id => GameData.GridFightRoleBasicInfoData[id].AvatarID).Distinct().Count() == 1 &&
        ahaTiers.Zip(ahaTiers.Skip(1), (lower, higher) => GameData.GridFightRoleBasicInfoData[lower].Rarity <
                                                          GameData.GridFightRoleBasicInfoData[higher].Rarity).All(ordered => ordered));
    var ahaBaseId = ahaTiers[0];
    var ahaBaseCost = GameData.GridFightRoleBasicInfoData[ahaBaseId].Rarity;
    Check("S2 幻乐之主: only the cheapest Aha tier is sold; the tiers above it are upgrade products",
        catalog.RolePool.Count(role => role.AvatarID == GameData.GridFightRoleBasicInfoData[ahaBaseId].AvatarID) == 1 &&
        catalog.RolePool.Any(role => role.ID == ahaBaseId) &&
        ahaTiers.Skip(1).Append(ahaSevenId).All(catalog.UpgradeOnlyRoleIds.Contains));
    bool IsAhaTier(uint roleId) => roleId == ahaSevenId || ahaTiers.Contains(roleId);
    var ahaChance = GridFightSession.AhaGlanceChanceForCost(ahaBaseCost);
    var (ahaRun, aha) = Board((ahaBaseId, 1));
    ahaRun.BuildSeasonTwoFollowUpSync();
    var ahaRolls = 0;
    for (var roll = 0; roll < 400; roll++)
    {
        aha.RefreshShop(false);
        if (aha.Shop.Any(slot => GridFightSession.IsAhaGoods(slot.SpecialGoodsId))) ahaRolls++;
    }
    Check($"S2 幻乐之主: a {ahaBaseCost}-cost Aha glances at about {ahaChance:P0} of the shop rolls, not every roll",
        aha.AhaShopGlanceChance == ahaChance && ahaRolls >= 400 * ahaChance / 2 && ahaRolls <= 400 * ahaChance * 3 / 2);
    for (var roll = 0; roll < 100 && !aha.Shop.Any(slot => GridFightSession.IsAhaGoods(slot.SpecialGoodsId)); roll++)
        aha.RefreshShop(false);
    var ahaSlot = aha.Shop.FirstOrDefault(slot => GridFightSession.IsAhaGoods(slot.SpecialGoodsId));
    var ahaGoldBefore = aha.Gold;
    Check("S2 幻乐之主: the shop offers an Aha good and it can be bought for its price",
        ahaSlot != null && aha.BuySpecialGoods([ahaSlot.Index]).Count == 1 && ahaSlot.SoldOut &&
        aha.RefreshShop(true));
    // The rolled Aha good is random and 346 (星神降临) hands out an Aha copy, so the copy rule runs on its own board.
    var (ahaCopyRun, ahaCopy) = Board((ahaBaseId, 1));
    ahaCopyRun.BuildSeasonTwoFollowUpSync();
    AddRole(ahaCopy, ahaBaseId);
    ahaCopyRun.BuildSeasonTwoFollowUpSync();
    Check("S2 幻乐之主: a second Aha becomes 幻月之力 instead of a merge",
        ahaCopy.Roles.Count(role => IsAhaTier(role.RoleId)) == 1 && ahaCopy.AhaGrowth == 1 &&
        ahaCopy.RefreshRoleSavedValues().ContainsKey(GridFightSession.AhaGrowthSavedValueKey) == false);

    // Growth steps. 4.7.51 (two star-less tiers): the client's GridFightAhaRarity turns the 4-cost form into the
    // 5-cost form at growth 5 and ends the bar there. 4.6.5x (three tiers): 4-cost at 3 and 5-cost at 6, the
    // server's earlier even spread below that client's 7-cost growth of 9.
    {
        var (growthRun, growth) = Board((ahaBaseId, 1));
        growthRun.BuildSeasonTwoFollowUpSync();
        var tierByGrowth = new Dictionary<uint, uint>();
        for (var copy = 0; copy < 8; copy++)
        {
            AddRole(growth, ahaBaseId);
            growthRun.BuildSeasonTwoFollowUpSync();
            tierByGrowth[growth.AhaGrowth] = growth.Roles.Single(role => IsAhaTier(role.RoleId)).RoleId;
        }
        var (sevenGrowth, maxGrowth) = growth.AhaGrowthRule;
        if (ahaTiers.Count == 2)
            Check("S2 幻乐之主: 4.7.51 tiers stay 4-cost below growth 5, turn 5-cost at 5 and stop growing there",
                sevenGrowth == 5 && maxGrowth == 5 && growth.AhaGrowth == 5 &&
                Enumerable.Range(1, 4).All(step => tierByGrowth[(uint)step] == ahaTiers[0]) && tierByGrowth[5] == ahaTiers[1]);
        else
            Check("S2 幻乐之主: 4.6.5x tiers turn 4-cost at growth 3 and 5-cost at growth 6",
                sevenGrowth == 9 && maxGrowth == 0 && growth.AhaGrowth == 8 &&
                tierByGrowth[2] == ahaTiers[0] && tierByGrowth[3] == ahaTiers[1] && tierByGrowth[5] == ahaTiers[1] &&
                tierByGrowth[6] == ahaTiers[2]);
        // 7-cost: enough growth on the last star-less tier plus 10 Elation (欢愉) stars (trait 300602 BaseDescParamList
        // [10, 12]).
        var topTier = growth.Roles.Single(role => IsAhaTier(role.RoleId));
        var stillStarless = topTier.RoleId == ahaTiers[^1];
        var elationMates = catalog.RolePool
            .Where(role => role.TraitList.Contains(GridFightSession.ElationTraitId) && !IsAhaTier(role.ID) &&
                           growth.IsRestrictedRoleUnlockedForRun(role.ID) &&
                           GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | 3))
            .Select(role => role.ID).Take(3).ToList();
        if (elationMates.Count == 3)
        {
            for (var index = 0; index < elationMates.Count; index++)
            {
                growth.TryAddRole(elationMates[index], 3, out var mate);
                mate.AcquiredRoles.Single().Position = (uint)index + 2;
            }
            while (growth.AhaGrowth < sevenGrowth)
            {
                AddRole(growth, ahaBaseId);
                growthRun.BuildSeasonTwoFollowUpSync();
            }
            growthRun.BuildSeasonTwoFollowUpSync();
            Check("S2 幻乐之主: the last star-less tier turns 7-cost once growth and 10 欢愉 stars are reached",
                stillStarless && topTier.RoleId == ahaSevenId &&
                growth.GetAhaSevenCostProgress().Any(entry => entry.TraitId == GridFightSession.ElationTraitId &&
                                                              entry.Progress >= entry.Target));
        }
    }

    // Aha goods placed directly into the shop (the glance itself is random).
    GridFightSession AhaGoodsBoard(uint goodsId, out uint index)
    {
        var (_, session) = Board((ahaBaseId, 1));
        session.AdjustGold(100);
        var slot = session.Shop[0];
        session.Shop[0] = new GridFightShopSlot { Index = slot.Index, SpecialGoodsId = goodsId, Price = 1 };
        index = slot.Index;
        return session;
    }
    // Aha good 333 (多回点血！): "获得#1点小队生命值和生命上限" raises the cap as well as the HP.
    var moreHp = AhaGoodsBoard(333, out var moreHpIndex);
    var (maxHpBefore, moreHpBefore) = (moreHp.MaxLineupHp, moreHp.LineupHp);
    var moreHpBought = moreHp.BuySpecialGoods([moreHpIndex]);
    Check("S2 幻乐之主: 多回点血！ raises max squad HP and heals by the same amount",
        moreHpBought.Count == 1 && moreHp.MaxLineupHp == maxHpBefore + 15 &&
        moreHp.LineupHp == Math.Min(moreHp.MaxLineupHp, moreHpBefore + 15) && moreHpBought[0].Application.LineupHpChanged);
    // Aha good 347 (开拓之旅) and its upgraded row 447 (哈基维利) hand out their GridFightSpecialGoods value (150 / 500 in
    // the OSBETAWin4.7.51 table the server carries; 4.6.5x had 1000 with a 200 override for 447).
    uint TrailblazePowerOf(uint goodsId)
    {
        var board = AhaGoodsBoard(goodsId, out var index);
        var before = board.TrailblazeCoin;
        return board.BuySpecialGoods([index]).Count == 1 ? board.TrailblazeCoin - before : 0;
    }
    uint AhaGoodsParam(uint goodsId) => (uint)GridFightSeasonTwoTables.AhaGoods.Single(row => row.Id == goodsId).Params[0];
    Check($"S2 幻乐之主: 开拓之旅 grants {AhaGoodsParam(347)} 开拓力 and 哈基维利 {AhaGoodsParam(447)}, their table values",
        AhaGoodsParam(347) > 0 && AhaGoodsParam(447) > 0 &&
        TrailblazePowerOf(347) == AhaGoodsParam(347) && TrailblazePowerOf(447) == AhaGoodsParam(447));
    // Aha good 341 (五费专场): the whole shop, the bought slot included, refills with 5-cost roles; 3★ roles stay out.
    var fiveCost = AhaGoodsBoard(341, out var fiveCostIndex);
    var fiveCostRoles = catalog.RolePool.Where(role => role.Rarity == 5 && fiveCost.IsRestrictedRoleUnlockedForRun(role.ID))
        .Select(role => role.ID).ToList();
    if (fiveCostRoles.Count >= 2)
    {
        var maxed = AddRole(fiveCost, fiveCostRoles[0]);
        maxed.Star = 3;
        var refilled = fiveCost.BuySpecialGoods([fiveCostIndex]).Count == 1;
        Check("S2 幻乐之主: 五费专场 refills every shop slot with 5-cost roles and leaves out 3-star roles",
            refilled && fiveCost.Shop.Count == catalog.ShopSize && fiveCost.Shop.All(slot =>
                slot.SpecialGoodsId == 0 && !slot.SoldOut && slot.RoleId != fiveCostRoles[0] &&
                GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity == 5));
    }
    Check("S2 幻乐之主: GetData carries the 7-cost progress modifier",
        ahaCopyRun.BuildCurrentInfo()!.GameData.GameItemList.Any(item => item.GEKCCCPGBGJ?.GridFightTraitInfo.Count == 2));

    // A 3★ 7-cost Aha (星神★阿哈), once confirmed: every fielded role joins MoonGame and Elation.
    var (masterRun, master) = Board((roleA, 1));
    master.TryAddRole(ahaSevenId, 3, out var masterAcquisition);
    masterAcquisition.AcquiredRoles.Single().Position = 2;
    masterRun.BuildSeasonTwoFollowUpSync();
    var ultra = masterRun.BuildPendingAction();
    var ultraQueue = master.SeasonTwoPending?.QueuePosition ?? 0;
    var confirmed = master.ConfirmAhaUltra(ultraQueue) == ultraQueue && ultraQueue != 0;
    Check("S2 幻乐之主: a 3-star 7-cost Aha opens the ultra block and confirming makes everyone join 幻月游戏 and 欢愉",
        ultra?.OKPMJOCEKBC?.Type == HOIDGPICBOJ.Jphakjkapei && confirmed &&
        master.Roles.Where(role => role.Position is >= 1 and <= 13).All(role =>
            master.ResolveRoleTraitIds(role).Contains(GridFightSession.MoonGameTraitId) &&
            master.ResolveRoleTraitIds(role).Contains(GridFightSession.ElationTraitId)) &&
        master.GetAhaUltraTraitGrants().Count == 2);

    // Spotlight (主角光环): route nodes 1-2 / 2-1 carry the spotlight flag; entering one opens a two-card selection.
    var (spotRun, spot) = Board((roleA, 1), (roleB, 2));
    var spotNodes = spotRun.BuildCurrentInfo()!.GameInfoList
        .Where(info => info.LevelInfo != null)
        .SelectMany(info => info.LevelInfo.OOMMIIKPHBC)
        .Count(node => node.Param.HPNNLMBHOPF);
    spot.TryMoveToSection(1, 2, out _);
    spot.HandlePending(spot.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
    for (var step = 0; step < 6 && spot.Phase != GridFightSessionPhase.Preparing; step++)
    {
        if (spot.Phase == GridFightSessionPhase.SelectingInvestment)
            spot.SelectInvestment(spot.PendingQueuePosition, spot.InvestmentOffer.First());
        else if (spot.Phase == GridFightSessionPhase.AwaitingBackToPrepare)
            spot.BackToPrepare();
        else
            spot.HandlePending(spot.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
    }
    // The client asks for the selection itself (8508 from the "选择主角" battle button after its spotlight hint), so
    // node entry must not push it, and the battle stays closed until it is made.
    var spotEntrySync = spotRun.BuildSeasonTwoFollowUpSync();
    var spotNodeFlagged = spotRun.BuildCurrentInfo()!.GameInfoList.Where(info => info.LevelInfo != null)
        .SelectMany(info => info.LevelInfo.OOMMIIKPHBC)
        .Single(node => node.DCAHJELNJNP == 1 && node.SectionId == 2).Param.HPNNLMBHOPF;
    Check("S2 主角光环: entering the node pushes no selection, keeps the node flag and keeps the battle closed",
        spot.SeasonTwoPending == null && spotNodeFlagged && spot.IsSpotLightSelectionDue && !spot.EnterBattle() &&
        spotEntrySync?.ResultList.SelectMany(result => result.UpdateDynamicList).All(data => data.PendingAction == null) != false);
    var spotEntered = spot.StartSpotLightFromEnter();
    var spotOpenSync = spotRun.BuildSpotLightSync(null, 0);
    var spotPending = spotRun.BuildPendingAction()?.EEKMIAMGHFL;
    Check("S2 主角光环: 8508 opens the node selection",
        spotEntered && spot.IsSpotLightSelecting && spotPending != null &&
        spotOpenSync.ResultList.SelectMany(result => result.UpdateDynamicList).Any(data => data.PendingAction?.EEKMIAMGHFL != null));
    var spotQueue = spot.SeasonTwoPending?.QueuePosition ?? 0;
    // The role is chosen by dragging it onto the spotlight stage; the client refuses board drags under the gameplay
    // lock, so the selection must stay unlocked and the server must accept team edits while it is open.
    var spotLocks = spotOpenSync?.ResultList.SelectMany(result => result.UpdateDynamicList)
        .Where(data => data.LockInfo != null).Select(data => data.LockInfo).ToList() ?? [];
    var spotBasicLock = spotRun.BuildCurrentInfo()!.GameInfoList.First(info => info.BasicInfo != null).BasicInfo.LockInfo;
    var spotRoleB = spot.Roles.First(role => role.RoleId == roleB);
    var spotMoved = spot.UpdatePositions([(spotRoleB.UniqueId, 5)]).Success;
    var spotBattleBlocked = !spot.EnterBattle();
    Check("S2 主角光环: the open selection sends no gameplay lock and still allows moving roles, but not entering battle",
        spotPending != null && spotLocks.All(info => info.LockType == GridFightLockType.None) &&
        spotBasicLock?.LockType != GridFightLockType.Gameplay && spotMoved && spotRoleB.Position == 5 &&
        spotBattleBlocked);
    var spotChanges = spotPending == null ? null
        : spot.SelectSpotLight(spotQueue, spotPending.HPKFKBKOADP[0].IHHNLLAJAGB, roleA, out _);
    var spotRole = spot.Roles.First(role => role.RoleId == roleA);
    Check("S2 主角光环: two spotlight nodes, a two-card selection on entry and the crown goes to the chosen role",
        spotNodes == 2 && spotPending?.HPKFKBKOADP.Count == 2 && spotChanges?.Added.Count == 1 &&
        spot.GetSpotLightIdForRole(spotRole.UniqueId) == spotPending.HPKFKBKOADP[0].IHHNLLAJAGB &&
        spotRun.BuildCurrentInfo()!.GameInfoList.Any(info => info.LEGMAJLHBLA?.FNNEPMAPOOF.Count == 1));
    // While the current node has HasSpotLight the S2 battle button sends 8508 instead of starting the battle, so the
    // finished selection must clear the flag on 1-2 (route node update 1779 and GetData) and keep it on 2-1.
    var spotRouteUpdates = spotChanges == null ? []
        : spotRun.BuildSpotLightSync(spotChanges, 0).ResultList.SelectMany(result => result.UpdateDynamicList)
            .Where(data => data.KBHOBGHKIBA != null).Select(data => data.KBHOBGHKIBA.DBIIPEEDBHI).ToList();
    var spotRouteAfter = spotRun.BuildCurrentInfo()!.GameInfoList
        .Where(info => info.LevelInfo != null)
        .SelectMany(info => info.LevelInfo.OOMMIIKPHBC)
        .ToList();
    Check("S2 主角光环: a finished selection clears the node's spotlight flag so the battle button starts the battle",
        spotRouteUpdates.Count == 1 && spotRouteUpdates[0].DCAHJELNJNP == 1 && spotRouteUpdates[0].SectionId == 2 &&
        !spotRouteUpdates[0].Param.HPNNLMBHOPF &&
        spotRouteAfter.Single(node => node.DCAHJELNJNP == 1 && node.SectionId == 2).Param.HPNNLMBHOPF == false &&
        spotRouteAfter.Single(node => node.DCAHJELNJNP == 2 && node.SectionId == 1).Param.HPNNLMBHOPF &&
        spot.ResolveStaleSpotLightNode() == null && spot.EnterBattle());

    // Will Inheritance (意志传承, 110002): the bound unit becomes a random role one cost higher, keeping slot, star and
    // the crown.
    var (willRun, will) = Board((roleA, 1), (roleB, 2));
    will.TryMoveToSection(1, 2, out _);
    will.HandlePending(will.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
    for (var step = 0; step < 6 && will.Phase != GridFightSessionPhase.Preparing; step++)
    {
        if (will.Phase == GridFightSessionPhase.SelectingInvestment)
            will.SelectInvestment(will.PendingQueuePosition, will.InvestmentOffer.First());
        else if (will.Phase == GridFightSessionPhase.AwaitingBackToPrepare)
            will.BackToPrepare();
        else
            will.HandlePending(will.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
    }
    willRun.BuildSeasonTwoFollowUpSync();
    will.StartSpotLightFromEnter();
    var willCandidates = (List<uint>)typeof(GridFightSession)
        .GetField("_spotLightCandidates", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .GetValue(will)!;
    if (willCandidates.Count > 0) willCandidates[0] = GridFightSession.WillInheritanceSpotLightId;
    var willUnit = will.Roles.First(role => role.RoleId == roleA);
    var (willUnique, willStar, willPos) = (willUnit.UniqueId, willUnit.Star, willUnit.Position);
    var willCost = GameData.GridFightRoleBasicInfoData[roleA].Rarity;
    var willChanges = will.SelectSpotLight(will.SeasonTwoPending?.QueuePosition ?? 0,
        GridFightSession.WillInheritanceSpotLightId, roleA, out _);
    var willSync = willChanges == null ? []
        : willRun.BuildSpotLightSync(willChanges, 0).ResultList.SelectMany(result => result.UpdateDynamicList).ToList();
    Check("S2 主角光环 意志传承: the unit becomes a same-star role one cost higher and keeps the crown",
        willCost < 5 && willChanges?.UpdatedRoles.Count == 1 && willUnit.UniqueId == willUnique &&
        willUnit.Star == willStar && willUnit.Position == willPos && willUnit.RoleId != roleA &&
        GameData.GridFightRoleBasicInfoData[willUnit.RoleId].Rarity == willCost + 1 &&
        will.GetSpotLightIdForRole(willUnique) == GridFightSession.WillInheritanceSpotLightId &&
        willSync.Any(data => data.UpdateRoleInfo?.Id == willUnit.RoleId));

    // Partner (本局奇遇) effects that change server rules.
    GridFightSession? RunWithPetPal(uint effectId)
    {
        for (var seed = 0; seed < 3000; seed++)
        {
            var manager = new GridFightManager(new PlayerInstance(new PlayerData { Uid = 1 }), new Random(seed));
            if (!manager.Start(catalog.CurrentSeason, catalog.HighestDivision.ID, false)) continue;
            var session = manager.Session!;
            if (session.PetPalEffectId != effectId) continue;
            session.HandlePending(session.PendingQueuePosition, GridFightPendingRequestKind.PortalSelect,
                session.PortalOffer.First(id => id != GridFightSession.BlueSeaPortalId));
            session.HandlePending(session.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
            session.GrantOfficialInitialRoundRewards(out _);
            session.HandlePending(session.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
            return session.Phase == GridFightSessionPhase.Preparing ? session : null;
        }
        return null;
    }
    if (RunWithPetPal(109004) is { } fanGifts)
    {
        fanGifts.Roles.Clear();
        AddRole(fanGifts, roleA).Position = 1;
        var supplied = false;
        for (var step = 0; step < 40 && !supplied; step++)
        {
            switch (fanGifts.Phase)
            {
                case GridFightSessionPhase.Preparing:
                    fanGifts.EnterBattle();
                    fanGifts.ResolveBattle(true, new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>()));
                    break;
                case GridFightSessionPhase.SelectingSupply:
                    var picked = fanGifts.SupplyOffer[0];
                    supplied = fanGifts.SupplyOffer.All(offer => offer.Star == 2) &&
                               fanGifts.SelectSupply(fanGifts.PendingQueuePosition, [1]).Success &&
                               fanGifts.Roles.Any(role => role.RoleId == picked.RoleId && role.Star >= 2);
                    step = 40;
                    break;
                case GridFightSessionPhase.SelectingInvestment:
                    fanGifts.SelectInvestment(fanGifts.PendingQueuePosition, fanGifts.InvestmentOffer.First());
                    break;
                case GridFightSessionPhase.SelectingEncounter:
                    fanGifts.SelectEncounter(fanGifts.PendingQueuePosition, fanGifts.EncounterOptions.First().EliteBranchId);
                    break;
                case GridFightSessionPhase.AwaitingBackToPrepare:
                    fanGifts.BackToPrepare();
                    break;
                default:
                    fanGifts.HandlePending(fanGifts.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
                    fanGifts.HandlePending(fanGifts.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
                    break;
            }
        }
        Check("S2 奇遇 绯绯狐 109004: every supply offers 2-star roles and grants them", supplied);
    }
    if (RunWithPetPal(102001) is { } guarded)
    {
        guarded.Roles.Clear();
        AddRole(guarded, roleA).Position = 1;
        var hpBefore = guarded.LineupHp;
        var lost = guarded.EnterBattle() &&
                   guarded.ResolveBattle(true, new GridFightBattleStatistics(10, [], [], [], [], 1, new Dictionary<string, uint>()))
                       is { Success: true, IsPerfectFinish: true };
        Check("S2 奇遇 汪汪丹 102001: the first failed battle counts as a perfect finish", lost && guarded.LineupHp >= hpBefore);
    }
    if (RunWithPetPal(104003) is { } coffee && coffee.BonfireNodes.FirstOrDefault() is { } bonfireNode)
    {
        coffee.TryMoveToSection(bonfireNode.ChapterId, bonfireNode.SectionId, out _);
        coffee.HandlePending(coffee.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
        for (var step = 0; step < 6 && coffee.Phase != GridFightSessionPhase.Preparing; step++)
        {
            if (coffee.Phase == GridFightSessionPhase.SelectingInvestment)
                coffee.SelectInvestment(coffee.PendingQueuePosition, coffee.InvestmentOffer.First());
            else if (coffee.Phase == GridFightSessionPhase.AwaitingBackToPrepare)
                coffee.BackToPrepare();
            else
                coffee.HandlePending(coffee.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
        }
        coffee.RefreshSeasonTwoTraits();
        var bonfirePending = coffee.SeasonTwoPending;
        var bootsBefore = coffee.Equipments.Count(item => item.EquipmentId == 47030101);
        var boots = bonfirePending == null ? null : coffee.BonfireOptions.FirstOrDefault(option => option.OptionId == 2);
        var gained = boots != null && coffee.SelectBonfireOption(bonfirePending!.QueuePosition, boots.EventId, boots.OptionId).Success;
        var section = coffee.SectionId;
        var left = gained && coffee.LeaveBonfire(bonfirePending!.QueuePosition, out _) == bonfirePending.QueuePosition;
        Check("S2 幕后茶歇: the partner's node opens a bonfire, its option pays out and leaving moves on",
            bonfirePending?.Kind == GridFightSeasonTwoPendingKind.Bonfire && bonfireNode.EventId == 10400301 &&
            coffee.Equipments.Count(item => item.EquipmentId == 47030101) == bootsBefore + 3 && left &&
            coffee.SectionId != section && coffee.Phase == GridFightSessionPhase.AwaitingBattleRound);
    }
    if (RunWithPetPal(105001) is { } blackHole)
    {
        blackHole.RefreshSeasonTwoTraits();
        Check("S2 奇遇 杨叔 105001: the run starts with 坍缩奇点",
            blackHole.Equipments.Count(item => item.EquipmentId == 353005) == 1 &&
            blackHole.RefreshSeasonTwoTraits().AddedEquipments.Count == 0);
    }

    // Every season-2 run snapshot (GetData) and battle survives a protobuf round trip with all effects present.
    var everyone = Board((GridFightSession.DanHengMainRoleId, 1), (GridFightSession.PhainonRoleId, 2),
        (GridFightSession.JadeRoleId, 3), (150402, 5), (GridFightSession.ClaraRoleId, 6));
    everyone.Manager.BuildSeasonTwoFollowUpSync();
    var everyoneInfo = everyone.Manager.BuildCurrentInfo()!;
    Check("S2 GetData with all 5-cost uniques round-trips and carries the context values",
        GridFightCurrentInfo.Parser.ParseFrom(everyoneInfo.ToByteArray()).Equals(everyoneInfo) &&
        everyoneInfo.GameInfoList.Any(info => info.ContextValueInfo is { FMNPBOJGLFI: > 0 } context &&
                                              context.DKMNOPCJDDC.Count == 2 && context.ContextValueVersion != 0));
}

// --- Run persistence: every saved field, a full route played on restored copies --------------------------------
ThreeEffectsRegression.Run(NewRun, catalog, Check);
EconomyEffectsRegression.Run(NewRun, catalog, Check);
BattleEffectsRegression.Run(NewRun, catalog, Check);
ElationUiRegression.Run(NewRun, catalog, Check);
OwlbertRegression.Run(NewRun, catalog, Check);
SpotLightEffectsRegression.Run(NewRun, catalog, Check);
SyncOrderRegression.Run(NewRun, catalog, Check);
TraitEquipmentRegression.Run(NewRun, catalog, Check);

var persistBase = NewRun(101);
var persistCatalog = persistBase.Catalog;
var persistSession = persistBase.Session!;
persistSession.Roles.Clear();
AddRole(persistSession, roleA).Position = 1;
var persistBytes = SnapshotProbe.Serialize(persistSession);
var fieldFailures = new List<string>();
foreach (var field in SnapshotProbe.PersistedFields)
{
    // Restore rejects a snapshot of another season by design (checked below).
    if (field.Name == "<Season>k__BackingField") continue;
    var probe = GridFightSession.Restore(persistCatalog, MemoryPackSerializer.Deserialize<GridFightRunSnapshot>(persistBytes)!);
    var sample = SnapshotProbe.Sample(field.FieldType);
    field.SetValue(probe, sample);
    try
    {
        var restoredValue = field.GetValue(SnapshotProbe.RoundTrip(persistCatalog, probe));
        if (SnapshotProbe.Difference(sample, restoredValue, field.Name,
                new Dictionary<object, object>(ReferenceEqualityComparer.Instance)) is { } difference)
            fieldFailures.Add(difference);
    }
    catch (Exception e)
    {
        fieldFailures.Add($"{field.Name}: {e.GetType().Name}: {e.Message}");
    }
}
foreach (var failure in fieldFailures) Console.WriteLine(failure);
Check($"each of the {SnapshotProbe.PersistedFields.Count} saved session fields survives a save and restore " +
      $"({GridFightSession.PersistenceTransientFields.Count} notification buffers excluded)", fieldFailures.Count == 0);
var otherSeason = persistSession.CaptureSnapshot();
otherSeason.Season++;
var otherSeasonRejected = false;
try
{
    GridFightSession.Restore(persistCatalog, otherSeason);
}
catch (InvalidDataException)
{
    otherSeasonRejected = true;
}
Check("a run saved for another season is rejected instead of restored", otherSeasonRejected);
// A run saved under other tables: OSBETAWin4.7.51 removed the 3-cost Aha 151102 that a 4.6.5x run can still hold.
var missingRole = persistSession.CaptureSnapshot();
missingRole.Roles[0].RoleId = GameData.GridFightRoleBasicInfoData.Keys.Max() + 1;
var missingRoleRejected = false;
try
{
    GridFightSession.Restore(persistCatalog, missingRole);
}
catch (InvalidDataException)
{
    missingRoleRejected = true;
}
Check("a run holding a role the loaded tables do not have is rejected instead of restored", missingRoleRejected);

// A whole route played on a copy restored before every step (and between battle start and settlement).
var replayManager = NewRun(routePortal);
var replay = replayManager.Session!;
replay.Roles.Clear();
AddRole(replay, roleA).Position = 1;
var sessionProperty = typeof(GridFightManager).GetProperty(nameof(GridFightManager.Session))!;
GridFightSession Reload(GridFightSession session)
{
    var restored = SnapshotProbe.RoundTrip(replayManager.Catalog, session);
    sessionProperty.SetValue(replayManager, restored);
    return restored;
}
var replayBattles = 0;
var replayFinished = false;
var replayStuck = "";
for (var step = 0; step < 300 && !replayFinished && replayStuck.Length == 0; step++)
{
    replay = Reload(replay);
    var queue = replay.PendingQueuePosition;
    var advanced = true;
    switch (replay.Phase)
    {
        case GridFightSessionPhase.Preparing:
            advanced = replay.EnterBattle();
            if (!advanced) break;
            replay = Reload(replay);
            var settlement = replay.ResolveBattle(true,
                new GridFightBattleStatistics(100, [], [], [], [], 1, new Dictionary<string, uint>()));
            advanced = settlement.Success;
            replayBattles++;
            replayFinished = settlement.Success && !settlement.RouteAdvanced;
            break;
        case GridFightSessionPhase.AwaitingBattleRound:
        case GridFightSessionPhase.AwaitingSupplyRound:
            advanced = replay.HandlePending(queue, GridFightPendingRequestKind.RoundBegin);
            replayManager.BuildBattleRoundSync(queue, []);
            break;
        case GridFightSessionPhase.SelectingInvestment:
            var investment = replay.InvestmentOffer.First();
            advanced = replay.SelectInvestment(queue, investment);
            replayManager.BuildInvestmentSelectionSync(queue, investment);
            break;
        case GridFightSessionPhase.SelectingSupply:
            var supply = replay.SelectSupply(queue, [1]);
            advanced = supply.Success;
            replayManager.BuildSupplySelectionSync(queue, 1, supply);
            break;
        case GridFightSessionPhase.SelectingEncounter:
            advanced = replay.SelectEncounter(queue, replay.EncounterOptions.First().EliteBranchId);
            break;
        case GridFightSessionPhase.AwaitingBattleReturn:
            advanced = replay.HandlePending(queue, GridFightPendingRequestKind.ReturnPreparation);
            break;
        case GridFightSessionPhase.AwaitingBackToPrepare:
            advanced = replay.BackToPrepare();
            replayManager.BuildPreparationSync();
            break;
        default:
            advanced = false;
            break;
    }
    if (!advanced) replayStuck = $"{replay.Phase} at {replay.ChapterId}/{replay.SectionId}";
}
if (replayStuck.Length > 0) Console.WriteLine($"restored route stuck: {replayStuck}");
Check("a run restored before every step still plays the full route to the final boss",
    replayFinished && replayBattles > 10 && replayStuck.Length == 0);

// --- Saved runs on a temporary database: re-login, restart, mid-battle disconnect, unusable blobs ---------------
// Runs last: once DatabaseHelper is initialised every GridFightManager attaches to it.
var databaseScratch = Path.Combine(Path.GetTempPath(), "m7h-gridfight-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(databaseScratch);
ConfigManager.Config.Path.DatabasePath = databaseScratch;
ConfigManager.Config.Database.DatabaseType = "sqlite";
ConfigManager.Config.Database.DatabaseName = "gridfight-regression.db";
new DatabaseHelper().Initialize(startSaveThread: false);
const int PersistUid = 70101;
GridFightManager Login() => new(new PlayerInstance(new PlayerData { Uid = PersistUid }) { Uid = PersistUid });

var firstLogin = Login();
var startedFresh = firstLogin.Session == null &&
                   firstLogin.Start(catalog.CurrentSeason, catalog.HighestDivision.ID, false);
var live = firstLogin.Session!;
live.HandlePending(live.PendingQueuePosition, GridFightPendingRequestKind.PortalSelect,
    live.PortalOffer.First(id => id != GridFightSession.BlueSeaPortalId));
live.HandlePending(live.PendingQueuePosition, GridFightPendingRequestKind.RoundBegin);
live.GrantOfficialInitialRoundRewards(out _);
live.HandlePending(live.PendingQueuePosition, GridFightPendingRequestKind.ReturnPreparation);
live.Roles.Clear();
AddRole(live, roleA).Position = 1;
live.AdjustGold(7);
firstLogin.Persist();
var secondLogin = Login();
Check("re-login on the same server restores the open run field for field",
    startedFresh && live.Phase == GridFightSessionPhase.Preparing && secondLogin.Session is { } relogged &&
    !ReferenceEquals(relogged, live) && SnapshotProbe.CompareSessions(live, relogged) == null);

var fighting = secondLogin.Session!;
var battlesBeforeRestart = fighting.BattlesFinished;
var nodeBeforeRestart = (fighting.ChapterId, fighting.SectionId);
var enteredBeforeRestart = fighting.EnterBattle();
secondLogin.Persist();
// Restart: flush to SQLite, forget every cached row and load the database again.
DatabaseHelper.Instance!.SaveUidData(PersistUid);
DatabaseHelper.UidInstanceMap.Clear();
new DatabaseHelper().Initialize(startSaveThread: false);
var afterRestart = Login();
var restartedUniqueId = afterRestart.Session?.UniqueId ?? 0;
Check("a restart reloads the run from the database and settles the battle left open like a quit",
    enteredBeforeRestart && afterRestart.Session is { } restarted &&
    restarted.Phase is not (GridFightSessionPhase.InBattle or GridFightSessionPhase.Preparing) &&
    restarted.UniqueId == fighting.UniqueId && restarted.BattlesFinished == battlesBeforeRestart + 1 &&
    (restarted.ChapterId, restarted.SectionId) != nodeBeforeRestart && restarted.KeepWinCount == 0);

var stored = DatabaseHelper.Instance.GetInstance<GridFightData>(PersistUid)!;
afterRestart.ClearSession();
var clearedOnLeave = stored.RunInstance == null;
afterRestart.Start(catalog.CurrentSeason, catalog.HighestDivision.ID, false);
Check("leaving a run clears its saved copy and the next run gets a new id",
    clearedOnLeave && stored.RunInstance != null && afterRestart.Session!.UniqueId > restartedUniqueId);

// A battle left open at 1 HP is lost at the restart; the run is over and is not reloaded half-settled.
var doomedRestart = afterRestart.Session!;
MoveToPenalizedNode(doomedRestart);
typeof(GridFightSession).GetProperty(nameof(GridFightSession.LineupHp))!.SetValue(doomedRestart, 1u);
// A rolled partner guard (effect 102001) would turn this loss into a clear; spend it so the partner roll cannot.
typeof(GridFightSession).GetField("_petPalGuardUsed",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
    .SetValue(doomedRestart, true);
var doomedRestartEntered = doomedRestart.EnterBattle();
afterRestart.Persist();
DatabaseHelper.Instance!.SaveUidData(PersistUid);
DatabaseHelper.UidInstanceMap.Clear();
new DatabaseHelper().Initialize(startSaveThread: false);
var afterDefeatRestart = Login();
Check("a restart that loses the open battle at 1 HP closes the run",
    doomedRestartEntered && afterDefeatRestart.Session == null && afterDefeatRestart.HasSettledSession &&
    DatabaseHelper.Instance.GetInstance<GridFightData>(PersistUid)!.RunInstance == null);
stored = DatabaseHelper.Instance.GetInstance<GridFightData>(PersistUid)!;
stored.RunInstance = "not a saved run";
var corruptLogin = Login();
Check("an unreadable saved run is dropped at login instead of failing it",
    corruptLogin.Session == null && stored.RunInstance == null);
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
try
{
    Directory.Delete(databaseScratch, true);
}
catch (IOException)
{
    Console.WriteLine($"Temporary database left at {databaseScratch}");
}

Console.WriteLine($"GridFight regression: {checks - failures.Count}/{checks} passed.");
return failures.Count == 0 ? 0 : 1;
