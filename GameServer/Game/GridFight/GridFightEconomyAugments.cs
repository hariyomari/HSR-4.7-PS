using March7thHoney.Data;
using March7thHoney.Enums.GridFight;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed partial class GridFightSession
{
    private bool _spentGoldThisNode;
    private readonly List<GridFightAugmentEffectResult> _economyEffects = [];
    private readonly List<uint> _bonusInvestmentQueue = [];
    private uint _bonusInvestmentSource;
    private uint _bonusInvestmentRerolls;
    private GridFightSessionPhase _bonusInvestmentReturnPhase;

    /// <summary>
    ///     Refreshes each offered card allows (client MaxRollTimes, per card like RollTimes). Silver Investment
    ///     (白银投资, 203201) offers "#1 refreshes" on its own pick; they are per card as well, because the client reads
    ///     the number as every card's allowance and would keep offering refreshes that a shared pool had already spent.
    /// </summary>
    public uint InvestmentFreeRerollCount => _bonusInvestmentSource != 0
        ? _bonusInvestmentRerolls : IsForesightActive || IsGoldFuturesInvestmentPending ? 0u : 1u;
    public uint CurrentBuyExpCost => ActiveInvestmentIds.Contains(300201)
        ? (_catalog.BuyExpCost > 0 ? _catalog.BuyExpCost - 1 : 0) : _catalog.BuyExpCost;

    private void SpendGold(uint amount)
    {
        Gold -= amount;
        if (amount > 0) _spentGoldThisNode = true;
    }

    public uint CurrentMaxInterestGold
    {
        get
        {
            var cap = _catalog.MaxInterestGold;
            if (ActiveInvestmentIds.Contains(300101)) cap = Math.Max(cap, 9);
            if (ActiveInvestmentIds.Contains(300801)) cap = Math.Max(cap, 10);
            return cap;
        }
    }

    private uint CalculateInvestmentInterest(uint balance) =>
        Math.Min(balance / _catalog.DepositPerInterest, CurrentMaxInterestGold);

    /// <summary>
    ///     Promotion Quota (晋升名额, 301301): each successful XP purchase promotes one unsold shop role by one cost.
    /// </summary>
    private void PromoteShopRoleAfterBuyingExp()
    {
        if (!ActiveInvestmentIds.Contains(301301)) return;
        var excluded = GetMaxStarExcludedRoleIds();
        var silverWolf = _silverWolfCurrentRoleId == 0
            ? GridFightSpecialFeatures.SilverWolfThreeCostRoleId : _silverWolfCurrentRoleId;
        var candidates = Shop.Where(slot => !slot.SoldOut && slot.SpecialGoodsId == 0)
            .Where(slot => GameData.GridFightRoleBasicInfoData.ContainsKey(slot.RoleId))
            .Select(slot => (Slot: slot, Roles: _catalog.GetRolesByCost(
                    GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity + 1, _temporarilyUnlockedRestrictedRoleIds)
                .Where(role => !excluded.Contains(role.ID) &&
                    (!GridFightSpecialFeatures.IsSilverWolfLv999Role(role.ID) || role.ID == silverWolf) &&
                    GameData.GridFightRoleStarData.ContainsKey(role.ID << 4 | slot.Star)).ToList()))
            .Where(candidate => candidate.Roles.Count > 0).ToList();
        if (candidates.Count == 0) return;
        var (selected, roles) = candidates[_catalog.NextRandom(0, candidates.Count)];
        var replacement = roles[_catalog.NextRandom(0, roles.Count)];
        selected.RoleId = replacement.ID;
        selected.Price = _catalog.GetBuyPrice(replacement.ID, selected.Star);
    }

    private void InitializeEconomyAugment(uint augmentId)
    {
        switch (augmentId)
        {
            case 201201: // 采购专员·金
            case 102201: // 市场干预
            case 101201: // 返利
            case 101701: // 成长基金
            case 203201: // 白银投资
                _investmentNodeCounters[augmentId] = 0;
                break;
            case 202201: // 四费晋升: only the next qualifying purchase.
                _investmentNodeCounters[augmentId] = 1;
                break;
        }
        if (augmentId == 203201) _bonusInvestmentQueue.Add(augmentId);
        if (augmentId == 101701) GrantGrowthFund();
    }

    private void OnEconomyNodeEntered()
    {
        if (ActiveInvestmentIds.Contains(300101) && !_spentGoldThisNode)
            _augmentNodeEffects.Add(ApplyAugmentGrant(300101, G with { Gold = CalculateInvestmentInterest(uint.MaxValue) }));
        _spentGoldThisNode = false;
        if (ActiveInvestmentIds.Contains(203201))
        {
            var nodes = _investmentNodeCounters.GetValueOrDefault(203201u);
            if (nodes < 3 && ++nodes == 3) _bonusInvestmentQueue.Add(203201);
            _investmentNodeCounters[203201] = nodes;
        }
    }

    private uint PurchaseStar(GridFightShopSlot slot)
    {
        if (ActiveInvestmentIds.Contains(202201) && _investmentNodeCounters.GetValueOrDefault(202201u) > 0 &&
            GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity == 4 && slot.Star == 1 &&
            GameData.GridFightRoleStarData.ContainsKey(slot.RoleId << 4 | 2))
        {
            _investmentNodeCounters[202201] = 0;
            return 2;
        }
        return slot.Star;
    }

    private void OnEconomyPurchase(IEnumerable<GridFightShopSlot> slots)
    {
        if (!ActiveInvestmentIds.Contains(101201)) return;
        var count = _investmentNodeCounters.GetValueOrDefault(101201u) +
            (uint)slots.Count(slot => GameData.GridFightRoleBasicInfoData[slot.RoleId].Rarity == 5);
        _investmentNodeCounters[101201] = count % 3;
        if (count >= 3) _economyEffects.Add(ApplyAugmentGrant(101201, G with { Gold = count / 3 * 3 }));
    }

    private void GrantGrowthFund()
    {
        if (PlayerLevel < 9 || !ActiveInvestmentIds.Contains(101701) ||
            _investmentNodeCounters.GetValueOrDefault(101701u) != 0) return;
        _investmentNodeCounters[101701] = 1;
        _economyEffects.Add(ApplyAugmentGrant(101701, G with { Gold = 40 }));
    }

    private void OnEconomyLevelChanged()
    {
        GrantGrowthFund();
        if (!ActiveInvestmentIds.Contains(300201)) return;
        // The free reroll is not a manual refresh and does not advance procurement counters.
        var locked = _shopLocked;
        _shopLocked = false;
        RefreshShop(false);
        _shopLocked = locked;
        var slots = Shop.Where(slot => !slot.SoldOut && slot.SpecialGoodsId == 0)
            .OrderByDescending(slot => slot.Price).ThenBy(slot => slot.Index).Take(3).ToList();
        var total = new GridFightRewardApplication { Success = true };
        foreach (var slot in slots)
        {
            var applied = ApplyRewards([new(GridFightBonusTypeEnum.SpecificAvatar, slot.RoleId, 1, slot.Star)]);
            if (!applied.Success) continue;
            slot.SoldOut = true;
            MergeRewardApplication(total, applied);
        }
        _economyEffects.Add(new(300201, total, true, false, false));
    }

    private bool TryBeginBonusInvestment(GridFightSessionPhase returnPhase)
    {
        if (_bonusInvestmentSource != 0 || _bonusInvestmentQueue.Count == 0) return false;
        var source = _bonusInvestmentQueue[0];
        var offers = _catalog.RollInvestmentOffer(ChapterId, GridFightAugmentQualityEnum.Silver,
            InvestmentOfferCount, ActiveInvestmentIds);
        _bonusInvestmentQueue.RemoveAt(0);
        if (offers.Count == 0) return TryBeginBonusInvestment(returnPhase);
        _bonusInvestmentSource = source;
        _bonusInvestmentRerolls = 3;
        _bonusInvestmentReturnPhase = returnPhase;
        InvestmentOffer = offers;
        ResetInvestmentRerolls();
        Phase = GridFightSessionPhase.SelectingInvestment;
        return true;
    }

    public IReadOnlyList<GridFightAugmentEffectResult> TakeEconomyEffects() => Drain(_economyEffects);
}

public sealed partial class GridFightManager
{
    public GridFightGetDataScRsp? BuildInterestDisplayRefresh(GridFightSession? previousSession, uint? previousCap)
    {
        if (Session == null || Session != previousSession || previousCap == Session.CurrentMaxInterestGold)
            return null;
        // The current protocol has no verified incremental max-interest field. Reuse the
        // normal current-info snapshot only when an existing run's cap actually changes.
        return new GridFightGetDataScRsp { SystemInfo = BuildSystemInfo(), CurrentInfo = BuildCurrentInfo() };
    }

    private void AppendEconomyEffects(GridFightSyncUpdateResultScNotify notify)
    {
        if (Session != null) AppendAugmentEffectResults(notify, Session.TakeEconomyEffects());
    }
}
