using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.GridFight;
using March7thHoney.GameServer.Game.GridFight.Persistence;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Converts a live run to and from <see cref="GridFightRunSnapshot" /> so it survives re-login and server restarts.
///     Every instance field is either captured here or listed in <see cref="PersistenceTransientFields" />; the
///     GridFight regression checks that split by reflection, so a new field fails the run until it is mapped.
/// </summary>
public sealed partial class GridFightSession
{
    /// <summary>
    ///     Fields deliberately left out of the snapshot. The catalog is rebuilt by the manager; the rest are
    ///     notification buffers whose state change was already applied to persisted fields and is only waiting to be
    ///     announced. A restored run always reaches the client through a full GetData, which supersedes them.
    /// </summary>
    public static IReadOnlySet<string> PersistenceTransientFields { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(_catalog),
        nameof(_augmentNodeEffects),
        nameof(_augmentSelectionEffects),
        nameof(_portalOpeningEffects),
        nameof(_portalRefreshEffects),
        nameof(_powerGrowthRoleUpdates),
        nameof(_economyEffects),
        nameof(_pepeCameoEvents),
        nameof(_owlbertArrivals),
        nameof(_goldOwlbertTickMs),
        nameof(_lizhiNpcRemovals),
        nameof(_roundBeginInvestmentRewards),
        nameof(_investmentSelectionRewards),
        nameof(_laborDispatchBossReward),
        nameof(_roleSwitchUpdate),
        nameof(_addedDiceEquipment),
        nameof(_removedDiceEquipment),
        nameof(_updatedDiceRoles),
        nameof(_spotLightsRebound),
        nameof(_pendingPamKingUnlock),
    };

