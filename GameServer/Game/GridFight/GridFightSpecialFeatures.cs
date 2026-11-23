using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

public enum GridFightRoleUnlockEffect
{
    CyrenePoemShop,
    SilverWolfTraitActivation,
    SilverWolfHeadPlayerPending,
    // Kept for source compatibility; new Head Player scheduling uses SilverWolfHeadPlayerPending.
    SilverWolfHackConsole,
}

public sealed record GridFightRewardEntry(
    GridFightBonusTypeEnum Type,
    uint ItemId,
    uint Count,
    uint RoleStar = 0);

public sealed class GridFightRewardApplication
{
    public bool Success { get; init; }
    public uint SourceUniqueId { get; init; }
    public uint SourceItemId { get; init; }
    public bool GoldChanged { get; set; }
    public bool PlayerLevelChanged { get; set; }
    public bool PlayerExpChanged { get; set; }
    public bool LineupHpChanged { get; set; }
    public bool RolePropertiesChanged { get; set; }
    public List<GridFightRewardEntry> Drops { get; } = [];
    public List<GridFightRoleAcquisitionResult> RoleAcquisitions { get; } = [];
    public List<GridFightEquipmentState> AddedEquipments { get; } = [];
    public List<GridFightConsumableChange> ConsumableChanges { get; } = [];
    public List<GridFightOrbState> AddedOrbs { get; } = [];
    public List<GridFightForgeItemState> AddedForgeItems { get; } = [];
}

public sealed record GridFightConsumableChange(
    GridFightConsumableState Consumable,
    bool IsNew,
    uint StackDelta);

public sealed class GridFightForgeItemState
{
    public uint UniqueId { get; init; }
    public uint ForgeItemId { get; init; }
    public uint Position { get; set; }
    public IReadOnlyList<uint> EquipmentChoices { get; init; } = [];
}

public sealed class GridFightHackConsoleState
{
    public uint GameItemUniqueId { get; init; }
    public uint RemainingGoldActions { get; set; } = GridFightSpecialFeatures.HackGoldActionCount;
    public uint RemainingHealActions { get; set; } = GridFightSpecialFeatures.HackHealActionCount;
    public uint RemainingOrbActions { get; set; } = GridFightSpecialFeatures.HackOrbActionCount;
    public uint EnemyHpPercent { get; set; } = GridFightSpecialFeatures.InitialHackEnemyHpPercent;
}

public sealed record GridFightSpecialGoodsPurchaseResult(
    uint GoodsIndex,
    uint SpecialGoodsId,
    GridFightRewardApplication Application);

public sealed record GridFightTraitEnhanceActivationResult(
    bool Success,
    uint FinishedQueuePosition,
    GridFightEquipmentState? Equipment)
{
    public static GridFightTraitEnhanceActivationResult Failed { get; } = new(false, 0, null);
}

public sealed record GridFightHackOptionResult(
    bool Success,
    uint OptionId,
    GridFightRewardApplication Application)
{
    public static GridFightHackOptionResult Failed { get; } =
        new(false, 0, new GridFightRewardApplication());
}

public static class GridFightSpecialFeatures
{
    public const uint CyreneRoleId = 1415;
    public const uint CyreneHealGoodsId = 201;
    public const uint CyreneWealthGoodsId = 202;
    public const uint CyreneInsigniaGoodsId = 203;
    public const uint CyreneCombatGoodsId = 204;
    public const uint CyreneHeroGoodsId = 205;
    public const uint CyreneHealAmount = 100;
    public const uint CyreneWealthBonusId = 11059;
    public const uint CyreneInsigniaBonusId = 11060;
    public const uint CyreneHeroBonusId = 11063;
    public const uint SilverWolfThreeCostRoleId = 15061;
    public const uint SilverWolfFourCostRoleId = 15062;
    public const uint SilverWolfFiveCostRoleId = 15063;
    public const uint SilverWolfTraitId = 3006;
    public const uint SilverWolfThreeCostTraitEffectId = 30061;
    public const uint SilverWolfFourCostTraitEffectId = 30062;
    public const uint SilverWolfFiveCostTraitEffectId = 30063;
    // Compatibility aliases kept for the already-implemented five-cost/three-star HackConsole.
    public const uint SilverWolfTraitEffectId = SilverWolfFiveCostTraitEffectId;
    public const uint SilverWolfUpgradeToFourCostOptionId = 8;
    public const uint SilverWolfUpgradeToFiveCostOptionId = 13;
    public const uint SilverWolfEnhanceOptionId = 25;
    public const uint SilverWolfHackEquipmentId = 352507;
    public const uint HeadPlayerEquipmentSource = 1;

