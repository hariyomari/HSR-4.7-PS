using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightTwinPrimesReward(uint EffectId, uint ExpAmount);

/// <summary>
/// 4.5 packet-backed Currency War portal and investment rewards. Keep battle-side random mechanics (notably
/// Skill Point trait lottery probabilities) client-authored; this class only materializes
/// persistent run state and server-owned shop/reward decisions seen on the wire.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint GunpowderPortalId = 101;
    public const uint TalentRecruitmentPortalId = 109;
    public const uint TalentRecruitmentOrbId = 200004;

    public const uint MarketVitalityAugmentId = 200801;
    public const uint GoldFuturesPlusAugmentId = 101102;
    public const uint TwinPrimesAugmentId = 200201;
    public const uint PeriodicBenefitsAugmentId = 200101;
    public const uint SecretCodexPlusAugmentId = 230902;
    public const uint OotdGoldAugmentId = 251301;
    public const uint SecretCodexForgeItemId = 99991;

    public const string MarketVitalitySavedValueKey = "Augment200801_SavedValue01";

    private readonly List<GridFightForgeItemState> _gunpowderInitialForgeItems = [];
    private GridFightOrbState? _talentRecruitmentInitialOrb;
    private readonly List<(uint EffectId, uint Value)> _marketVitalitySavedValueUpdates = [];
    private readonly List<GridFightTwinPrimesReward> _twinPrimesRewards = [];
    private readonly HashSet<uint> _twinPrimesRewardedRarities = [];
    private bool _goldFuturesPendingReady;
    private bool _goldFuturesInvestmentPending;
    private uint _lastGoldFuturesChildSelection;
    private uint _goldFuturesNodeCount;
    private bool _shopLocked;

    public IReadOnlyList<GridFightForgeItemState> GunpowderInitialForgeItems => _gunpowderInitialForgeItems;
    public GridFightOrbState? TalentRecruitmentInitialOrb => _talentRecruitmentInitialOrb;
    public bool IsGoldFuturesInvestmentPending => _goldFuturesInvestmentPending;
    public bool ShopLocked => _shopLocked;

    public bool TakeGoldFuturesChildSelection(uint augmentId)
    {
        if (_lastGoldFuturesChildSelection != augmentId) return false;
        _lastGoldFuturesChildSelection = 0;
        return true;
    }

    private void InitializePortalRewards()
    {
        if (SelectedPortalId == GunpowderPortalId && _gunpowderInitialForgeItems.Count == 0)
        {
            for (var i = 0; i < 2; i++)
                if (TryAddForgeItem(SimpleArmamentBoxForgeId, out var forge))
                    _gunpowderInitialForgeItems.Add(forge);
        }

        if (SelectedPortalId == TalentRecruitmentPortalId && _talentRecruitmentInitialOrb == null &&
            TryAddOrb(TalentRecruitmentOrbId, out var orb))
            _talentRecruitmentInitialOrb = orb;
    }

    public bool SetShopLocked(bool locked)
    {
        if (Phase != GridFightSessionPhase.Preparing) return false;
        _shopLocked = locked;
        return true;
    }

    private uint GetMarketVitalityChance()
    {
        // Capture: L4/L5/L6/L7 = 16/18/20/22. The linear table is the only server-owned
        // extrapolation here and is isolated so it can be replaced by newer resource data.
        return Math.Min(100u, 8u + PlayerLevel * 2u);
    }

    private void QueueMarketVitalitySavedValue(uint effectId)
    {
        if (!ActiveInvestmentIds.Contains(MarketVitalityAugmentId)) return;
        _marketVitalitySavedValueUpdates.Add((effectId, GetMarketVitalityChance()));
    }

    public IReadOnlyList<(uint EffectId, uint Value)> TakeMarketVitalitySavedValueUpdates()
    {
        if (_marketVitalitySavedValueUpdates.Count == 0) return [];
        var result = _marketVitalitySavedValueUpdates.ToList();
        _marketVitalitySavedValueUpdates.Clear();
        return result;
    }

    private uint ResolveMarketVitalityStar(uint roleId)
    {
        if (!ActiveInvestmentIds.Contains(MarketVitalityAugmentId) ||
            !GameData.GridFightRoleBasicInfoData.TryGetValue(roleId, out var role) ||
            role.Rarity is not (1 or 2) ||
            !GameData.GridFightRoleStarData.ContainsKey(roleId << 4 | 2u))
            return 1;
        return Random.Shared.Next(100) < GetMarketVitalityChance() ? 2u : 1u;
    }

    private void PrepareTwinPrimesRewards(IEnumerable<uint> roleIds)
    {
        if (!ActiveInvestmentIds.Contains(TwinPrimesAugmentId)) return;
        foreach (var rarity in roleIds
                     .Select(id => GameData.GridFightRoleBasicInfoData.GetValueOrDefault(id)?.Rarity ?? 0)
                     .Where(rarity => rarity is >= 1 and <= 5)
                     .Distinct()
                     .Order())
        {
            if (!_twinPrimesRewardedRarities.Add(rarity)) continue;
            var (effectId, exp) = rarity switch
            {
                1 => (112u, 2u),
                2 => (113u, 3u),
                3 => (114u, 5u),
                4 => (115u, 7u),
                5 => (116u, 11u),
                _ => (0u, 0u),
            };
            if (effectId == 0) continue;
            AddPlayerExp(exp);
            _twinPrimesRewards.Add(new GridFightTwinPrimesReward(effectId, exp));
        }
    }

    public IReadOnlyList<GridFightTwinPrimesReward> TakeTwinPrimesRewards()
    {
        if (_twinPrimesRewards.Count == 0) return [];
        var result = _twinPrimesRewards.ToList();
        _twinPrimesRewards.Clear();
        return result;
    }

    private void OnRewardInvestmentSelected(uint augmentId)
    {
        switch (augmentId)
        {
            case MarketVitalityAugmentId:
                QueueMarketVitalitySavedValue(108);
                break;
            case GoldFuturesPlusAugmentId:
                _goldFuturesNodeCount = 0;
                _goldFuturesPendingReady = false;
                break;
            case PeriodicBenefitsAugmentId:
                AddGold(4);
                break;
            case SecretCodexPlusAugmentId:
                if (TryAddForgeItem(SecretCodexForgeItemId, out var forge))
                    _investmentSelectionRewards.Add(new(augmentId, 116, ForgeItem: forge, GoldValue: AddGold(12)));
                else
                    AddGold(12);
                break;
        }
    }

    private void OnRewardInvestmentNodeEntered()
    {
        if (ActiveInvestmentIds.Contains(PeriodicBenefitsAugmentId))
        {
            AddGold(2);
            _roundBeginInvestmentRewards.Add(new(PeriodicBenefitsAugmentId, 107, GoldValue: Gold));
        }

        if (ActiveInvestmentIds.Contains(GoldFuturesPlusAugmentId) && !_goldFuturesPendingReady)
        {
            _goldFuturesNodeCount++;
            if (_goldFuturesNodeCount >= 4)
                _goldFuturesPendingReady = true;
        }
    }

    private bool TryBeginGoldFuturesInvestment()
    {
        if (!_goldFuturesPendingReady || !ActiveInvestmentIds.Contains(GoldFuturesPlusAugmentId)) return false;
        var offer = _catalog.RollInvestmentOffer(
            ChapterId,
            GridFightAugmentQualityEnum.Gold,
            InvestmentOfferCount,
            ActiveInvestmentIds);
        if (offer.Count == 0) return false;
        InvestmentOffer = offer;
        ResetInvestmentRerolls();
        _goldFuturesPendingReady = false;
        _goldFuturesNodeCount = 0;
        _goldFuturesInvestmentPending = true;
        Phase = GridFightSessionPhase.SelectingInvestment;
        return true;
    }

    private void EnterCurrentNodePostRoundPhase()
    {
        if (TryBeginBonusInvestment(GridFightSessionPhase.SelectingInvestment)) return;
        if (TryBeginGoldFuturesInvestment()) return;
        if (CurrentNode.NodeType == GridFightNodeTypeEnum.Supply)
        {
            PrepareSupplyOffer();
            Phase = GridFightSessionPhase.SelectingSupply;
        }
        else if (CurrentNode.NodeType == GridFightNodeTypeEnum.EliteBranch)
        {
            Phase = GridFightSessionPhase.SelectingEncounter;
        }
        else if (CurrentNode.Template.IsAugment != 0)
        {
            PrepareInvestmentOffer();
            // Every remaining augment may already be owned late in a run; skip the choice instead of
            // publishing an empty offer that the client can never resolve.
            Phase = InvestmentOffer.Count == 0
                ? GridFightSessionPhase.AwaitingBattleReturn
                : GridFightSessionPhase.SelectingInvestment;
        }
        else
        {
            Phase = GridFightSessionPhase.AwaitingBattleReturn;
        }
    }

    public uint OotdDistinctEquipmentCount
    {
        get
        {
            if (!ActiveInvestmentIds.Contains(OotdGoldAugmentId)) return 0;
            return (uint)Roles
                .SelectMany(GetEquippedEquipment)
                .Where(item => GameData.GridFightEquipmentData.TryGetValue(item.EquipmentId, out var config) &&
                               IsOotdCountableCategory(config.EquipCategory))
                .Select(item => item.EquipmentId)
                .Distinct()
                .Count();
        }
    }

    private static bool IsOotdCountableCategory(GridFightEquipCategoryEnum category) => category is
        GridFightEquipCategoryEnum.Basic or
        GridFightEquipCategoryEnum.Craftable or
        GridFightEquipCategoryEnum.Emblem or
        GridFightEquipCategoryEnum.Crown or
        GridFightEquipCategoryEnum.Artifacts or
        GridFightEquipCategoryEnum.Radiant or
        GridFightEquipCategoryEnum.Support or
        GridFightEquipCategoryEnum.Other;
}