    public GridFightRunSnapshot CaptureSnapshot()
    {
        var battlePlanEncounterIndex = -1;
        if (BattlePlan != null)
            for (var index = 0; index < EncounterOptions.Count; index++)
                if (ReferenceEquals(EncounterOptions[index].BattlePlan, BattlePlan))
                {
                    battlePlanEncounterIndex = index;
                    break;
                }

        return new GridFightRunSnapshot
        {
            Season = Season,
            DivisionId = DivisionId,
            IsOverlock = IsOverlock,
            UniqueId = UniqueId,
            PetPalId = PetPalId,
            PetPalEffectId = PetPalEffectId,
            CampIds = CampIds.ToList(),
            RewardCampId = RewardCampId,
            BossMonsterIds = Bosses.Select(boss => boss.MonsterID).ToList(),
            AffixIds = AffixIds.ToList(),

            Phase = (int)Phase,
            PendingQueuePosition = PendingQueuePosition,
            ChapterId = ChapterId,
            SectionId = SectionId,
            Gold = Gold,
            PlayerLevel = PlayerLevel,
            PlayerExp = PlayerExp,
            LineupHp = LineupHp,
            MaxLineupHp = MaxLineupHp,
            KeepWinCount = KeepWinCount,
            BattlesFinished = BattlesFinished,
            SelectedPortalId = SelectedPortalId,
            PortalOffer = PortalOffer.ToList(),
            PortalRerollCount = PortalRerollCount,
            InvestmentOffer = InvestmentOffer.ToList(),
            SupplyOffer = SupplyOffer.Select(offer => new GridFightSupplyOfferSnapshot
            {
                RoleId = offer.RoleId,
                EquipmentId = offer.EquipmentId,
                Star = offer.Star,
                ExtraEquipmentIds = offer.ExtraEquipmentIds.ToList(),
            }).ToList(),
            ActiveInvestmentIds = ActiveInvestmentIds.ToList(),
            SupplyRerollCount = SupplyRerollCount,
            EncounterOptions = EncounterOptions.Select(option => new GridFightEncounterOptionSnapshot
            {
                EliteBranchId = option.EliteBranchId,
                EncounterId = option.EncounterId,
                Quality = option.Quality,
                BattlePlan = CaptureBattlePlan(option.BattlePlan),
                Rewards = option.Rewards?.Select(drop => new GridFightEncounterDropSnapshot
                {
                    Type = (int)drop.Type,
                    ItemId = drop.ItemId,
                    Num = drop.Num,
                }).ToList(),
            }).ToList(),
            BattlePlan = BattlePlan == null || battlePlanEncounterIndex >= 0 ? null : CaptureBattlePlan(BattlePlan),
            BattlePlanEncounterIndex = battlePlanEncounterIndex,

            Roles = Roles.Select(CaptureRole).ToList(),
            Equipments = Equipments.Select(CaptureEquipment).ToList(),
            Orbs = Orbs.Select(CaptureOrb).ToList(),
            Consumables = Consumables.Select(consumable => new GridFightConsumableSnapshot
            {
                GroupId = consumable.GroupId,
                ItemId = consumable.ItemId,
                Count = consumable.Count,
            }).ToList(),
            ForgeItems = ForgeItems.Select(CaptureForgeItem).ToList(),
            Shop = Shop.Select(slot => new GridFightShopSlotSnapshot
            {
                Index = slot.Index,
                RoleId = slot.RoleId,
                SpecialGoodsId = slot.SpecialGoodsId,
                Star = slot.Star,
                Price = slot.Price,
                SoldOut = slot.SoldOut,
            }).ToList(),
            HasCyreneCombatEnhancement = HasCyreneCombatEnhancement,
            HackConsole = HackConsole == null
                ? null
                : new GridFightHackConsoleSnapshot
                {
                    GameItemUniqueId = HackConsole.GameItemUniqueId,
                    RemainingGoldActions = HackConsole.RemainingGoldActions,
                    RemainingHealActions = HackConsole.RemainingHealActions,
                    RemainingOrbActions = HackConsole.RemainingOrbActions,
                    EnemyHpPercent = HackConsole.EnemyHpPercent,
                },
            LastBattleStatistics = CaptureStatistics(LastBattleStatistics),
            AstaDogNpc = AstaDogNpc == null ? null : CaptureNpc(AstaDogNpc),
            PepeNpc = PepeNpc == null ? null : CaptureNpc(PepeNpc),
            EquipmentRecommendation = EquipmentRecommendation == null
                ? null
                : new GridFightEquipmentRecommendationSnapshot
                {
                    QueuePosition = EquipmentRecommendation.QueuePosition,
                    RoleUniqueId = EquipmentRecommendation.RoleUniqueId,
                    EquipmentIds = EquipmentRecommendation.EquipmentIds.ToList(),
                },
            InvitationPortalEquipment = InvitationPortalEquipment == null ? null : CaptureEquipment(InvitationPortalEquipment),
            LaborDispatchEmblem = LaborDispatchEmblem == null ? null : CaptureEquipment(LaborDispatchEmblem),
            LaborDispatchTraitId = LaborDispatchTraitId,

            RoleSavedValues = new Dictionary<string, uint>(_roleSavedValues),
            NextEntityUniqueId = _nextEntityUniqueId,
            OrbClaimed = _orbClaimed,
            OfficialInitialRoundGranted = _officialInitialRoundGranted,
            MortenaxBladeTraitEffectInitialized = _mortenaxBladeTraitEffectInitialized,
            MortenaxBladeBattleEffects = _mortenaxBladeBattleEffects.Select(effect => new GridFightMortenaxBladeEffectSnapshot
            {
                RoleUniqueId = effect.RoleUniqueId,
                EquipmentId = effect.EquipmentId,
                ReplacedEquipmentUniqueId = effect.ReplacedEquipmentUniqueId,
                TemporaryInstanceId = effect.TemporaryInstanceId,
                NeedsTraitEffectSync = effect.NeedsTraitEffectSync,
            }).ToList(),
            CyrenePoemShopUnlocked = _cyrenePoemShopUnlocked,
            SilverWolfHackUnlocked = _silverWolfHackUnlocked,
            HasTraitEffectSelectEnhanceQueuePosition = _traitEffectSelectEnhanceQueuePosition.HasValue,
            TraitEffectSelectEnhanceQueuePosition = _traitEffectSelectEnhanceQueuePosition ?? 0,
            RerolledInvestmentIds = _rerolledInvestmentIds.ToList(),
            RerolledAwayInvestmentIds = _rerolledAwayInvestmentIds.ToList(),
            TemporarilyUnlockedRestrictedRoleIds = _temporarilyUnlockedRestrictedRoleIds.ToList(),
            TriggeredTalentRewards = _triggeredTalentRewards.Select(reward => new GridFightTalentRewardSnapshot
            {
                TalentId = reward.TalentId,
                EffectId = reward.EffectId,
                Trigger = (int)reward.Trigger,
                ChapterId = reward.ChapterId,
            }).ToList(),
            EncounterRerollCount = _encounterRerollCount,
            RoleSwitchIds = new Dictionary<uint, uint>(_roleSwitchIds),

            AugmentEffectNodeCounters = new Dictionary<uint, uint>(_augmentEffectNodeCounters),
            ActivePortalIds = _activePortalIds.ToList(),
            PortalSavedValues = new Dictionary<string, uint>(_portalSavedValues),
            InitialRolesMaterialized = _initialRolesMaterialized,
            JoyContractInitialized = _joyContractInitialized,
            JoyContractInitialRoleUniqueId = _joyContractInitialRoleUniqueId,
            ConceptStockInitialEquipment = _conceptStockInitialEquipment == null
                ? null
                : CaptureEquipment(_conceptStockInitialEquipment),
            DiceBindings = _diceBindings.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
            DotContractInitialOrbs = _dotContractInitialOrbs.Select(CaptureOrb).ToList(),
            DotContractBattleRewards = _dotContractBattleRewards.Select(CaptureOrb).ToList(),
            DotContractBlackSwanGranted = _dotContractBlackSwanGranted,
            EquipmentTracks = _equipmentTracks.Select(track => new GridFightEquipmentTrackSnapshot
            {
                RoleId = track.RoleId,
                TargetEquipmentIds = track.TargetEquipmentIds.ToList(),
                CompletedEquipmentIds = track.CompletedEquipmentIds.ToList(),
                Source = (int)track.Source,
                Priority = track.Priority,
            }).ToList(),
            PendingTrackedAutoEquipRoles = _pendingTrackedAutoEquipRoles.Select(CaptureRole).ToList(),
            FateDivinerPending = _fateDivinerPending == null
                ? null
                : new GridFightFateDivinerPendingSnapshot
                {
                    QueuePosition = _fateDivinerPending.QueuePosition,
                    OptionIds = _fateDivinerPending.OptionIds.ToList(),
                    IsInitial = _fateDivinerPending.IsInitial,
                },
            FateDivinerInitialized = _fateDivinerInitialized,
            FateDivinerReady = _fateDivinerReady,
            FateDivinerInitialActivationSyncSent = _fateDivinerInitialActivationSyncSent,
            FateDivinerBattleCount = _fateDivinerBattleCount,
            FateConsumedOptions = _fateConsumedOptions.ToList(),
            FateBattleBuffOptions = _fateBattleBuffOptions.ToList(),
            FateOption4Count = _fateOption4Count,
            FateOption6Count = _fateOption6Count,
            FateOption5GameItemUniqueId = _fateOption5GameItemUniqueId,
            ForesightBonusAugments = _foresightBonusAugments.ToList(),
            DifficultyExemptInvestmentIds = _difficultyExemptInvestmentIds.ToList(),
            GalaxyScholarDna = _galaxyScholarDna,
            GalaxyScholarRoundUpdateDirty = _galaxyScholarRoundUpdateDirty,
            GalaxyScholarGrantedThresholds = _galaxyScholarGrantedThresholds.ToList(),
            GalaxyScholarPendingRewardOrbs = _galaxyScholarPendingRewardOrbs.Select(CaptureOrb).ToList(),
            HeadPlayerPending = _headPlayerPending == null
                ? null
                : new GridFightHeadPlayerPendingSnapshot
                {
                    QueuePosition = _headPlayerPending.QueuePosition,
                    RoleUniqueId = _headPlayerPending.RoleUniqueId,
                    RoleId = _headPlayerPending.RoleId,
                    EffectId = _headPlayerPending.EffectId,
                    OptionIds = _headPlayerPending.OptionIds.ToList(),
                    IsFinalHack = _headPlayerPending.IsFinalHack,
                },
            HandledHeadPlayerStates = _handledHeadPlayerStates.Select(state => new GridFightUIntTripleSnapshot
            {
                First = state.UniqueId,
                Second = state.RoleId,
                Third = state.Star,
            }).ToList(),
            SilverWolfTraitInitialized = _silverWolfTraitInitialized,
            SilverWolfUpgradeRouteClosed = _silverWolfUpgradeRouteClosed,
            SilverWolfCurrentRoleId = _silverWolfCurrentRoleId,
            InvestmentNodeCounters = new Dictionary<uint, uint>(_investmentNodeCounters),
            RoundBeginSavedValueUpdates = _roundBeginSavedValueUpdates.Select(update => new GridFightSavedValueUpdateSnapshot
            {
                AugmentId = update.AugmentId,
                EffectId = update.EffectId,
                Key = update.Key,
                Value = update.Value,
            }).ToList(),
            LizhiNpcs = _lizhiNpcs.Select(CaptureNpc).ToList(),
            LizhiPermanentBenchBonus = _lizhiPermanentBenchBonus,
            GunpowderInitialForgeItems = _gunpowderInitialForgeItems.Select(CaptureForgeItem).ToList(),
            TalentRecruitmentInitialOrb = _talentRecruitmentInitialOrb == null ? null : CaptureOrb(_talentRecruitmentInitialOrb),
            TwinPrimesRewards = _twinPrimesRewards.Select(reward => new GridFightTwinPrimesRewardSnapshot
            {
                EffectId = reward.EffectId,
                ExpAmount = reward.ExpAmount,
            }).ToList(),
            TwinPrimesRewardedRarities = _twinPrimesRewardedRarities.ToList(),
            GoldFuturesPendingReady = _goldFuturesPendingReady,
            GoldFuturesInvestmentPending = _goldFuturesInvestmentPending,
            LastGoldFuturesChildSelection = _lastGoldFuturesChildSelection,
            GoldFuturesNodeCount = _goldFuturesNodeCount,
            ShopLocked = _shopLocked,
            MarketVitalitySavedValueUpdates = _marketVitalitySavedValueUpdates.Select(update => Pair(update.EffectId, update.Value)).ToList(),
            PepeCameoNodes = _pepeCameoNodes,
            PepeCameoActive = _pepeCameoActive,
            RoughPepeDropPending = _roughPepeDropPending,
            ManualRefreshCount = _manualRefreshCount,
            PaidRefreshCount = _paidRefreshCount,
            LongTermRefreshDiscount = _longTermRefreshDiscount,
            SuccessExperienceNodes = _successExperienceNodes,
            OrdinaryInvestmentOffers = _ordinaryInvestmentOffers,
            ReportedRoleSavedValues = new Dictionary<string, uint>(_reportedRoleSavedValues),
            RotationPortalInitialized = _rotationPortalInitialized,
            RotationBoostedRarity = _rotationBoostedRarity,
            HeroEntranceInitialized = _heroEntranceInitialized,
            HeroEntranceRemaining = _heroEntranceRemaining,
            HeroEntranceRoleUniqueId = _heroEntranceRoleUniqueId,
            HeroEntranceRoleId = _heroEntranceRoleId,
            HeroEntrancePerfectAdvancePending = _heroEntrancePerfectAdvancePending,
            HeroEntranceRoundStateDirty = _heroEntranceRoundStateDirty,
            HeroEntranceStateDirty = _heroEntranceStateDirty,
            MoonGatheringSecondHalf = _moonGatheringSecondHalf.ToList(),
            InvestmentUpgradePending = _investmentUpgradePending,
            LastBattleBoardUniqueIds = _lastBattleBoardUniqueIds.ToList(),
            RefreshVisionRefreshCount = _refreshVisionRefreshCount,
            AdvancedRefreshVisionRefreshCount = _advancedRefreshVisionRefreshCount,
            GoldOwlbertPaidThisNode = _goldOwlbertPaidThisNode,
            TrackedTraitIds = _trackedTraitIds.ToList(),
            PreparationPhaseCount = _preparationPhaseCount,

            AhaModifierUniqueId = _ahaModifierUniqueId,
            AhaUltraConfirmed = _ahaUltraConfirmed,
            AhaUltraIssued = _ahaUltraIssued,
            AhaUltraItemIds = new Dictionary<uint, uint>(_ahaUltraItemIds),
            BonfireNodes = _bonfireNodes.Select(node => new GridFightBonfireNodeSnapshot
            {
                ChapterId = node.ChapterId,
                SectionId = node.SectionId,
                EventId = node.EventId,
                PetPalEffectId = node.PetPalEffectId,
            }).ToList(),
            BonfireResolved = _bonfireResolved.Select(node => Pair(node.ChapterId, node.SectionId)).ToList(),
            BonfireOptions = _bonfireOptions.Select(option => new GridFightBonfireOptionSnapshot
            {
                ShowType = option.ShowType,
                RoleId = option.RoleId,
                EventId = option.EventId,
                OptionId = option.OptionId,
            }).ToList(),
            BonfireSelected = _bonfireSelected,
            ClaraEnhancements = _claraEnhancements.Select(entry => Pair(entry.EnhanceId, entry.Num)).ToList(),
            ClaraFielded = _claraFielded,
            ClaraPendingUpgrades = _claraPendingUpgrades,
            ClaraNodesSinceUpgrade = _claraNodesSinceUpgrade,
            ClaraUpgradeCount = _claraUpgradeCount,
            ClaraNode = Pair(_claraNode.ChapterId, _claraNode.SectionId),
            AhaGrowth = _ahaGrowth,
            ElationTemplateId = _elationTemplateId,
            ElationSlots = _elationSlots.Select(slot => new GridFightElationSlotSnapshot
            {
                PresetEquipmentId = slot.PresetEquipmentId,
                EquipmentUniqueId = slot.EquipmentUniqueId,
            }).ToList(),
            ChimeraEnergy = _chimeraEnergy,
            Chimeras = _chimeras.Select(chimera => new GridFightChimeraSnapshot
            {
                UniqueId = chimera.UniqueId,
                ChimeraId = chimera.ChimeraId,
                Star = chimera.Star,
            }).ToList(),
            ChimeraNode = Pair(_chimeraNode.ChapterId, _chimeraNode.SectionId),
            ChimeraFreeLayers = _chimeraFreeLayers.ToList(),
            ChimeraRecruits = _chimeraRecruits,
            ChimeraGoods = _chimeraGoods.Select(good => Pair(good.Type, good.Param)).ToList(),
            ChimeraRerolls = _chimeraRerolls,
            ChimeraPassiveGold = _chimeraPassiveGold,
            MoonGameSubTraitId = _moonGameSubTraitId,
            MoonGameWishPower = _moonGameWishPower,
            XianzhouBattleSwaps = new Dictionary<uint, uint>(_xianzhouBattleSwaps),
            PetPalStartGranted = _petPalStartGranted,
            PetPalGuardUsed = _petPalGuardUsed,
            PetPalCraftRefundChapters = _petPalCraftRefundChapters.ToList(),
            PetPalPendingEquipment = _petPalPendingEquipment.ToList(),
            JadeGoods = _jadeGoods.Select(good => new GridFightJadeGoodSnapshot
            {
                SlotId = good.SlotId,
                GoodId = good.GoodId,
                Rewards = good.Rewards.Select(CaptureRewardEntry).ToList(),
            }).ToList(),
            JadeGoodsNode = Pair(_jadeGoodsNode.ChapterId, _jadeGoodsNode.SectionId),
            JadeGoodsStar = _jadeGoodsStar,
            JadeAutoOpen = _jadeAutoOpen,
            TrailblazeCoin = _trailblazeCoin,
            TrailblazeSpent = _trailblazeCostedCoin,
            PamKingGranted = _pamKingUnlocked,
            TrailblazeGoods = _trailblazeGoods.Select(good => Pair(good.GoodId, good.Cost)).ToList(),
            TrailblazeBought = new Dictionary<uint, uint>(_trailblazeBought),
            TrailblazeGoodsNode = Pair(_trailblazeGoodsNode.ChapterId, _trailblazeGoodsNode.SectionId),
            TrailblazeActivated = _trailblazeActivated,
            PamKingModifierUniqueId = _pamKingModifierUniqueId,
            AstropolisNode = Pair(_astropolisNode.ChapterId, _astropolisNode.SectionId),
            FreeShopRefreshCount = _freeShopRefreshCount,
            SpotLights = _spotLights.Select(spotLight => new GridFightSpotLightSnapshot
            {
                UniqueId = spotLight.UniqueId,
                SpotLightId = spotLight.SpotLightId,
                RoleId = spotLight.RoleId,
                RoleUniqueId = spotLight.RoleUniqueId,
                IsLoss = spotLight.IsLoss,
            }).ToList(),
            SpotLightCandidates = _spotLightCandidates.ToList(),
            SpotLightTraitItemIds = new Dictionary<uint, uint>(_spotLightTraitItemIds),
            SpotLightRerollsLeft = _spotLightRerollsLeft,
            SpotLightTargetUniqueId = _spotLightTargetUniqueId,
            SpotLightGrantRoleId = _spotLightGrantRoleId,
            SpotLightReplaceTotal = _spotLightReplaceTotal,
            SpotLightReplaceUsed = _spotLightReplaceUsed,
            SpotLightFreeRefreshTotal = _spotLightFreeRefreshTotal,
            SpotLightFreeRefreshUsed = _spotLightFreeRefreshUsed,
            SpotLightPendingSelections = _spotLightPendingSelections,
            SpotLightNodesDone = _spotLightNodesDone.Select(node => Pair(node.ChapterId, node.SectionId)).ToList(),
            SpotLightNodesVisited = _spotLightNodesVisited.Select(node => Pair(node.ChapterId, node.SectionId)).ToList(),
            SpotLightCountersDirty = _spotLightCountersDirty,
            TraitTemplateIds = new Dictionary<uint, uint>(_traitTemplateIds),
            TemplateEquipmentUniqueIds = _templateEquipmentUniqueIds.ToDictionary(pair => pair.Key, pair => pair.Value.ToList()),
            DanHengChosenTraitId = _danHengChosenTraitId,
            DanHengSubUniqueId = _danHengSubUniqueId,
            DanHengMainUniqueId = _danHengMainUniqueId,
            PhainonKindling = _phainonKindling,
            PhainonConsumedCount = _phainonConsumedCount,
            RangerRoleIds = _rangerRoleIds.ToList(),
            MemberLinkUniqueIds = _memberLinkUniqueIds.ToList(),
            SeasonTwoPending = _seasonTwoPending == null
                ? null
                : new GridFightSeasonTwoPendingSnapshot
                {
                    Kind = (int)_seasonTwoPending.Kind,
                    QueuePosition = _seasonTwoPending.QueuePosition,
                    TraitId = _seasonTwoPending.TraitId,
                    EffectId = _seasonTwoPending.EffectId,
                    Options = _seasonTwoPending.Options.ToList(),
                },
            FinishedSeasonTwoQueuePositions = _finishedSeasonTwoQueuePositions.ToList(),
            SpentGoldThisNode = _spentGoldThisNode,
            BonusInvestmentQueue = _bonusInvestmentQueue.ToList(),
            BonusInvestmentSource = _bonusInvestmentSource,
            BonusInvestmentRerolls = _bonusInvestmentRerolls,
            BonusInvestmentReturnPhase = (int)_bonusInvestmentReturnPhase,
            OwlbertNpcs = _owlbertNpcs.Select(CaptureNpc).ToList(),
        };
    }