    public static IReadOnlyList<uint> SilverWolfThreeCostEquipmentOptions { get; } = [9, 10, 11, 12];
    public static IReadOnlyList<uint> SilverWolfFourCostEquipmentOptions { get; } = [14, 15, 16, 17, 18];
    public static IReadOnlyList<uint> SilverWolfFiveCostEquipmentOptions { get; } = [19, 20, 21, 22, 23, 24];

    public static IReadOnlyDictionary<uint, uint> SilverWolfOptionEquipmentIds { get; } =
        new Dictionary<uint, uint>
        {
            [9] = 352301,   // 数据拷贝仪
            [10] = 352302,  // 病毒防火墙
            [11] = 352303,  // 破解芯片
            [12] = 352304,  // 扑满病毒
            [14] = 352401,  // 数据拷贝仪 Pro
            [15] = 352402,  // 欢愉卡带
            [16] = 352404,  // 星核猎手卡带
            [17] = 352405,  // 分身墨镜
            [18] = 352406,  // 扑满病毒 Pro
            [19] = 352502,  // 欢愉卡带 Max
            [20] = 352503,  // 星核猎手卡带 Max
            [21] = 352501,  // 数据拷贝仪 Max
            [22] = 352504,  // 分身墨镜 Max
            [23] = 352505,  // 病毒防火墙 Max
            [24] = 352506,  // 扑满病毒 Max
        };
    public const uint HackAddGoldOptionId = 1;
    public const uint HackFullHealOptionId = 2;
    public const uint HackGrantOrbOptionId = 3;
    public const uint HackGoldPerAction = 10;
    public const uint HackGoldActionCount = 99;
    public const uint HackHealActionCount = 1;
    public const uint HackOrbActionCount = 5;
    public const uint MinimumEnemyHpPercent = 1;
    public const uint MaximumEnemyHpPercent = 1000;
    public const uint InitialHackEnemyHpPercent = MinimumEnemyHpPercent;
    public const uint HackEquipmentSource = 1;
    public const string HackRankFiveEquipmentConstName = "GridFight_Hack_Rank5";

    public static IReadOnlyList<uint> CyreneSpecialGoodsIds { get; } =
        [CyreneHealGoodsId, CyreneWealthGoodsId, CyreneInsigniaGoodsId, CyreneCombatGoodsId, CyreneHeroGoodsId];

    public static IReadOnlyList<uint> HackOrbIds { get; } = [111079, 111080, 111081, 111082, 111083];

    public static bool IsSilverWolfLv999Role(uint roleId) =>
        roleId is SilverWolfThreeCostRoleId or SilverWolfFourCostRoleId or SilverWolfFiveCostRoleId;

    public static uint GetSilverWolfTraitEffectId(uint roleId) => roleId switch
    {
        SilverWolfThreeCostRoleId => SilverWolfThreeCostTraitEffectId,
        SilverWolfFourCostRoleId => SilverWolfFourCostTraitEffectId,
        SilverWolfFiveCostRoleId => SilverWolfFiveCostTraitEffectId,
        _ => 0,
    };

    public static uint GetSilverWolfCostTier(uint roleId) => roleId switch
    {
        SilverWolfThreeCostRoleId => 3,
        SilverWolfFourCostRoleId => 4,
        SilverWolfFiveCostRoleId => 5,
        _ => 0,
    };

    public static IReadOnlyList<uint> GetSilverWolfEquipmentOptionPool(uint roleId) => roleId switch
    {
        SilverWolfThreeCostRoleId => SilverWolfThreeCostEquipmentOptions,
        SilverWolfFourCostRoleId => SilverWolfFourCostEquipmentOptions,
        SilverWolfFiveCostRoleId => SilverWolfFiveCostEquipmentOptions,
        _ => [],
    };

