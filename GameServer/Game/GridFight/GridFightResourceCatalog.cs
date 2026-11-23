using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.GridFight;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightRouteNode(
    uint RouteId,
    uint ChapterId,
    uint SectionId,
    GridFightNodeTemplateExcel Template)
{
    public uint StageId => Template.StageID;
    public GridFightNodeTypeEnum NodeType => Template.NodeType;
    public uint BasicGoldReward => Template.BasicGoldRewardNum;
    public uint PenaltyBonusRuleId => Template.PenaltyBonusRuleID;
    public IReadOnlyList<uint> Parameters => Template.ParamList;
}

public sealed record GridFightMonsterPlan(
    GridFightMonsterExcel Monster,
    uint RoleStar,
    IReadOnlyList<GridFightRewardEntry> Drops)
{
    public GridFightMonsterPlan(GridFightMonsterExcel monster, uint roleStar) : this(monster, roleStar, [])
    {
    }
}

public sealed record GridFightMonsterWavePlan(
    uint WaveId,
    uint FormationWaveId,
    IReadOnlyList<GridFightMonsterPlan> Monsters);

public sealed record GridFightBattlePlan(
    uint CampId,
    uint EliteGroupId,
    uint PenaltyRuleId,
    uint DifficultyAdd,
    IReadOnlyList<GridFightMonsterWavePlan> Waves)
{
    public IReadOnlyList<GridFightMonsterPlan> Monsters => Waves.SelectMany(wave => wave.Monsters).ToList();
}

public sealed record GridFightEncounterOptionState(
    uint EliteBranchId,
    uint EncounterId,
    uint Quality,
    GridFightBattlePlan BattlePlan,
    IReadOnlyList<GridFightEncounterDrop>? Rewards = null);

/// <summary>One encounter (遭遇) reward as shown on the option card and paid on clear (see GridFightEncounterRewards).</summary>
public sealed record GridFightEncounterDrop(GridFightDropType Type, uint ItemId, uint Num);

public sealed record GridFightRoleSwitchGroup(
    uint BaseRoleId,
    uint SwitchRoleId,
    string Condition,
    IReadOnlyList<uint> ParamList,
    IReadOnlyList<uint> RoleIdList);

public sealed class GridFightResourceCatalog
{
    private const uint HighestDivisionStartingHpAffixId = 4020;
    internal const uint PostBattleHealingTalentId = 1081;
    private const int PostBattleHealingEffectIndex = 2;
    private const uint FinalBattleTurnHealingTalentId = 2011;
    private const int FinalBattleTurnThresholdEffectIndex = 1;
    private const int FinalBattleTurnHealingEffectIndex = 2;
    private const uint BossRoleStar = 2;
    private const uint BossFormationMaxTeammateCount = 5;
    // Per-run restriction seed for automatic role sources. These roles stay in the global
    // pool so GM/direct grants, portals and investment effects remain valid; shop, supply
    // and crystal/orb rolls may use them only after the current run has unlocked them.
    internal static readonly HashSet<uint> OpeningAndShopBannedRoleIds =
    [
        1003,  // Himeko
        1006,  // Silver Wolf
        1106,  // Pela
        1108,  // Sampo
        1201,  // Qingque
        1202,  // Tingyun
        1205,  // Blade
        1223,  // Moze
        1301,  // Gallagher
        12041, // Jing Yuan, 1-cost variant
        12042, // Jing Yuan, 3-cost variant
    ];

    // The seed lists season-1 role ids. Season 2 roles never use these ids, but its role orbs
    // (101000 + avatar id) do, so the restriction must not leak into other seasons through the orb path.
    internal static bool IsRestrictedRole(uint roleId) =>
        GridFightSeasonScope.CurrentSeason <= 1 && OpeningAndShopBannedRoleIds.Contains(roleId);

    private static bool IsRestrictedRoleAllowed(uint roleId, IReadOnlySet<uint>? temporarilyUnlockedRoleIds) =>
        !IsRestrictedRole(roleId) ||
        temporarilyUnlockedRoleIds?.Contains(roleId) == true;

    private static bool IsRestrictedRoleOrbAllowed(uint orbId, IReadOnlySet<uint>? temporarilyUnlockedRoleIds)
    {
        if (orbId is < 101000 or >= 102000) return true;
        return IsRestrictedRoleAllowed(orbId - 100000, temporarilyUnlockedRoleIds);
    }

    private readonly Random _random;

    public uint CurrentSeason { get; }
    public uint CurrentSubSeason { get; }
    public uint ActivityModuleId { get; }
    public uint ContentModuleId { get; }
    public GridFightDivisionInfoExcel HighestDivision { get; }
    public uint StandardRouteId { get; }
    public IReadOnlyList<GridFightRouteNode> StandardRoute { get; }
    public IReadOnlyList<GridFightRoleBasicInfoExcel> RolePool { get; }
    public IReadOnlyList<GridFightRoleSwitchGroup> RoleSwitchGroups { get; }
    public IReadOnlyList<GridFightCampExcel> CampPool { get; }
    public IReadOnlyList<GridFightCampExcel> RewardCampPool { get; }
    public IReadOnlyList<uint> PortalPool { get; }

    /// <summary>
    ///     4.6.51 client data bug: SpecificAvatar basic bonuses 1503 / 1505 have no BonusTypeParam, and the portal card
    ///     builds its preview role from that field (GridFightPortalData.BuildBonusData -> GridFightRoleConfig ctor NRE).
    ///     The exception aborts GridFightPortalBuffSelectPage setup, so the other cards cannot be picked either
    ///     (seen with 2205 千星商会概念股; 2203 and 2214 reference the same rows). Such portals are kept out of the offer.
    /// </summary>
    private static bool HasClientBrokenShowBonus(uint portalId)
    {
        if (!GameData.GridFightPortalBuffData.TryGetValue(portalId, out var portal)) return false;
        return portal.ShowBonusIDList
            .Where(GameData.GridFightCombinationBonusData.ContainsKey)
            .SelectMany(bonusId => GameData.GridFightCombinationBonusData[bonusId].CombinationBonusList)
            .Select(id => GameData.GridFightBasicBonusPoolV2Data.GetValueOrDefault(id))
            .Any(basic => basic is { BonusType: GridFightBonusTypeEnum.SpecificAvatar, BonusTypeParam: 0 });
    }
    public IReadOnlyList<uint> AugmentPool { get; }
    public IReadOnlyList<uint> BasicEquipmentPool { get; }
    public IReadOnlySet<uint> InitialExcludedRoleIds { get; }
    public uint ShopSize => GetUInt("GridFight_CardNumberPerRefresh");
    public uint RefreshCost => GetUInt("GridFight_LotteryRefreshGold");
    public uint BuyExpCost => GetUInt("GridFight_LevelUpGold");
    public uint BuyExpAmount => GetUInt("GridFight_LevelUpExp");
    public uint BattleWinExp => RequireUInt("GridFight_GainExpWhenWaveEnd");
    public uint InitialHp => GetUInt("GridFight_InitGlobalPlayerHP");
    public uint InitialPlayerLevel => RequireUInt("GridFight_LotteryShopStartLevel");
    // Season 2 adds a level-11 row that is only reachable through effects; the const caps normal play.
    public uint MaxPlayerLevel => GetUInt("GridFight_DefaultPlayerLevelMax") is var cap and > 0
        ? Math.Min(cap, GameData.GridFightPlayerLevelData.Keys.Max())
        : GameData.GridFightPlayerLevelData.Keys.Max();
    public uint InitialRoleCount => RequireUInt("GridFight_BaseSupplyRoleNum");
    public uint BenchSize => RequireUInt("GridFight_Bench_AvatarNum");
    public uint BenchOverflowSize => RequireUInt("GridFight_Bench_OverFlow_AvatarNum");
    public uint OffFieldInitialCount => RequireUInt("GridFight_Back_AvatarInitialNum");
    public uint DepositPerInterest => RequireUInt("GridFight_DepositPerInterest");
    public uint MaxInterestGold => RequireUInt("GridFight_GainInterestMax");
    public uint PostBattleHealingSourceId => PostBattleHealingTalentId;
    public uint FinalBattleTurnHealingSourceId => FinalBattleTurnHealingTalentId;
    public uint SeasonExpLimit => RequireUInt("GridFight_ExtraSeasonExp_Limit");
    public IReadOnlyList<uint> InitialEquipmentIds => GetUIntList("GridFight_PhainonEquipPreset");
    public uint InitialGold { get; }
    public uint TutorialCompletion { get; }

    // Per tutorial stage (GridFight_TutorialStageList) the final unlock and tutorial task, i.e. a finished stage.
    public IReadOnlyList<(uint DivisionId, uint Completion, uint TutorialTask)> CompletedTutorialStages { get; }

    // The capture-backed special mechanics (Cyrene, Silver Wolf LV999, season talents 2011/2061/4031,
    // the 4.5 shop rarity table) describe season 1 only; season 2 reuses some of those IDs for other effects.
    public bool IsSeasonOne => CurrentSeason == 1;

    // 4.6.51 dumps dropped GridFightRoleBasicInfo.RoleSavedValueList. Season 1 caches still carry it and
    // use it to select runtime-capable roles; without it the season-2 rules below apply.
    public bool HasRoleSavedValueLists { get; }

    // Season 2 cost-tier variants (Aha: 151112/151122/151132 after base 151102 in 4.6.5x, 151122/151132 after
    // base 151112 from 4.7.51) are upgrade products of the base role and are never sold or supplied directly,
    // like the season-1 Silver Wolf LV999 tiers.
    public IReadOnlySet<uint> UpgradeOnlyRoleIds { get; }

    // Cost tiers of Aha (星神★阿哈), read from the tables instead of fixed IDs: the star-less tiers of
    // GridFight_Aha_No_Star_Role_IDList in cost order (the first one is the role the shop sells) and the starred
    // tier of GridFight_SevenPriceRoleIdList. Empty / 0 when the season has no such role.
    public IReadOnlyList<uint> AhaGrowthRoleIds { get; }
    public uint AhaSevenCostRoleId { get; }

    private static IReadOnlySet<string> RoleModifierSavedValueKeys => GameData.GridFightRoleSkillModifyData
        .Select(row => row.MultipleValueKey)
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .ToHashSet(StringComparer.Ordinal);

