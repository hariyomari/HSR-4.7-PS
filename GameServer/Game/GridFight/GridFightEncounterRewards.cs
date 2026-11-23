using March7thHoney.Enums.GridFight;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Encounter (遭遇, EliteBranch) rewards. The official reward tables are server-side and unpublished; the wiki only
///     says that low tiers give a token amount of gold, exp or free refreshes, higher tiers give more of them plus
///     basic equipment or 4/5-cost characters, and tier 4 gives the best items (Wealth Diamond, advanced equipment
///     selectors). The pools below follow that description; amounts are our own estimates. One reward is rolled per
///     option when the options are generated, so the card shows exactly what clearing it pays. Free Upgrade
///     (免费升舱, 103901) raises the tier by one, and tier 4 gets one extra tier-4 reward instead.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint AdvancedArmamentBoxForgeId = 99998;     // 进阶武装箱
    public const uint FreeUpgradeAugmentId = 103901;          // 免费升舱
    private const uint MaxEncounterRewardTier = 4;

    private IReadOnlyList<GridFightEncounterOptionState> RollEncounterOptionsWithRewards()
    {
        var upgraded = ActiveInvestmentIds.Contains(FreeUpgradeAugmentId);
        return _catalog.RollEncounterOptions(CurrentNode, CampIds, DivisionId)
            .Select(option =>
            {
                var rewards = RollEncounterRewardTier(Math.Min(option.Quality + (upgraded ? 1u : 0u), MaxEncounterRewardTier))
                    .ToList();
                if (upgraded && option.Quality >= MaxEncounterRewardTier)
                    rewards.AddRange(RollEncounterRewardTier(MaxEncounterRewardTier, rewards));
                return option with { Rewards = rewards };
            })
            .ToList();
    }

    /// <summary>One reward from the tier's pool; <paramref name="taken" /> keeps an extra roll from repeating a kind.</summary>
    private IReadOnlyList<GridFightEncounterDrop> RollEncounterRewardTier(
        uint tier,
        IReadOnlyList<GridFightEncounterDrop>? taken = null)
    {
        var chapter = ChapterId;
        var pool = new List<Func<IReadOnlyList<GridFightEncounterDrop>>>();
        void Coin(uint amount) => pool.Add(() => [new(GridFightDropType.Coin, 0, amount)]);
        void Exp(uint amount) => pool.Add(() => [new(GridFightDropType.Exp, 0, amount)]);
        void Refresh(uint amount) => pool.Add(() => [new(GridFightDropType.Refresh, 0, amount)]);
        void Item(uint itemId) => pool.Add(() => [new(GridFightDropType.Item, itemId, 1)]);
        void Equipment(GridFightEquipCategoryEnum category, int count) => pool.Add(() => Enumerable.Range(0, count)
            .Select(_ => _catalog.RollEquipment(category, 0))
            .Where(id => id != 0)
            .Select(id => new GridFightEncounterDrop(GridFightDropType.Item, id, 1))
            .ToList());

        switch (tier)
        {
            case 1:
                Coin(chapter + 1); Exp(2); Refresh(2);
                break;
            case 2:
                Coin(chapter + 3); Exp(4); Refresh(4); Equipment(GridFightEquipCategoryEnum.Basic, 1);
                break;
            case 3:
                // The encounter card renders no Role drop (empty preview), so the wiki's 4/5-cost character rewards are
                // stood in for by equipment of matching value.
                Coin(chapter * 2 + 4); Exp(6); Refresh(6); Equipment(GridFightEquipCategoryEnum.Basic, 2);
                Equipment(GridFightEquipCategoryEnum.Craftable, 1);
                break;
            default:
                Item(WealthDiamondEquipmentId); Item(AdvancedArmamentBoxForgeId); Item(PrivilegeArmamentBoxForgeId);
                break;
        }

        foreach (var roll in pool.OrderBy(_ => _catalog.NextRandom(0, int.MaxValue)))
        {
            var drops = roll();
            if (drops.Count == 0) continue;
            if (taken != null && taken.Any(previous => previous.Type == drops[0].Type && previous.ItemId == drops[0].ItemId))
                continue;
            return drops;
        }
        return [];
    }

    private void GrantEncounterReward(GridFightBattlePlan? battlePlan)
    {
        var option = EncounterOptions.FirstOrDefault(entry => entry.BattlePlan == battlePlan);
        if (option?.Rewards is not { Count: > 0 } rewards) return;
        var treasure = _spotLights.Any(entry => entry.SpotLightId == HardBattleTreasureSpotLightId &&
            !entry.IsLoss && Roles.Any(role => role.UniqueId == entry.RoleUniqueId));
        var drops = treasure
            ? rewards.Concat(RollEncounterRewardTier(Math.Min(option.Quality +
                (ActiveInvestmentIds.Contains(FreeUpgradeAugmentId) ? 1u : 0u), MaxEncounterRewardTier))).ToList()
            : rewards;
        var grant = G;
        foreach (var drop in drops)
            grant = drop.Type switch
            {
                GridFightDropType.Coin => grant with { Gold = grant.Gold + drop.Num },
                GridFightDropType.Exp => grant with { Exp = grant.Exp + drop.Num },
                GridFightDropType.Refresh => grant with { FreeRefresh = grant.FreeRefresh + drop.Num },
                GridFightDropType.Role => grant with { SpecificRoles = [.. grant.SpecificRoles, drop.ItemId] },
                GridFightDropType.Item => grant with { Items = [.. grant.Items, (drop.ItemId, drop.Num)] },
                _ => grant,
            };
        _augmentNodeEffects.Add(ApplyAugmentGrant(0, grant));
    }
}