    public static bool TryResolveSilverWolfOptionEquipmentId(uint optionId, out uint equipmentId) =>
        SilverWolfOptionEquipmentIds.TryGetValue(optionId, out equipmentId);

    public static bool TryResolveSilverWolfEquipmentId(out uint equipmentId)
    {
        equipmentId = 0;
        if (!GameData.GridFightConstValueData.TryGetValue(HackRankFiveEquipmentConstName, out var rankFiveEquipment) ||
            !rankFiveEquipment.AsUInt32List().Contains(SilverWolfHackEquipmentId) ||
            !GameData.GridFightEquipmentData.TryGetValue(SilverWolfHackEquipmentId, out var equipment) ||
            equipment.EquipCategory != GridFightEquipCategoryEnum.Hack ||
            !equipment.DressRuleParamList.Contains(SilverWolfFiveCostRoleId))
            return false;

        equipmentId = equipment.ID;
        return true;
    }

    /// <summary>
    /// Returns whether the server can safely materialize an orb instance. 4.4 Currency War
    /// added several reward orbs that can be present in the client/capture while an older
    /// server Resource.bin does not yet contain the corresponding GridFightOrb row.
    /// A configured reward therefore counts as a valid server-side definition as well.
    /// </summary>
    public static bool CanMaterializeOrb(uint orbItemId)
    {
        // These Currency War orb IDs are capture-confirmed in 4.4 but can be absent from an
        // older server Resource.bin, and GridFightBasicOrbRewards.json need not list them.
        if (orbItemId is 103 or 303 or 200002 or 200003 or 200004 or 200006 or 200019) return true;
        if (orbItemId is >= 101000 and < 102000 &&
            GameData.GridFightRoleBasicInfoData.ContainsKey(orbItemId - 100000)) return true;
        return GameData.GridFightOrbData.ContainsKey(orbItemId) ||
               GameData.GridFightBasicOrbRewardsConfig.OrbRewards.ContainsKey(orbItemId);
    }

    /// <summary>
    ///     Orbs whose BonusID has no row in any shipped bonus table: GridFightOrb 121006/121007/121008 -> bonus
    ///     21006-21008, the kill drops of the loot orb augments (爆晶矿). They open like the generic orb of the same
    ///     colour. The real contents are server-side; without the alias such an orb can be granted but every open fails
    ///     with "消息获取失败".
    /// </summary>
    private static readonly Dictionary<uint, uint> OrbRecipeAliases = new()
    {
        [121006] = 199, // white: 2 gold
        [121007] = 204, // blue
        [121008] = 305, // gold
    };

    public static bool TryResolveOrb(uint orbItemId, out IReadOnlyList<GridFightRewardEntry> rewards)
    {
        rewards = [];
        if (OrbRecipeAliases.TryGetValue(orbItemId, out var alias)) return TryResolveOrb(alias, out rewards);

        // Capture-confirmed 4.4 Currency War semantics. Put these before cached custom data:
        // older Resource.bin snapshots contain the pre-4.4 definition for 103 (350101 x1),
        // while the official 119 reward capture opens it as 350101 x2.
        if (TryResolveCapturedCurrencyWarOrb(orbItemId, out rewards)) return true;

        // Prefer explicit server-side compatibility definitions before GridFightOrbData lookup.
        // A newer client may know an orb that an older Resource.bin does not.
        if (GameData.GridFightBasicOrbRewardsConfig.OrbRewards.TryGetValue(orbItemId, out var configured))
            return TryResolveConfiguredOrb(configured, out rewards);

        // A cached Resource.bin can contain the 4.4 orb row while still missing (or carrying an
        // older form of) the bonus rows it references. Do not let a failed native bonus lookup
        // suppress the capture-backed compatibility fallback below.
        if (GameData.GridFightOrbData.TryGetValue(orbItemId, out var orb) &&
            TryResolveBonus(orb.BonusID, out rewards))
            return true;

        // 4.5 role orbs use 100000 + GridFight role id (e.g. 101005 Kafka, 101218 Jiaoqiu); from season 2 on
        // the suffix is the avatar id of the running season's role.
        if (orbItemId is >= 101000 and < 102000)
        {
            var roleId = GridFightSeasonScope.ResolveRoleId(orbItemId - 100000, GridFightSeasonScope.CurrentSeason);
            if (roleId != 0)
            {
                rewards = [new GridFightRewardEntry(GridFightBonusTypeEnum.SpecificAvatar, roleId, 1, 1)];
                return true;
            }
        }

        // 303 is present in normal 4.4 data, but keep a narrow compatibility fallback for an
        // older cache. The capture only proves that the opened result is an Emblem-class item,
        // so do not hard-code the single observed equipment ID.
        if (orbItemId == 303)
        {
            var result = new List<GridFightRewardEntry>();
            var emblems = GridFightSeasonScope.Equipment(GridFightSeasonScope.CurrentSeason)
                .Where(row => row.EquipCategory == GridFightEquipCategoryEnum.Emblem)
                .OrderBy(row => row.ID)
                .ToList();
            if (!AddRandomEquipment(emblems, 1, result)) return false;
            rewards = result;
            return true;
        }

        return false;
    }