    public GridFightResourceCatalog(Random? random = null)
    {
        _random = random ?? Random.Shared;
        CurrentSeason = RequireUInt("GridFight_Season_ID");
        CurrentSubSeason = RequireUInt("GridFight_Sub_Season_ID");
        ActivityModuleId = GameData.GridFightSeasonModuleData.Values
            .Single(row => row.SeasonID == CurrentSeason && row.SubSeasonID == CurrentSubSeason)
            .ActivityModuleID;
        ContentModuleId = ResolveContentModuleId();
        HighestDivision = GameData.GridFightDivisionInfoData.Values
            .Where(row => row.SeasonID == CurrentSeason)
            .OrderBy(row => row.Progress)
            .ThenBy(row => row.ID)
            .Last();
        StandardRouteId = ResolveStandardRouteId();
        StandardRoute = BuildRoute(StandardRouteId);

        var bannedRoles = GameData.GridFightModuleBanRoleData
            .Where(row => row.ModuleId == ContentModuleId)
            .Select(row => row.RoleId)
            .ToHashSet();
        var seasonRoles = GameData.GridFightRoleBasicInfoData.Values
            .Where(row => row.SeasonID == CurrentSeason)
            .ToList();
        HasRoleSavedValueLists = seasonRoles.Any(row => row.RoleSavedValueList.Count > 0);
        UpgradeOnlyRoleIds = ResolveUpgradeOnlyRoleIds(seasonRoles);
        var starlessTiers = GetUIntList("GridFight_Aha_No_Star_Role_IDList").ToHashSet();
        AhaGrowthRoleIds = seasonRoles
            .Where(row => starlessTiers.Contains(row.ID))
            .OrderBy(row => row.Rarity)
            .ThenBy(row => row.ID)
            .Select(row => row.ID)
            .ToList();
        var tierAvatars = seasonRoles.Where(row => starlessTiers.Contains(row.ID)).Select(row => row.AvatarID).ToHashSet();
        var sevenPriceRoles = GetUIntList("GridFight_SevenPriceRoleIdList").ToHashSet();
        AhaSevenCostRoleId = seasonRoles
            .Where(row => sevenPriceRoles.Contains(row.ID) && tierAvatars.Contains(row.AvatarID))
            .Select(row => row.ID)
            .Order()
            .FirstOrDefault();
        InitialExcludedRoleIds = GetUIntList("GridFight_InitialExceptRoleIdList").ToHashSet();
        RolePool = seasonRoles
            .Where(row => row.IsInPool && !bannedRoles.Contains(row.ID))
            .Where(IsRuntimeShopRole)
            // Season 2: GridFight_InitialExceptRoleIdList seeds the client's GridFightTeam._ExceptedRoleIDs. Tenglong
            // (丹恒•腾荒, 141402) only exists as the Dan Heng trait (双龙吟) summon, so it is never sold or handed out
            // (e.g. by the Aha good 我5费呢？！).
            .Where(row => IsSeasonOne || !InitialExcludedRoleIds.Contains(row.ID))
            .OrderBy(row => row.ID)
            .ToList();
        RoleSwitchGroups = GameData.GridFightRoleSwitchConfigData.Values
            .GroupBy(row => row.BaseRoleID)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var rows = group.OrderBy(row => row.RoleID).ToList();
                var roleIds = rows.Select(row => row.RoleID)
                    .Prepend(group.Key)
                    .Distinct()
                    .ToList();
                var first = rows[0];
                return new GridFightRoleSwitchGroup(
                    group.Key,
                    first.RoleID,
                    first.Condition,
                    first.ParamList.ToList(),
                    roleIds);
            })
            .ToList();
        // 4.6.51 removed GridFightCamp.SeasonID: camps without it are shared by every season.
        CampPool = GameData.GridFightCampData.Values
            .Where(row => GridFightSeasonScope.IsCampInSeason(row, CurrentSeason) &&
                          row.IfRandomEnabled != 0 && row.MonsterList.Count > 0)
            .Where(row => row.MonsterList.All(GameData.GridFightMonsterData.ContainsKey))
            .Where(row => row.MonsterList.Any(id => GameData.GridFightMonsterData[id].MonsterTier >= 5))
            .OrderBy(row => row.ID)
            .ToList();
        var rewardCampCodes = StandardRoute
            .Where(node => node.NodeType == GridFightNodeTypeEnum.Monster)
            .SelectMany(node => node.Parameters.Take(1))
            .Select(parameter => parameter / 100)
            .Where(code => code != 0)
            .ToHashSet();
        RewardCampPool = GameData.GridFightCampData.Values
            .Where(row => GridFightSeasonScope.IsCampInSeason(row, CurrentSeason) && row.IfRandomEnabled == 0)
            .Where(row => rewardCampCodes.Contains(row.InitialRandomCode))
            .Where(row => row.MonsterList.Count > 0 && row.MonsterList.All(GameData.GridFightMonsterData.ContainsKey))
            .OrderBy(row => row.ID)
            .ToList();

        var bannedPortals = GameData.GridFightModuleBanPortalData
            .Where(row => row.ModuleId == ContentModuleId)
            .Select(row => row.BanPortalId)
            .ToHashSet();
        PortalPool = GameData.GridFightSeasonPortalData.GetValueOrDefault(CurrentSeason, [])
            .Where(id => !bannedPortals.Contains(id))
            .Where(id => !HasClientBrokenShowBonus(id))
            .Distinct()
            .Order()
            .ToList();

        var bannedAugments = GameData.GridFightModuleBanAugmentData
            .Where(row => row.ModuleId == ContentModuleId)
            .Select(row => row.BanAugmentId)
            .ToHashSet();
        AugmentPool = GameData.GridFightSeasonAugmentData.GetValueOrDefault(CurrentSeason, [])
            .Where(id => !bannedAugments.Contains(id))
            .Distinct()
            .Order()
            .ToList();
        BasicEquipmentPool = SeasonEquipment
            .Where(row => row.EquipCategory == GridFightEquipCategoryEnum.Basic)
            .Select(row => row.ID)
            .Distinct()
            .Order()
            .ToList();

        InitialGold = ResolveInitialGold();
        TutorialCompletion = GameData.GridFightTutorialStageNodeData.Max(row => row.Unlock);
        var tutorialStageIds = GetUIntList("GridFight_TutorialStageList");
        CompletedTutorialStages = (tutorialStageIds.Count > 0 ? tutorialStageIds : GameData.GridFightTutorialStageData.Keys.Order().ToList())
            .Select(divisionId => (DivisionId: divisionId,
                Nodes: GameData.GridFightTutorialStageNodeData.Where(row => row.DivisionID == divisionId).ToList()))
            .Where(stage => stage.Nodes.Count > 0)
            .Select(stage => (stage.DivisionId, stage.Nodes.Max(row => row.Unlock), stage.Nodes.Max(row => row.UnlockTutorialTask)))
            .ToList();
    }

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (HighestDivision.Progress == 0) errors.Add("highest division progress is zero");
        if (SeasonExpLimit == 0) errors.Add("season exp limit is zero");
        if (Enumerable.Range(0, checked((int)InitialHp + 1)).Any(hp => FindSettleRank((uint)hp) == null))
            errors.Add("settle rank table does not cover the lineup HP range");
        if (InitialEquipmentIds.Count == 0) errors.Add("initial equipment list is empty");
        if (InitialEquipmentIds.Any(id => !GameData.GridFightEquipmentData.ContainsKey(id)))
            errors.Add("initial equipment list references missing equipment");
        if (RolePool.Count == 0) errors.Add("role pool is empty");
        if (RolePool.Any(row => !IsRuntimeShopRole(row)))
            errors.Add("role pool contains an unusable runtime role");
        if (RolePool.Any(row => row.SeasonID != CurrentSeason))
            errors.Add("role pool mixes seasons");
        if (RolePool.Any(row => !GameData.GridFightRoleStarData.ContainsKey(row.ID << 4 | 1u)))
            errors.Add("role pool contains a role without one-star data");
        if (GameData.GridFightFrontSkillData.Count == 0)
            errors.Add("grid fight front skill data is empty");
        // Cyrene's skill modifiers are keyed by the season-1 RoleSavedValueList column.
        if (HasRoleSavedValueLists && RoleModifierSavedValueKeys.Count == 0)
            errors.Add("grid fight role modifier saved value keys are empty");
        foreach (var key in HasRoleSavedValueLists ? RoleModifierSavedValueKeys : new HashSet<string>())
        {
            var owners = GameData.GridFightRoleBasicInfoData.Values
                .Where(role => role.RoleSavedValueList.Contains(key, StringComparer.Ordinal))
                .ToList();
            if (owners.Count != 1)
            {
                errors.Add($"grid fight role modifier key {key} has {owners.Count} owners");
                continue;
            }
            foreach (var roleStar in GameData.GridFightRoleStarData.Values.Where(row => row.ID == owners[0].ID))
                if (!TryResolveRoleSavedValueLayer(roleStar, out _))
                    errors.Add($"grid fight role {roleStar.ID} star {roleStar.Star} has no saved value layer");
        }
        if (RolePool.Count(row =>
                !InitialExcludedRoleIds.Contains(row.ID) &&
                !IsRestrictedRole(row.ID)) < InitialRoleCount)
            errors.Add("initial role pool is too small");
        if (CampPool.Count == 0) errors.Add("monster camp pool is empty");
        if (CampPool.Any(camp => GetCampMonsters(camp.ID, monster => monster.MonsterTier == 2).Count == 0))
            errors.Add("monster camp pool contains a camp without normal monsters");
        if (RewardCampPool.Count == 0) errors.Add("reward monster camp pool is empty");
        ValidateRewardRules(errors);
        if (PortalPool.Count == 0) errors.Add("portal pool is empty");
        if (AugmentPool.Count == 0) errors.Add("augment pool is empty");
        foreach (var chapterId in StandardRoute.Select(node => node.ChapterId).Distinct())
            if (!Enum.GetValues<GridFightAugmentQualityEnum>()
                    .Where(quality => quality != GridFightAugmentQualityEnum.None)
                    .Any(quality => GetInvestmentCandidates(chapterId, quality).Count >= 3))
                errors.Add($"chapter {chapterId} has no augment quality pool with three entries");
        if (BasicEquipmentPool.Count == 0) errors.Add("basic equipment pool is empty");
        if (RolePool.Count < 5) errors.Add("supply role pool has fewer than five entries");
        if (StandardRoute.Count < 2) errors.Add("standard route has fewer than two nodes");
        if (!StandardRoute.Any(node => node.ChapterId == 1 && node.SectionId == 1))
            errors.Add("standard route has no 1-1 node");
        if (!StandardRoute.Any(node => node.ChapterId == 1 && node.SectionId == 2))
            errors.Add("standard route has no 1-2 node");
        if (StandardRoute.Any(node => node.StageId == 0)) errors.Add("standard route has missing stage ids");
        if (StandardRoute.Any(node => !GameData.StageConfigData.ContainsKey((int)node.StageId)))
            errors.Add("standard route references missing stage configs");
        if (GameData.GridFightPlayerLevelData.Count == 0) errors.Add("player level table is empty");
        if (GameData.GridFightShopPriceData.Count == 0) errors.Add("shop price table is empty");
        if (DepositPerInterest == 0) errors.Add("interest deposit step is zero");
        if (GameData.GridFightVictoryBonusData.Count == 0 || !GameData.GridFightVictoryBonusData.ContainsKey(0))
            errors.Add("victory bonus table has no zero-win reward");
        if (!HasEffectParameter(GameData.GridFightAffixConfigData.GetValueOrDefault(HighestDivisionStartingHpAffixId), 0))
            errors.Add("highest-division starting HP affix is incomplete");
        if (!HasEffectParameter(GameData.GridFightTalentData.GetValueOrDefault(PostBattleHealingTalentId),
                PostBattleHealingEffectIndex))
            errors.Add("post-battle healing talent is incomplete");
        if (IsSeasonOne && !HasFinalBattleTurnHealing())
            errors.Add("final-battle turn healing talent is incomplete");
        if (!GameData.GridFightDivisionStageData.ContainsKey(HighestDivision.ID))
            errors.Add("highest division has no stage difficulty row");
        if (Enumerable.Range(1801, 19).Any(id => !GameData.GridFightEliteGroupData.ContainsKey((uint)id)))
            errors.Add("GridFight progression elite groups are incomplete");
        if (GameData.GridFightDivisionStageData.TryGetValue(HighestDivision.ID, out var divisionStage))
        {
            var baseDifficulty = divisionStage.EnemyDifficultyLevel;
            if (StandardRoute.Select(node => node.ChapterId).Distinct().Any(chapter =>
                    !GameData.GridFightEnemyDifficultyLvData.TryGetValue(chapter, out var rows) ||
                    !rows.ContainsKey(baseDifficulty)))
                errors.Add("GridFight enemy difficulty table has no highest-division baseline");
        }
        if (StandardRoute.Any(node => !GameData.GridFightStageLevelValueData.ContainsKey(node.StageId)))
            errors.Add("standard route references missing GridFight stage level values");
        var requiredFormationSizes = GameData.GridFightRewardRulesConfig.CombatNodeProfiles.Values
            .SelectMany(profile => profile.Waves)
            .Select(wave => wave.MaxActiveMonsterCount)
            .Append(GameData.GridFightRewardRulesConfig.EncounterBattleProfile.MaxActiveMonsterCount)
            .Append(BossFormationMaxTeammateCount)
            .Distinct();
        if (requiredFormationSizes.Any(maxTeammateCount => FindFormationWave(maxTeammateCount) == null))
            errors.Add("GridFight formation wave table is incomplete");
        ValidateCombatRules(errors);
        foreach (var node in StandardRoute.Where(node => node.NodeType == GridFightNodeTypeEnum.EliteBranch))
        {
            var group = node.Parameters.FirstOrDefault();
            if (GameData.GridFightBinaryNodeRuleData.Values.Count(rule => rule.ID / 100 == group) != 4)
                errors.Add($"GridFight encounter group {group} is incomplete");
            if (Enumerable.Range(1, 4).Any(quality =>
                    !GameData.GridFightPenaltyRuleData.ContainsKey(91000u + node.ChapterId * 100 + 10u + (uint)quality)))
                errors.Add($"GridFight encounter penalties for chapter {node.ChapterId} are incomplete");
        }
        if (!GameData.GridFightCampData.Values.Any(row => GridFightSeasonScope.IsCampInSeason(row, CurrentSeason)))
            errors.Add("current season has no monster camp");
        if (IsSeasonOne)
            ValidateSeasonOneSpecialFeatures(errors);
        return errors;
    }

    private static void ValidateSeasonOneSpecialFeatures(List<string> errors)
    {
        if (!GameData.GridFightRoleStarData.ContainsKey(
                GridFightSpecialFeatures.CyreneRoleId << 4 | 3) ||
            !GameData.GridFightRoleStarData.ContainsKey(
                GridFightSpecialFeatures.SilverWolfFiveCostRoleId << 4 | 3))
            errors.Add("GridFight special roles have no three-star resource");
        if (!GridFightSpecialFeatures.TryResolveSilverWolfEquipmentId(out _))
            errors.Add("Silver Wolf hack equipment is missing");
        if (GridFightSpecialFeatures.HackOrbIds.Any(id =>
                !GridFightSpecialFeatures.TryResolveOrb(id, out _)))
            errors.Add("GridFight hack orb rewards are incomplete");
        if (!GridFightSpecialFeatures.TryResolveBonus(GridFightSpecialFeatures.CyreneWealthBonusId, out _) ||
            !GridFightSpecialFeatures.TryResolveBonus(GridFightSpecialFeatures.CyreneInsigniaBonusId, out _) ||
            !GridFightSpecialFeatures.TryResolveBonus(GridFightSpecialFeatures.CyreneHeroBonusId, out _))
            errors.Add("Cyrene special goods rewards are incomplete");
    }

    public IReadOnlyDictionary<string, uint> ResolveRoleSavedValues(uint roleId, uint star)
    {
        if (!GameData.GridFightRoleBasicInfoData.TryGetValue(roleId, out var role)) return new Dictionary<string, uint>();
        var values = role.RoleSavedValueList
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(key => key, _ => 0u, StringComparer.Ordinal);
        if (!GameData.GridFightRoleStarData.TryGetValue(roleId << 4 | star, out var roleStar) ||
            !TryResolveRoleSavedValueLayer(roleStar, out var layer))
            return values;

        var modifierKeys = RoleModifierSavedValueKeys;
        foreach (var key in role.RoleSavedValueList.Where(modifierKeys.Contains))
        {
            values[key] = layer;
            var companionIndex = role.RoleSavedValueList.IndexOf(key) + 1;
            if (companionIndex < role.RoleSavedValueList.Count)
                values[role.RoleSavedValueList[companionIndex]] = layer;
        }
        return values;
    }

    private static bool TryResolveRoleSavedValueLayer(GridFightRoleStarExcel roleStar, out uint layer)
    {
        layer = 0;
        var passiveIndex = roleStar.SkillOverrideSrc.IndexOf(0);
        if (passiveIndex < 0 || passiveIndex >= roleStar.SkillOverrideDest.Count ||
            !GameData.GridFightFrontSkillData.TryGetValue(roleStar.SkillOverrideDest[passiveIndex], out var skill) ||
            skill.ParamList.Count == 0)
            return false;
        var value = skill.ParamList[0].Value;
        if (!double.IsFinite(value) || value < 0 || value > uint.MaxValue || value != Math.Truncate(value))
            return false;
        layer = (uint)value;
        return true;
    }

    private void ValidateRewardRules(List<string> errors)
    {
        errors.AddRange(GridFightSpecialFeatures.ValidateConfiguredOrbRewards());
        var rules = GameData.GridFightRewardRulesConfig;
        var rewardParameters = StandardRoute
            .Where(node => node.NodeType == GridFightNodeTypeEnum.Monster)
            .Select(node => node.Parameters.FirstOrDefault())
            .Where(parameter => parameter != 0)
            .Distinct()
            .ToList();
        foreach (var parameter in rewardParameters)
        {
            if (!rules.NodeProfiles.TryGetValue(parameter, out var profile))
            {
                errors.Add($"GridFight reward node {parameter} has no reward profile");
                continue;
            }
            if (profile.MonsterTiers.Count == 0 || profile.MonsterTiers.Any(tier => tier == 0))
                errors.Add($"GridFight reward node {parameter} has invalid monster tiers");
            if (profile.MinimumOrbPerMonster == 0 ||
                (ulong)profile.MonsterTiers.Count * profile.MinimumOrbPerMonster > profile.OrbRollCount)
                errors.Add($"GridFight reward node {parameter} cannot give every monster a minimum orb");
            if (profile.OrbWeights.Count == 0 || profile.OrbWeights.Values.Any(weight => weight == 0))
                errors.Add($"GridFight reward node {parameter} has invalid orb weights");
            if (profile.OrbWeights.Keys.Any(orbId =>
                    !GameData.GridFightBasicOrbRewardsConfig.OrbRewards.ContainsKey(orbId)))
                errors.Add($"GridFight reward node {parameter} references an orb without an opening recipe");
            if (profile.FormationWaveId != 5 ||
                !GameData.GridFightFormationWaveData.ContainsKey(profile.FormationWaveId))
                errors.Add($"GridFight reward node {parameter} has invalid formation wave");
            if (profile.RoleStar != 1)
                errors.Add($"GridFight reward node {parameter} has invalid monster star");
            foreach (var camp in RewardCampPool)
                if (profile.MonsterTiers.Any(tier =>
                        GetCampMonsters(camp.ID, monster => monster.MonsterTier == tier).Count == 0))
                    errors.Add($"GridFight reward camp {camp.ID} cannot satisfy node {parameter} tiers");
        }

        var chapters = StandardRoute.Select(node => node.ChapterId).ToHashSet();
        foreach (var rule in rules.TalentRules.Where(rule => rule.AppliesToSeason(CurrentSeason)))
        {
            if (!GameData.GridFightTalentData.ContainsKey(rule.TalentId) &&
                !GameData.GridFightSeasonTalentData.ContainsKey(rule.TalentId))
                errors.Add($"GridFight talent reward references missing talent {rule.TalentId}");
            if (rule.EffectId == 0 || rule.ChapterIds.Count == 0 ||
                rule.ChapterIds.Any(chapterId => !chapters.Contains(chapterId)))
                errors.Add($"GridFight talent reward {rule.TalentId} has invalid trigger parameters");
            if (rule.OrbIds.Count == 0 || rule.OrbIds.Any(orbId =>
                    !GameData.GridFightBasicOrbRewardsConfig.OrbRewards.ContainsKey(orbId)))
                errors.Add($"GridFight talent reward {rule.TalentId} references an orb without an opening recipe");
        }
    }

    private void ValidateCombatRules(List<string> errors)
    {
        var rules = GameData.GridFightRewardRulesConfig;
        foreach (var parameter in StandardRoute
                     .Where(node => node.NodeType == GridFightNodeTypeEnum.CampMonster)
                     .Select(node => node.Parameters.FirstOrDefault())
                     .Distinct())
        {
            if (!rules.CombatNodeProfiles.TryGetValue(parameter, out var profile))
            {
                errors.Add($"GridFight combat node {parameter} has no battle profile");
                continue;
            }
            var totalCount = profile.Waves.Aggregate(0UL, (sum, wave) => sum + wave.MonsterCount);
            if (profile.Waves.Count == 0 || profile.Waves.Any(wave =>
                    wave.MonsterCount == 0 || wave.MaxActiveMonsterCount == 0))
                errors.Add($"GridFight combat node {parameter} has an invalid wave profile");
            if (profile.EliteMonsterCount > totalCount || profile.MaxRoleStar == 0)
                errors.Add($"GridFight combat node {parameter} has an invalid monster profile");
        }

        var bossNodes = StandardRoute
            .Where(node => node.NodeType == GridFightNodeTypeEnum.Boss)
            .ToList();
        if (bossNodes.GroupBy(node => node.ChapterId).Any(group => group.Count() != 1))
            errors.Add("GridFight route must contain one boss node per chapter");
        foreach (var node in bossNodes)
        {
            var parameter = node.Parameters.FirstOrDefault();
            if (!rules.BossNodeProfiles.TryGetValue(parameter, out var profile) || profile.MonsterTier == 0)
            {
                errors.Add($"GridFight boss node {parameter} has no valid battle profile");
                continue;
            }
            foreach (var camp in CampPool)
                if (GetCampMonsters(camp.ID, monster => monster.MonsterTier == profile.MonsterTier).Count == 0)
                    errors.Add(
                        $"GridFight camp {camp.ID} cannot satisfy boss node {parameter} tier {profile.MonsterTier}");
        }

        var encounter = rules.EncounterBattleProfile;
        if (encounter.MinimumMonsterCount == 0 ||
            encounter.MaximumMonsterCount < encounter.MinimumMonsterCount ||
            encounter.MaxActiveMonsterCount == 0 || encounter.MaxRoleStar == 0)
            errors.Add("GridFight encounter battle profile is invalid");
    }

    public GridFightRouteNode GetNode(uint chapterId, uint sectionId)
    {
        return StandardRoute.Single(node => node.ChapterId == chapterId && node.SectionId == sectionId);
    }

    public GridFightRouteNode? GetNextNode(uint chapterId, uint sectionId)
    {
        var index = StandardRoute.ToList().FindIndex(node =>
            node.ChapterId == chapterId && node.SectionId == sectionId);
        return index >= 0 && index + 1 < StandardRoute.Count ? StandardRoute[index + 1] : null;
    }

    public GridFightPlayerLevelExcel GetPlayerLevel(uint level)
    {
        return GameData.GridFightPlayerLevelData[level];
    }

    public uint GetEnemyDifficultyLevel(uint divisionId)
    {
        // S1 A8 Wealth Creator 50: GridFightDivisionStage 10950 (4.4+).
        // Keep the current S2 rank/route, but benchmark its highest rank's enemies against this row.
        if (CurrentSeason == 2 && divisionId == HighestDivision.ID) return 120;
        return GameData.GridFightDivisionStageData[divisionId].EnemyDifficultyLevel;
    }

    public uint GetBattleDivisionId(uint divisionId) =>
        CurrentSeason == 2 && divisionId == HighestDivision.ID ? 10950u : divisionId;

    public uint GetInitialLineupHp(uint divisionId)
    {
        if (divisionId != HighestDivision.ID) return InitialHp;
        var reduction = GetEffectParameter(
            GameData.GridFightAffixConfigData[HighestDivisionStartingHpAffixId].EffectParamList,
            0);
        return reduction >= InitialHp ? 0 : InitialHp - reduction;
    }

    public uint GetPostBattleHealing()
    {
        return GetEffectParameter(
            GameData.GridFightTalentData[PostBattleHealingTalentId].EffectParamList,
            PostBattleHealingEffectIndex);
    }

    public uint GetFinalBattleTurnHealing(uint roundCount)
    {
        // Season 2 reuses talent 2011 for a different effect (two parameters, no healing).
        if (!IsSeasonOne) return 0;
        var talent = GameData.GridFightSeasonTalentData[FinalBattleTurnHealingTalentId];
        var threshold = GetEffectParameter(talent.EffectParamList, FinalBattleTurnThresholdEffectIndex);
        return roundCount < threshold
            ? GetEffectParameter(talent.EffectParamList, FinalBattleTurnHealingEffectIndex)
            : 0;
    }

    public GridFightPenaltyRuleExcel GetPenaltyRule(uint penaltyRuleId)
    {
        if (!GameData.GridFightPenaltyRuleData.TryGetValue(penaltyRuleId, out var rule))
            throw new InvalidDataException($"Missing GridFight penalty rule: {penaltyRuleId}");
        return rule;
    }

    public uint CalculateInterest(uint balance)
    {
        return Math.Min(balance / DepositPerInterest, MaxInterestGold);
    }

    public uint GetVictoryGoldBonus(uint keepWinCount)
    {
        return GameData.GridFightVictoryBonusData.Values
            .Where(row => row.VictoryCount <= keepWinCount)
            .OrderBy(row => row.VictoryCount)
            .Last()
            .GoldBonus;
    }

    // Season 2 prices the seven-cost Aha tier (151132, Rarity 5) from the dedicated ShopPrice row 7.
    private static uint ResolvePriceRarity(GridFightRoleBasicInfoExcel role)
    {
        const uint sevenPriceRarity = 7;
        return GetUIntList("GridFight_SevenPriceRoleIdList").Contains(role.ID) &&
               GameData.GridFightShopPriceData.ContainsKey(sevenPriceRarity)
            ? sevenPriceRarity
            : role.Rarity;
    }

    public uint GetBuyPrice(uint roleId, uint star = 1)
    {
        var role = GameData.GridFightRoleBasicInfoData[roleId];
        var prices = GameData.GridFightShopPriceData[ResolvePriceRarity(role)].BuyGoldList;
        return prices[(int)Math.Clamp(star, 1u, (uint)prices.Count) - 1];
    }

    public uint GetSellPrice(uint roleId, uint star)
    {
        var role = GameData.GridFightRoleBasicInfoData[roleId];
        var prices = GameData.GridFightShopPriceData[ResolvePriceRarity(role)].SellGoldList;
        return prices[(int)Math.Clamp(star, 1u, (uint)prices.Count) - 1];
    }

    // 4.5 client shop rarity table. Keep the roll source and the UI source identical;
    // stale GridFight_CardWeight constants in older resource caches otherwise diverge from
    // the client formula (and also make portal 114 double the wrong base probability).
    private static readonly IReadOnlyDictionary<uint, uint[]> OfficialShopRarityWeights =
        new Dictionary<uint, uint[]>
        {
            [1] = [100, 0, 0, 0, 0],
            [2] = [100, 0, 0, 0, 0],
            [3] = [100, 0, 0, 0, 0],
            [4] = [65, 25, 10, 0, 0],
            [5] = [45, 33, 20, 2, 0],
            [6] = [30, 40, 25, 5, 0],
            [7] = [19, 30, 40, 10, 1],
            [8] = [18, 25, 32, 22, 3],
            [9] = [15, 20, 25, 30, 10],
            [10] = [5, 10, 20, 40, 25],
        };

    public IReadOnlyList<uint> GetShopRarityWeights(uint level)
    {
        // The hard-coded table is the 4.5 (season 1) client formula; season 2 ships its own weights.
        if (!IsSeasonOne)
            return GetPlayerLevel(Math.Clamp(level, 1u, GameData.GridFightPlayerLevelData.Keys.Max())).RarityWeights;
        var resolved = Math.Clamp(level, 1u, 10u);
        return OfficialShopRarityWeights.TryGetValue(resolved, out var weights)
            ? weights
            : GetPlayerLevel(level).RarityWeights;
    }

    public IReadOnlyList<GridFightRoleBasicInfoExcel> RollShop(
        uint level,
        uint silverWolfRoleId = 0,
        IReadOnlyList<uint>? rarityWeights = null,
        uint boostedTraitId = 0,
        double boostedTraitWeightMultiplier = 1.0,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null,
        IReadOnlySet<uint>? excludedRoleIds = null)
    {
        var count = checked((int)ShopSize);
        var weights = rarityWeights?.ToList() ?? GetShopRarityWeights(level).ToList();
        if (weights.Count == 0) weights = GetShopRarityWeights(level).ToList();

        bool IsAllowedSilverWolfRole(GridFightRoleBasicInfoExcel role) =>
            silverWolfRoleId == 0 ||
            !GridFightSpecialFeatures.IsSilverWolfLv999Role(role.ID) ||
            role.ID == silverWolfRoleId;

        var shopPool = RolePool
            .Where(role => IsRestrictedRoleAllowed(role.ID, temporarilyUnlockedRoleIds))
            .Where(role => excludedRoleIds?.Contains(role.ID) != true)
            .ToList();
        // LV999 variants may have no RoleSavedValueList and thus be absent from RolePool.
        if (silverWolfRoleId != 0 && excludedRoleIds?.Contains(silverWolfRoleId) != true &&
            !shopPool.Any(role => role.ID == silverWolfRoleId) &&
            GameData.GridFightRoleBasicInfoData.TryGetValue(silverWolfRoleId, out var silverWolf) &&
            silverWolf.IsInPool && silverWolf.SeasonID == CurrentSeason &&
            !GameData.GridFightModuleBanRoleData.Any(row =>
                row.ModuleId == ContentModuleId && row.RoleId == silverWolfRoleId))
            shopPool.Add(silverWolf);
        var allowedPool = shopPool.Where(IsAllowedSilverWolfRole).ToList();
        if (allowedPool.Count == 0) return [];

        var result = new List<GridFightRoleBasicInfoExcel>(count);
        for (var index = 0; index < count; index++)
        {
            var rarity = RollIndex(weights) + 1u;
            var candidates = allowedPool.Where(role => role.Rarity == rarity).ToList();
            if (candidates.Count == 0) candidates = allowedPool;

            // Concept-stock portals do not alter the rarity/cost distribution. They only
            // increase the relative weight of roles that belong to the selected trait.
            // Current packet sampling is best explained by a 1.5x same-rarity weight.
            if (boostedTraitId == 0 || boostedTraitWeightMultiplier <= 1.0)
            {
                result.Add(candidates[_random.Next(candidates.Count)]);
                continue;
            }

            var weighted = candidates
                .Select(role => (Role: role, Weight:
                    role.TraitList.Contains(boostedTraitId) ? boostedTraitWeightMultiplier : 1.0))
                .ToList();
            var totalWeight = weighted.Sum(entry => entry.Weight);
            var roll = _random.NextDouble() * totalWeight;
            var selected = weighted[^1].Role;
            foreach (var entry in weighted)
            {
                roll -= entry.Weight;
                if (roll > 0) continue;
                selected = entry.Role;
                break;
            }
            result.Add(selected);
        }
        return result;
    }

    public IReadOnlyList<GridFightRoleBasicInfoExcel> RollInitialRoles(int count, IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null)
    {
        var candidates = RolePool.Where(role =>
                !InitialExcludedRoleIds.Contains(role.ID) &&
                IsRestrictedRoleAllowed(role.ID, temporarilyUnlockedRoleIds))
            .ToList();
        if (count != 4) return TakeRandomDistinct(candidates, count);

        // Official ordinary opening supply: four one-star roles whose base costs sum to exactly 6.
        // Duplicate role IDs are allowed (confirmed by multiple official captures).
        // GridFightRoleBasicInfo.Rarity is the base cost tier used by one-star shop goods.
        const uint targetTotalCost = 6;
        if (candidates.Count == 0) return [];

        // Roll with replacement so duplicated opening roles remain possible, then condition on cost=6.
        // This avoids inventing an unobserved fixed probability between the two valid partitions
        // (1+1+1+3 and 1+1+2+2).
        for (var attempt = 0; attempt < 512; attempt++)
        {
            var rolled = new List<GridFightRoleBasicInfoExcel>(count);
            for (var index = 0; index < count; index++)
                rolled.Add(candidates[_random.Next(candidates.Count)]);
            if (rolled.Aggregate(0u, (sum, role) => sum + role.Rarity) == targetTotalCost)
                return rolled;
        }

        // Deterministic safety fallback. With four positive cost tiers summing to six, the only
        // possible sorted partitions are 1+1+1+3 and 1+1+2+2. Choose a valid partition, then
        // sample each tier independently (still allowing duplicate role IDs).
        var byCost = candidates
            .GroupBy(role => role.Rarity)
            .ToDictionary(group => group.Key, group => group.ToList());
        var patterns = new[]
            {
                new uint[] { 1, 1, 1, 3 },
                new uint[] { 1, 1, 2, 2 },
            }
            .Where(pattern => pattern.All(byCost.ContainsKey))
            .ToList();
        if (patterns.Count == 0)
            throw new InvalidDataException("GridFight opening supply cannot satisfy the official total-cost=6 constraint.");

        var selectedPattern = patterns[_random.Next(patterns.Count)].OrderBy(_ => _random.Next()).ToList();
        return selectedPattern
            .Select(costTier => byCost[costTier][_random.Next(byCost[costTier].Count)])
            .ToList();
    }

    /// <summary>
    /// Fills the non-guaranteed opening slots for concept-stock portals. Official captures
    /// do not enforce the ordinary opening total-cost=6 rule here; featured-role portals can override it.
    /// </summary>
    public IReadOnlyList<GridFightRoleBasicInfoExcel> RollConceptStockFillRoles(int count, IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null)
    {
        var candidates = RolePool.Where(role =>
                !InitialExcludedRoleIds.Contains(role.ID) &&
                IsRestrictedRoleAllowed(role.ID, temporarilyUnlockedRoleIds))
            .ToList();
        if (candidates.Count == 0 || count <= 0) return [];
        var result = new List<GridFightRoleBasicInfoExcel>(count);
        for (var index = 0; index < count; index++)
            result.Add(candidates[_random.Next(candidates.Count)]);
        return result;
    }

    /// <summary>
    /// Random roles of one base cost (Rarity), with replacement, for investment grants such as
    /// "获得4个随机3费角色". Uses the same season and restricted-role filters as the shop.
    /// </summary>
    public IReadOnlyList<GridFightRoleBasicInfoExcel> RollRolesByCost(
        uint cost,
        int count,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null)
    {
        var candidates = RolePool.Where(role =>
                role.Rarity == cost &&
                IsRestrictedRoleAllowed(role.ID, temporarilyUnlockedRoleIds))
            .ToList();
        if (candidates.Count == 0 || count <= 0) return [];
        var result = new List<GridFightRoleBasicInfoExcel>(count);
        for (var index = 0; index < count; index++)
            result.Add(candidates[_random.Next(candidates.Count)]);
        return result;
    }

    public int NextRandom(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

    /// <summary>Distinct current-season shop roles carrying a trait (e.g. "获得3个随机欢愉角色").</summary>
    public IReadOnlyList<GridFightRoleBasicInfoExcel> GetRolesByTrait(
        uint traitId,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null) =>
        RolePool.Where(role => role.TraitList.Contains(traitId) && IsRestrictedRoleAllowed(role.ID, temporarilyUnlockedRoleIds))
            .GroupBy(role => role.ID)
            .Select(group => group.First())
            .OrderBy(role => role.ID)
            .ToList();

    public IReadOnlyList<GridFightRoleBasicInfoExcel> RollRolesByTrait(
        uint traitId,
        int count,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null) =>
        TakeRandomDistinct(GetRolesByTrait(traitId, temporarilyUnlockedRoleIds).ToList(), count);

    public IReadOnlyList<GridFightRoleBasicInfoExcel> GetRolesByCost(
        uint cost,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null) =>
        RolePool.Where(role => role.Rarity == cost && IsRestrictedRoleAllowed(role.ID, temporarilyUnlockedRoleIds))
            .GroupBy(role => role.ID)
            .Select(group => group.First())
            .ToList();

    public uint RollMortenaxBladeTemporaryEquipment(uint roleId, string frontBackType)
    {
        if (!GameData.GridFightRoleRecommendEquipData.TryGetValue(roleId, out var rows)) return 0;
        var row = rows.FirstOrDefault(candidate =>
                      string.Equals(candidate.FrontBackType, frontBackType, StringComparison.OrdinalIgnoreCase)) ??
                  rows.FirstOrDefault();
        if (row == null) return 0;

        var recommended = row.FirstRecommendEquipList
            .Concat(row.SecondRecommendEquipList)
            .Distinct()
            .ToHashSet();

        static bool IsAdvanced(uint id) => id is >= 35030000 and < 35040000;

        // Prefer advanced equipment directly present in the character's recommendation table.
        var candidates = recommended
            .Where(IsAdvanced)
            .Where(GameData.GridFightEquipmentData.ContainsKey)
            .Order()
            .ToList();

        // When the recommendation table lists components, derive matching advanced equipment from recipes.
        if (candidates.Count == 0)
            candidates = GridFightSeasonScope.Crafts(CurrentSeason)
                .Where(recipe => IsAdvanced(recipe.CraftEquipID))
                .Where(recipe => recipe.CostEquipList.Count > 0 &&
                                 recipe.CostEquipList.All(recommended.Contains))
                .Select(recipe => recipe.CraftEquipID)
                .Where(GameData.GridFightEquipmentData.ContainsKey)
                .Distinct()
                .Order()
                .ToList();

        if (candidates.Count == 0)
            candidates = GridFightSeasonScope.Crafts(CurrentSeason)
                .Where(recipe => IsAdvanced(recipe.CraftEquipID))
                .Where(recipe => recipe.CostEquipList.Any(recommended.Contains))
                .Select(recipe => recipe.CraftEquipID)
                .Where(GameData.GridFightEquipmentData.ContainsKey)
                .Distinct()
                .Order()
                .ToList();

        // Capture-confirmed compatibility fallback for 4.4.5.
        if (candidates.Count == 0)
            candidates = new uint[] { 35030407, 35030102 }
                .Where(GameData.GridFightEquipmentData.ContainsKey)
                .ToList();

        return candidates.Count == 0 ? 0 : candidates[_random.Next(candidates.Count)];
    }

    public IReadOnlyList<uint> RollPortalOffer(int count)
    {
        return TakeRandomDistinct(PortalPool.ToList(), count);
    }

    public uint RollBlueSeaPortal()
    {
        var candidates = PortalPool
            .Where(id => id != GridFightSession.BlueSeaPortalId)
            .ToList();
        return candidates.Count == 0 ? 0u : candidates[_random.Next(candidates.Count)];
    }

    public GridFightAugmentQualityEnum RollInvestmentQuality(
        uint chapterId,
        int count,
        IEnumerable<uint>? exclude = null)
    {
        var excluded = exclude?.ToHashSet() ?? [];
        var qualities = Enum.GetValues<GridFightAugmentQualityEnum>()
            .Where(quality => quality != GridFightAugmentQualityEnum.None)
            .Where(quality => GetInvestmentCandidates(chapterId, quality).Count(id => !excluded.Contains(id)) >= count)
            .ToList();
        return qualities.Count == 0
            ? GridFightAugmentQualityEnum.None
            : qualities[_random.Next(qualities.Count)];
    }

    public IReadOnlyList<uint> RollInvestmentOffer(
        uint chapterId,
        GridFightAugmentQualityEnum quality,
        int count,
        IEnumerable<uint>? exclude = null)
    {
        var excluded = exclude?.ToHashSet() ?? [];
        var candidates = GetInvestmentCandidates(chapterId, quality)
            .Where(id => !excluded.Contains(id))
            .ToList();
        return TakeRandomDistinct(candidates, count);
    }

    private List<uint> GetInvestmentCandidates(uint chapterId, GridFightAugmentQualityEnum quality)
    {
        return AugmentPool
            .Where(id => GameData.GridFightAugmentData.TryGetValue(id, out var augment) &&
                         augment.Quality == quality &&
                         (augment.ChapterLimitList.Count == 0 || augment.ChapterLimitList.Contains(chapterId)))
            .ToList();
    }

    public IReadOnlyList<(uint RoleId, uint EquipmentId)> RollSupplyOffer(int count, IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null)
    {
        var candidates = RolePool.Where(role => IsRestrictedRoleAllowed(role.ID, temporarilyUnlockedRoleIds)).ToList();
        var roles = TakeRandomDistinct(candidates, count);
        return roles.Select(role =>
            (role.ID, BasicEquipmentPool[_random.Next(BasicEquipmentPool.Count)])).ToList();
    }

    public IReadOnlyList<uint> RollForgeEquipment(uint forgeItemId)
    {
        // 4.5 Secret Codex+ uses forge 99991 (four Emblem choices). Older Resource.bin
        // snapshots may lack only the forge row while still containing all 35051/35052 items.
        if (forgeItemId == GridFightSession.SecretCodexForgeItemId &&
            !GameData.GridFightForgeData.ContainsKey(forgeItemId))
        {
            var emblems = SeasonEquipment
                .Where(row => row.EquipCategory == GridFightEquipCategoryEnum.Emblem)
                .Select(row => row.ID)
                .Distinct()
                .ToList();
            return emblems.Count < 4 ? [] : TakeRandomDistinct(emblems, 4);
        }

        if (!GameData.GridFightForgeData.TryGetValue(forgeItemId, out var forge) ||
            forge.FuncType != GridFightForgeFuncTypeEnum.Equip)
            return [];
        var category = (GridFightEquipCategoryEnum)forge.ParamList.FirstOrDefault();
        var candidates = SeasonEquipment
            .Where(row => row.EquipCategory == category)
            .Select(row => row.ID)
            .Distinct()
            .ToList();
        if (candidates.Count < forge.EquipNum) return [];
        return TakeRandomDistinct(candidates, checked((int)forge.EquipNum));
    }

    public uint RollEquipment(GridFightEquipCategoryEnum category, uint excludedEquipmentId)
    {
        var candidates = SeasonEquipment
            .Where(equipment => equipment.EquipCategory == category && equipment.ID != excludedEquipmentId)
            .Select(equipment => equipment.ID)
            .Order()
            .ToList();
        return candidates.Count == 0 ? 0 : candidates[_random.Next(candidates.Count)];
    }

    public IReadOnlyList<uint> RollPepeEmblems(int count)
    {
        if (count <= 0) return [];
        var candidates = SeasonEquipment
            .Where(equipment => equipment.EquipCategory == GridFightEquipCategoryEnum.Emblem)
            .Where(equipment => equipment.EquipFunc is GridFightEquipFuncTypeEnum.OriginEmblem or
                GridFightEquipFuncTypeEnum.ClassEmblem)
            .Select(equipment => equipment.ID)
            .Distinct()
            .Order()
            .ToList();
        return TakeRandomDistinct(candidates, count);
    }

    public uint RollLaborDispatchEmblem()
    {
        var roleTraits = RolePool
            .Where(role => GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | 1u))
            .SelectMany(role => role.TraitList)
            .ToHashSet();
        var candidates = SeasonEquipment
            .Where(equipment => equipment.EquipCategory == GridFightEquipCategoryEnum.Emblem)
            .Where(equipment => equipment.EquipFunc is GridFightEquipFuncTypeEnum.OriginEmblem or
                GridFightEquipFuncTypeEnum.ClassEmblem)
            .Where(equipment => roleTraits.Contains(equipment.EquipFuncParamList.FirstOrDefault()))
            .Select(equipment => equipment.ID)
            .Distinct()
            .Order()
            .ToList();
        return candidates.Count == 0 ? 0 : candidates[_random.Next(candidates.Count)];
    }

    public uint RollRoleForTrait(uint traitId)
    {
        var candidates = RolePool
            .Where(role => role.TraitList.Contains(traitId))
            .Where(role => GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | 1u))
            .Select(role => role.ID)
            .Distinct()
            .Order()
            .ToList();
        return candidates.Count == 0 ? 0 : candidates[_random.Next(candidates.Count)];
    }

    public bool RollChance(double probability)
    {
        if (probability <= 0) return false;
        if (probability >= 1) return true;
        return _random.NextDouble() < probability;
    }

    public uint RollOne(IReadOnlyList<uint> values)
    {
        return values.Count == 0 ? 0 : values[_random.Next(values.Count)];
    }

    public IReadOnlyList<uint> RollRecommendedEquipment(uint roleId, string frontBackType, int count)
    {
        if (count <= 0 || !GameData.GridFightRoleRecommendEquipData.TryGetValue(roleId, out var rows)) return [];
        var row = rows.FirstOrDefault(candidate =>
                      string.Equals(candidate.FrontBackType, frontBackType, StringComparison.OrdinalIgnoreCase)) ??
                  rows.FirstOrDefault();
        if (row == null) return [];

        var firstCount = (count + 1) / 2;
        var selected = TakeRandomDistinct(row.FirstRecommendEquipList.Distinct().ToList(), firstCount).ToList();
        selected.AddRange(TakeRandomDistinct(
            row.SecondRecommendEquipList.Where(id => !selected.Contains(id)).Distinct().ToList(),
            count - selected.Count));
        if (selected.Count < count)
            selected.AddRange(TakeRandomDistinct(
                row.FirstRecommendEquipList.Concat(row.SecondRecommendEquipList)
                    .Where(id => !selected.Contains(id)).Distinct().ToList(),
                count - selected.Count));
        return TakeRandomDistinct(selected, selected.Count);
    }

    public uint GetInvestmentDifficultyAdd(uint divisionId, uint augmentId)
    {
        if (!GameData.GridFightDivisionInfoData.TryGetValue(divisionId, out var division) ||
            !GameData.GridFightAugmentData.TryGetValue(augmentId, out var augment) ||
            !GameData.GridFightAugmentMonsterData.TryGetValue(division.DivisionLevel, out var byQuality) ||
            !byQuality.TryGetValue(augment.Quality, out var difficulty))
            return 0;
        return difficulty.EnemyDiffLvAdd;
    }

    public IReadOnlyList<uint> RollCampIds(int count)
    {
        return TakeRandomDistinct(CampPool.ToList(), count).Select(camp => camp.ID).ToList();
    }

    public uint RollRewardCampId()
    {
        return RewardCampPool[_random.Next(RewardCampPool.Count)].ID;
    }

    /// <summary>
    ///     Season 2 runs carry one partner; the client game flow dereferences its first synced effect on entry and
    ///     throws without one. Season 1 has no partners. Partner 1 has no effects and is never rolled.
    /// </summary>
    public (uint PetPalId, uint EffectId)? RollPetPal()
    {
        if (IsSeasonOne) return null;
        var candidates = GameData.GridFightPetPalData.Values
            .Where(row => row.EffIDList.Count > 0)
            .OrderBy(row => row.ID)
            .ToList();
        if (candidates.Count == 0) return null;
        var petPal = candidates[_random.Next(candidates.Count)];
        return (petPal.ID, petPal.EffIDList[_random.Next(petPal.EffIDList.Count)]);
    }

    public GridFightMonsterExcel RollBossMonster(uint campId, GridFightRouteNode bossNode)
    {
        var parameter = bossNode.Parameters.FirstOrDefault();
        if (!GameData.GridFightRewardRulesConfig.BossNodeProfiles.TryGetValue(parameter, out var profile))
            throw new InvalidOperationException($"GridFight boss node {parameter} has no battle profile");
        var candidates = GameData.GridFightCampData[campId].MonsterList
            .Select(id => GameData.GridFightMonsterData[id])
            .Where(monster => monster.MonsterTier == profile.MonsterTier)
            .ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"GridFight camp {campId} has no tier {profile.MonsterTier} boss monster");
        return candidates[_random.Next(candidates.Count)];
    }

    public IReadOnlyList<uint> RollAffixIds(uint divisionId)
    {
        var counts = GameData.GridFightDivisionStageData[divisionId].AffixChooseNumList;
        var result = new List<uint>();
        for (var index = 0; index < counts.Count; index++)
        {
            var group = (uint)index + 1;
            var candidates = GameData.GridFightAffixConfigData.Values
                .Where(affix => affix.ID / 1000 == group)
                .OrderBy(affix => affix.ID)
                .ToList();
            result.AddRange(TakeRandomDistinct(candidates, checked((int)counts[index])).Select(affix => affix.ID));
        }
        return result;
    }

    public GridFightBattlePlan? RollBattlePlan(
        GridFightRouteNode node,
        IReadOnlyList<uint> activeCampIds,
        uint rewardCampId,
        IReadOnlyList<GridFightMonsterExcel> bosses,
        uint selectedPortalId = 0,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds = null)
    {
        if (node.NodeType is GridFightNodeTypeEnum.GridFightNodeNone or GridFightNodeTypeEnum.Supply)
            return null;

        var parameter = node.Parameters.FirstOrDefault();
        if (node.NodeType == GridFightNodeTypeEnum.Boss)
        {
            var bossIndex = checked((int)node.ChapterId - 1);
            if (bossIndex < 0 || bossIndex >= bosses.Count) return null;
            return BuildPlan(
                activeCampIds.ElementAtOrDefault(bossIndex),
                node,
                [BuildBossMonsterPlans(activeCampIds.ElementAtOrDefault(bossIndex), bosses[bossIndex])],
                [ResolveFormationWaveId(BossFormationMaxTeammateCount)]);
        }

        if (node.NodeType == GridFightNodeTypeEnum.Monster)
        {
            if (!GameData.GridFightRewardRulesConfig.NodeProfiles.TryGetValue(parameter, out var rewardProfile))
                return null;

            var rewardMonsters = selectedPortalId == 119
                ? BuildSevereOverheatRewardMonsters(parameter, rewardProfile, temporarilyUnlockedRoleIds)
                : RollRewardMonsters(rewardCampId, parameter, rewardProfile, temporarilyUnlockedRoleIds);
            return BuildPlan(
                rewardCampId,
                node,
                [rewardMonsters],
                [rewardProfile.FormationWaveId]);
        }

        var campId = activeCampIds[_random.Next(activeCampIds.Count)];
        var tier2 = GetCampMonsters(campId, monster => monster.MonsterTier == 2);
        var tier3 = GetCampMonsters(campId, monster => monster.MonsterTier == 3);
        if (tier2.Count == 0)
            tier2 = GetCampMonsters(campId, monster => monster.MonsterTier < 3);
        if (tier2.Count == 0) return null;

        if (node.NodeType == GridFightNodeTypeEnum.EliteBranch)
            return BuildEncounterBattlePlan(node, campId, tier2, tier3, 1);

        if (!GameData.GridFightRewardRulesConfig.CombatNodeProfiles.TryGetValue(parameter, out var combatProfile))
            return null;
        var totalCount = checked((int)combatProfile.Waves.Sum(wave => wave.MonsterCount));
        var monsters = combatProfile.EliteMonsterCount == 0
            ? RollEarlyMonsters(tier2, totalCount, combatProfile.MaxRoleStar)
            : RollMixedMonsters(
                tier2,
                tier3,
                totalCount,
                checked((int)combatProfile.EliteMonsterCount),
                combatProfile.MaxRoleStar);
        return BuildPlan(
            campId,
            node,
            SplitWaves(monsters, combatProfile.Waves),
            combatProfile.Waves.Select(wave => ResolveFormationWaveId(wave.MaxActiveMonsterCount)).ToList());
    }

    public IReadOnlyList<GridFightEncounterOptionState> RollEncounterOptions(
        GridFightRouteNode node,
        IReadOnlyList<uint> activeCampIds,
        uint divisionId)
    {
        var group = node.Parameters.FirstOrDefault();
        var ruleIds = GameData.GridFightBinaryNodeRuleData.Values
            .Where(rule => rule.ID / 100 == group)
            .OrderBy(rule => rule.Quality)
            .ToList();
        if (ruleIds.Count == 0)
            ruleIds = GameData.GridFightBinaryNodeRuleData.Values
                .Where(rule => rule.ID is >= 1 and <= 4)
                .OrderBy(rule => rule.Quality)
                .ToList();
        ruleIds = ruleIds.Where(rule => rule.Quality is >= 1 and <= 4).ToList();
        var rules = TakeRandomDistinct(ruleIds, 2).OrderBy(rule => rule.Quality).ToList();
        var result = new List<GridFightEncounterOptionState>(rules.Count);
        var campId = activeCampIds[_random.Next(activeCampIds.Count)];
        var tier2 = GetCampMonsters(campId, monster => monster.MonsterTier == 2);
        var tier3 = GetCampMonsters(campId, monster => monster.MonsterTier == 3);
        if (tier2.Count == 0) tier2 = GetCampMonsters(campId, monster => monster.MonsterTier < 3);
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            var plan = BuildEncounterBattlePlan(node, campId, tier2, tier3, rule.Quality);
            var penaltyRuleId = 91000u + node.ChapterId * 100 + 10 + rule.Quality;
            plan = plan with
            {
                PenaltyRuleId = penaltyRuleId,
                DifficultyAdd = GetEncounterDifficultyAdd(divisionId, rule.Quality),
            };
            result.Add(new GridFightEncounterOptionState(
                (uint)index + 1,
                group * 100 + rule.Quality,
                rule.Quality,
                plan));
        }
        return result;
    }

    public uint GetEncounterDifficultyAdd(uint divisionId, uint quality)
    {
        if (!GameData.GridFightDivisionStageData.TryGetValue(divisionId, out var stage) ||
            !GameData.GridFightBinaryDiffAddRuleData.TryGetValue(stage.BinaryNodeDiffAddRule, out var rules) ||
            !rules.TryGetValue(quality, out var rule))
            return 0;
        return rule.EnemyDifficultyAddValue;
    }

    private GridFightBattlePlan BuildEncounterBattlePlan(
        GridFightRouteNode node,
        uint campId,
        IReadOnlyList<GridFightMonsterExcel> tier2,
        IReadOnlyList<GridFightMonsterExcel> tier3,
        uint quality)
    {
        var profile = GameData.GridFightRewardRulesConfig.EncounterBattleProfile;
        var totalCount = _random.Next(
            checked((int)profile.MinimumMonsterCount),
            checked((int)profile.MaximumMonsterCount + 1));
        var eliteCount = tier3.Count == 0 ? 0 : Math.Clamp(2 + checked((int)quality - 1) / 2, 2, 3);
        return BuildPlan(campId, node,
            [RollMixedMonsters(tier2, tier3, totalCount, eliteCount, profile.MaxRoleStar)],
            [ResolveFormationWaveId(profile.MaxActiveMonsterCount)]);
    }

    private GridFightBattlePlan BuildPlan(
        uint campId,
        GridFightRouteNode node,
        IReadOnlyList<IReadOnlyList<GridFightMonsterPlan>> waves,
        IReadOnlyList<uint> formationWaveIds)
    {
        var result = waves.Select((monsters, index) => new GridFightMonsterWavePlan(
            (uint)index + 1,
            formationWaveIds[Math.Min(index, formationWaveIds.Count - 1)],
            monsters)).ToList();
        return new GridFightBattlePlan(
            campId,
            ResolveEliteGroupId(node),
            node.PenaltyBonusRuleId,
            0,
            result);
    }

    private uint ResolveEliteGroupId(GridFightRouteNode node)
    {
        if (node.NodeType == GridFightNodeTypeEnum.Monster)
        {
            var parameter = node.Parameters.FirstOrDefault();
            return parameter <= 901 ? 1816u : 1815u + parameter - 900;
        }
        var combatNodes = StandardRoute.Where(candidate => candidate.NodeType is
            GridFightNodeTypeEnum.CampMonster or GridFightNodeTypeEnum.EliteBranch or GridFightNodeTypeEnum.Boss).ToList();
        var index = combatNodes.FindIndex(candidate =>
            candidate.ChapterId == node.ChapterId && candidate.SectionId == node.SectionId);
        return index < 0 ? 0 : 1801u + (uint)index;
    }

    private static uint ResolveFormationWaveId(uint maxTeammateCount)
    {
        return FindFormationWave(maxTeammateCount)?.ID ??
               throw new InvalidDataException($"Missing GridFight formation wave for {maxTeammateCount} monsters");
    }

    // 4.6.51 added a second MaxTeammateCount=5 row without an ID (0). Battle packets keep using the
    // numbered wave, so prefer ID == MaxTeammateCount and only fall back to the unnumbered row.
    private static GridFightFormationWaveExcel? FindFormationWave(uint maxTeammateCount)
    {
        return GameData.GridFightFormationWaveData.Values
            .Where(row => row.MaxTeammateCount == maxTeammateCount)
            .OrderBy(row => row.ID == maxTeammateCount ? 0 : row.ID == 0 ? 2 : 1)
            .ThenBy(row => row.ID)
            .FirstOrDefault();
    }

    private IEnumerable<GridFightEquipmentExcel> SeasonEquipment => GridFightSeasonScope.Equipment(CurrentSeason);

    private bool IsRuntimeShopRole(GridFightRoleBasicInfoExcel role)
    {
        if (role.IsFakeAvatar || UpgradeOnlyRoleIds.Contains(role.ID)) return false;
        // Season 1: roles without saved values (Trailblazer switch forms, Silver Wolf LV999 tiers) are
        // granted by their own mechanics only.
        if (HasRoleSavedValueLists)
            return role.AvatarID is >= 1000 and < 2000 && role.RoleSavedValueList.Count > 0;
        return role.AvatarID is >= 1000 and < 2000 or >= 8000 and < 9000 &&
               !GridFightSpecialFeatures.IsSilverWolfLv999Role(role.ID);
    }

    private static IReadOnlySet<uint> ResolveUpgradeOnlyRoleIds(IReadOnlyList<GridFightRoleBasicInfoExcel> seasonRoles)
    {
        var tiered = GetUIntList("GridFight_Aha_No_Star_Role_IDList")
            .Concat(GetUIntList("GridFight_SevenPriceRoleIdList"))
            .ToHashSet();
        return seasonRoles
            .Where(role => tiered.Contains(role.ID))
            .GroupBy(role => role.AvatarID)
            .SelectMany(group => group.OrderBy(role => role.ID).Skip(1))
            .Select(role => role.ID)
            .ToHashSet();
    }

    private static IReadOnlyList<GridFightMonsterPlan> BuildBossMonsterPlans(
        uint campId,
        GridFightMonsterExcel boss)
    {
        var result = new List<GridFightMonsterPlan> { new(boss, BossRoleStar) };
        if (!GameData.MonsterConfigData.TryGetValue((int)boss.MonsterID, out var bossConfig)) return result;

        var componentByTemplate = GetCampMonsters(campId, monster => monster.MonsterTier == 0)
            .Select(monster => new
            {
                Monster = monster,
                Config = GameData.MonsterConfigData.GetValueOrDefault((int)monster.MonsterID),
            })
            .Where(entry => entry.Config != null)
            .GroupBy(entry => entry.Config!.MonsterTemplateID)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Monster);
        if (componentByTemplate.Count == 0) return result;

        var components = GameData.StageConfigData.Values
            .SelectMany(stage => stage.MonsterList)
            .Select(GetStageMonsterIds)
            .Where(ids => ids.Count(id => ResolveMonsterTemplateId(id) == bossConfig.MonsterTemplateID) == 1)
            .Select(ids => ids
                .Where(id => ResolveMonsterTemplateId(id) != bossConfig.MonsterTemplateID)
                .Select(id => componentByTemplate.GetValueOrDefault(ResolveMonsterTemplateId(id)))
                .ToList())
            .Where(monsters => monsters.Count > 0 && monsters.All(monster => monster != null))
            .Select(monsters => monsters.OfType<GridFightMonsterExcel>().ToList())
            .OrderByDescending(monsters => monsters.Count)
            .FirstOrDefault();
        if (components == null) return result;

        result.AddRange(components.Select(monster => new GridFightMonsterPlan(monster, BossRoleStar)));
        return result;
    }

    private static IReadOnlyList<int> GetStageMonsterIds(StageMonsterList monsters)
    {
        return new[] { monsters.Monster0, monsters.Monster1, monsters.Monster2, monsters.Monster3, monsters.Monster4 }
            .Where(id => id != 0)
            .ToList();
    }

    private static int ResolveMonsterTemplateId(int monsterId)
    {
        return GameData.MonsterConfigData.TryGetValue(monsterId, out var config)
            ? config.MonsterTemplateID
            : monsterId;
    }

    private static IReadOnlyList<IReadOnlyList<GridFightMonsterPlan>> SplitWaves(
        IReadOnlyList<GridFightMonsterPlan> monsters,
        IReadOnlyList<GridFightCombatWaveProfile> waveProfiles)
    {
        var result = new List<IReadOnlyList<GridFightMonsterPlan>>(waveProfiles.Count);
        var offset = 0;
        foreach (var profile in waveProfiles)
        {
            var count = checked((int)profile.MonsterCount);
            result.Add(monsters.Skip(offset).Take(count).ToList());
            offset += count;
        }
        return result;
    }

    private IReadOnlyList<GridFightMonsterPlan> RollEarlyMonsters(
        IReadOnlyList<GridFightMonsterExcel> pool,
        int count,
        uint maxStar)
    {
        var result = RollMonsters(pool, count, 1).ToList();
        var enhanced = maxStar >= 2 ? Math.Min(2, count) : 0;
        for (var index = 0; index < enhanced; index++)
            result[index] = result[index] with { RoleStar = index == 0 && maxStar >= 3 ? 3u : 2u };
        Shuffle(result);
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<GridFightRewardEntry>>? BuildOfficialRewardNodeDrops(
        uint rewardNodeParameter,
        int monsterCount)
    {
        IReadOnlyList<IReadOnlyList<GridFightRewardEntry>>? drops = rewardNodeParameter switch
        {
            // Capture-confirmed normal reward node 70000001 / parameter 900.
            900 =>
            [
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 102, 1),
                 new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 204, 1)],
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 203, 1)],
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 199, 1)],
            ],
            // Capture-confirmed normal reward node 70000002 / parameter 901.
            901 =>
            [
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 204, 1)],
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 203, 1)],
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 199, 1)],
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 199, 1)],
                [new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 103, 1)],
            ],
            _ => null,
        };
        return drops?.Count == monsterCount ? drops : null;
    }

    private IReadOnlyList<GridFightMonsterPlan> BuildSevereOverheatRewardMonsters(
        uint rewardNodeParameter,
        GridFightRewardNodeProfile profile,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds)
    {
        var monster = GetRequiredGridFightMonster(800205802);
        return rewardNodeParameter switch
        {
            // Official 119 capture: first reward node has two Super Trotters.
            900 =>
            [
                new GridFightMonsterPlan(monster, 1,
                [
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 199, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 204, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 200003, 1),
                ]),
                new GridFightMonsterPlan(monster, 1,
                [
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 102, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 203, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 200006, 1),
                ]),
            ],
            // Official 119 capture: second reward node has three Super Trotters.
            901 =>
            [
                new GridFightMonsterPlan(monster, 1,
                [
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 199, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 303, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 200006, 1),
                ]),
                new GridFightMonsterPlan(monster, 1,
                [
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 199, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 204, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 200019, 1),
                ]),
                new GridFightMonsterPlan(monster, 1,
                [
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 103, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 203, 1),
                    new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, 200002, 2),
                ]),
            ],
            // Later 119 reward nodes are not present in the supplied captures. Keep the
            // official semantic (all reward monsters become Super Trotters) while preserving
            // the existing node's base reward pool instead of inventing advanced rewards.
            _ => BuildSevereOverheatFallback(monster, profile, temporarilyUnlockedRoleIds),
        };
    }

    private static IReadOnlyList<GridFightMonsterPlan> BuildSevereOverheatFallback(
        GridFightMonsterExcel monster,
        GridFightRewardNodeProfile profile,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds)
    {
        var orbBag = profile.OrbWeights
            .Where(entry => IsRestrictedRoleOrbAllowed(entry.Key, temporarilyUnlockedRoleIds))
            .OrderBy(entry => entry.Key)
            .SelectMany(entry => Enumerable.Repeat(entry.Key, checked((int)entry.Value)))
            .ToList();
        if (orbBag.Count == 0) return [];

        var monsterCount = Math.Max(1, checked((int)Math.Ceiling(orbBag.Count / 2.0)));
        var drops = Enumerable.Range(0, monsterCount)
            .Select(_ => new List<GridFightRewardEntry>())
            .ToList();
        for (var index = 0; index < orbBag.Count; index++)
            drops[index % monsterCount].Add(new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, orbBag[index], 1));

        return drops.Select(monsterDrops =>
            new GridFightMonsterPlan(monster, profile.RoleStar, monsterDrops)).ToList();
    }

    private static GridFightMonsterExcel GetRequiredGridFightMonster(uint monsterId)
    {
        if (!GameData.GridFightMonsterData.TryGetValue(monsterId, out var monster))
            throw new KeyNotFoundException($"GridFight monster not found: {monsterId}");
        return monster;
    }

    private IReadOnlyList<GridFightMonsterPlan> RollRewardMonsters(
        uint campId,
        uint rewardNodeParameter,
        GridFightRewardNodeProfile profile,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds)
    {
        var monsters = new List<GridFightMonsterExcel>(profile.MonsterTiers.Count);
        foreach (var tier in profile.MonsterTiers)
        {
            var pool = GetCampMonsters(campId, monster => monster.MonsterTier == tier);
            if (pool.Count == 0) return [];
            monsters.Add(pool[_random.Next(pool.Count)]);
        }
        Shuffle(monsters);

        var officialDrops = BuildOfficialRewardNodeDrops(rewardNodeParameter, monsters.Count);
        if (officialDrops != null)
            return monsters.Select((monster, index) =>
                new GridFightMonsterPlan(monster, profile.RoleStar, officialDrops[index])).ToList();

        var drops = Enumerable.Range(0, monsters.Count)
            .Select(_ => new List<GridFightRewardEntry>())
            .ToList();
        var remaining = profile.OrbRollCount;
        for (var minimumIndex = 0u; minimumIndex < profile.MinimumOrbPerMonster; minimumIndex++)
        for (var monsterIndex = 0; monsterIndex < monsters.Count; monsterIndex++)
        {
            drops[monsterIndex].Add(new GridFightRewardEntry(
                GridFightBonusTypeEnum.Orb,
                RollWeightedOrb(profile.OrbWeights, temporarilyUnlockedRoleIds),
                1));
            remaining--;
        }
        while (remaining > 0)
        {
            var monsterIndex = _random.Next(monsters.Count);
            drops[monsterIndex].Add(new GridFightRewardEntry(
                GridFightBonusTypeEnum.Orb,
                RollWeightedOrb(profile.OrbWeights, temporarilyUnlockedRoleIds),
                1));
            remaining--;
        }
        return monsters.Select((monster, index) =>
            new GridFightMonsterPlan(monster, profile.RoleStar, drops[index])).ToList();
    }

    private uint RollWeightedOrb(
        IReadOnlyDictionary<uint, uint> weights,
        IReadOnlySet<uint>? temporarilyUnlockedRoleIds)
    {
        var allowed = weights
            .Where(entry => entry.Value > 0 && IsRestrictedRoleOrbAllowed(entry.Key, temporarilyUnlockedRoleIds))
            .OrderBy(entry => entry.Key)
            .ToList();
        if (allowed.Count == 0)
            throw new InvalidOperationException("GridFight orb pool contains only locked restricted role orbs");
        var total = allowed.Aggregate(0L, (sum, entry) => sum + entry.Value);
        var roll = _random.NextInt64(total);
        foreach (var (orbId, weight) in allowed)
        {
            if (roll < weight) return orbId;
            roll -= weight;
        }
        throw new InvalidOperationException("GridFight orb weight selection failed");
    }

    private IReadOnlyList<GridFightMonsterPlan> RollMixedMonsters(
        IReadOnlyList<GridFightMonsterExcel> tier2,
        IReadOnlyList<GridFightMonsterExcel> tier3,
        int totalCount,
        int eliteCount,
        uint maxStar)
    {
        eliteCount = Math.Min(eliteCount, totalCount);
        var result = RollMonsters(tier3.Count > 0 ? tier3 : tier2, eliteCount, Math.Min(2u, maxStar)).ToList();
        result.AddRange(RollMonsters(tier2, totalCount - eliteCount, 1));
        var enhancedCount = Math.Min(3, result.Count);
        for (var index = 0; index < enhancedCount; index++)
            result[index] = result[index] with { RoleStar = Math.Min(maxStar, (uint)(2 + index / 2)) };
        Shuffle(result);
        return result;
    }

    private IReadOnlyList<GridFightMonsterPlan> RollMonsters(
        IReadOnlyList<GridFightMonsterExcel> pool,
        int count,
        uint star)
    {
        return Enumerable.Range(0, count)
            .Select(_ => new GridFightMonsterPlan(pool[_random.Next(pool.Count)], star))
            .ToList();
    }

    private static List<GridFightMonsterExcel> GetCampMonsters(
        uint campId,
        Func<GridFightMonsterExcel, bool> predicate)
    {
        if (!GameData.GridFightCampData.TryGetValue(campId, out var camp)) return [];
        return camp.MonsterList
            .Select(id => GameData.GridFightMonsterData.GetValueOrDefault(id))
            .OfType<GridFightMonsterExcel>()
            .Where(predicate)
            .ToList();
    }

    private void Shuffle<T>(IList<T> values)
    {
        for (var index = values.Count - 1; index > 0; index--)
        {
            var other = _random.Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }

    private uint ResolveContentModuleId()
    {
        // The S2 dump still ships S1 ban/switch tables for module 7110501. Their largest ID is
        // not the active S2 module: client trait/role visibility must use the season module.
        if (CurrentSeason == 2) return ActivityModuleId;

        var moduleIds = GameData.GridFightModuleBanRoleData.Select(row => row.ModuleId)
            .Concat(GameData.GridFightModuleBanPortalData.Select(row => row.ModuleId))
            .Concat(GameData.GridFightModuleBanAugmentData.Select(row => row.ModuleId))
            .Distinct()
            .Order()
            .ToList();
        return moduleIds.Count > 0 ? moduleIds[^1] : ActivityModuleId;
    }

    private uint ResolveStandardRouteId()
    {
        var excluded = GetUIntList("GridFight_TutorialStageList")
            .Select(divisionId => divisionId * 100)
            .ToHashSet();
        excluded.UnionWith(GameData.GridFightTutorialStageData.Keys.Select(id => id * 100));
        excluded.Add(GetUInt("GridFight_FirstPlay_StageRouteID"));

        var candidates = GameData.GridFightStageRouteData
            .Where(pair => !excluded.Contains(pair.Key))
            .Where(pair => pair.Value.Values.All(row =>
                GameData.GridFightNodeTemplateData.ContainsKey(row.NodeTemplateID)))
            .ToList();
        // The client draws the route it is sent, so a season-1 route in a season-2 run shows the wrong stages.
        var seasonRoutes = candidates
            .Where(pair => GridFightSeasonScope.IsRouteInSeason(pair.Key, CurrentSeason))
            .ToList();
        return (seasonRoutes.Count > 0 ? seasonRoutes : candidates)
            .OrderBy(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key)
            .Last().Key;
    }

    private static IReadOnlyList<GridFightRouteNode> BuildRoute(uint routeId)
    {
        return GameData.GridFightStageRouteData[routeId].Values
            .OrderBy(row => row.ChapterID)
            .ThenBy(row => row.SectionID)
            .Select(row => new GridFightRouteNode(
                row.ID,
                row.ChapterID,
                row.SectionID,
                GameData.GridFightNodeTemplateData[row.NodeTemplateID]))
            .ToList();
    }

    private uint ResolveInitialGold()
    {
        var poolId = RequireUInt("GridFight_InitRandomBonusPool");
        if (!GameData.GridFightBonusPoolV2Data.TryGetValue(poolId, out var pool)) return 0;
        return pool.BonusList.Any(id => GameData.GridFightBasicBonusPoolV2Data.TryGetValue(id, out var bonus) &&
                                       bonus.BonusType == GridFightBonusTypeEnum.Gold)
            ? pool.TotalValue
            : 0;
    }

    private uint RollIndex(IReadOnlyList<uint> weights)
    {
        var total = weights.Aggregate(0UL, (value, weight) => value + weight);
        if (total == 0) return 0;
        var roll = (ulong)_random.NextInt64((long)total);
        for (var index = 0; index < weights.Count; index++)
        {
            if (roll < weights[index]) return (uint)index;
            roll -= weights[index];
        }
        return (uint)(weights.Count - 1);
    }

    private IReadOnlyList<T> TakeRandomDistinct<T>(List<T> candidates, int count)
    {
        var result = new List<T>(Math.Min(count, candidates.Count));
        while (result.Count < count && candidates.Count > 0)
        {
            var index = _random.Next(candidates.Count);
            result.Add(candidates[index]);
            candidates.RemoveAt(index);
        }
        return result;
    }

    private static uint RequireUInt(string key)
    {
        if (!GameData.GridFightConstValueData.TryGetValue(key, out var value))
            throw new InvalidDataException($"Missing GridFight constant: {key}");
        return value.AsUInt32();
    }

    private static uint GetUInt(string key)
    {
        return GameData.GridFightConstValueData.GetValueOrDefault(key)?.AsUInt32() ?? 0;
    }

    private static IReadOnlyList<uint> GetUIntList(string key)
    {
        return GameData.GridFightConstValueData.GetValueOrDefault(key)?.AsUInt32List() ?? [];
    }

    private static bool HasEffectParameter(GridFightAffixConfigExcel? row, int index)
    {
        return row?.EffectParamList.Count > index && row.EffectParamList[index].Value >= 0;
    }

    private static bool HasEffectParameter(GridFightTalentExcel? row, int index)
    {
        return row?.EffectParamList.Count > index && row.EffectParamList[index].Value >= 0;
    }

    private bool HasFinalBattleTurnHealing()
    {
        return GameData.GridFightSeasonTalentData.TryGetValue(FinalBattleTurnHealingTalentId, out var row) &&
               row.SeasonID == CurrentSeason &&
               row.EffectParamList.Count > FinalBattleTurnHealingEffectIndex &&
               row.EffectParamList[FinalBattleTurnThresholdEffectIndex].Value > 0 &&
               row.EffectParamList[FinalBattleTurnHealingEffectIndex].Value >= 0;
    }

    private static uint GetEffectParameter(IReadOnlyList<GridFightRatioValue> parameters, int index)
    {
        return checked((uint)Math.Floor(parameters[index].Value));
    }

    public uint GetSettleRank(uint lineupHp)
    {
        var row = FindSettleRank(lineupHp) ??
                  throw new InvalidDataException($"Missing GridFight settle rank for lineup HP {lineupHp}");
        return ToProtocolSettleRank(row);
    }

    public uint GetMinimumSettleRank()
    {
        var row = GameData.GridFightSettleRankData.Values.OrderBy(candidate => candidate.ID).FirstOrDefault() ??
                  throw new InvalidDataException("Missing GridFight settle rank table");
        return ToProtocolSettleRank(row);
    }

    private static GridFightSettleRankExcel? FindSettleRank(uint lineupHp)
    {
        return GameData.GridFightSettleRankData.Values
            .Where(row => lineupHp >= row.Rank_LeftInterval && lineupHp <= row.Rank_RightInterval)
            .OrderBy(row => row.ID)
            .FirstOrDefault();
    }

    private static uint ToProtocolSettleRank(GridFightSettleRankExcel row)
    {
        return row.ID == 0 ? 0 : row.ID - 1;
    }
}