    /// <summary>
    ///     Rebuilds a run from its snapshot. Throws <see cref="InvalidDataException" /> when the snapshot does not fit the
    ///     loaded resources (other season, missing role or monster), so the caller can drop the run instead of
    ///     corrupting it.
    /// </summary>
    public static GridFightSession Restore(GridFightResourceCatalog catalog, GridFightRunSnapshot snapshot)
    {
        if (snapshot.Version > GridFightRunSnapshot.CurrentVersion)
            throw new InvalidDataException($"GridFight snapshot version {snapshot.Version} is newer than this server.");
        if (snapshot.Season != catalog.CurrentSeason)
            throw new InvalidDataException(
                $"GridFight snapshot season {snapshot.Season} does not match the loaded season {catalog.CurrentSeason}.");
        if (!Enum.IsDefined(typeof(GridFightSessionPhase), snapshot.Phase))
            throw new InvalidDataException($"Unknown GridFight phase {snapshot.Phase}.");
        // A run saved under other tables can hold a role the loaded ones no longer have (OSBETAWin4.7.51 removed the
        // 3-cost "星神★阿哈" 151102); every later lookup of such a role or shop slot would throw.
        var missingRoleId = snapshot.Roles.Select(role => role.RoleId)
            .Concat(snapshot.Shop.Select(slot => slot.RoleId))
            .FirstOrDefault(roleId => roleId != 0 && !GameData.GridFightRoleBasicInfoData.ContainsKey(roleId));
        if (missingRoleId != 0)
            throw new InvalidDataException($"GridFight role {missingRoleId} is missing from the loaded resources.");
        return new GridFightSession(catalog, snapshot);
    }