    private static bool TryResolveCapturedCurrencyWarOrb(
        uint orbItemId,
        out IReadOnlyList<GridFightRewardEntry> rewards)
    {
        var result = new List<GridFightRewardEntry>();
        switch (orbItemId)
        {
            case 103:
                // Official 119 second reward node: 103 -> 350101 x2.
                if (!IsValidItemReward(350101))
                {
                    rewards = [];
                    return false;
                }
                result.Add(new GridFightRewardEntry(GridFightBonusTypeEnum.Item, 350101, 2));
                break;
            case 200002:
                if (!AddRandomAvatar(2, 1, 1, result))
                {
                    rewards = [];
                    return false;
                }
                break;
            case 200003:
                if (!AddRandomAvatar(3, 1, 1, result))
                {
                    rewards = [];
                    return false;
                }
                break;
            case 200004:
                if (!AddRandomAvatar(4, 1, 1, result))
                {
                    rewards = [];
                    return false;
                }
                break;
            case 200006:
                var basicEquipment = GridFightSeasonScope.Equipment(GridFightSeasonScope.CurrentSeason)
                    .Where(row => row.EquipCategory == GridFightEquipCategoryEnum.Basic)
                    .OrderBy(row => row.ID)
                    .ToList();
                if (!AddRandomEquipment(basicEquipment, 1, result))
                {
                    rewards = [];
                    return false;
                }
                break;
            case 200019:
                if (!AddRandomAvatar(1, 2, 1, result))
                {
                    rewards = [];
                    return false;
                }
                break;
            default:
                rewards = [];
                return false;
        }

        rewards = result;
        return true;
    }

    public static IReadOnlyList<string> ValidateConfiguredOrbRewards()
    {
        var errors = new List<string>();
        foreach (var (orbId, configured) in GameData.GridFightBasicOrbRewardsConfig.OrbRewards)
        {
            if (configured.OrbId != orbId)
                errors.Add($"GridFight orb reward {orbId} has mismatched orb id {configured.OrbId}");
            // Do not reject a configured orb solely because the server Resource.bin is older.
            // Config/Custom/GridFightBasicOrbRewards.json is also used as a forward-compatibility
            // definition for capture-confirmed Currency War orbs.
            if (configured.Rewards.Count == 0 || configured.Rewards.Values.Any(rewards => rewards.Count == 0))
                errors.Add($"GridFight orb reward {orbId} has an empty variant");
            foreach (var reward in configured.Rewards.Values.SelectMany(rewards => rewards))
                if (!CanResolveConfiguredReward(reward))
                    errors.Add($"GridFight orb reward {orbId} contains invalid {reward.BonusType} reward");
        }
        return errors;
    }

    public static bool TryResolveBonus(uint bonusId, out IReadOnlyList<GridFightRewardEntry> rewards)
    {
        var result = new List<GridFightRewardEntry>();
        var success = ResolveBonus(bonusId, 1, [], result);
        rewards = success ? result : [];
        return success;
    }

