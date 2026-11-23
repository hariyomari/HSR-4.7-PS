using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

public enum GridFightSessionPhase
{
    SelectingPortal,
    SelectingNestedPortal,
    AwaitingInitialRound,
    AwaitingInitialOrb,
    AwaitingInitialReturn,
    Preparing,
    InBattle,
    AwaitingBattleRound,
    SelectingInvestment,
    SelectingSupply,
    SelectingEncounter,
    AwaitingSupplyRound,
    AwaitingBattleReturn,
    AwaitingBackToPrepare,
    /// <summary>Squad HP reached 0; the run only waits for its defeat settle.</summary>
    Defeated,
}

public enum GridFightPendingRequestKind
{
    PortalReroll,
    PortalSelect,
    RoundBegin,
    ReturnPreparation,
}

public sealed record GridFightSupplyOfferState(uint RoleId, uint EquipmentId)
{
    public uint Star { get; init; } = 1;
    public IReadOnlyList<uint> ExtraEquipmentIds { get; init; } = [];
}

public sealed record GridFightSupplySelectionResult(
    bool Success,
    GridFightRoleAcquisitionResult Acquisition,
    GridFightEquipmentState? Equipment)
{
    public static GridFightSupplySelectionResult Failed { get; } =
        new(false, GridFightRoleAcquisitionResult.Failed, null);
}

public sealed class GridFightRoleState
{
    public uint UniqueId { get; init; }
    public uint RoleId { get; set; }
    public uint Star { get; set; } = 1;
    public uint Position { get; set; }
    public List<uint> EquippedEquipmentUniqueIds { get; } = [];
    public bool HasPowerGrowth { get; set; }
    public Dictionary<uint, uint> PermanentPropertyBonuses { get; } = [];
}

public sealed class GridFightEquipmentState
{
    public uint UniqueId { get; init; }
    public uint EquipmentId { get; init; }
    public uint Source { get; init; }
    public bool IsLocked { get; set; }

    /// <summary>
    ///     Bond-granted equipment (星核猎手 Script Equipment): wearable, but it leaves when the bond level drops and cannot
    ///     be crafted, rerolled, upgraded or consumed like an owned item. The client shows it as temporary.
    /// </summary>
    public bool IsTemporary { get; set; }

    /// <summary>Held by an NPC (IsLocked) or bond-granted (IsTemporary): not an ordinary item of the player.</summary>
    public bool IsRestricted => IsLocked || IsTemporary;
}

public sealed class GridFightOrbState
{
    public uint UniqueId { get; init; }
    public uint OrbItemId { get; init; }
}

public sealed class GridFightConsumableState
{
    public uint GroupId { get; init; }
    public uint ItemId { get; init; }
    public uint Count { get; set; }
}

public sealed class GridFightShopSlot
{
    public uint Index { get; init; }
    public uint RoleId { get; set; }
    public uint SpecialGoodsId { get; init; }
    public uint Star { get; init; } = 1;
    public uint Price { get; set; }
    public bool SoldOut { get; set; }
}

public sealed record GridFightSessionChange(
    bool Success,
    IReadOnlyList<GridFightRoleState> AddedRoles,
    IReadOnlyList<uint> RemovedRoleIds,
    IReadOnlyList<GridFightRoleState> UpdatedRoles,
    IReadOnlyList<GridFightForgeItemState> UpdatedForgeItems)
{
    public static GridFightSessionChange Failed { get; } = new(false, [], [], [], []);
    public static GridFightSessionChange Succeeded { get; } = new(true, [], [], [], []);
    public bool SilverWolfTraitActivated { get; init; }
    public bool HeadPlayerPendingStarted { get; init; }
    public uint FailureRetcode { get; init; } = 1;
}

public sealed record GridFightForgeUseResult(
    bool Success,
    uint RemovedForgeUniqueId,
    GridFightEquipmentState? Equipment,
    IReadOnlyList<GridFightRoleState> UpdatedRoles,
    IReadOnlyList<GridFightForgeItemState> UpdatedForgeItems)
{
    public static GridFightForgeUseResult Failed { get; } = new(false, 0, null, [], []);
}

public sealed record GridFightRoleMergeStep(
    IReadOnlyList<uint> RemovedRoleIds,
    GridFightRoleState AddedRole);

public sealed record GridFightRoleAcquisitionResult(
    bool Success,
    IReadOnlyList<GridFightRoleState> AcquiredRoles,
    IReadOnlyList<GridFightRoleMergeStep> MergeSteps,
    IReadOnlyList<GridFightRoleState> FinalMergedRoles)
{
    public static GridFightRoleAcquisitionResult Failed { get; } = new(false, [], [], []);
    public IReadOnlyList<GridFightRoleUnlockEffect> UnlockEffects { get; init; } = [];
}

public sealed record GridFightRoleBattleDamage(uint RoleId, double Damage, double SecondaryDamage);

public sealed record GridFightTraitBattleDamage(uint TraitId, double Damage, double SecondaryDamage);

public sealed record GridFightAugmentBattleDamage(uint AugmentId, double Damage, double SecondaryDamage);

public sealed record GridFightKilledMonsterInstance(uint WaveIndex, uint MonsterIndex, uint MonsterId);

public sealed record GridFightBattleStatistics(
    uint ProgressPercent,
    IReadOnlyList<GridFightRoleBattleDamage> RoleDamage,
    IReadOnlyList<GridFightTraitBattleDamage> TraitDamage,
    IReadOnlyList<GridFightAugmentBattleDamage> AugmentDamage,
    IReadOnlyList<GridFightKilledMonsterInstance> KilledMonsters,
    uint RoundCount,
    IReadOnlyDictionary<string, uint> SavedValues,
    bool IsPresent = true)
{
    public static GridFightBattleStatistics Empty { get; } = new(0, [], [], [], [], 0, new Dictionary<string, uint>(), false);
}

public sealed record GridFightTalentOrbReward(
    uint TalentId,
    uint EffectId,
    IReadOnlyList<GridFightOrbState> AddedOrbs);

public sealed record GridFightMortenaxBladeBattleEffect(
    uint RoleUniqueId,
    uint EquipmentId,
    uint ReplacedEquipmentUniqueId,
    uint TemporaryInstanceId,
    bool NeedsTraitEffectSync);

public enum GridFightHpChangeKind
{
    BasicPenalty,
    ProgressPenalty,
    ThresholdPenalty,
    TalentHealing,
    FinalBattleTurnHealing,
    AugmentHealing,
    AugmentDamage,
}

public sealed record GridFightHpChange(
    GridFightHpChangeKind Kind,
    uint PreviousHp,
    uint CurrentHp,
    uint Amount,
    uint SourceId = 0);

public sealed record GridFightBattleSettlementResult(
    bool Success,
    bool RouteAdvanced,
    uint ProgressPercent,
    uint DeadLinePercent,
    bool IsPerfectFinish,
    uint PreviousKeepWinCount,
    uint KeepWinCount,
    uint BasicGold,
    uint InterestGold,
    uint KeepWinGold,
    uint GoldAfterBasic,
    uint GoldAfterInterest,
    uint GoldAfterKeepWin,
    uint AddedExp,
    uint LineupHpBefore,
    uint LineupHpAfter,
    IReadOnlyList<GridFightHpChange> HpChanges,
    GridFightBattleStatistics Statistics,
    IReadOnlyList<GridFightOrbState> AddedBattleOrbs,
    IReadOnlyList<GridFightTalentOrbReward> TalentRewards)
{
    public static GridFightBattleSettlementResult Failed { get; } = new(
        false, false, 0, 0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [],
        GridFightBattleStatistics.Empty, [], []);

    /// <summary>The squad HP reached 0: the run ends with a defeat settle instead of moving on.</summary>
    public bool Defeated { get; init; }
}

public sealed partial class GridFightSession
{
    private const int InvestmentOfferCount = 3;
    private const int SupplyOfferCount = 5;
    private const uint SupplySelectionQueueAdvance = 4;
    private const uint SupplyEquipmentSource = 9;
    private const uint OfficialInitialGold = 3;
    private const int OfficialInitialRoleCount = 4;
    private const uint OfficialInitialOrbId = 203;
    private const uint OfficialInitialConsumableId = 350101;
    // Talent 1081 carries both the opening-round reward and the post-battle healing.
    private const uint OfficialInitialTalentId = GridFightResourceCatalog.PostBattleHealingTalentId;
    private const uint OfficialInitialTalentEffectId = 36;
    private const uint MortenaxBladeRoleId = 1507;
    private const uint MortenaxBladeTraitEffectParamValue = 106;

    private readonly GridFightResourceCatalog _catalog;
    private readonly Dictionary<string, uint> _roleSavedValues = new(StringComparer.Ordinal);
    // Official runtime entity IDs reserve 1/2; capture-visible roles/items begin at 3.
    private uint _nextEntityUniqueId = 3;
    private bool _orbClaimed;
    private bool _officialInitialRoundGranted;
    private bool _mortenaxBladeTraitEffectInitialized;
    private IReadOnlyList<GridFightMortenaxBladeBattleEffect> _mortenaxBladeBattleEffects = [];
    private bool _cyrenePoemShopUnlocked;
    private bool _silverWolfHackUnlocked;
    private uint? _traitEffectSelectEnhanceQueuePosition;
    // One entry per reroll used on the card that now shows this id (the client counts refreshes per card).
    private readonly List<uint> _rerolledInvestmentIds = [];
    // Offers replaced by a reroll must not come back into another slot of the same investment choice.
    private readonly HashSet<uint> _rerolledAwayInvestmentIds = [];
    private readonly HashSet<uint> _temporarilyUnlockedRestrictedRoleIds = [];
    private readonly HashSet<(uint TalentId, uint EffectId, GridFightTalentRewardTrigger Trigger, uint ChapterId)>
        _triggeredTalentRewards = [];
    private uint _encounterRerollCount;
    private Dictionary<uint, uint> _roleSwitchIds = [];
    private GridFightRoleSwitchUpdate? _roleSwitchUpdate;

    public IReadOnlyList<GridFightMortenaxBladeBattleEffect> MortenaxBladeBattleEffects => _mortenaxBladeBattleEffects;
    public bool HasMortenaxBladeTraitEffectSync => _mortenaxBladeBattleEffects.Any(effect => effect.NeedsTraitEffectSync);
    public uint MortenaxBladeTraitEffectParam => MortenaxBladeTraitEffectParamValue;

    public uint Season { get; }
    public uint DivisionId { get; }
    public bool IsOverlock { get; }
    public uint UniqueId { get; }
    public uint PetPalId { get; }
    public uint PetPalEffectId { get; }
    public GridFightSessionPhase Phase { get; private set; } = GridFightSessionPhase.SelectingPortal;
    public uint PendingQueuePosition { get; private set; } = 1;
    public uint ChapterId { get; private set; } = 1;
    public uint SectionId { get; private set; } = 1;
    public uint Gold { get; private set; }
    public uint PlayerLevel { get; private set; }
    public uint PlayerExp { get; private set; }
    public uint LineupHp { get; private set; }
    public uint MaxLineupHp { get; private set; }
    // Free shop refreshes (investments, portals and season-2 traits share _freeShopRefreshCount);
    // consumed before gold and shown as a zero refresh cost.
    public uint FreeRefreshCount => _freeShopRefreshCount;
    public uint CurrentRefreshCost => EffectiveShopRefreshCost;
    public uint KeepWinCount { get; private set; }
    public uint BattlesFinished { get; private set; }
    public uint SelectedPortalId { get; private set; }
    public IReadOnlyList<uint> ActivePortalIds => _activePortalIds;
    public IReadOnlyList<uint> CampIds { get; }
    public uint RewardCampId { get; }
    public IReadOnlyList<GridFightMonsterExcel> Bosses { get; }
    public IReadOnlyList<uint> AffixIds { get; }
    public IReadOnlyList<uint> PortalOffer { get; private set; }
    public const uint PortalFreeRerollCount = 1;
    public uint PortalRerollCount { get; private set; }
    public IReadOnlyList<uint> InvestmentOffer { get; private set; } = [];
    public IReadOnlyList<GridFightSupplyOfferState> SupplyOffer { get; private set; } = [];
    public List<uint> ActiveInvestmentIds { get; } = [];
    public uint SupplyRerollCount { get; private set; }
    public GridFightBattlePlan? BattlePlan { get; private set; }
    public IReadOnlyList<GridFightEncounterOptionState> EncounterOptions { get; private set; } = [];
    public IReadOnlyList<GridFightMonsterPlan> Enemies => BattlePlan?.Monsters ?? [];
    public List<GridFightRoleState> Roles { get; } = [];
    public List<GridFightEquipmentState> Equipments { get; } = [];
    public List<GridFightOrbState> Orbs { get; } = [];
    public List<GridFightConsumableState> Consumables { get; } = [];
    public List<GridFightForgeItemState> ForgeItems { get; } = [];
    public List<GridFightShopSlot> Shop { get; } = [];
    public IReadOnlyDictionary<string, uint> RoleSavedValues => _roleSavedValues;
    public IReadOnlySet<uint> TemporarilyUnlockedRestrictedRoleIds => _temporarilyUnlockedRestrictedRoleIds;
    public bool HasCyreneCombatEnhancement { get; private set; }
    public GridFightHackConsoleState? HackConsole { get; private set; }
    public uint? TraitEffectSelectEnhanceQueuePosition => _traitEffectSelectEnhanceQueuePosition;
    public GridFightBattleStatistics LastBattleStatistics { get; private set; } = GridFightBattleStatistics.Empty;
    public IReadOnlyList<GridFightRoleSwitchEntryState> RoleSwitchEntries =>
        _catalog.RoleSwitchGroups
            .Select(group => new GridFightRoleSwitchEntryState(
                group.RoleIdList,
                _roleSwitchIds.GetValueOrDefault(group.BaseRoleId, group.BaseRoleId)))
            .ToList();