    private GridFightSession(GridFightResourceCatalog catalog, GridFightRunSnapshot s)
    {
        _catalog = catalog;
        Season = s.Season;
        DivisionId = s.DivisionId;
        IsOverlock = s.IsOverlock;
        UniqueId = s.UniqueId;
        PetPalId = s.PetPalId;
        PetPalEffectId = s.PetPalEffectId;
        CampIds = s.CampIds.ToList();
        RewardCampId = s.RewardCampId;
        Bosses = s.BossMonsterIds.Select(ResolveMonster).ToList();
        AffixIds = s.AffixIds.ToList();

        // Entity pools first so every later reference resolves to the same instance it had before the save.
        var roles = new Dictionary<uint, GridFightRoleState>();
        var equipments = new Dictionary<uint, GridFightEquipmentState>();
        var orbs = new Dictionary<uint, GridFightOrbState>();
        var forgeItems = new Dictionary<uint, GridFightForgeItemState>();
        var npcs = new Dictionary<uint, GridFightNpcState>();
        foreach (var role in s.Roles) Roles.Add(ResolveRole(roles, role));
        foreach (var equipment in s.Equipments) Equipments.Add(ResolveEquipment(equipments, equipment));
        foreach (var orb in s.Orbs) Orbs.Add(ResolveOrb(orbs, orb));
        foreach (var forgeItem in s.ForgeItems) ForgeItems.Add(ResolveForgeItem(forgeItems, forgeItem));
        foreach (var consumable in s.Consumables)
            Consumables.Add(new GridFightConsumableState
            {
                GroupId = consumable.GroupId,
                ItemId = consumable.ItemId,
                Count = consumable.Count,
            });
        foreach (var slot in s.Shop)
            Shop.Add(new GridFightShopSlot
            {
                Index = slot.Index,
                RoleId = slot.RoleId,
                SpecialGoodsId = slot.SpecialGoodsId,
                Star = slot.Star,
                Price = slot.Price,
                SoldOut = slot.SoldOut,
            });

        Phase = (GridFightSessionPhase)s.Phase;
        PendingQueuePosition = s.PendingQueuePosition;
        ChapterId = s.ChapterId;
        SectionId = s.SectionId;
        Gold = s.Gold;
        PlayerLevel = s.PlayerLevel;
        PlayerExp = s.PlayerExp;
        LineupHp = s.LineupHp;
        MaxLineupHp = s.MaxLineupHp;
        KeepWinCount = s.KeepWinCount;
        BattlesFinished = s.BattlesFinished;
        SelectedPortalId = s.SelectedPortalId;
        PortalOffer = s.PortalOffer.ToList();
        PortalRerollCount = s.PortalRerollCount;
        InvestmentOffer = s.InvestmentOffer.ToList();
        SupplyOffer = s.SupplyOffer.Select(offer => new GridFightSupplyOfferState(offer.RoleId, offer.EquipmentId)
        {
            Star = offer.Star,
            ExtraEquipmentIds = offer.ExtraEquipmentIds.ToList(),
        }).ToList();
        ActiveInvestmentIds.AddRange(s.ActiveInvestmentIds);
        SupplyRerollCount = s.SupplyRerollCount;
        EncounterOptions = s.EncounterOptions.Select(option => new GridFightEncounterOptionState(
            option.EliteBranchId,
            option.EncounterId,
            option.Quality,
            RestoreBattlePlan(option.BattlePlan),
            option.Rewards?.Select(drop => new GridFightEncounterDrop((GridFightDropType)drop.Type, drop.ItemId, drop.Num))
                .ToList())).ToList();
        if (s.BattlePlanEncounterIndex >= 0)
        {
            if (s.BattlePlanEncounterIndex >= EncounterOptions.Count)
                throw new InvalidDataException("GridFight snapshot battle plan points past its encounter options.");
            BattlePlan = EncounterOptions[s.BattlePlanEncounterIndex].BattlePlan;
        }
        else
        {
            BattlePlan = s.BattlePlan == null ? null : RestoreBattlePlan(s.BattlePlan);
        }

        HasCyreneCombatEnhancement = s.HasCyreneCombatEnhancement;
        HackConsole = s.HackConsole == null
            ? null
            : new GridFightHackConsoleState
            {
                GameItemUniqueId = s.HackConsole.GameItemUniqueId,
                RemainingGoldActions = s.HackConsole.RemainingGoldActions,
                RemainingHealActions = s.HackConsole.RemainingHealActions,
                RemainingOrbActions = s.HackConsole.RemainingOrbActions,
                EnemyHpPercent = s.HackConsole.EnemyHpPercent,
            };
        LastBattleStatistics = RestoreStatistics(s.LastBattleStatistics);
        AstaDogNpc = s.AstaDogNpc == null ? null : ResolveNpc(npcs, s.AstaDogNpc);
        PepeNpc = s.PepeNpc == null ? null : ResolveNpc(npcs, s.PepeNpc);
        EquipmentRecommendation = s.EquipmentRecommendation == null
            ? null
            : new GridFightEquipmentRecommendationState(
                s.EquipmentRecommendation.QueuePosition,
                s.EquipmentRecommendation.RoleUniqueId,
                s.EquipmentRecommendation.EquipmentIds.ToList());
        InvitationPortalEquipment = s.InvitationPortalEquipment == null
            ? null
            : ResolveEquipment(equipments, s.InvitationPortalEquipment);
        LaborDispatchEmblem = s.LaborDispatchEmblem == null ? null : ResolveEquipment(equipments, s.LaborDispatchEmblem);
        LaborDispatchTraitId = s.LaborDispatchTraitId;

        foreach (var (key, value) in s.RoleSavedValues) _roleSavedValues[key] = value;
        _nextEntityUniqueId = s.NextEntityUniqueId;
        _orbClaimed = s.OrbClaimed;
        _officialInitialRoundGranted = s.OfficialInitialRoundGranted;
        _mortenaxBladeTraitEffectInitialized = s.MortenaxBladeTraitEffectInitialized;
        _mortenaxBladeBattleEffects = s.MortenaxBladeBattleEffects.Select(effect => new GridFightMortenaxBladeBattleEffect(
            effect.RoleUniqueId,
            effect.EquipmentId,
            effect.ReplacedEquipmentUniqueId,
            effect.TemporaryInstanceId,
            effect.NeedsTraitEffectSync)).ToList();
        _cyrenePoemShopUnlocked = s.CyrenePoemShopUnlocked;
        _silverWolfHackUnlocked = s.SilverWolfHackUnlocked;
        _traitEffectSelectEnhanceQueuePosition =
            s.HasTraitEffectSelectEnhanceQueuePosition ? s.TraitEffectSelectEnhanceQueuePosition : null;
        foreach (var id in s.RerolledInvestmentIds) _rerolledInvestmentIds.Add(id);
        foreach (var id in s.RerolledAwayInvestmentIds) _rerolledAwayInvestmentIds.Add(id);
        foreach (var id in s.TemporarilyUnlockedRestrictedRoleIds) _temporarilyUnlockedRestrictedRoleIds.Add(id);
        foreach (var reward in s.TriggeredTalentRewards)
            _triggeredTalentRewards.Add((reward.TalentId, reward.EffectId, (GridFightTalentRewardTrigger)reward.Trigger,
                reward.ChapterId));
        _encounterRerollCount = s.EncounterRerollCount;
        _roleSwitchIds = new Dictionary<uint, uint>(s.RoleSwitchIds);

        foreach (var (key, value) in s.AugmentEffectNodeCounters) _augmentEffectNodeCounters[key] = value;
        _activePortalIds.AddRange(s.ActivePortalIds);
        foreach (var (key, value) in s.PortalSavedValues) _portalSavedValues[key] = value;
        _initialRolesMaterialized = s.InitialRolesMaterialized;
        _joyContractInitialized = s.JoyContractInitialized;
        _joyContractInitialRoleUniqueId = s.JoyContractInitialRoleUniqueId;
        _conceptStockInitialEquipment = s.ConceptStockInitialEquipment == null
            ? null
            : ResolveEquipment(equipments, s.ConceptStockInitialEquipment);
        foreach (var (key, value) in s.DiceBindings) _diceBindings[key] = value.ToArray();
        _dotContractInitialOrbs.AddRange(s.DotContractInitialOrbs.Select(orb => ResolveOrb(orbs, orb)));
        _dotContractBattleRewards.AddRange(s.DotContractBattleRewards.Select(orb => ResolveOrb(orbs, orb)));
        _dotContractBlackSwanGranted = s.DotContractBlackSwanGranted;
        foreach (var track in s.EquipmentTracks)
        {
            var state = new GridFightEquipmentTrackState
            {
                RoleId = track.RoleId,
                Source = (GridFightEquipTrackSource)track.Source,
                Priority = track.Priority,
            };
            state.TargetEquipmentIds.AddRange(track.TargetEquipmentIds);
            state.CompletedEquipmentIds.AddRange(track.CompletedEquipmentIds);
            _equipmentTracks.Add(state);
        }
        _pendingTrackedAutoEquipRoles.AddRange(s.PendingTrackedAutoEquipRoles.Select(role => ResolveRole(roles, role)));
        _fateDivinerPending = s.FateDivinerPending == null
            ? null
            : new GridFightFateDivinerPendingState(
                s.FateDivinerPending.QueuePosition,
                s.FateDivinerPending.OptionIds.ToList(),
                s.FateDivinerPending.IsInitial);
        _fateDivinerInitialized = s.FateDivinerInitialized;
        _fateDivinerReady = s.FateDivinerReady;
        _fateDivinerInitialActivationSyncSent = s.FateDivinerInitialActivationSyncSent;
        _fateDivinerBattleCount = s.FateDivinerBattleCount;
        foreach (var id in s.FateConsumedOptions) _fateConsumedOptions.Add(id);
        foreach (var id in s.FateBattleBuffOptions) _fateBattleBuffOptions.Add(id);
        _fateOption4Count = s.FateOption4Count;
        _fateOption6Count = s.FateOption6Count;
        _fateOption5GameItemUniqueId = s.FateOption5GameItemUniqueId;
        _foresightBonusAugments.AddRange(s.ForesightBonusAugments);
        foreach (var id in s.DifficultyExemptInvestmentIds) _difficultyExemptInvestmentIds.Add(id);
        _galaxyScholarDna = s.GalaxyScholarDna;
        _galaxyScholarRoundUpdateDirty = s.GalaxyScholarRoundUpdateDirty;
        foreach (var threshold in s.GalaxyScholarGrantedThresholds) _galaxyScholarGrantedThresholds.Add(threshold);
        _galaxyScholarPendingRewardOrbs.AddRange(s.GalaxyScholarPendingRewardOrbs.Select(orb => ResolveOrb(orbs, orb)));
        _headPlayerPending = s.HeadPlayerPending == null
            ? null
            : new GridFightHeadPlayerPendingState(
                s.HeadPlayerPending.QueuePosition,
                s.HeadPlayerPending.RoleUniqueId,
                s.HeadPlayerPending.RoleId,
                s.HeadPlayerPending.EffectId,
                s.HeadPlayerPending.OptionIds.ToList(),
                s.HeadPlayerPending.IsFinalHack);
        foreach (var state in s.HandledHeadPlayerStates)
            _handledHeadPlayerStates.Add((state.First, state.Second, state.Third));
        _silverWolfTraitInitialized = s.SilverWolfTraitInitialized;
        _silverWolfUpgradeRouteClosed = s.SilverWolfUpgradeRouteClosed;
        _silverWolfCurrentRoleId = s.SilverWolfCurrentRoleId;
        foreach (var (key, value) in s.InvestmentNodeCounters) _investmentNodeCounters[key] = value;
        _roundBeginSavedValueUpdates.AddRange(s.RoundBeginSavedValueUpdates.Select(update =>
            (update.AugmentId, update.EffectId, update.Key, update.Value)));
        _lizhiNpcs.AddRange(s.LizhiNpcs.Select(npc => ResolveNpc(npcs, npc)));
        _lizhiPermanentBenchBonus = s.LizhiPermanentBenchBonus;
        _gunpowderInitialForgeItems.AddRange(s.GunpowderInitialForgeItems.Select(item => ResolveForgeItem(forgeItems, item)));
        _talentRecruitmentInitialOrb = s.TalentRecruitmentInitialOrb == null
            ? null
            : ResolveOrb(orbs, s.TalentRecruitmentInitialOrb);
        _twinPrimesRewards.AddRange(s.TwinPrimesRewards.Select(reward =>
            new GridFightTwinPrimesReward(reward.EffectId, reward.ExpAmount)));
        foreach (var rarity in s.TwinPrimesRewardedRarities) _twinPrimesRewardedRarities.Add(rarity);
        _goldFuturesPendingReady = s.GoldFuturesPendingReady;
        _goldFuturesInvestmentPending = s.GoldFuturesInvestmentPending;
        _lastGoldFuturesChildSelection = s.LastGoldFuturesChildSelection;
        _goldFuturesNodeCount = s.GoldFuturesNodeCount;
        _spentGoldThisNode = s.SpentGoldThisNode;
        _bonusInvestmentQueue.AddRange(s.BonusInvestmentQueue ?? []);
        _bonusInvestmentSource = s.BonusInvestmentSource;
        _bonusInvestmentRerolls = s.BonusInvestmentRerolls;
        _bonusInvestmentReturnPhase = (GridFightSessionPhase)s.BonusInvestmentReturnPhase;
        _owlbertNpcs.AddRange(s.OwlbertNpcs.Select(npc => ResolveNpc(npcs, npc)));
        _shopLocked = s.ShopLocked;
        _marketVitalitySavedValueUpdates.AddRange(s.MarketVitalitySavedValueUpdates.Select(pair => (pair.First, pair.Second)));
        _pepeCameoNodes = s.PepeCameoNodes;
        _pepeCameoActive = s.PepeCameoActive;
        _roughPepeDropPending = s.RoughPepeDropPending;
        _manualRefreshCount = s.ManualRefreshCount;
        _paidRefreshCount = s.PaidRefreshCount;
        _longTermRefreshDiscount = s.LongTermRefreshDiscount;
        _successExperienceNodes = s.SuccessExperienceNodes;
        _ordinaryInvestmentOffers = s.OrdinaryInvestmentOffers;
        foreach (var (key, value) in s.ReportedRoleSavedValues) _reportedRoleSavedValues[key] = value;
        _rotationPortalInitialized = s.RotationPortalInitialized;
        _rotationBoostedRarity = s.RotationBoostedRarity;
        _heroEntranceInitialized = s.HeroEntranceInitialized;
        _heroEntranceRemaining = s.HeroEntranceRemaining;
        _heroEntranceRoleUniqueId = s.HeroEntranceRoleUniqueId;
        _heroEntranceRoleId = s.HeroEntranceRoleId;
        _heroEntrancePerfectAdvancePending = s.HeroEntrancePerfectAdvancePending;
        _heroEntranceRoundStateDirty = s.HeroEntranceRoundStateDirty;
        _heroEntranceStateDirty = s.HeroEntranceStateDirty;
        _moonGatheringSecondHalf.AddRange(s.MoonGatheringSecondHalf);
        _investmentUpgradePending = s.InvestmentUpgradePending;
        foreach (var id in s.LastBattleBoardUniqueIds) _lastBattleBoardUniqueIds.Add(id);
        _refreshVisionRefreshCount = s.RefreshVisionRefreshCount;
        _advancedRefreshVisionRefreshCount = s.AdvancedRefreshVisionRefreshCount;
        _goldOwlbertPaidThisNode = s.GoldOwlbertPaidThisNode;
        _trackedTraitIds.AddRange(s.TrackedTraitIds);
        _preparationPhaseCount = s.PreparationPhaseCount;

        _ahaModifierUniqueId = s.AhaModifierUniqueId;
        _ahaUltraConfirmed = s.AhaUltraConfirmed;
        _ahaUltraIssued = s.AhaUltraIssued;
        foreach (var (key, value) in s.AhaUltraItemIds) _ahaUltraItemIds[key] = value;
        _bonfireNodes.AddRange(s.BonfireNodes.Select(node =>
            new GridFightBonfireNodeState(node.ChapterId, node.SectionId, node.EventId, node.PetPalEffectId)));
        foreach (var node in s.BonfireResolved) _bonfireResolved.Add((node.First, node.Second));
        _bonfireOptions = s.BonfireOptions.Select(option =>
            new GridFightBonfireOption(option.ShowType, option.RoleId, option.EventId, option.OptionId)).ToList();
        _bonfireSelected = s.BonfireSelected;
        _claraEnhancements.AddRange(s.ClaraEnhancements.Select(pair => (pair.First, pair.Second)));
        _claraFielded = s.ClaraFielded;
        _claraPendingUpgrades = s.ClaraPendingUpgrades;
        _claraNodesSinceUpgrade = s.ClaraNodesSinceUpgrade;
        _claraUpgradeCount = s.ClaraUpgradeCount;
        _claraNode = (s.ClaraNode.First, s.ClaraNode.Second);
        _ahaGrowth = s.AhaGrowth;
        _elationTemplateId = s.ElationTemplateId;
        _elationSlots.AddRange(s.ElationSlots.Select(slot => new GridFightElationSlotState
        {
            PresetEquipmentId = slot.PresetEquipmentId,
            EquipmentUniqueId = slot.EquipmentUniqueId,
        }));
        _chimeraEnergy = s.ChimeraEnergy;
        _chimeras.AddRange(s.Chimeras.Select(chimera => new GridFightChimeraState
        {
            UniqueId = chimera.UniqueId,
            ChimeraId = chimera.ChimeraId,
            Star = chimera.Star,
        }));
        _chimeraNode = (s.ChimeraNode.First, s.ChimeraNode.Second);
        foreach (var layer in s.ChimeraFreeLayers) _chimeraFreeLayers.Add(layer);
        _chimeraRecruits = s.ChimeraRecruits;
        _chimeraGoods = s.ChimeraGoods.Select(good => new GridFightChimeraGood(good.First, good.Second)).ToList();
        _chimeraRerolls = s.ChimeraRerolls;
        _chimeraPassiveGold = s.ChimeraPassiveGold;
        _moonGameSubTraitId = s.MoonGameSubTraitId;
        _moonGameWishPower = s.MoonGameWishPower;
        foreach (var (key, value) in s.XianzhouBattleSwaps) _xianzhouBattleSwaps[key] = value;
        _petPalStartGranted = s.PetPalStartGranted;
        _petPalGuardUsed = s.PetPalGuardUsed;
        foreach (var chapter in s.PetPalCraftRefundChapters) _petPalCraftRefundChapters.Add(chapter);
        _petPalPendingEquipment.AddRange(s.PetPalPendingEquipment);
        _jadeGoods.AddRange(s.JadeGoods.Select(good =>
            new GridFightJadeShopGoodState(good.SlotId, good.GoodId, good.Rewards.Select(RestoreRewardEntry).ToList())));
        _jadeGoodsNode = (s.JadeGoodsNode.First, s.JadeGoodsNode.Second);
        _jadeGoodsStar = s.JadeGoodsStar;
        _jadeAutoOpen = s.JadeAutoOpen;
        _trailblazeCoin = s.TrailblazeCoin;
        _trailblazeCostedCoin = s.TrailblazeSpent;
        _pamKingUnlocked = s.PamKingGranted;
        _trailblazeGoods.AddRange(s.TrailblazeGoods.Select(good => new GridFightTrailblazeGoodState(good.First, good.Second)));
        foreach (var (key, value) in s.TrailblazeBought) _trailblazeBought[key] = value;
        _trailblazeGoodsNode = (s.TrailblazeGoodsNode.First, s.TrailblazeGoodsNode.Second);
        _trailblazeActivated = s.TrailblazeActivated;
        _pamKingModifierUniqueId = s.PamKingModifierUniqueId;
        _astropolisNode = (s.AstropolisNode.First, s.AstropolisNode.Second);
        _freeShopRefreshCount = s.FreeShopRefreshCount;
        _spotLights.AddRange(s.SpotLights.Select(spotLight => new GridFightSpotLightState
        {
            UniqueId = spotLight.UniqueId,
            SpotLightId = spotLight.SpotLightId,
            RoleId = spotLight.RoleId,
            RoleUniqueId = spotLight.RoleUniqueId,
            IsLoss = spotLight.IsLoss,
        }));
        _spotLightCandidates.AddRange(s.SpotLightCandidates);
        foreach (var (key, value) in s.SpotLightTraitItemIds) _spotLightTraitItemIds[key] = value;
        _spotLightRerollsLeft = s.SpotLightRerollsLeft;
        _spotLightTargetUniqueId = s.SpotLightTargetUniqueId;
        _spotLightGrantRoleId = s.SpotLightGrantRoleId;
        _spotLightReplaceTotal = s.SpotLightReplaceTotal;
        _spotLightReplaceUsed = s.SpotLightReplaceUsed;
        _spotLightFreeRefreshTotal = s.SpotLightFreeRefreshTotal;
        _spotLightFreeRefreshUsed = s.SpotLightFreeRefreshUsed;
        _spotLightPendingSelections = s.SpotLightPendingSelections;
        foreach (var node in s.SpotLightNodesDone) _spotLightNodesDone.Add((node.First, node.Second));
        foreach (var node in s.SpotLightNodesVisited) _spotLightNodesVisited.Add((node.First, node.Second));
        _spotLightCountersDirty = s.SpotLightCountersDirty;
        foreach (var (key, value) in s.TraitTemplateIds) _traitTemplateIds[key] = value;
        foreach (var (key, value) in s.TemplateEquipmentUniqueIds) _templateEquipmentUniqueIds[key] = value.ToList();
        _danHengChosenTraitId = s.DanHengChosenTraitId;
        _danHengSubUniqueId = s.DanHengSubUniqueId;
        _danHengMainUniqueId = s.DanHengMainUniqueId;
        _phainonKindling = s.PhainonKindling;
        _phainonConsumedCount = s.PhainonConsumedCount;
        _rangerRoleIds.AddRange(s.RangerRoleIds);
        _memberLinkUniqueIds.AddRange(s.MemberLinkUniqueIds);
        _seasonTwoPending = s.SeasonTwoPending == null
            ? null
            : new GridFightSeasonTwoPendingState(
                (GridFightSeasonTwoPendingKind)s.SeasonTwoPending.Kind,
                s.SeasonTwoPending.QueuePosition,
                s.SeasonTwoPending.TraitId,
                s.SeasonTwoPending.EffectId,
                s.SeasonTwoPending.Options.ToList());
        foreach (var position in s.FinishedSeasonTwoQueuePositions) _finishedSeasonTwoQueuePositions.Add(position);
        // Blobs from before the "帕姆王" modifier: an active train still needs one for its progress bar.
        if (_trailblazeActivated && _pamKingModifierUniqueId == 0) _pamKingModifierUniqueId = _nextEntityUniqueId++;
        // Older builds recorded these investments but never created their NPC/rewards.
        foreach (var augmentId in ActiveInvestmentIds)
            InitializeOwlbert(augmentId);
        _owlbertArrivals.Clear();
        _augmentSelectionEffects.Clear(); // Reconnect sends the resulting full inventory/team snapshot.
    }