    private static bool ResolveBonus(
        uint bonusId,
        uint multiplier,
        HashSet<uint> resolving,
        List<GridFightRewardEntry> rewards)
    {
        if (!resolving.Add(bonusId)) return false;
        try
        {
            if (GameData.GridFightCombinationBonusData.TryGetValue(bonusId, out var combination))
            {
                if (combination.CombinationBonusList.Count != combination.BonusNumberList.Count) return false;
                for (var index = 0; index < combination.CombinationBonusList.Count; index++)
                {
                    var scaled = (ulong)multiplier * combination.BonusNumberList[index] / 10000;
                    if (scaled == 0 || scaled > uint.MaxValue ||
                        !ResolveBonus(combination.CombinationBonusList[index], (uint)scaled, resolving, rewards))
                        return false;
                }
                return true;
            }

            if (!GameData.GridFightBasicBonusPoolV2Data.TryGetValue(bonusId, out var basic)) return false;
            return TryMaterializeBasicBonus(basic, multiplier, rewards);
        }
        finally
        {
            resolving.Remove(bonusId);
        }
    }

    private static bool TryMaterializeBasicBonus(
        GridFightBasicBonusPoolV2Excel basic,
        uint multiplier,
        List<GridFightRewardEntry> result)
    {
        if (multiplier == 0) return false;
        var firstParam = basic.BonusTypeParamList.FirstOrDefault();
        if (firstParam == 0) firstParam = basic.BonusTypeParam;

        switch (basic.BonusType)
        {
            case GridFightBonusTypeEnum.Gold:
            case GridFightBonusTypeEnum.Exp:
                result.Add(new GridFightRewardEntry(basic.BonusType, 0, multiplier));
                return true;
            case GridFightBonusTypeEnum.SpecificAvatar:
                var star = basic.BonusTypeParamList.ElementAtOrDefault(1);
                var roleId = GridFightSeasonScope.ResolveRoleId(firstParam, GridFightSeasonScope.CurrentSeason);
                if (!IsValidRoleReward(roleId, star)) return false;
                result.Add(new GridFightRewardEntry(GridFightBonusTypeEnum.SpecificAvatar, roleId, multiplier, star));
                return true;
            case GridFightBonusTypeEnum.RandomAvatar:
                var rarity = firstParam;
                var randomStar = basic.BonusTypeParamList.ElementAtOrDefault(1);
                var roles = RandomAvatarPool(rarity, randomStar);
                if (roles.Count == 0) return false;
                for (var index = 0u; index < multiplier; index++)
                {
                    var role = roles[Random.Shared.Next(roles.Count)];
                    result.Add(new GridFightRewardEntry(
                        GridFightBonusTypeEnum.SpecificAvatar, role.ID, 1, randomStar));
                }
                return true;
            case GridFightBonusTypeEnum.AllAvatarByRarity:
                return AddAllAvatarsByRarity(firstParam, basic.BonusTypeParamList.ElementAtOrDefault(1), multiplier, result);
            case GridFightBonusTypeEnum.Item:
                if (!IsValidItemReward(firstParam)) return false;
                result.Add(new GridFightRewardEntry(GridFightBonusTypeEnum.Item, firstParam, multiplier));
                return true;
            case GridFightBonusTypeEnum.Orb:
                if (!CanMaterializeOrb(firstParam)) return false;
                result.Add(new GridFightRewardEntry(GridFightBonusTypeEnum.Orb, firstParam, multiplier));
                return true;
            case GridFightBonusTypeEnum.SpotLightReplaceCnt:
            case GridFightBonusTypeEnum.SpotLightFreeRefreshCnt:
                // Spotlight (主角光环) charges, e.g. the MoonGame (幻月游戏) orb 123156 -> bonus 23156: 1 replace + 8 free
                // spotlight rerolls.
                result.Add(new GridFightRewardEntry(basic.BonusType, 0, multiplier));
                return true;
            case GridFightBonusTypeEnum.RandomEquipByCategory:
                var category = (GridFightEquipCategoryEnum)firstParam;
                if (category == GridFightEquipCategoryEnum.None) return false;
                var equipment = GridFightSeasonScope.Equipment(GridFightSeasonScope.CurrentSeason)
                    .Where(row => row.EquipCategory == category)
                    .OrderBy(row => row.ID)
                    .ToList();
                return AddRandomEquipment(equipment, multiplier, result);
            default:
                return false;
        }
    }