    public IReadOnlyDictionary<string, uint> RefreshRoleSavedValues()
    {
        var next = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var role in Roles.Where(role => IsActivePosition(role.Position)))
            foreach (var (key, value) in ResolveRoleSavedValues(role, role.RoleId))
                next[key] = Math.Max(next.GetValueOrDefault(key), value);
        // Growth the battle reported for avatars whose table rows list no saved-value key (e.g. 飞霄's 军衔).
        foreach (var (key, value) in _reportedRoleSavedValues)
            next.TryAdd(key, value);
        ApplyAhaSavedValues(next);

        var changes = _roleSavedValues.Keys
            .Concat(next.Keys)
            .Distinct(StringComparer.Ordinal)
            .Where(key => _roleSavedValues.GetValueOrDefault(key) != next.GetValueOrDefault(key))
            .Order(StringComparer.Ordinal)
            .ToDictionary(key => key, key => next.GetValueOrDefault(key), StringComparer.Ordinal);
        _roleSavedValues.Clear();
        foreach (var (key, value) in next) _roleSavedValues[key] = value;
        return changes;
    }

    public IReadOnlyDictionary<string, uint> ResolveRoleSavedValues(GridFightRoleState role, uint roleId)
    {
        var values = OverlayReportedRoleSavedValues(_catalog.ResolveRoleSavedValues(roleId, role.Star));
        if (!IsSeasonTwoRules || (roleId != PhainonRoleId && !IsAhaRole(roleId))) return values;
        var withSeasonValues = new Dictionary<string, uint>(values, StringComparer.Ordinal);
        ApplySeasonTwoRoleSavedValues(roleId, withSeasonValues);
        if (IsAhaRole(roleId)) withSeasonValues[AhaGrowthSavedValueKey] = _ahaGrowth;
        return withSeasonValues;
    }

    public GridFightRoleSwitchUpdate? ConsumeRoleSwitchUpdate()
    {
        var update = _roleSwitchUpdate;
        _roleSwitchUpdate = null;
        return update;
    }

    private void RecalculateRoleSwitches(bool emit = true)
    {
        var previousRoleIds = Roles.ToDictionary(role => role.UniqueId, role => role.RoleId);
        var previousSwitchIds = _roleSwitchIds.ToDictionary(pair => pair.Key, pair => pair.Value);
        var currentSwitchIds = ResolveRoleSwitchIds();
        foreach (var role in Roles)
        {
            var group = _catalog.RoleSwitchGroups.FirstOrDefault(candidate =>
                candidate.RoleIdList.Contains(role.RoleId));
            if (group != null)
                role.RoleId = currentSwitchIds[group.BaseRoleId];
        }

        _roleSwitchIds = currentSwitchIds;
        if (!emit)
        {
            _roleSwitchUpdate = null;
            return;
        }

        var baselineRoleIds = _roleSwitchUpdate?.PreviousRoleIds ?? previousRoleIds;
        var baselineSwitchIds = _roleSwitchUpdate?.PreviousRoleSwitchIds ?? previousSwitchIds;
        var currentRoleIds = Roles.ToDictionary(role => role.UniqueId, role => role.RoleId);
        var changedRoleIds = baselineRoleIds.Keys
            .Intersect(currentRoleIds.Keys)
            .Where(uniqueId => baselineRoleIds.GetValueOrDefault(uniqueId) !=
                               currentRoleIds.GetValueOrDefault(uniqueId))
            .ToHashSet();
        var changedSwitchIds = baselineSwitchIds.Keys
            .Concat(currentSwitchIds.Keys)
            .Distinct()
            .Where(baseRoleId => baselineSwitchIds.GetValueOrDefault(baseRoleId, baseRoleId) !=
                                 currentSwitchIds.GetValueOrDefault(baseRoleId, baseRoleId))
            .ToHashSet();
        if (changedRoleIds.Count == 0 && changedSwitchIds.Count == 0)
        {
            _roleSwitchUpdate = null;
            return;
        }

        _roleSwitchUpdate = new GridFightRoleSwitchUpdate(
            new Dictionary<uint, uint>(baselineRoleIds),
            new Dictionary<uint, uint>(baselineSwitchIds),
            changedRoleIds,
            changedSwitchIds);
    }

    private Dictionary<uint, uint> ResolveRoleSwitchIds()
    {
        var result = new Dictionary<uint, uint>();
        foreach (var group in _catalog.RoleSwitchGroups)
        {
            var members = Roles
                .Where(role => group.RoleIdList.Contains(role.RoleId))
                .ToList();
            var useSwitchRole = group.Condition switch
            {
                "ByPlacedInBackRegion" => members.Any(role => IsBackPosition(role.Position)),
                "ByMaxTrait" => ResolveByMaxTrait(group),
                _ => false,
            };
            result[group.BaseRoleId] = useSwitchRole ? group.SwitchRoleId : group.BaseRoleId;
        }
        return result;
    }

    private bool ResolveByMaxTrait(GridFightRoleSwitchGroup group)
    {
        var groupRoleIds = group.RoleIdList.ToHashSet();
        var activeRoles = Roles
            .Where(role => IsActivePosition(role.Position) && !groupRoleIds.Contains(role.RoleId))
            .ToList();
        var firstTrait = group.ParamList.ElementAtOrDefault(0);
        var secondTrait = group.ParamList.ElementAtOrDefault(1);
        if (firstTrait == 0 || secondTrait == 0) return false;
        var firstCount = activeRoles.Count(role => ResolveRoleTraitIds(role).Contains(firstTrait));
        var secondCount = activeRoles.Count(role => ResolveRoleTraitIds(role).Contains(secondTrait));
        return firstCount > secondCount;
    }

    public GridFightSession(
        GridFightResourceCatalog catalog,
        uint season,
        uint divisionId,
        bool isOverlock,
        uint uniqueId)
    {
        _catalog = catalog;
        _roleSwitchIds = catalog.RoleSwitchGroups
            .ToDictionary(group => group.BaseRoleId, group => group.BaseRoleId);
        Season = season;
        DivisionId = divisionId;
        IsOverlock = isOverlock;
        UniqueId = uniqueId;
        PlayerLevel = catalog.InitialPlayerLevel;
        LineupHp = catalog.GetInitialLineupHp(divisionId);
        MaxLineupHp = catalog.InitialHp;
        PortalOffer = catalog.RollPortalOffer(3);
        var chapterCount = checked((int)catalog.StandardRoute.Max(node => node.ChapterId));
        CampIds = catalog.RollCampIds(chapterCount);
        RewardCampId = catalog.RollRewardCampId();
        var bossNodes = catalog.StandardRoute
            .Where(node => node.NodeType == GridFightNodeTypeEnum.Boss)
            .OrderBy(node => node.ChapterId)
            .ToList();
        Bosses = CampIds.Zip(bossNodes, catalog.RollBossMonster).ToList();
        AffixIds = catalog.RollAffixIds(divisionId);
        if (catalog.RollPetPal() is { } petPal)
        {
            PetPalId = petPal.PetPalId;
            PetPalEffectId = petPal.EffectId;
        }
        PrepareCurrentNode();
        if (IsSeasonTwoRules)
        {
            InitializeSeasonTwoTraits();
            InitializeSeasonTwoOrigins();
        }
    }

    public uint MaxActiveRoles => Math.Min(TeamPopulationLimit,
        BattleMaxActiveRoles + EffectiveWealthDiamondBonus + LizhiActiveRoleBonus);

    public bool HandlePending(uint queuePosition, GridFightPendingRequestKind kind, uint portalId = 0)
    {
        if (queuePosition != PendingQueuePosition) return false;
        switch (kind)
        {
            case GridFightPendingRequestKind.PortalReroll
                when Phase == GridFightSessionPhase.SelectingPortal && PortalRerollCount < PortalFreeRerollCount:
                PortalOffer = _catalog.RollPortalOffer(3);
                PortalRerollCount++;
                return true;
            case GridFightPendingRequestKind.PortalSelect
                when Phase == GridFightSessionPhase.SelectingPortal && PortalOffer.Contains(portalId):
                if (portalId == BlueSeaPortalId)
                {
                    BeginBlueSeaNestedPortal();
                    return true;
                }
                FinalizeInitialPortalSelection(portalId, PendingQueuePosition + 1);
                return true;
            case GridFightPendingRequestKind.PortalSelect
                when Phase == GridFightSessionPhase.SelectingNestedPortal && PortalOffer.Contains(portalId):
                FinalizeInitialPortalSelection(portalId, 2);
                return true;
            case GridFightPendingRequestKind.RoundBegin
                when Phase == GridFightSessionPhase.AwaitingInitialRound:
                AdvanceRotationHeroPortalRound();
                RefreshShop(false);
                Phase = GridFightSessionPhase.AwaitingInitialOrb;
                PendingQueuePosition = 10;
                return true;
            case GridFightPendingRequestKind.ReturnPreparation
                when Phase is GridFightSessionPhase.AwaitingInitialOrb or GridFightSessionPhase.AwaitingInitialReturn:
                EnterPreparingPhase();
                PendingQueuePosition++;
                return true;
            case GridFightPendingRequestKind.RoundBegin
                when Phase == GridFightSessionPhase.AwaitingBattleRound:
                AdvanceRotationHeroPortalRound();
                PrepareLaborDispatchBossReward();
                PendingQueuePosition++;
                EnterCurrentNodePostRoundPhase();
                return true;
            case GridFightPendingRequestKind.RoundBegin
                when Phase == GridFightSessionPhase.AwaitingSupplyRound:
                AdvanceRotationHeroPortalRound();
                PrepareLaborDispatchBossReward();
                PendingQueuePosition++;
                // The node after a supply pick can itself be an elite branch (season-2 routes 2400-2801), an
                // investment node or the point where gold futures is due; resolve it like any other round begin.
                EnterCurrentNodePostRoundPhase();
                return true;
            case GridFightPendingRequestKind.ReturnPreparation
                when Phase == GridFightSessionPhase.AwaitingBattleReturn:
                Phase = GridFightSessionPhase.AwaitingBackToPrepare;
                PendingQueuePosition++;
                QueueRecurringFateDivinerAfterReturnPreparation();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Refreshes already used on the card that currently offers this augment (client RollTimes).</summary>
    public uint GetInvestmentRerollCount(uint augmentId) => (uint)_rerolledInvestmentIds.Count(id => id == augmentId);

    public bool IsInvestmentRerolled(uint augmentId) => GetInvestmentRerollCount(augmentId) >= InvestmentFreeRerollCount;

    public bool RerollInvestment(uint queuePosition, uint augmentId)
    {
        if (Phase != GridFightSessionPhase.SelectingInvestment ||
            queuePosition != PendingQueuePosition ||
            _goldFuturesInvestmentPending ||
            (_bonusInvestmentSource == 0 && IsForesightActive) ||
            IsInvestmentRerolled(augmentId))
            return false;
        var index = InvestmentOffer.ToList().IndexOf(augmentId);
        if (index < 0) return false;
        var quality = GameData.GridFightAugmentData[augmentId].Quality;
        var excluded = InvestmentOffer.Concat(ActiveInvestmentIds).Concat(_rerolledAwayInvestmentIds).ToHashSet();
        var replacement = _catalog.RollInvestmentOffer(ChapterId, quality, 1, excluded).SingleOrDefault();
        if (replacement == 0) return false;
        var offer = InvestmentOffer.ToList();
        offer[index] = replacement;
        InvestmentOffer = offer;
        // The card keeps its count under its new augment id.
        var used = GetInvestmentRerollCount(augmentId) + 1;
        _rerolledInvestmentIds.RemoveAll(id => id == augmentId);
        for (var count = 0u; count < used; count++) _rerolledInvestmentIds.Add(replacement);
        _rerolledAwayInvestmentIds.Add(augmentId);
        return true;
    }

    public bool SelectInvestment(uint queuePosition, uint augmentId)
    {
        if (Phase != GridFightSessionPhase.SelectingInvestment ||
            queuePosition != PendingQueuePosition ||
            !InvestmentOffer.Contains(augmentId))
            return false;
        var isGoldFuturesChild = _goldFuturesInvestmentPending;
        var isBonusChild = _bonusInvestmentSource != 0;
        var bonusReturnPhase = _bonusInvestmentReturnPhase;
        _bonusInvestmentSource = 0;
        OnForesightInvestmentSelected(augmentId);
        if (!ActiveInvestmentIds.Contains(augmentId)) ActiveInvestmentIds.Add(augmentId);
        PrepareInvestmentSelectionReward(augmentId);
        OnRewardInvestmentSelected(augmentId);
        ApplyAugmentSelectionEffects(augmentId);
        OnPortalInvestmentSelected();
        InvestmentOffer = [];
        ResetInvestmentRerolls();
        _goldFuturesInvestmentPending = false;
        if (isGoldFuturesChild) _lastGoldFuturesChildSelection = augmentId;
        PendingQueuePosition++;
        if (isGoldFuturesChild)
            EnterCurrentNodePostRoundPhase();
        else
            Phase = GridFightSessionPhase.AwaitingBattleReturn;
        if (isBonusChild)
        {
            if (bonusReturnPhase == GridFightSessionPhase.SelectingInvestment) EnterCurrentNodePostRoundPhase();
            else Phase = bonusReturnPhase;
        }
        TryBeginBonusInvestment(Phase);
        return true;
    }

    public bool RerollSupply(uint queuePosition)
    {
        if (Phase != GridFightSessionPhase.SelectingSupply ||
            queuePosition != PendingQueuePosition ||
            SupplyRerollCount != 0)
            return false;
        SupplyOffer = _catalog.RollSupplyOffer(SupplyOfferCount, _temporarilyUnlockedRestrictedRoleIds)
            .Select(offer => new GridFightSupplyOfferState(offer.RoleId, offer.EquipmentId))
            .ToList();
        SupplyOffer = ApplyPetPalSupplyRules(SupplyOffer);
        SupplyRerollCount = 1;
        return true;
    }

    public bool RerollEncounter(uint queuePosition)
    {
        if (Phase != GridFightSessionPhase.SelectingEncounter ||
            queuePosition != PendingQueuePosition ||
            _encounterRerollCount != 0)
            return false;
        EncounterOptions = RollEncounterOptionsWithRewards();
        _encounterRerollCount = 1;
        return EncounterOptions.Count == 2;
    }

    public bool SelectEncounter(uint queuePosition, uint eliteBranchId)
    {
        if (Phase != GridFightSessionPhase.SelectingEncounter ||
            queuePosition != PendingQueuePosition)
            return false;
        var selected = EncounterOptions.FirstOrDefault(option => option.EliteBranchId == eliteBranchId);
        if (selected == null) return false;
        BattlePlan = selected.BattlePlan;
        Phase = GridFightSessionPhase.AwaitingBattleReturn;
        PendingQueuePosition++;
        return true;
    }

    public bool UpdateEncounterSelection(uint eliteBranchId)
    {
        if (CurrentNode.NodeType != GridFightNodeTypeEnum.EliteBranch ||
            BattlePlan == null ||
            Phase is not (GridFightSessionPhase.AwaitingBattleReturn or
                GridFightSessionPhase.AwaitingBackToPrepare or
                GridFightSessionPhase.Preparing))
            return false;
        var selected = EncounterOptions.FirstOrDefault(option => option.EliteBranchId == eliteBranchId);
        if (selected == null) return false;
        BattlePlan = selected.BattlePlan;
        return true;
    }

    public GridFightSupplySelectionResult SelectSupply(uint queuePosition, IReadOnlyList<uint> indices)
    {
        if (Phase != GridFightSessionPhase.SelectingSupply ||
            queuePosition != PendingQueuePosition ||
            indices.Count != 1 ||
            indices[0] == 0 ||
            indices[0] > SupplyOffer.Count ||
            _catalog.GetNextNode(ChapterId, SectionId) == null)
            return GridFightSupplySelectionResult.Failed;

        var selected = SupplyOffer[checked((int)indices[0] - 1)];
        if (!TryAddEquipment(selected.EquipmentId, out var equipment, SupplyEquipmentSource))
            return GridFightSupplySelectionResult.Failed;

        var role = new GridFightRoleState
        {
            UniqueId = _nextEntityUniqueId++,
            RoleId = selected.RoleId,
            Star = Math.Max(selected.Star, 1u),
            Position = GetFreeBenchPosition(),
        };
        // Extra supply equipment (绯绯狐: 2 random emblems) arrives with the season-2 follow-up sync.
        _petPalPendingEquipment.AddRange(selected.ExtraEquipmentIds.Where(id => id != 0));
        Roles.Add(role);
        RecalculateRoleSwitches();
        var mergeSteps = MergeRoles(role.RoleId);
        RecalculateRoleSwitches();
        var acquisition = new GridFightRoleAcquisitionResult(
            true,
            [role],
            mergeSteps,
            GetFinalMergedRoles(mergeSteps));

        SupplyOffer = [];
        SupplyRerollCount = 0;
        if (!AdvanceToNextNode()) return GridFightSupplySelectionResult.Failed;
        Phase = GridFightSessionPhase.AwaitingSupplyRound;
        PendingQueuePosition += SupplySelectionQueueAdvance;
        acquisition = FinalizeRoleAcquisition(acquisition);
        return new GridFightSupplySelectionResult(
            true,
            acquisition,
            equipment);
    }

    public bool ClaimInitialOrb()
    {
        if (_orbClaimed) return true;
        if (Phase != GridFightSessionPhase.AwaitingInitialOrb) return false;
        _orbClaimed = true;
        Phase = GridFightSessionPhase.AwaitingInitialReturn;
        return true;
    }

    public uint AdjustGold(int delta)
    {
        if (delta >= 0)
        {
            Gold = (uint)Math.Min(uint.MaxValue, (ulong)Gold + (uint)delta);
            return Gold;
        }

        var amount = (ulong)(-(long)delta);
        Gold = amount >= Gold ? 0 : Gold - (uint)amount;
        return Gold;
    }

    public bool IsRestrictedRoleUnlockedForRun(uint roleId) =>
        !GridFightResourceCatalog.IsRestrictedRole(roleId) ||
        _temporarilyUnlockedRestrictedRoleIds.Contains(roleId);

    private void UnlockRestrictedRoleForRun(uint roleId)
    {
        if (GridFightResourceCatalog.IsRestrictedRole(roleId))
            _temporarilyUnlockedRestrictedRoleIds.Add(roleId);
    }

    private bool TryResolveOrbRewardsForCurrentRun(
        uint orbItemId,
        out IReadOnlyList<GridFightRewardEntry> rewards)
    {
        rewards = [];

        // Portal 109 talent recruitment is an investment-environment grant and intentionally
        // bypasses the automatic-source restricted-role filter. FinalizeRoleAcquisition() will
        // transiently unlock a restricted role if this orb legitimately rolls one.
        if (orbItemId == TalentRecruitmentOrbId)
            return GridFightSpecialFeatures.TryResolveOrb(orbItemId, out rewards);

        // Direct role-orbs are deterministic. A locked restricted role must not be obtainable
        // from the crystal/orb path until that role has been unlocked by another acquisition source.
        if (orbItemId is >= 101000 and < 102000 &&
            !IsRestrictedRoleUnlockedForRun(orbItemId - 100000))
            return false;

        // Generic crystals/orbs may resolve RandomAvatar pools at open time. Re-resolve until
        // the result contains no still-locked restricted role. This keeps the restriction local
        // to crystal rewards without changing investment/portal/GM bonus resolution.
        for (var attempt = 0; attempt < 128; attempt++)
        {
            if (!GridFightSpecialFeatures.TryResolveOrb(orbItemId, out var candidate))
                return false;
            if (candidate.All(reward =>
                    reward.Type != GridFightBonusTypeEnum.SpecificAvatar ||
                    IsRestrictedRoleUnlockedForRun(reward.ItemId)))
            {
                rewards = candidate;
                return true;
            }
        }

        return false;
    }

    public bool TryAddRole(uint roleId, uint star, out GridFightRoleAcquisitionResult acquisition)
    {
        acquisition = GridFightRoleAcquisitionResult.Failed;
        if (!GameData.GridFightRoleBasicInfoData.TryGetValue(roleId, out var basic) || basic.SeasonID != Season ||
            // Fake avatars (帕姆王 604202, a 开拓银轨 reward) have no AvatarConfig row in any layout.
            (basic.UsesNativeSkillOverrides && !basic.IsFakeAvatar && !GameData.AvatarConfigData.ContainsKey((int)basic.AvatarID)) ||
            !GameData.GridFightRoleStarData.ContainsKey(roleId << 4 | star))
            return false;

        var role = new GridFightRoleState
        {
            UniqueId = _nextEntityUniqueId++,
            RoleId = roleId,
            Star = star,
            Position = GetFreeBenchPosition(),
        };
        Roles.Add(role);
        RecalculateRoleSwitches();
        // All role acquisition sources must run through the same merge pipeline.
        // Shop purchases already did this, while orb/crystal rewards used TryAddRole()
        // and could leave three identical copies unmerged until the next shop purchase.
        var mergeSteps = MergeRoles(role.RoleId);
        RecalculateRoleSwitches();
        acquisition = FinalizeRoleAcquisition(new GridFightRoleAcquisitionResult(
            true,
            [role],
            mergeSteps,
            GetFinalMergedRoles(mergeSteps)));
        return true;
    }

    public bool TryAddEquipment(uint equipmentId, out GridFightEquipmentState equipment, uint source = 1, bool isLocked = false)
    {
        equipment = null!;
        if (!GameData.GridFightEquipmentData.ContainsKey(equipmentId)) return false;
        equipment = new GridFightEquipmentState
        {
            UniqueId = _nextEntityUniqueId++,
            EquipmentId = equipmentId,
            Source = source,
            IsLocked = isLocked,
        };
        Equipments.Add(equipment);
        TryAutoEquipTrackedEquipment(equipment);
        return true;
    }

    public bool TryAddOrb(uint orbItemId, out GridFightOrbState orb)
    {
        orb = null!;
        if (!GridFightSpecialFeatures.CanMaterializeOrb(orbItemId)) return false;
        orb = new GridFightOrbState
        {
            UniqueId = _nextEntityUniqueId++,
            OrbItemId = orbItemId,
        };
        Orbs.Add(orb);
        return true;
    }

    public bool TryAddConsumable(
        uint itemId,
        out GridFightConsumableChange change,
        uint count = 1)
    {
        change = null!;
        if (!GameData.GridFightConsumablesData.ContainsKey(itemId) || count == 0) return false;

        var consumable = Consumables.FirstOrDefault(item => item.ItemId == itemId);
        if (consumable == null)
        {
            consumable = new GridFightConsumableState
            {
                GroupId = _nextEntityUniqueId++,
                ItemId = itemId,
                Count = count,
            };
            Consumables.Add(consumable);
            change = new GridFightConsumableChange(consumable, true, count);
            return true;
        }

        var previousCount = consumable.Count;
        consumable.Count = (uint)Math.Min(uint.MaxValue, (ulong)previousCount + count);
        var stackDelta = consumable.Count - previousCount;
        if (stackDelta == 0) return false;
        change = new GridFightConsumableChange(consumable, false, stackDelta);
        return true;
    }

    public bool IsSpecialGoodsPurchase(IReadOnlyList<uint> indices)
    {
        if (indices.Count == 0) return false;
        return indices.All(index => Shop.FirstOrDefault(slot => slot.Index == index) is
        {
            SoldOut: false,
            SpecialGoodsId: not 0,
        });
    }

    public IReadOnlyList<GridFightSpecialGoodsPurchaseResult> BuySpecialGoods(IReadOnlyList<uint> indices)
    {
        if (Phase != GridFightSessionPhase.Preparing ||
            indices.Count == 0 ||
            indices.Distinct().Count() != indices.Count ||
            !IsSpecialGoodsPurchase(indices))
            return [];

        var results = new List<GridFightSpecialGoodsPurchaseResult>();
        foreach (var index in indices)
        {
            var slot = Shop.Single(goods => goods.Index == index);
            // An earlier Aha good of the same request may have refilled the shop with roles already.
            if (slot.SpecialGoodsId == 0 || slot.SoldOut) continue;
            GridFightRewardApplication application;
            if (IsAhaGoods(slot.SpecialGoodsId))
            {
                if (Gold < slot.Price) return [];
                Gold -= slot.Price;
                application = ApplyAhaSpecialGoods(slot.SpecialGoodsId);
                if (!application.Success)
                {
                    Gold += slot.Price;
                    return [];
                }
                application.GoldChanged = true;
                if (slot.Price > 0) _spentGoldThisNode = true;
            }
            else
            {
                application = ApplyCyreneSpecialGoods(slot.SpecialGoodsId);
            }
            if (!application.Success) return [];
            slot.SoldOut = true;
            results.Add(new GridFightSpecialGoodsPurchaseResult(index, slot.SpecialGoodsId, application));
        }
        return results;
    }

    public IReadOnlyList<GridFightRewardApplication> OpenOrbs(
        IReadOnlyList<uint> uniqueIds,
        bool takeAll)
    {
        var selected = takeAll
            ? Orbs.ToList()
            : uniqueIds.Distinct()
                .Select(uniqueId => Orbs.FirstOrDefault(orb => orb.UniqueId == uniqueId))
                .Where(orb => orb != null)
                .Cast<GridFightOrbState>()
                .ToList();
        if (selected.Count == 0 || (!takeAll && selected.Count != uniqueIds.Distinct().Count())) return [];

        var resolved = new List<(GridFightOrbState Orb, IReadOnlyList<GridFightRewardEntry> Rewards)>();
        foreach (var orb in selected)
        {
            if (TryResolveOrbRewardsForCurrentRun(orb.OrbItemId, out var rewards)) resolved.Add((orb, rewards));
            // "Open all" leaves an orb the server cannot open in the bag instead of refusing the whole batch.
            else if (!takeAll) return [];
        }
        if (resolved.Count == 0) return [];

        var snapshot = CaptureRewardState();
        try
        {
            var applications = new List<GridFightRewardApplication>();
            foreach (var (orb, rewards) in resolved)
            {
                var application = ApplyRewards(rewards, orb.UniqueId, orb.OrbItemId);
                if (!application.Success)
                {
                    RestoreRewardState(snapshot);
                    return [];
                }
                applications.Add(application);
            }
            foreach (var (orb, _) in resolved) Orbs.Remove(orb);
            return applications;
        }
        catch
        {
            RestoreRewardState(snapshot);
            return [];
        }
    }

    public IReadOnlyList<GridFightTalentOrbReward> GrantOfficialInitialRoundRewards(
        out GridFightConsumableChange? initialConsumable)
    {
        initialConsumable = null;
        if (_officialInitialRoundGranted) return [];

        var nextEntityUniqueId = _nextEntityUniqueId;
        if (!TryAddOrb(OfficialInitialOrbId, out var orb)) return [];
        if (!TryAddConsumable(OfficialInitialConsumableId, out var consumable))
        {
            Orbs.Remove(orb);
            _nextEntityUniqueId = nextEntityUniqueId;
            return [];
        }

        _officialInitialRoundGranted = true;
        _triggeredTalentRewards.Add((
            OfficialInitialTalentId,
            OfficialInitialTalentEffectId,
            GridFightTalentRewardTrigger.PlaneEnter,
            ChapterId));
        initialConsumable = consumable;
        return [new GridFightTalentOrbReward(
            OfficialInitialTalentId,
            OfficialInitialTalentEffectId,
            [orb])];
    }

    public IReadOnlyList<GridFightTalentOrbReward> TakeTalentOrbRewards(
        GridFightTalentRewardTrigger trigger,
        uint chapterId)
    {
        var result = new List<GridFightTalentOrbReward>();
        foreach (var rule in GameData.GridFightRewardRulesConfig.TalentRules.Where(rule =>
                     rule.Trigger == trigger && rule.ChapterIds.Contains(chapterId) && rule.AppliesToSeason(Season)))
        {
            var key = (rule.TalentId, rule.EffectId, rule.Trigger, chapterId);
            if (_triggeredTalentRewards.Contains(key)) continue;
            var nextEntityUniqueId = _nextEntityUniqueId;
            var added = new List<GridFightOrbState>();
            foreach (var orbId in rule.OrbIds)
            {
                if (TryAddOrb(orbId, out var orb))
                {
                    added.Add(orb);
                    continue;
                }
                Orbs.RemoveAll(orb => added.Contains(orb));
                _nextEntityUniqueId = nextEntityUniqueId;
                added.Clear();
                break;
            }
            if (added.Count == 0) continue;
            _triggeredTalentRewards.Add(key);
            result.Add(new GridFightTalentOrbReward(rule.TalentId, rule.EffectId, added));
        }
        return result;
    }

    private GridFightRewardStateSnapshot CaptureRewardState()
    {
        return new GridFightRewardStateSnapshot(
            _nextEntityUniqueId,
            _cyrenePoemShopUnlocked,
            _silverWolfHackUnlocked,
            _traitEffectSelectEnhanceQueuePosition,
            _headPlayerPending,
            _silverWolfTraitInitialized,
            _silverWolfUpgradeRouteClosed,
            _silverWolfCurrentRoleId,
            _handledHeadPlayerStates.ToList(),
            _heroEntranceRoleUniqueId,
            _heroEntranceRoleId,
            _heroEntranceRemaining,
            _heroEntrancePerfectAdvancePending,
            _heroEntranceStateDirty,
            Gold,
            PlayerLevel,
            PlayerExp,
            Roles.Select(CloneRole).ToList(),
            Equipments.Select(equipment => new GridFightEquipmentState
            {
                UniqueId = equipment.UniqueId,
                EquipmentId = equipment.EquipmentId,
                Source = equipment.Source,
                IsLocked = equipment.IsLocked,
                IsTemporary = equipment.IsTemporary,
            }).ToList(),
            Orbs.Select(orb => new GridFightOrbState
            {
                UniqueId = orb.UniqueId,
                OrbItemId = orb.OrbItemId,
            }).ToList(),
            Consumables.Select(consumable => new GridFightConsumableState
            {
                GroupId = consumable.GroupId,
                ItemId = consumable.ItemId,
                Count = consumable.Count,
            }).ToList(),
            ForgeItems.Select(forgeItem => new GridFightForgeItemState
            {
                UniqueId = forgeItem.UniqueId,
                ForgeItemId = forgeItem.ForgeItemId,
                Position = forgeItem.Position,
                EquipmentChoices = forgeItem.EquipmentChoices.ToList(),
            }).ToList(),
            Shop.Select(slot => new GridFightShopSlot
            {
                Index = slot.Index,
                RoleId = slot.RoleId,
                SpecialGoodsId = slot.SpecialGoodsId,
                Star = slot.Star,
                Price = slot.Price,
                SoldOut = slot.SoldOut,
            }).ToList(),
            _spentGoldThisNode,
            new Dictionary<uint, uint>(_investmentNodeCounters),
            _economyEffects.ToList());
    }

    private void RestoreRewardState(GridFightRewardStateSnapshot snapshot)
    {
        _nextEntityUniqueId = snapshot.NextEntityUniqueId;
        _cyrenePoemShopUnlocked = snapshot.CyrenePoemShopUnlocked;
        _silverWolfHackUnlocked = snapshot.SilverWolfHackUnlocked;
        _traitEffectSelectEnhanceQueuePosition = snapshot.TraitEffectSelectEnhanceQueuePosition;
        _headPlayerPending = snapshot.HeadPlayerPending;
        _silverWolfTraitInitialized = snapshot.SilverWolfTraitInitialized;
        _silverWolfUpgradeRouteClosed = snapshot.SilverWolfUpgradeRouteClosed;
        _silverWolfCurrentRoleId = snapshot.SilverWolfCurrentRoleId;
        _handledHeadPlayerStates.Clear();
        foreach (var state in snapshot.HandledHeadPlayerStates) _handledHeadPlayerStates.Add(state);
        _heroEntranceRoleUniqueId = snapshot.HeroEntranceRoleUniqueId;
        _heroEntranceRoleId = snapshot.HeroEntranceRoleId;
        _heroEntranceRemaining = snapshot.HeroEntranceRemaining;
        _heroEntrancePerfectAdvancePending = snapshot.HeroEntrancePerfectAdvancePending;
        _heroEntranceStateDirty = snapshot.HeroEntranceStateDirty;
        Gold = snapshot.Gold;
        PlayerLevel = snapshot.PlayerLevel;
        PlayerExp = snapshot.PlayerExp;
        Roles.Clear();
        Roles.AddRange(snapshot.Roles.Select(CloneRole));
        Equipments.Clear();
        Equipments.AddRange(snapshot.Equipments);
        Orbs.Clear();
        Orbs.AddRange(snapshot.Orbs);
        Consumables.Clear();
        Consumables.AddRange(snapshot.Consumables);
        ForgeItems.Clear();
        ForgeItems.AddRange(snapshot.ForgeItems);
        Shop.Clear();
        Shop.AddRange(snapshot.Shop);
        _spentGoldThisNode = snapshot.SpentGoldThisNode;
        _investmentNodeCounters.Clear();
        foreach (var (id, count) in snapshot.InvestmentCounters) _investmentNodeCounters[id] = count;
        _economyEffects.Clear();
        _economyEffects.AddRange(snapshot.EconomyEffects);
        RecalculateRoleSwitches(false);
    }

    private static GridFightRoleState CloneRole(GridFightRoleState role)
    {
        var clone = new GridFightRoleState
        {
            UniqueId = role.UniqueId,
            RoleId = role.RoleId,
            Star = role.Star,
            Position = role.Position,
        };
        clone.EquippedEquipmentUniqueIds.AddRange(role.EquippedEquipmentUniqueIds);
        clone.HasPowerGrowth = role.HasPowerGrowth;
        foreach (var (property, bonus) in role.PermanentPropertyBonuses)
            clone.PermanentPropertyBonuses[property] = bonus;
        return clone;
    }

    private sealed record GridFightRewardStateSnapshot(
        uint NextEntityUniqueId,
        bool CyrenePoemShopUnlocked,
        bool SilverWolfHackUnlocked,
        uint? TraitEffectSelectEnhanceQueuePosition,
        GridFightHeadPlayerPendingState? HeadPlayerPending,
        bool SilverWolfTraitInitialized,
        bool SilverWolfUpgradeRouteClosed,
        uint SilverWolfCurrentRoleId,
        IReadOnlyList<(uint UniqueId, uint RoleId, uint Star)> HandledHeadPlayerStates,
        uint HeroEntranceRoleUniqueId,
        uint HeroEntranceRoleId,
        uint HeroEntranceRemaining,
        bool HeroEntrancePerfectAdvancePending,
        bool HeroEntranceStateDirty,
        uint Gold,
        uint PlayerLevel,
        uint PlayerExp,
        IReadOnlyList<GridFightRoleState> Roles,
        IReadOnlyList<GridFightEquipmentState> Equipments,
        IReadOnlyList<GridFightOrbState> Orbs,
        IReadOnlyList<GridFightConsumableState> Consumables,
        IReadOnlyList<GridFightForgeItemState> ForgeItems,
        IReadOnlyList<GridFightShopSlot> Shop,
        bool SpentGoldThisNode,
        IReadOnlyDictionary<uint, uint> InvestmentCounters,
        IReadOnlyList<GridFightAugmentEffectResult> EconomyEffects);

    public GridFightTraitEnhanceActivationResult ActivateSilverWolfHackConsole(
        uint queuePosition,
        uint enhanceOptionId)
    {
        if (_traitEffectSelectEnhanceQueuePosition != queuePosition ||
            enhanceOptionId != GridFightSpecialFeatures.SilverWolfEnhanceOptionId ||
            HackConsole != null)
            return GridFightTraitEnhanceActivationResult.Failed;

        if (!GridFightSpecialFeatures.TryResolveSilverWolfEquipmentId(out var equipmentId) ||
            !TryAddEquipment(equipmentId, out var equipment, GridFightSpecialFeatures.HackEquipmentSource))
            return GridFightTraitEnhanceActivationResult.Failed;

        HackConsole = new GridFightHackConsoleState { GameItemUniqueId = _nextEntityUniqueId++ };
        _traitEffectSelectEnhanceQueuePosition = null;
        return new GridFightTraitEnhanceActivationResult(true, queuePosition, equipment);
    }

    public GridFightHackOptionResult UseHackOption(uint optionId, uint enemyHpPercent)
    {
        var console = HackConsole;
        if (console == null) return GridFightHackOptionResult.Failed;
        var application = new GridFightRewardApplication { Success = true };

        switch (optionId)
        {
            case 0 when enemyHpPercent is >= GridFightSpecialFeatures.MinimumEnemyHpPercent and
                <= GridFightSpecialFeatures.MaximumEnemyHpPercent:
                console.EnemyHpPercent = enemyHpPercent;
                break;
            case GridFightSpecialFeatures.HackAddGoldOptionId when console.RemainingGoldActions > 0:
                console.RemainingGoldActions--;
                AdjustGold(checked((int)GridFightSpecialFeatures.HackGoldPerAction));
                application.GoldChanged = true;
                break;
            case GridFightSpecialFeatures.HackFullHealOptionId when console.RemainingHealActions > 0:
                console.RemainingHealActions--;
                LineupHp = MaxLineupHp;
                application.LineupHpChanged = true;
                break;
            case GridFightSpecialFeatures.HackGrantOrbOptionId when console.RemainingOrbActions > 0:
                var index = GridFightSpecialFeatures.HackOrbActionCount - console.RemainingOrbActions;
                if (!TryAddOrb(GridFightSpecialFeatures.HackOrbIds[checked((int)index)], out var orb))
                    return GridFightHackOptionResult.Failed;
                console.RemainingOrbActions--;
                application.AddedOrbs.Add(orb);
                break;
            default:
                return GridFightHackOptionResult.Failed;
        }

        return new GridFightHackOptionResult(true, optionId, application);
    }

    private GridFightRewardApplication ApplyCyreneSpecialGoods(uint specialGoodsId)
    {
        switch (specialGoodsId)
        {
            case GridFightSpecialFeatures.CyreneHealGoodsId:
                LineupHp = (uint)Math.Min(MaxLineupHp, (ulong)LineupHp + GridFightSpecialFeatures.CyreneHealAmount);
                return new GridFightRewardApplication { Success = true, LineupHpChanged = true };
            case GridFightSpecialFeatures.CyreneWealthGoodsId:
                return GridFightSpecialFeatures.TryResolveBonus(
                    GridFightSpecialFeatures.CyreneWealthBonusId,
                    out var wealthRewards)
                    ? ApplyRewards(wealthRewards)
                    : new GridFightRewardApplication();
            case GridFightSpecialFeatures.CyreneInsigniaGoodsId:
                return GridFightSpecialFeatures.TryResolveBonus(
                    GridFightSpecialFeatures.CyreneInsigniaBonusId,
                    out var insigniaRewards)
                    ? ApplyRewards(insigniaRewards)
                    : new GridFightRewardApplication();
            case GridFightSpecialFeatures.CyreneCombatGoodsId:
                HasCyreneCombatEnhancement = true;
                return new GridFightRewardApplication { Success = true, RolePropertiesChanged = true };
            case GridFightSpecialFeatures.CyreneHeroGoodsId:
                return GridFightSpecialFeatures.TryResolveBonus(
                    GridFightSpecialFeatures.CyreneHeroBonusId,
                    out var heroRewards)
                    ? ApplyRewards(heroRewards)
                    : new GridFightRewardApplication();
            default:
                return new GridFightRewardApplication();
        }
    }

    private GridFightRewardApplication ApplyRewards(
        IReadOnlyList<GridFightRewardEntry> rewards,
        uint sourceUniqueId = 0,
        uint sourceItemId = 0)
    {
        var application = new GridFightRewardApplication
        {
            Success = true,
            SourceUniqueId = sourceUniqueId,
            SourceItemId = sourceItemId,
        };
        foreach (var reward in rewards)
        {
            application.Drops.Add(reward);
            switch (reward.Type)
            {
                case GridFightBonusTypeEnum.Gold:
                    Gold = (uint)Math.Min(uint.MaxValue, (ulong)Gold + reward.Count);
                    application.GoldChanged = true;
                    break;
                case GridFightBonusTypeEnum.Exp:
                    var previousLevel = PlayerLevel;
                    var previousExp = PlayerExp;
                    AddPlayerExp(reward.Count);
                    application.PlayerLevelChanged |= PlayerLevel != previousLevel;
                    application.PlayerExpChanged |= PlayerExp != previousExp;
                    break;
                case GridFightBonusTypeEnum.SpecificAvatar:
                    for (var index = 0u; index < reward.Count; index++)
                    {
                        if (!TryAddRole(reward.ItemId, reward.RoleStar == 0 ? 1 : reward.RoleStar, out var acquisition))
                            return new GridFightRewardApplication();
                        application.RoleAcquisitions.Add(acquisition);
                    }
                    break;
                case GridFightBonusTypeEnum.Item:
                    if (GameData.GridFightEquipmentData.ContainsKey(reward.ItemId))
                    {
                        for (var index = 0u; index < reward.Count; index++)
                        {
                            if (!TryAddEquipment(reward.ItemId, out var equipment))
                                return new GridFightRewardApplication();
                            application.AddedEquipments.Add(equipment);
                        }
                    }
                    else if (GameData.GridFightConsumablesData.ContainsKey(reward.ItemId))
                    {
                        if (!TryAddConsumable(reward.ItemId, out var change, reward.Count))
                            return new GridFightRewardApplication();
                        application.ConsumableChanges.Add(change);
                    }
                    else if (GameData.GridFightForgeData.TryGetValue(reward.ItemId, out var hireForge) &&
                             hireForge.FuncType == GridFightForgeFuncTypeEnum.Role)
                    {
                        // Hiring book (聘用书, 99992-99996, FuncType Role, params [cost, star]). Its 4-pick prompt
                        // (GridFightForgeRoleActionInfo) has unconfirmed fields and no capture, so the book is
                        // redeemed on receipt for a random role of that cost and star instead of a choice.
                        var hireCost = hireForge.ParamList.ElementAtOrDefault(0);
                        var hireStar = Math.Max(1u, hireForge.ParamList.ElementAtOrDefault(1));
                        for (var index = 0u; index < reward.Count; index++)
                        {
                            var hired = _catalog.RollRolesByCost(hireCost, 1, _temporarilyUnlockedRestrictedRoleIds).FirstOrDefault();
                            if (hired == null || !TryAddRole(hired.ID, hireStar, out var hireAcquisition))
                                return new GridFightRewardApplication();
                            application.RoleAcquisitions.Add(hireAcquisition);
                        }
                    }
                    else if (GameData.GridFightForgeData.ContainsKey(reward.ItemId))
                    {
                        for (var index = 0u; index < reward.Count; index++)
                        {
                            if (!TryAddForgeItem(reward.ItemId, out var forgeItem))
                                return new GridFightRewardApplication();
                            application.AddedForgeItems.Add(forgeItem);
                        }
                    }
                    else
                    {
                        return new GridFightRewardApplication();
                    }
                    break;
                case GridFightBonusTypeEnum.Orb:
                    for (var index = 0u; index < reward.Count; index++)
                    {
                        if (!TryAddOrb(reward.ItemId, out var orb)) return new GridFightRewardApplication();
                        application.AddedOrbs.Add(orb);
                    }
                    break;
                case GridFightBonusTypeEnum.SpotLightReplaceCnt:
                case GridFightBonusTypeEnum.SpotLightFreeRefreshCnt:
                    AddSpotLightCharges(reward.Type, reward.Count);
                    break;
                default:
                    return new GridFightRewardApplication();
            }
        }
        return application;
    }

    private bool TryAddForgeItem(uint forgeItemId, out GridFightForgeItemState forgeItem)
    {
        forgeItem = null!;
        var choices = _catalog.RollForgeEquipment(forgeItemId);
        var position = GetFreeBenchPosition();
        if (choices.Count == 0 || position == 0) return false;
        forgeItem = new GridFightForgeItemState
        {
            UniqueId = _nextEntityUniqueId++,
            ForgeItemId = forgeItemId,
            Position = position,
            EquipmentChoices = choices,
        };
        ForgeItems.Add(forgeItem);
        return true;
    }

    public GridFightForgeUseResult UseForge(uint uniqueId, uint targetIndex)
    {
        // Round-begin rewards (e.g. a 遭遇 进阶武装箱) arrive while the client already shows the board but has not sent
        // GridFightBackToPrepareCsReq yet; opening a box only adds an item, so allow it in that window too.
        if (Phase is not (GridFightSessionPhase.Preparing or GridFightSessionPhase.AwaitingBackToPrepare))
            return GridFightForgeUseResult.Failed;
        var forgeItem = ForgeItems.FirstOrDefault(candidate => candidate.UniqueId == uniqueId);
        if (forgeItem == null ||
            !GameData.GridFightForgeData.TryGetValue(forgeItem.ForgeItemId, out var forge) ||
            forge.FuncType != GridFightForgeFuncTypeEnum.Equip ||
            targetIndex >= forgeItem.EquipmentChoices.Count)
            return GridFightForgeUseResult.Failed;

        var equipmentId = forgeItem.EquipmentChoices[checked((int)targetIndex)];
        if (!TryAddEquipment(equipmentId, out var equipment))
            return GridFightForgeUseResult.Failed;

        ForgeItems.Remove(forgeItem);
        var promoted = PromoteOverflowItemsToBench();
        return new GridFightForgeUseResult(
            true,
            uniqueId,
            equipment,
            promoted.Roles,
            promoted.ForgeItems);
    }

    public bool TryMoveToSection(uint chapterId, uint sectionId, out uint finishedPendingPosition)
    {
        finishedPendingPosition = 0;
        if (Phase == GridFightSessionPhase.InBattle ||
            !_catalog.StandardRoute.Any(node => node.ChapterId == chapterId && node.SectionId == sectionId))
            return false;

        if (Phase is GridFightSessionPhase.SelectingPortal or
            GridFightSessionPhase.AwaitingInitialRound or
            GridFightSessionPhase.AwaitingInitialOrb or
            GridFightSessionPhase.AwaitingInitialReturn or
            GridFightSessionPhase.AwaitingBattleRound or
            GridFightSessionPhase.AwaitingBattleReturn)
            finishedPendingPosition = PendingQueuePosition;

        ChapterId = chapterId;
        SectionId = sectionId;
        InvestmentOffer = [];
        SupplyOffer = [];
        ResetInvestmentRerolls();
        SupplyRerollCount = 0;
        PrepareCurrentNode();
        RefreshShop(false);
        Phase = GridFightSessionPhase.AwaitingBattleRound;
        return true;
    }

    public GridFightRoleAcquisitionResult BuyGoods(IReadOnlyList<uint> indices)
    {
        if (Phase != GridFightSessionPhase.Preparing || HasBlockingPendingSelection || indices.Count == 0 || indices.Distinct().Count() != indices.Count)
            return GridFightRoleAcquisitionResult.Failed;
        var slots = indices.Select(index => Shop.FirstOrDefault(slot => slot.Index == index)).ToList();
        if (slots.Any(slot => slot == null || slot.SoldOut || slot.SpecialGoodsId != 0))
            return GridFightRoleAcquisitionResult.Failed;
        var cost = slots.OfType<GridFightShopSlot>().Aggregate(0UL, (sum, slot) => sum + slot.Price);
        if (cost > Gold) return GridFightRoleAcquisitionResult.Failed;

        SpendGold((uint)cost);
        PrepareTwinPrimesRewards(slots.OfType<GridFightShopSlot>().Select(slot => slot.RoleId));
        var acquiredRoles = new List<GridFightRoleState>();
        var mergeSteps = new List<GridFightRoleMergeStep>();
        foreach (var slot in slots.OfType<GridFightShopSlot>())
        {
            slot.SoldOut = true;
            var role = new GridFightRoleState
            {
                UniqueId = _nextEntityUniqueId++,
                RoleId = slot.RoleId,
                Star = PurchaseStar(slot),
                Position = GetFreeBenchPosition(),
            };
            Roles.Add(role);
            acquiredRoles.Add(role);
            mergeSteps.AddRange(MergeRoles(role.RoleId));
        }
        OnEconomyPurchase(slots.OfType<GridFightShopSlot>());
        RecalculateRoleSwitches();
        return FinalizeRoleAcquisition(new GridFightRoleAcquisitionResult(
            true,
            acquiredRoles,
            mergeSteps,
            GetFinalMergedRoles(mergeSteps)));
    }

    public GridFightSessionChange RecycleRole(uint uniqueId)
    {
        if (Phase != GridFightSessionPhase.Preparing || HasBlockingPendingSelection) return GridFightSessionChange.Failed;
        var role = Roles.FirstOrDefault(candidate => candidate.UniqueId == uniqueId);
        // The summoned Dan Heng • Permansor Terrae (丹恒•腾荒) cannot be sold (the client refuses locally as well).
        if (role == null || IsPopulationFreeRole(role)) return GridFightSessionChange.Failed;
        AddGold(_catalog.GetSellPrice(role.RoleId, role.Star));
        DestroyDiceFilledOnRole(role);
        Roles.Remove(role);
        RecalculateRoleSwitches();
        var promoted = PromoteOverflowItemsToBench();
        return new GridFightSessionChange(true, [], [uniqueId], promoted.Roles, promoted.ForgeItems);
    }

    public bool BuyExp()
    {
        if (Phase != GridFightSessionPhase.Preparing || Gold < CurrentBuyExpCost) return false;
        var current = _catalog.GetPlayerLevel(PlayerLevel);
        if (current.LevelUpExp == 0) return false;

        SpendGold(CurrentBuyExpCost);
        AddPlayerExp(_catalog.BuyExpAmount + PetPalBuyExpBonus());
        PromoteShopRoleAfterBuyingExp();
        return true;
    }

    private void AddPlayerExp(uint amount)
    {
        var previousLevel = PlayerLevel;
        PlayerExp += amount;
        // Season 2 appends a level-11 row reachable only through effects; level 10 has no LevelUpExp, so without
        // the cap any exp gained at level 10 would roll straight into it.
        while (PlayerLevel < _catalog.MaxPlayerLevel && GameDataHasNextLevel() &&
               _catalog.GetPlayerLevel(PlayerLevel).LevelUpExp is var levelUpExp and > 0 &&
               PlayerExp >= levelUpExp)
        {
            PlayerExp -= levelUpExp;
            PlayerLevel++;
        }
        if (PlayerLevel != previousLevel)
        {
            QueueMarketVitalitySavedValue(109);
            OnEconomyLevelChanged();
        }
    }

    public GridFightSessionChange UpdatePositions(IReadOnlyList<(uint UniqueId, uint Position)> updates)
    {
        if (Phase != GridFightSessionPhase.Preparing || HasBlockingPendingSelection || updates.Count == 0 ||
            updates.Select(update => update.UniqueId).Distinct().Count() != updates.Count)
            return GridFightSessionChange.Failed;
        var rolesById = Roles.ToDictionary(role => role.UniqueId);
        var forgeItemsById = ForgeItems.ToDictionary(item => item.UniqueId);
        if (updates.Any(update => rolesById.ContainsKey(update.UniqueId) &&
                                  IsHeroEntrancePositionBanned(update.UniqueId, update.Position)))
            return GridFightSessionChange.Failed with
            {
                FailureRetcode = HeroEntrancePositionBanRetcode,
            };
        if (updates.Any(update =>
                rolesById.ContainsKey(update.UniqueId) == forgeItemsById.ContainsKey(update.UniqueId) ||
                rolesById.ContainsKey(update.UniqueId) && !IsValidPosition(update.Position) ||
                forgeItemsById.ContainsKey(update.UniqueId) && !IsBenchPosition(update.Position) ||
                PepeNpc != null && update.Position == PepeNpc.Position ||
                AstaDogNpc != null && update.Position == AstaDogNpc.Position ||
                update.Position != 0 && OwlbertNpcs.Any(npc => npc.Position == update.Position)))
            return GridFightSessionChange.Failed;

        var rolePositions = Roles.ToDictionary(role => role.UniqueId, role => role.Position);
        var forgePositions = ForgeItems.ToDictionary(item => item.UniqueId, item => item.Position);
        foreach (var update in updates)
            if (rolePositions.ContainsKey(update.UniqueId))
                rolePositions[update.UniqueId] = update.Position;
            else
                forgePositions[update.UniqueId] = update.Position;
        var occupied = rolePositions.Values.Concat(forgePositions.Values)
            .Where(position => position != 0)
            .ToList();
        if (PepeNpc != null) occupied.Add(PepeNpc.Position);
        if (AstaDogNpc != null) occupied.Add(AstaDogNpc.Position);
        occupied.AddRange(OwlbertNpcs.Where(npc => npc.Position != 0).Select(npc => npc.Position));
        if (occupied.Distinct().Count() != occupied.Count)
            return GridFightSessionChange.Failed;
        if (CountPopulationRoles(rolePositions.Select(pair => (pair.Key, pair.Value))) > MaxActiveRoles)
            return GridFightSessionChange.Failed;
        // TwoInOneSubBlockConstrait: the summoned Dan Heng • Permansor Terrae only moves inside the back area.
        if (updates.Any(update => update.UniqueId == _danHengSubUniqueId && _danHengSubUniqueId != 0 &&
                                  !IsBackPosition(update.Position)))
            return GridFightSessionChange.Failed;

        var updatedRoles = new List<GridFightRoleState>();
        var updatedForgeItems = new List<GridFightForgeItemState>();
        foreach (var update in updates)
        {
            if (rolesById.TryGetValue(update.UniqueId, out var role))
            {
                role.Position = update.Position;
                updatedRoles.Add(role);
            }
            else
            {
                var forgeItem = forgeItemsById[update.UniqueId];
                forgeItem.Position = update.Position;
                updatedForgeItems.Add(forgeItem);
            }
        }
        RecalculateRoleSwitches();
        var updatedRoleIds = updatedRoles.Select(role => role.UniqueId).ToHashSet();
        var updatedForgeItemIds = updatedForgeItems.Select(item => item.UniqueId).ToHashSet();
        var promoted = PromoteOverflowItemsToBench();
        updatedRoles.AddRange(promoted.Roles.Where(role => updatedRoleIds.Add(role.UniqueId)));
        updatedForgeItems.AddRange(promoted.ForgeItems.Where(item => updatedForgeItemIds.Add(item.UniqueId)));
        var headPlayer = RefreshHeadPlayerAfterRoleStateChange();
        RefreshFateDivinerAfterRoleStateChange();
        return new GridFightSessionChange(true, [], [], updatedRoles, updatedForgeItems)
        {
            SilverWolfTraitActivated = headPlayer.TraitActivated,
            HeadPlayerPendingStarted = headPlayer.PendingStarted,
        };
    }

    public void PrepareMortenaxBladeBattleEffects()
    {
        _mortenaxBladeBattleEffects = [];
        var effects = new List<GridFightMortenaxBladeBattleEffect>();
        foreach (var role in Roles.Where(role =>
                     role.RoleId == MortenaxBladeRoleId && IsActivePosition(role.Position)))
        {
            var equipped = GetEquippedEquipment(role).ToList();
            uint equipmentId;
            uint replacedUniqueId = 0;

            if (equipped.Count >= 3)
            {
                // Official talent: when already wearing 3 items, one item is temporarily promoted
                // to its privileged counterpart for this battle only.
                var upgradable = equipped
                    .Where(item => GameData.GridFightEquipUpgradeData.TryGetValue(item.EquipmentId, out var upgrade) &&
                                   upgrade.UpgradeID != 0 &&
                                   GameData.GridFightEquipmentData.ContainsKey(upgrade.UpgradeID))
                    .ToList();
                if (upgradable.Count == 0) continue;
                var selected = upgradable[Random.Shared.Next(upgradable.Count)];
                equipmentId = GameData.GridFightEquipUpgradeData[selected.EquipmentId].UpgradeID;
                replacedUniqueId = selected.UniqueId;
            }
            else
            {
                if (!GameData.GridFightRoleBasicInfoData.TryGetValue(role.RoleId, out var basic)) continue;
                equipmentId = _catalog.RollMortenaxBladeTemporaryEquipment(role.RoleId, basic.FrontBackType);
                if (equipmentId == 0) continue;
            }

            // Captures show the TraitEffect lifecycle only on the first activation in a run.
            var needsTraitSync = !_mortenaxBladeTraitEffectInitialized && effects.Count == 0;
            var temporaryInstanceId = needsTraitSync ? _nextEntityUniqueId++ : 0u;
            effects.Add(new GridFightMortenaxBladeBattleEffect(
                role.UniqueId,
                equipmentId,
                replacedUniqueId,
                temporaryInstanceId,
                needsTraitSync));
        }
        _mortenaxBladeBattleEffects = effects;
        PrepareXianzhouBattleSwaps();
    }

    public IReadOnlyList<(uint EquipmentId, uint UniqueId)> GetBattleEquipment(GridFightRoleState role)
    {
        var result = GetEquippedEquipment(role)
            .Select(item => (EquipmentId: ResolveXianzhouBattleEquipment(item.UniqueId, item.EquipmentId), item.UniqueId))
            .ToList();
        ApplyNanoModification(role, result);
        var effect = _mortenaxBladeBattleEffects.FirstOrDefault(item => item.RoleUniqueId == role.UniqueId);
        if (effect == null) return result;

        if (effect.ReplacedEquipmentUniqueId != 0)
        {
            var index = result.FindIndex(item => item.UniqueId == effect.ReplacedEquipmentUniqueId);
            if (index >= 0) result[index] = (effect.EquipmentId, 0u);
        }
        else
        {
            // Official battle packets carry temporary equipment without a persistent UniqueId.
            result.Add((effect.EquipmentId, 0u));
        }
        return result;
    }

    public void CompleteMortenaxBladeTraitEffectSync()
    {
        if (_mortenaxBladeBattleEffects.Any(effect => effect.NeedsTraitEffectSync))
            _mortenaxBladeTraitEffectInitialized = true;
    }

    public void ClearMortenaxBladeBattleEffects()
    {
        _mortenaxBladeBattleEffects = [];
        _xianzhouBattleSwaps.Clear();
    }

    public bool EnterBattle()
    {
        var activeRoleCount = CountPopulationRoles(Roles.Select(role => (role.UniqueId, role.Position)));
        if (Phase != GridFightSessionPhase.Preparing ||
            HasPendingSelection ||
            IsSpotLightSelectionDue ||
            !IsCombatNode(CurrentNode) ||
            BattlePlan == null ||
            activeRoleCount == 0 ||
            activeRoleCount > MaxActiveRoles)
            return false;
        RecordSpotLightBattleParticipants();
        Phase = GridFightSessionPhase.InBattle;
        return true;
    }

    /// <param name="reportedDrops">
    ///     Drops the client created inside the battle (GridFightBattleResultStatistics field 5), e.g. Trotters spawned by
    ///     season talent 4061 or a Trotter Virus. The planned reward-node drops are not part of it.
    /// </param>
    public GridFightBattleSettlementResult ResolveBattle(
        bool battleEndedNormally,
        GridFightBattleStatistics statistics,
        IReadOnlyList<GridFightRewardEntry>? reportedDrops = null)
    {
        if (Phase != GridFightSessionPhase.InBattle) return GridFightBattleSettlementResult.Failed;
        // Retiring (or losing) the battle settles it as a lost battle at the progress reached so far; returning to
        // the same node's preparation would let the team be rebuilt and the battle replayed without a penalty.
        if (!battleEndedNormally)
            statistics = statistics with { ProgressPercent = Math.Min(statistics.ProgressPercent, 99u) };

        RecordReportedRoleSavedValues(statistics);
        var progressPercent = ApplyPetPalGuard(Math.Min(statistics.ProgressPercent, 100u));
        statistics = statistics with { ProgressPercent = progressPercent };
        var isPerfectFinish = progressPercent == 100;
        var penaltyRule = _catalog.GetPenaltyRule(CurrentPenaltyRuleId);
        var lineupHpBefore = LineupHp;
        var isFinalBoss = CurrentNode.NodeType == GridFightNodeTypeEnum.Boss &&
                          _catalog.GetNextNode(ChapterId, SectionId) == null;
        var hpChanges = ApplyBattleHpChanges(
            penaltyRule,
            progressPercent,
            isPerfectFinish,
            isFinalBoss,
            statistics.RoundCount);
        var previousKeepWinCount = KeepWinCount;
        if (CurrentNode.NodeType is GridFightNodeTypeEnum.CampMonster or
            GridFightNodeTypeEnum.EliteBranch or GridFightNodeTypeEnum.Boss)
            KeepWinCount = isPerfectFinish ? KeepWinCount + 1 : 0;

        var settlementBalance = Gold;
        var basicGold = CurrentNode.BasicGoldReward;
        var interestGold = CalculateInvestmentInterest(settlementBalance);
        var keepWinGold = _catalog.GetVictoryGoldBonus(KeepWinCount);
        var goldAfterBasic = AddGold(basicGold);
        var goldAfterInterest = AddGold(interestGold);
        var goldAfterKeepWin = AddGold(keepWinGold);
        AddPlayerExp(_catalog.BattleWinExp);
        var battlePlan = BattlePlan;
        var nodeType = CurrentNode.NodeType;
        var chapterId = ChapterId;
        IReadOnlyList<GridFightOrbState> addedBattleOrbs = nodeType == GridFightNodeTypeEnum.Monster && battlePlan != null
            ? AddKilledMonsterOrbs(battlePlan, statistics.KilledMonsters)
            : [];
        addedBattleOrbs = [.. addedBattleOrbs, .. RollLootOrbsOnKills(statistics.KilledMonsters)];
        addedBattleOrbs = [.. addedBattleOrbs, .. AddReportedBattleOrbs(reportedDrops ?? [], statistics.KilledMonsters, addedBattleOrbs)];
        IReadOnlyList<GridFightTalentOrbReward> talentRewards = nodeType == GridFightNodeTypeEnum.Boss
            ? TakeTalentOrbRewards(GridFightTalentRewardTrigger.BossClear, chapterId)
            : [];
        ScheduleRoughPepeDrop(battleEndedNormally);
        RecordHeroEntranceBattleResult(isPerfectFinish);
        PrepareGalaxyScholarBattleReward();
        GrantTrailblazeBattleCoin();
        GrantMoonGameBattlePower(isPerfectFinish);
        ApplyDotContractBattleStatistics(statistics);
        RecordFateDivinerBattleParticipation();
        if (nodeType is GridFightNodeTypeEnum.Monster or GridFightNodeTypeEnum.CampMonster or
            GridFightNodeTypeEnum.EliteBranch or GridFightNodeTypeEnum.Boss)
            OnPepeCameoBattleCleared(isPerfectFinish);
        if (nodeType == GridFightNodeTypeEnum.EliteBranch &&
            (isPerfectFinish || progressPercent >= penaltyRule.ThresholdPosition))
            GrantEncounterReward(battlePlan);
        var defeated = LineupHp == 0;
        var routeAdvanced = !defeated && AdvanceToNextNode();
        BattlesFinished++;
        LastBattleStatistics = statistics;
        ApplyDiceEquipmentRefills();
        if (defeated)
        {
            Phase = GridFightSessionPhase.Defeated;
        }
        else if (routeAdvanced)
        {
            Phase = GridFightSessionPhase.AwaitingBattleRound;
            PendingQueuePosition++;
        }
        else
        {
            Phase = GridFightSessionPhase.Preparing;
        }
        return new GridFightBattleSettlementResult(
            true,
            routeAdvanced,
            progressPercent,
            penaltyRule.ThresholdPosition,
            isPerfectFinish,
            previousKeepWinCount,
            KeepWinCount,
            basicGold,
            interestGold,
            keepWinGold,
            goldAfterBasic,
            goldAfterInterest,
            goldAfterKeepWin,
            _catalog.BattleWinExp,
            lineupHpBefore,
            LineupHp,
            hpChanges,
            statistics,
            addedBattleOrbs,
            talentRewards)
        {
            Defeated = defeated,
        };
    }

    /// <summary>
    ///     Orbs the battle dropped on its own (Trotters outside the planned reward-node drops). The client showed them
    ///     dropping, but the server never granted them. Only orbs the server can open are taken, at most one per killed
    ///     enemy, and orbs this battle already granted through its plan count against the report.
    /// </summary>
    private IReadOnlyList<GridFightOrbState> AddReportedBattleOrbs(
        IReadOnlyList<GridFightRewardEntry> reportedDrops,
        IReadOnlyList<GridFightKilledMonsterInstance> killedMonsters,
        IReadOnlyList<GridFightOrbState> alreadyGranted)
    {
        var budget = killedMonsters.Select(killed => (killed.WaveIndex, killed.MonsterIndex)).Distinct().Count();
        var granted = alreadyGranted.GroupBy(orb => orb.OrbItemId).ToDictionary(group => group.Key, group => group.Count());
        var result = new List<GridFightOrbState>();
        foreach (var drop in reportedDrops.Where(drop => drop.Type == GridFightBonusTypeEnum.Orb))
        {
            if (!GridFightSpecialFeatures.TryResolveOrb(drop.ItemId, out _)) continue;
            for (var index = 0u; index < drop.Count && result.Count < budget; index++)
            {
                if (granted.TryGetValue(drop.ItemId, out var planned) && planned > 0)
                {
                    granted[drop.ItemId] = planned - 1;
                    continue;
                }
                if (TryAddOrb(drop.ItemId, out var orb)) result.Add(orb);
            }
        }
        return result;
    }

    private IReadOnlyList<GridFightOrbState> AddKilledMonsterOrbs(
        GridFightBattlePlan battlePlan,
        IReadOnlyList<GridFightKilledMonsterInstance> killedMonsters)
    {
        var result = new List<GridFightOrbState>();
        var seen = new HashSet<(uint WaveIndex, uint MonsterIndex)>();
        foreach (var killed in killedMonsters)
        {
            if (!seen.Add((killed.WaveIndex, killed.MonsterIndex)) ||
                killed.WaveIndex == 0 || killed.WaveIndex > battlePlan.Waves.Count)
                continue;
            var wave = battlePlan.Waves[checked((int)killed.WaveIndex - 1)];
            if (killed.MonsterIndex == 0 || killed.MonsterIndex > wave.Monsters.Count)
                continue;
            var monster = wave.Monsters[checked((int)killed.MonsterIndex - 1)];
            if (monster.Monster.MonsterID != killed.MonsterId) continue;
            foreach (var drop in monster.Drops.Where(drop => drop.Type == GridFightBonusTypeEnum.Orb))
            for (var index = 0u; index < drop.Count; index++)
                if (TryAddOrb(drop.ItemId, out var orb))
                    result.Add(orb);
        }
        return result;
    }

    private IReadOnlyList<GridFightHpChange> ApplyBattleHpChanges(
        GridFightPenaltyRuleExcel penaltyRule,
        uint progressPercent,
        bool isPerfectFinish,
        bool isFinalBoss,
        uint roundCount)
    {
        var basicPenalty = isPerfectFinish ? 0 : penaltyRule.ThresholdPassBasicPlayerHPPenalty;
        var progressPenalty = isPerfectFinish
            ? 0
            : (uint)(((ulong)(100 - progressPercent) * penaltyRule.ProgressPenaltyCoefficient) / 100);
        var thresholdPenalty = isPerfectFinish || progressPercent >= penaltyRule.ThresholdPosition
            ? 0
            : penaltyRule.ThresholdFailPlayerHPPenalty;
        List<GridFightHpChange> changes =
        [
            ApplyHpLoss(GridFightHpChangeKind.BasicPenalty, basicPenalty),
            ApplyHpLoss(GridFightHpChangeKind.ProgressPenalty, progressPenalty),
            ApplyHpLoss(GridFightHpChangeKind.ThresholdPenalty, thresholdPenalty),
        ];
        // A squad brought to 0 HP is out of the run; the post-battle heals (长线作战 +2 after every node battle) must not
        // bring it back, or no amount of lost battles could ever end the run.
        if (LineupHp == 0) return changes;
        changes.Add(ApplyHpHealing(
            GridFightHpChangeKind.TalentHealing,
            _catalog.GetPostBattleHealing(),
            _catalog.PostBattleHealingSourceId));
        var finalBattleHealing = isFinalBoss ? _catalog.GetFinalBattleTurnHealing(roundCount) : 0;
        if (finalBattleHealing > 0)
            changes.Add(ApplyHpHealing(
                GridFightHpChangeKind.FinalBattleTurnHealing,
                finalBattleHealing,
                _catalog.FinalBattleTurnHealingSourceId));
        return changes;
    }

    private GridFightHpChange ApplyHpLoss(GridFightHpChangeKind kind, uint amount)
    {
        var previous = LineupHp;
        LineupHp = amount >= LineupHp ? 0 : LineupHp - amount;
        return new GridFightHpChange(kind, previous, LineupHp, previous - LineupHp);
    }

    private GridFightHpChange ApplyHpHealing(GridFightHpChangeKind kind, uint amount, uint sourceId)
    {
        var previous = LineupHp;
        LineupHp = (uint)Math.Min(MaxLineupHp, (ulong)LineupHp + amount);
        return new GridFightHpChange(kind, previous, LineupHp, amount, sourceId);
    }

    private uint AddGold(uint amount)
    {
        Gold = (uint)Math.Min(uint.MaxValue, (ulong)Gold + amount);
        return Gold;
    }

    public void AbortBattleStart()
    {
        ClearMortenaxBladeBattleEffects();
        if (Phase == GridFightSessionPhase.InBattle) Phase = GridFightSessionPhase.Preparing;
    }

    public bool BackToPrepare()
    {
        if (Phase != GridFightSessionPhase.AwaitingBackToPrepare) return false;
        EnterPreparingPhase();
        return true;
    }

    public bool RefreshShop(bool chargeGold)
    {
        if (chargeGold &&
            (Phase != GridFightSessionPhase.Preparing ||
             Gold < CurrentRefreshCost ||
             // Cyrene's poem shop must be bought out first; Aha goods are ordinary slots.
             Shop.Any(slot => slot.SpecialGoodsId != 0 && !IsAhaGoods(slot.SpecialGoodsId) && !slot.SoldOut)))
            return false;
        if (!chargeGold && _shopLocked && Shop.Count > 0) return true;
        if (chargeGold)
        {
            var paidWithGold = !TryConsumeFreeShopRefresh();
            if (paidWithGold) SpendGold(BaseRefreshCost);
            _shopLocked = false;
            ApplyAstropolisRefreshBonus();
            OnPortalShopRefreshed(paidWithGold);
            OnSpotLightShopRefreshed();
            if (paidWithGold && ActiveInvestmentIds.Contains(301601)) AddPlayerExp(2);
        }
        var procurementRoles = chargeGold ? RollProcurementShop() : null;
        Shop.Clear();
        var boostedTraitId = ConceptStockBoostedTraitId;
        var roles = procurementRoles ?? _catalog.RollShop(
            PlayerLevel,
            _silverWolfCurrentRoleId == 0 ? GridFightSpecialFeatures.SilverWolfThreeCostRoleId : _silverWolfCurrentRoleId,
            HasRotationPortal ? GetEffectiveShopRarityWeights(_catalog.GetShopRarityWeights(PlayerLevel)) : null,
            boostedTraitId,
            ConceptStockShopWeightMultiplier,
            _temporarilyUnlockedRestrictedRoleIds,
            GetMaxStarExcludedRoleIds());
        for (var index = 0; index < roles.Count; index++)
        {
            var star = ResolveMarketVitalityStar(roles[index].ID);
            Shop.Add(new GridFightShopSlot
            {
                Index = (uint)index,
                RoleId = roles[index].ID,
                Star = star,
                Price = _catalog.GetBuyPrice(roles[index].ID, star),
            });
        }
        if (procurementRoles == null) ApplyAhaShopGlance();
        return true;
    }

    private HashSet<uint> GetMaxStarExcludedRoleIds()
    {
        var excluded = Roles.Where(role => role.Star >= 3).Select(role => role.RoleId).ToHashSet();
        foreach (var group in _catalog.RoleSwitchGroups)
            if (group.RoleIdList.Any(excluded.Contains))
                excluded.UnionWith(group.RoleIdList);
        return excluded;
    }

    public GridFightRouteNode CurrentNode => _catalog.GetNode(ChapterId, SectionId);
    public uint CampId => CampIds[checked((int)ChapterId - 1)];
    public uint FightCampId => BattlePlan?.CampId ??
                               (CurrentNode.NodeType == GridFightNodeTypeEnum.Monster ? RewardCampId : 0);
    public uint EliteBranchId => EncounterOptions.FirstOrDefault(option => option.BattlePlan == BattlePlan)?.EliteBranchId ?? 0;
    public uint EncounterRerollCount => _encounterRerollCount;
    public uint CurrentPenaltyRuleId => BattlePlan?.PenaltyRuleId ?? CurrentNode.PenaltyBonusRuleId;

    public uint EffectiveEnemyDifficultyLevel
    {
        get
        {
            var value = (ulong)_catalog.GetEnemyDifficultyLevel(DivisionId) +
                        ActiveInvestmentIds.Where(id => !_difficultyExemptInvestmentIds.Contains(id)).Aggregate(0UL,
                            (sum, id) => sum + _catalog.GetInvestmentDifficultyAdd(DivisionId, id)) +
                        (BattlePlan?.DifficultyAdd ?? 0) +
                        SpotLightEnemyDifficulty;
            return (uint)Math.Min(value, uint.MaxValue);
        }
    }

    private void PrepareInvestmentOffer()
    {
        if (RollForesightInvestmentOffer() is { } foresightOffer)
        {
            InvestmentOffer = foresightOffer;
            ResetInvestmentRerolls();
            return;
        }
        var quality = _catalog.RollInvestmentQuality(ChapterId, InvestmentOfferCount, ActiveInvestmentIds);
        if (ResolvePortalInvestmentQuality(++_ordinaryInvestmentOffers) is { } forced &&
            _catalog.RollInvestmentOffer(ChapterId, forced, InvestmentOfferCount, ActiveInvestmentIds).Count == InvestmentOfferCount)
            quality = forced;
        if (quality != GridFightAugmentQualityEnum.None) quality = ApplyInvestmentUpgrade(quality);
        InvestmentOffer = quality == GridFightAugmentQualityEnum.None
            ? []
            : _catalog.RollInvestmentOffer(ChapterId, quality, InvestmentOfferCount, ActiveInvestmentIds);
        ResetInvestmentRerolls();
    }

    private void ResetInvestmentRerolls()
    {
        _rerolledInvestmentIds.Clear();
        _rerolledAwayInvestmentIds.Clear();
    }

    private void PrepareSupplyOffer()
    {
        SupplyOffer = _catalog.RollSupplyOffer(SupplyOfferCount, _temporarilyUnlockedRestrictedRoleIds)
            .Select(offer => new GridFightSupplyOfferState(offer.RoleId, offer.EquipmentId))
            .ToList();
        SupplyOffer = ApplyPetPalSupplyRules(SupplyOffer);
        SupplyRerollCount = 0;
    }

    private void MaterializeInitialRoles()
    {
        if (_initialRolesMaterialized) return;
        _initialRolesMaterialized = true;
        var conceptRoleIds = RollConceptStockInitialRoleIds();
        var roleIds = conceptRoleIds ?? RollTalentReserveInitialRoleIds() ?? _catalog.RollInitialRoles(OfficialInitialRoleCount, _temporarilyUnlockedRestrictedRoleIds).Select(role => role.ID).ToList();
        foreach (var roleId in roleIds)
            Roles.Add(new GridFightRoleState
            {
                UniqueId = _nextEntityUniqueId++,
                RoleId = roleId,
                Position = GetFreeBenchPosition(),
            });
        RecalculateRoleSwitches();
    }

    private List<GridFightRoleMergeStep> MergeRoles(uint roleId)
    {
        var steps = new List<GridFightRoleMergeStep>();
        // The Aha tiers below 7-cost have no stars; extra copies become growth (AbsorbAhaCopies) instead.
        if (IsSeasonTwoRules && IsAhaRole(roleId) && roleId != AhaSevenCostRoleId) return steps;
        var switchGroup = _catalog.RoleSwitchGroups.FirstOrDefault(group => group.RoleIdList.Contains(roleId));
        var mergeRoleIds = switchGroup?.RoleIdList.ToHashSet() ?? [roleId];
        var starRoleId = switchGroup?.BaseRoleId ?? roleId;
        while (true)
        {
            var group = Roles.Where(role => mergeRoleIds.Contains(role.RoleId) && !IsPopulationFreeRole(role))
                .GroupBy(role => role.Star)
                .OrderBy(grouping => grouping.Key)
                .FirstOrDefault(grouping => grouping.Count() >= 3 &&
                    GameData.GridFightRoleStarData.ContainsKey(starRoleId << 4 | (grouping.Key + 1)));
            if (group == null) return steps;

            // Official merges retain an existing deployed copy's position and gear, not the third copy's.
            // Prefer foreground, background, then bench; prefer gear and then age within a position.
            // This keeps chained 1→2→3-star upgrades on the deployed copy when it participates.
            var candidates = group.ToList();
            var retained = candidates
                .OrderBy(GetMergePositionPriority)
                .ThenByDescending(role => role.EquippedEquipmentUniqueIds.Count)
                .ThenBy(role => role.UniqueId)
                .First();
            var merge = new[] { retained }
                .Concat(candidates
                    .Where(role => role != retained)
                    .OrderBy(role => role.UniqueId)
                    .Take(2))
                .ToList();

            foreach (var role in merge)
            {
                if (role != retained) DestroyDiceFilledOnRole(role);
                Roles.Remove(role);
            }
            var mergedRole = new GridFightRoleState
            {
                UniqueId = _nextEntityUniqueId++,
                RoleId = roleId,
                Star = group.Key + 1,
                Position = ResolveHeroEntranceMergedPosition(merge, retained.Position),
            };
            mergedRole.EquippedEquipmentUniqueIds.AddRange(retained.EquippedEquipmentUniqueIds);
            mergedRole.HasPowerGrowth = merge.Any(role => role.HasPowerGrowth);
            foreach (var role in merge)
                foreach (var (property, bonus) in role.PermanentPropertyBonuses)
                    mergedRole.PermanentPropertyBonuses[property] =
                        mergedRole.PermanentPropertyBonuses.GetValueOrDefault(property) + bonus;
            Roles.Add(mergedRole);
            var removedUniqueIds = merge.Select(role => role.UniqueId).ToList();
            RebindHeroEntranceAfterMerge(removedUniqueIds, mergedRole);
            RebindSpotLightsAfterMerge(removedUniqueIds, mergedRole);
            steps.Add(new GridFightRoleMergeStep(
                removedUniqueIds,
                mergedRole));
        }
    }

    private static int GetMergePositionPriority(GridFightRoleState role)
    {
        return role.Position switch
        {
            >= 1 and <= 4 => 0,  // foreground: never kick an on-field role back to the bench
            >= 5 and <= 13 => 1, // active background slot
            >= 14 => 2,          // bench
            _ => 3,
        };
    }

    private IReadOnlyList<GridFightRoleState> GetFinalMergedRoles(
        IEnumerable<GridFightRoleMergeStep> mergeSteps)
    {
        return mergeSteps
            .Select(step => step.AddedRole)
            .Where(role => Roles.Contains(role))
            .ToList();
    }

    private GridFightRoleAcquisitionResult FinalizeRoleAcquisition(GridFightRoleAcquisitionResult acquisition)
    {
        if (!acquisition.Success) return acquisition;

        // Grants outside shop/crystal/supply unlock restricted roles for the run. Those three sources
        // also reach this path, but only for roles already unlocked.
        foreach (var role in acquisition.AcquiredRoles)
            UnlockRestrictedRoleForRun(role.RoleId);

        var acquiredStars = acquisition.AcquiredRoles
            .Concat(acquisition.MergeSteps.Select(step => step.AddedRole))
            .Concat(acquisition.FinalMergedRoles)
            .Where(role => role.Star >= 3)
            .ToList();
        var effects = new List<GridFightRoleUnlockEffect>();

        if (!_cyrenePoemShopUnlocked &&
            acquiredStars.Any(role => role.RoleId == GridFightSpecialFeatures.CyreneRoleId))
        {
            _cyrenePoemShopUnlocked = true;
            RefreshCyrenePoemShop();
            effects.Add(GridFightRoleUnlockEffect.CyrenePoemShop);
        }

        var headPlayer = RefreshHeadPlayerAfterRoleStateChange();
        RefreshFateDivinerAfterRoleStateChange();
        if (headPlayer.TraitActivated)
            effects.Add(GridFightRoleUnlockEffect.SilverWolfTraitActivation);
        if (headPlayer.PendingStarted)
            effects.Add(GridFightRoleUnlockEffect.SilverWolfHeadPlayerPending);

        return effects.Count == 0 ? acquisition : acquisition with { UnlockEffects = effects };
    }

    private void RefreshCyrenePoemShop()
    {
        Shop.Clear();
        for (var index = 0; index < GridFightSpecialFeatures.CyreneSpecialGoodsIds.Count; index++)
            Shop.Add(new GridFightShopSlot
            {
                Index = (uint)index,
                SpecialGoodsId = GridFightSpecialFeatures.CyreneSpecialGoodsIds[index],
                Price = 0,
            });
    }

    private bool GameDataHasNextLevel()
    {
        try
        {
            _ = _catalog.GetPlayerLevel(PlayerLevel + 1);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    private bool AdvanceToNextNode()
    {
        var next = _catalog.GetNextNode(ChapterId, SectionId);
        if (next == null) return false;
        var previousNode = CurrentNode;
        ChapterId = next.ChapterId;
        SectionId = next.SectionId;
        PrepareCurrentNode();
        OnInvestmentNodeEntered();
        OnAugmentEffectNodeEntered(previousNode);
        OnPepeCameoNodeEntered();
        OnSpotLightNodeEntered();
        RefreshShop(false);
        return true;
    }

    private void PrepareCurrentNode()
    {
        BattlePlan = null;
        EncounterOptions = [];
        _encounterRerollCount = 0;
        if (CurrentNode.NodeType == GridFightNodeTypeEnum.EliteBranch)
        {
            EncounterOptions = RollEncounterOptionsWithRewards();
            return;
        }
        BattlePlan = _catalog.RollBattlePlan(
            CurrentNode,
            CampIds,
            RewardCampId,
            Bosses,
            SelectedPortalId,
            _temporarilyUnlockedRestrictedRoleIds);
    }

    private uint GetFreeBenchPosition()
    {
        var max = 13u + _catalog.BenchSize + _catalog.BenchOverflowSize;
        for (var position = 14u; position <= max; position++)
            if (Roles.All(role => role.Position != position) &&
                ForgeItems.All(item => item.Position != position))
                return position;
        return 0;
    }

    private (IReadOnlyList<GridFightRoleState> Roles, IReadOnlyList<GridFightForgeItemState> ForgeItems)
        PromoteOverflowItemsToBench()
    {
        const uint firstBenchPosition = 14;
        var firstOverflowPosition = firstBenchPosition + _catalog.BenchSize;
        var freeBenchPositions = new List<uint>();
        for (var position = firstBenchPosition; position < firstOverflowPosition; position++)
            if (Roles.All(role => role.Position != position) &&
                ForgeItems.All(item => item.Position != position))
                freeBenchPositions.Add(position);

        var overflowItems = Roles
            .Where(role => role.Position >= firstOverflowPosition)
            .Select(role => (
                role.Position,
                role.UniqueId,
                Role: (GridFightRoleState?)role,
                ForgeItem: (GridFightForgeItemState?)null))
            .Concat(ForgeItems
                .Where(item => item.Position >= firstOverflowPosition)
                .Select(item => (
                    item.Position,
                    item.UniqueId,
                    Role: (GridFightRoleState?)null,
                    ForgeItem: (GridFightForgeItemState?)item)))
            .OrderBy(item => item.Position)
            .ThenBy(item => item.UniqueId)
            .ToList();
        var promotedRoles = new List<GridFightRoleState>();
        var promotedForgeItems = new List<GridFightForgeItemState>();
        for (var index = 0; index < freeBenchPositions.Count && index < overflowItems.Count; index++)
        {
            var item = overflowItems[index];
            if (item.Role != null)
            {
                item.Role.Position = freeBenchPositions[index];
                promotedRoles.Add(item.Role);
            }
            else
            {
                item.ForgeItem!.Position = freeBenchPositions[index];
                promotedForgeItems.Add(item.ForgeItem);
            }
        }
        return (promotedRoles, promotedForgeItems);
    }

    private bool IsValidPosition(uint position)
    {
        return position == 0 || IsActivePosition(position) ||
               IsBenchPosition(position);
    }

    private bool IsBenchPosition(uint position) =>
        position is >= 14 && position <= 13 + _catalog.BenchSize + _catalog.BenchOverflowSize;

    private static bool IsActivePosition(uint position) => position is >= 1 and <= 13;

    private static bool IsBackPosition(uint position) => position is >= 5 and <= 13;

    private static bool IsCombatNode(GridFightRouteNode node)
    {
        return node.NodeType is GridFightNodeTypeEnum.Monster or
            GridFightNodeTypeEnum.CampMonster or
            GridFightNodeTypeEnum.EliteBranch or
            GridFightNodeTypeEnum.Boss;
    }
}