    private static GridFightUIntPairSnapshot Pair(uint first, uint second) => new() { First = first, Second = second };

    private static GridFightRoleSnapshot CaptureRole(GridFightRoleState role) => new()
    {
        UniqueId = role.UniqueId,
        RoleId = role.RoleId,
        Star = role.Star,
        Position = role.Position,
        EquippedEquipmentUniqueIds = role.EquippedEquipmentUniqueIds.ToList(),
        HasPowerGrowth = role.HasPowerGrowth,
        PermanentPropertyBonuses = new Dictionary<uint, uint>(role.PermanentPropertyBonuses),
    };

    private static GridFightEquipmentSnapshot CaptureEquipment(GridFightEquipmentState equipment) => new()
    {
        UniqueId = equipment.UniqueId,
        EquipmentId = equipment.EquipmentId,
        Source = equipment.Source,
        IsLocked = equipment.IsLocked,
        IsTemporary = equipment.IsTemporary,
    };

    private static GridFightOrbSnapshot CaptureOrb(GridFightOrbState orb) => new()
    {
        UniqueId = orb.UniqueId,
        OrbItemId = orb.OrbItemId,
    };

    private static GridFightForgeItemSnapshot CaptureForgeItem(GridFightForgeItemState item) => new()
    {
        UniqueId = item.UniqueId,
        ForgeItemId = item.ForgeItemId,
        Position = item.Position,
        EquipmentChoices = item.EquipmentChoices.ToList(),
    };