    private static bool TryResolveConfiguredOrb(
        GridFightBasicOrbRewardsInfo configured,
        out IReadOnlyList<GridFightRewardEntry> rewards)
    {
        rewards = [];
        var variants = configured.Rewards.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        if (variants.Count == 0) return false;
        var result = new List<GridFightRewardEntry>();
        foreach (var reward in variants[Random.Shared.Next(variants.Count)])
            if (!TryResolveConfiguredReward(reward, result))
                return false;
        rewards = result;
        return true;
    }

    private static bool TryResolveConfiguredReward(
        GridFightBasicBonusPoolV2Excel reward,
        List<GridFightRewardEntry> result)
    {
        var count = reward.Value;
        if (count == 0) return false;
        var firstParam = reward.BonusTypeParamList.FirstOrDefault();
        if (firstParam == 0) firstParam = reward.BonusTypeParam;
        switch (reward.BonusType)
        {
            case GridFightBonusTypeEnum.Gold:
            case GridFightBonusTypeEnum.Exp:
                result.Add(new GridFightRewardEntry(reward.BonusType, 0, count));
                return true;
            case GridFightBonusTypeEnum.SpecificAvatar:
                var star = reward.BonusTypeParamList.ElementAtOrDefault(1);
                var roleId = GridFightSeasonScope.ResolveRoleId(firstParam, GridFightSeasonScope.CurrentSeason);
                if (!IsValidRoleReward(roleId, star)) return false;
                result.Add(new GridFightRewardEntry(reward.BonusType, roleId, count, star));
                return true;
            case GridFightBonusTypeEnum.RandomAvatar:
                var rarity = firstParam;
                var randomStar = reward.BonusTypeParamList.ElementAtOrDefault(1);
                var roles = RandomAvatarPool(rarity, randomStar);
                if (roles.Count == 0) return false;
                var role = roles[Random.Shared.Next(roles.Count)];
                result.Add(new GridFightRewardEntry(
                    GridFightBonusTypeEnum.SpecificAvatar,
                    role.ID,
                    count,
                    randomStar));
                return true;
            case GridFightBonusTypeEnum.AllAvatarByRarity:
                // Like the RandomAvatar rows above, Value repeats the rarity in these rows, so it is not a count here.
                return AddAllAvatarsByRarity(firstParam, reward.BonusTypeParamList.ElementAtOrDefault(1), 1, result);
            case GridFightBonusTypeEnum.Item:
                if (!IsValidItemReward(firstParam)) return false;
                result.Add(new GridFightRewardEntry(reward.BonusType, firstParam, count));
                return true;
            case GridFightBonusTypeEnum.Orb:
                if (!CanMaterializeOrb(firstParam)) return false;
                result.Add(new GridFightRewardEntry(reward.BonusType, firstParam, count));
                return true;
            case GridFightBonusTypeEnum.RandomEquipByCategory:
                var category = (GridFightEquipCategoryEnum)firstParam;
                if (category == GridFightEquipCategoryEnum.None) return false;
                var equipmentByCategory = GridFightSeasonScope.Equipment(GridFightSeasonScope.CurrentSeason)
                    .Where(equipment => equipment.EquipCategory == category)
                    .OrderBy(equipment => equipment.ID)
                    .ToList();
                return AddRandomEquipment(equipmentByCategory, count, result);
            default:
                return false;
        }
    }

    private static bool CanResolveConfiguredReward(GridFightBasicBonusPoolV2Excel reward)
    {
        if (reward.Value == 0) return false;
        var firstParam = reward.BonusTypeParamList.FirstOrDefault();
        if (firstParam == 0) firstParam = reward.BonusTypeParam;
        return reward.BonusType switch
        {
            GridFightBonusTypeEnum.Gold or GridFightBonusTypeEnum.Exp => true,
            GridFightBonusTypeEnum.SpecificAvatar =>
                IsValidRoleReward(GridFightSeasonScope.ResolveRoleId(firstParam, GridFightSeasonScope.CurrentSeason),
                    reward.BonusTypeParamList.ElementAtOrDefault(1)),
            GridFightBonusTypeEnum.RandomAvatar or GridFightBonusTypeEnum.AllAvatarByRarity =>
                RandomAvatarPool(firstParam, reward.BonusTypeParamList.ElementAtOrDefault(1)).Count > 0,
            GridFightBonusTypeEnum.Item => IsValidItemReward(firstParam),
            GridFightBonusTypeEnum.Orb => CanMaterializeOrb(firstParam),
            GridFightBonusTypeEnum.RandomEquipByCategory =>
                (GridFightEquipCategoryEnum)firstParam != GridFightEquipCategoryEnum.None &&
                GridFightSeasonScope.Equipment(GridFightSeasonScope.CurrentSeason).Any(equipment =>
                    equipment.EquipCategory == (GridFightEquipCategoryEnum)firstParam),
            _ => false,
        };
    }