    private static GridFightNpcSnapshot CaptureNpc(GridFightNpcState npc) => new()
    {
        UniqueId = npc.UniqueId,
        NpcId = npc.NpcId,
        Position = npc.Position,
        EquippedEquipmentUniqueIds = npc.EquippedEquipmentUniqueIds.ToList(),
    };

    private static GridFightRewardEntrySnapshot CaptureRewardEntry(GridFightRewardEntry entry) => new()
    {
        Type = (int)entry.Type,
        ItemId = entry.ItemId,
        Count = entry.Count,
        RoleStar = entry.RoleStar,
    };

    private static GridFightRewardEntry RestoreRewardEntry(GridFightRewardEntrySnapshot entry) =>
        new((GridFightBonusTypeEnum)entry.Type, entry.ItemId, entry.Count, entry.RoleStar);

    private static GridFightBattlePlanSnapshot CaptureBattlePlan(GridFightBattlePlan plan) => new()
    {
        CampId = plan.CampId,
        EliteGroupId = plan.EliteGroupId,
        PenaltyRuleId = plan.PenaltyRuleId,
        DifficultyAdd = plan.DifficultyAdd,
        Waves = plan.Waves.Select(wave => new GridFightMonsterWaveSnapshot
        {
            WaveId = wave.WaveId,
            FormationWaveId = wave.FormationWaveId,
            Monsters = wave.Monsters.Select(monster => new GridFightMonsterPlanSnapshot
            {
                MonsterId = monster.Monster.MonsterID,
                RoleStar = monster.RoleStar,
                Drops = monster.Drops.Select(CaptureRewardEntry).ToList(),
            }).ToList(),
        }).ToList(),
    };