    private static bool IsValidRoleReward(uint roleId, uint star)
    {
        return roleId != 0 && star != 0 && GameData.GridFightRoleBasicInfoData.ContainsKey(roleId) &&
               GameData.GridFightRoleStarData.ContainsKey(roleId << 4 | star);
    }

    private static bool IsValidItemReward(uint itemId)
    {
        return itemId != 0 && (GameData.GridFightEquipmentData.ContainsKey(itemId) ||
                              GameData.GridFightConsumablesData.ContainsKey(itemId) ||
                              GameData.GridFightForgeData.ContainsKey(itemId)) &&
               GridFightSeasonScope.IsItemInSeason(itemId, GridFightSeasonScope.CurrentSeason);
    }

    private static bool AddRandomAvatar(
        uint rarity,
        uint star,
        uint count,
        List<GridFightRewardEntry> result)
    {
        if (rarity == 0 || star == 0 || count == 0) return false;
        var roles = RandomAvatarPool(rarity, star);
        if (roles.Count == 0) return false;
        for (var index = 0u; index < count; index++)
        {
            var role = roles[Random.Shared.Next(roles.Count)];
            result.Add(new GridFightRewardEntry(GridFightBonusTypeEnum.SpecificAvatar, role.ID, 1, star));
        }
        return true;
    }

    /// <summary>
    ///     AllAvatarByRarity (OSBETAWin4.7.51, BasicBonusPoolV2 116-130): BonusTypeParamList is [rarity, star] exactly
    ///     like the RandomAvatar rows 101-115 next to it, and the type hands out every role of that rarity instead of a
    ///     random one. "Every role" is read as the pool RandomAvatar draws from (so without the roles that are never
    ///     handed out on their own); the official pool is server-side. No CombinationBonus or Orb row of that export
    ///     points at these rows, so nothing in the tables reaches them yet.
    /// </summary>
    private static bool AddAllAvatarsByRarity(uint rarity, uint star, uint copies, List<GridFightRewardEntry> result)
    {
        if (rarity == 0 || star == 0 || copies == 0) return false;
        var roles = RandomAvatarPool(rarity, star);
        if (roles.Count == 0) return false;
        result.AddRange(roles.Select(role =>
            new GridFightRewardEntry(GridFightBonusTypeEnum.SpecificAvatar, role.ID, copies, star)));
        return true;
    }

    // Random role rewards draw only from the running season; 4.6.51 tables hold both seasons' roles. They also leave
    // out the roles a run never hands out on their own (GridFightSeasonScope.RewardExcludedRoleIds), which IsInPool
    // does not tell apart: a random 5-cost reward could otherwise be Tenglong (丹恒•腾荒) or a 5-/7-cost Aha (星神★阿哈).
    private static List<GridFightRoleBasicInfoExcel> RandomAvatarPool(uint rarity, uint star)
    {
        var season = GridFightSeasonScope.CurrentSeason;
        var excluded = GridFightSeasonScope.RewardExcludedRoleIds(season);
        return GridFightSeasonScope.Roles(season)
            .Where(role => role.IsInPool && role.Rarity == rarity && !excluded.Contains(role.ID) &&
                           GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | star))
            .OrderBy(role => role.ID)
            .ToList();
    }

    private static bool AddRandomEquipment(
        IReadOnlyList<GridFightEquipmentExcel> equipment,
        uint count,
        List<GridFightRewardEntry> result)
    {
        if (equipment.Count == 0) return false;
        for (var index = 0u; index < count; index++)
        {
            var selected = equipment[Random.Shared.Next(equipment.Count)];
            result.Add(new GridFightRewardEntry(GridFightBonusTypeEnum.Item, selected.ID, 1));
        }
        return true;
    }
}