    private static GridFightBattlePlan RestoreBattlePlan(GridFightBattlePlanSnapshot plan) => new(
        plan.CampId,
        plan.EliteGroupId,
        plan.PenaltyRuleId,
        plan.DifficultyAdd,
        plan.Waves.Select(wave => new GridFightMonsterWavePlan(
            wave.WaveId,
            wave.FormationWaveId,
            wave.Monsters.Select(monster => new GridFightMonsterPlan(
                ResolveMonster(monster.MonsterId),
                monster.RoleStar,
                monster.Drops.Select(RestoreRewardEntry).ToList())).ToList())).ToList());

    private static GridFightMonsterExcel ResolveMonster(uint monsterId) =>
        GameData.GridFightMonsterData.TryGetValue(monsterId, out var monster)
            ? monster
            : throw new InvalidDataException($"GridFight monster {monsterId} is missing from the loaded resources.");

    private static GridFightBattleStatisticsSnapshot CaptureStatistics(GridFightBattleStatistics statistics) => new()
    {
        ProgressPercent = statistics.ProgressPercent,
        RoleDamage = statistics.RoleDamage.Select(row => new GridFightBattleDamageSnapshot
            { Id = row.RoleId, Damage = row.Damage, SecondaryDamage = row.SecondaryDamage }).ToList(),
        TraitDamage = statistics.TraitDamage.Select(row => new GridFightBattleDamageSnapshot
            { Id = row.TraitId, Damage = row.Damage, SecondaryDamage = row.SecondaryDamage }).ToList(),
        AugmentDamage = statistics.AugmentDamage.Select(row => new GridFightBattleDamageSnapshot
            { Id = row.AugmentId, Damage = row.Damage, SecondaryDamage = row.SecondaryDamage }).ToList(),
        KilledMonsters = statistics.KilledMonsters.Select(row => new GridFightKilledMonsterSnapshot
            { WaveIndex = row.WaveIndex, MonsterIndex = row.MonsterIndex, MonsterId = row.MonsterId }).ToList(),
        RoundCount = statistics.RoundCount,
        SavedValues = new Dictionary<string, uint>(statistics.SavedValues),
        IsPresent = statistics.IsPresent,
    };

    private static GridFightBattleStatistics RestoreStatistics(GridFightBattleStatisticsSnapshot statistics) =>
        statistics.IsPresent || statistics.ProgressPercent != 0 || statistics.RoundCount != 0
            ? new GridFightBattleStatistics(
                statistics.ProgressPercent,
                statistics.RoleDamage.Select(row => new GridFightRoleBattleDamage(row.Id, row.Damage, row.SecondaryDamage))
                    .ToList(),
                statistics.TraitDamage.Select(row => new GridFightTraitBattleDamage(row.Id, row.Damage, row.SecondaryDamage))
                    .ToList(),
                statistics.AugmentDamage.Select(row =>
                    new GridFightAugmentBattleDamage(row.Id, row.Damage, row.SecondaryDamage)).ToList(),
                statistics.KilledMonsters.Select(row =>
                    new GridFightKilledMonsterInstance(row.WaveIndex, row.MonsterIndex, row.MonsterId)).ToList(),
                statistics.RoundCount,
                new Dictionary<string, uint>(statistics.SavedValues),
                statistics.IsPresent)
            : GridFightBattleStatistics.Empty;

    private static GridFightRoleState ResolveRole(Dictionary<uint, GridFightRoleState> pool, GridFightRoleSnapshot role)
    {
        if (pool.TryGetValue(role.UniqueId, out var existing)) return existing;
        var state = new GridFightRoleState
        {
            UniqueId = role.UniqueId,
            RoleId = role.RoleId,
            Star = role.Star,
            Position = role.Position,
        };
        state.EquippedEquipmentUniqueIds.AddRange(role.EquippedEquipmentUniqueIds);
        state.HasPowerGrowth = role.HasPowerGrowth;
        foreach (var (property, bonus) in role.PermanentPropertyBonuses ?? [])
            state.PermanentPropertyBonuses[property] = bonus;
        pool[role.UniqueId] = state;
        return state;
    }

    private static GridFightEquipmentState ResolveEquipment(
        Dictionary<uint, GridFightEquipmentState> pool,
        GridFightEquipmentSnapshot equipment)
    {
        if (pool.TryGetValue(equipment.UniqueId, out var existing)) return existing;
        var state = new GridFightEquipmentState
        {
            UniqueId = equipment.UniqueId,
            EquipmentId = equipment.EquipmentId,
            Source = equipment.Source,
            IsLocked = equipment.IsLocked,
            IsTemporary = equipment.IsTemporary,
        };
        pool[equipment.UniqueId] = state;
        return state;
    }

    private static GridFightOrbState ResolveOrb(Dictionary<uint, GridFightOrbState> pool, GridFightOrbSnapshot orb)
    {
        if (pool.TryGetValue(orb.UniqueId, out var existing)) return existing;
        var state = new GridFightOrbState { UniqueId = orb.UniqueId, OrbItemId = orb.OrbItemId };
        pool[orb.UniqueId] = state;
        return state;
    }

    private static GridFightForgeItemState ResolveForgeItem(
        Dictionary<uint, GridFightForgeItemState> pool,
        GridFightForgeItemSnapshot item)
    {
        if (pool.TryGetValue(item.UniqueId, out var existing)) return existing;
        var state = new GridFightForgeItemState
        {
            UniqueId = item.UniqueId,
            ForgeItemId = item.ForgeItemId,
            Position = item.Position,
            EquipmentChoices = item.EquipmentChoices.ToList(),
        };
        pool[item.UniqueId] = state;
        return state;
    }

    private static GridFightNpcState ResolveNpc(Dictionary<uint, GridFightNpcState> pool, GridFightNpcSnapshot npc)
    {
        if (pool.TryGetValue(npc.UniqueId, out var existing)) return existing;
        var state = new GridFightNpcState { UniqueId = npc.UniqueId, NpcId = npc.NpcId, Position = npc.Position };
        state.EquippedEquipmentUniqueIds.AddRange(npc.EquippedEquipmentUniqueIds);
        pool[npc.UniqueId] = state;
        return state;
    }
}
