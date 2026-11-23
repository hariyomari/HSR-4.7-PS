using MemoryPack;

namespace March7thHoney.GameServer.Game.GridFight.Persistence;

// Persisted form of a live GridFight run (GridFightData.RunInstance, Base64 MemoryPack).
// MemoryPack reads members by declaration order: only ever append members, never reorder or remove them.
// Enums are stored as int so renaming an enum member never breaks an older blob.

[MemoryPackable]
public partial class GridFightRunSnapshot
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;

    // ---- Identity (constructor state) ----
    public uint Season { get; set; }
    public uint DivisionId { get; set; }
    public bool IsOverlock { get; set; }
    public uint UniqueId { get; set; }
    public uint PetPalId { get; set; }
    public uint PetPalEffectId { get; set; }
    public List<uint> CampIds { get; set; } = [];
    public uint RewardCampId { get; set; }
    public List<uint> BossMonsterIds { get; set; } = [];
    public List<uint> AffixIds { get; set; } = [];

    // ---- Core run state ----
    public int Phase { get; set; }
    public uint PendingQueuePosition { get; set; }
    public uint ChapterId { get; set; }
    public uint SectionId { get; set; }
    public uint Gold { get; set; }
    public uint PlayerLevel { get; set; }
    public uint PlayerExp { get; set; }
    public uint LineupHp { get; set; }
    public uint MaxLineupHp { get; set; }
    public uint KeepWinCount { get; set; }
    public uint BattlesFinished { get; set; }
    public uint SelectedPortalId { get; set; }
    public List<uint> PortalOffer { get; set; } = [];
    public uint PortalRerollCount { get; set; }
    public List<uint> InvestmentOffer { get; set; } = [];
    public List<GridFightSupplyOfferSnapshot> SupplyOffer { get; set; } = [];
    public List<uint> ActiveInvestmentIds { get; set; } = [];
    public uint SupplyRerollCount { get; set; }
    public List<GridFightEncounterOptionSnapshot> EncounterOptions { get; set; } = [];
    public GridFightBattlePlanSnapshot? BattlePlan { get; set; }

    // Index into EncounterOptions whose plan *is* BattlePlan (record equality compares the wave lists by
    // reference, e.g. EliteBranchId); -1 when BattlePlan is a node plan of its own.
    public int BattlePlanEncounterIndex { get; set; } = -1;

    public List<GridFightRoleSnapshot> Roles { get; set; } = [];
    public List<GridFightEquipmentSnapshot> Equipments { get; set; } = [];
    public List<GridFightOrbSnapshot> Orbs { get; set; } = [];
    public List<GridFightConsumableSnapshot> Consumables { get; set; } = [];
    public List<GridFightForgeItemSnapshot> ForgeItems { get; set; } = [];
    public List<GridFightShopSlotSnapshot> Shop { get; set; } = [];
    public bool HasCyreneCombatEnhancement { get; set; }
    public GridFightHackConsoleSnapshot? HackConsole { get; set; }
    public GridFightBattleStatisticsSnapshot LastBattleStatistics { get; set; } = new();
    public GridFightNpcSnapshot? AstaDogNpc { get; set; }
    public GridFightNpcSnapshot? PepeNpc { get; set; }
    public GridFightEquipmentRecommendationSnapshot? EquipmentRecommendation { get; set; }
    public GridFightEquipmentSnapshot? InvitationPortalEquipment { get; set; }
    public GridFightEquipmentSnapshot? LaborDispatchEmblem { get; set; }
    public uint LaborDispatchTraitId { get; set; }

    // ---- Private session fields (GridFightSession.cs) ----
    public Dictionary<string, uint> RoleSavedValues { get; set; } = [];
    public uint NextEntityUniqueId { get; set; }
    public bool OrbClaimed { get; set; }
    public bool OfficialInitialRoundGranted { get; set; }
    public bool MortenaxBladeTraitEffectInitialized { get; set; }
    public List<GridFightMortenaxBladeEffectSnapshot> MortenaxBladeBattleEffects { get; set; } = [];
    public bool CyrenePoemShopUnlocked { get; set; }
    public bool SilverWolfHackUnlocked { get; set; }
    public bool HasTraitEffectSelectEnhanceQueuePosition { get; set; }
    public uint TraitEffectSelectEnhanceQueuePosition { get; set; }
    public List<uint> RerolledInvestmentIds { get; set; } = [];
    public List<uint> RerolledAwayInvestmentIds { get; set; } = [];
    public List<uint> TemporarilyUnlockedRestrictedRoleIds { get; set; } = [];
    public List<GridFightTalentRewardSnapshot> TriggeredTalentRewards { get; set; } = [];
    public uint EncounterRerollCount { get; set; }
    public Dictionary<uint, uint> RoleSwitchIds { get; set; } = [];

    // ---- Portals, investments and augments ----
    public Dictionary<uint, uint> AugmentEffectNodeCounters { get; set; } = [];
    public List<uint> ActivePortalIds { get; set; } = [];
    public Dictionary<string, uint> PortalSavedValues { get; set; } = [];
    public bool InitialRolesMaterialized { get; set; }
    public bool JoyContractInitialized { get; set; }
    public uint JoyContractInitialRoleUniqueId { get; set; }
    public GridFightEquipmentSnapshot? ConceptStockInitialEquipment { get; set; }
    public Dictionary<uint, List<uint>> DiceBindings { get; set; } = [];
    public List<GridFightOrbSnapshot> DotContractInitialOrbs { get; set; } = [];
    public List<GridFightOrbSnapshot> DotContractBattleRewards { get; set; } = [];
    public bool DotContractBlackSwanGranted { get; set; }
    public List<GridFightEquipmentTrackSnapshot> EquipmentTracks { get; set; } = [];
    public List<GridFightRoleSnapshot> PendingTrackedAutoEquipRoles { get; set; } = [];
    public GridFightFateDivinerPendingSnapshot? FateDivinerPending { get; set; }
    public bool FateDivinerInitialized { get; set; }
    public bool FateDivinerReady { get; set; }
    public bool FateDivinerInitialActivationSyncSent { get; set; }
    public uint FateDivinerBattleCount { get; set; }
    public List<uint> FateConsumedOptions { get; set; } = [];
    public List<uint> FateBattleBuffOptions { get; set; } = [];
    public uint FateOption4Count { get; set; }
    public uint FateOption6Count { get; set; }
    public uint FateOption5GameItemUniqueId { get; set; }
    public List<uint> ForesightBonusAugments { get; set; } = [];
    public List<uint> DifficultyExemptInvestmentIds { get; set; } = [];
    public uint GalaxyScholarDna { get; set; }
    public bool GalaxyScholarRoundUpdateDirty { get; set; }
    public List<uint> GalaxyScholarGrantedThresholds { get; set; } = [];
    public List<GridFightOrbSnapshot> GalaxyScholarPendingRewardOrbs { get; set; } = [];
    public GridFightHeadPlayerPendingSnapshot? HeadPlayerPending { get; set; }
    public List<GridFightUIntTripleSnapshot> HandledHeadPlayerStates { get; set; } = [];
    public bool SilverWolfTraitInitialized { get; set; }
    public bool SilverWolfUpgradeRouteClosed { get; set; }
    public uint SilverWolfCurrentRoleId { get; set; }
    public Dictionary<uint, uint> InvestmentNodeCounters { get; set; } = [];
    public List<GridFightSavedValueUpdateSnapshot> RoundBeginSavedValueUpdates { get; set; } = [];
    public List<GridFightNpcSnapshot> LizhiNpcs { get; set; } = [];
    public uint LizhiPermanentBenchBonus { get; set; }
    public List<GridFightForgeItemSnapshot> GunpowderInitialForgeItems { get; set; } = [];
    public GridFightOrbSnapshot? TalentRecruitmentInitialOrb { get; set; }
    public List<GridFightTwinPrimesRewardSnapshot> TwinPrimesRewards { get; set; } = [];
    public List<uint> TwinPrimesRewardedRarities { get; set; } = [];
    public bool GoldFuturesPendingReady { get; set; }
    public bool GoldFuturesInvestmentPending { get; set; }
    public uint LastGoldFuturesChildSelection { get; set; }
    public uint GoldFuturesNodeCount { get; set; }
    public bool ShopLocked { get; set; }
    public List<GridFightUIntPairSnapshot> MarketVitalitySavedValueUpdates { get; set; } = [];
    public uint PepeCameoNodes { get; set; }
    public bool PepeCameoActive { get; set; }
    public bool RoughPepeDropPending { get; set; }
    public uint ManualRefreshCount { get; set; }
    public uint PaidRefreshCount { get; set; }
    public bool LongTermRefreshDiscount { get; set; }
    public uint SuccessExperienceNodes { get; set; }
    public uint OrdinaryInvestmentOffers { get; set; }
    public Dictionary<string, uint> ReportedRoleSavedValues { get; set; } = [];
    public bool RotationPortalInitialized { get; set; }
    public uint RotationBoostedRarity { get; set; }
    public bool HeroEntranceInitialized { get; set; }
    public uint HeroEntranceRemaining { get; set; }
    public uint HeroEntranceRoleUniqueId { get; set; }
    public uint HeroEntranceRoleId { get; set; }
    public bool HeroEntrancePerfectAdvancePending { get; set; }
    public bool HeroEntranceRoundStateDirty { get; set; }
    public bool HeroEntranceStateDirty { get; set; }
    public List<uint> MoonGatheringSecondHalf { get; set; } = [];
    public bool InvestmentUpgradePending { get; set; }
    public List<uint> LastBattleBoardUniqueIds { get; set; } = [];
    public uint RefreshVisionRefreshCount { get; set; }
    public List<uint> TrackedTraitIds { get; set; } = [];
    public uint PreparationPhaseCount { get; set; }

    // ---- Season 2 traits ----
    public uint AhaModifierUniqueId { get; set; }
    public bool AhaUltraConfirmed { get; set; }
    public bool AhaUltraIssued { get; set; }
    public Dictionary<uint, uint> AhaUltraItemIds { get; set; } = [];
    public List<GridFightBonfireNodeSnapshot> BonfireNodes { get; set; } = [];
    public List<GridFightUIntPairSnapshot> BonfireResolved { get; set; } = [];
    public List<GridFightBonfireOptionSnapshot> BonfireOptions { get; set; } = [];
    public bool BonfireSelected { get; set; }
    public List<GridFightUIntPairSnapshot> ClaraEnhancements { get; set; } = [];
    public bool ClaraFielded { get; set; }
    public uint ClaraPendingUpgrades { get; set; }
    public uint ClaraNodesSinceUpgrade { get; set; }
    public uint ClaraUpgradeCount { get; set; }
    public GridFightUIntPairSnapshot ClaraNode { get; set; } = new();
    public uint AhaGrowth { get; set; }
    public uint ElationTemplateId { get; set; }
    public List<GridFightElationSlotSnapshot> ElationSlots { get; set; } = [];
    public uint ChimeraEnergy { get; set; }
    public List<GridFightChimeraSnapshot> Chimeras { get; set; } = [];
    public GridFightUIntPairSnapshot ChimeraNode { get; set; } = new();
    public List<uint> ChimeraFreeLayers { get; set; } = [];
    public uint ChimeraRecruits { get; set; }
    public List<GridFightUIntPairSnapshot> ChimeraGoods { get; set; } = [];
    public uint ChimeraRerolls { get; set; }
    public uint ChimeraPassiveGold { get; set; }
    public uint MoonGameSubTraitId { get; set; }
    public uint MoonGameWishPower { get; set; }
    public Dictionary<uint, uint> XianzhouBattleSwaps { get; set; } = [];
    public bool PetPalStartGranted { get; set; }
    public bool PetPalGuardUsed { get; set; }
    public List<uint> PetPalCraftRefundChapters { get; set; } = [];
    public List<uint> PetPalPendingEquipment { get; set; } = [];
    public List<GridFightJadeGoodSnapshot> JadeGoods { get; set; } = [];
    public GridFightUIntPairSnapshot JadeGoodsNode { get; set; } = new();
    public uint JadeGoodsStar { get; set; }
    public bool JadeAutoOpen { get; set; }
    public uint TrailblazeCoin { get; set; }
    // Pam King (帕姆王) progress: the session's _trailblazeCostedCoin and _pamKingUnlocked.
    public uint TrailblazeSpent { get; set; }
    public bool PamKingGranted { get; set; }
    public List<GridFightUIntPairSnapshot> TrailblazeGoods { get; set; } = [];
    public Dictionary<uint, uint> TrailblazeBought { get; set; } = [];
    public GridFightUIntPairSnapshot TrailblazeGoodsNode { get; set; } = new();
    public bool TrailblazeActivated { get; set; }
    public GridFightUIntPairSnapshot AstropolisNode { get; set; } = new();
    public uint FreeShopRefreshCount { get; set; }
    public List<GridFightSpotLightSnapshot> SpotLights { get; set; } = [];
    public List<uint> SpotLightCandidates { get; set; } = [];
    public Dictionary<uint, uint> SpotLightTraitItemIds { get; set; } = [];
    public uint SpotLightRerollsLeft { get; set; }
    public uint SpotLightTargetUniqueId { get; set; }
    public uint SpotLightGrantRoleId { get; set; }
    public uint SpotLightReplaceTotal { get; set; }
    public uint SpotLightReplaceUsed { get; set; }
    public uint SpotLightFreeRefreshTotal { get; set; }
    public uint SpotLightFreeRefreshUsed { get; set; }
    public int SpotLightPendingSelections { get; set; }
    public List<GridFightUIntPairSnapshot> SpotLightNodesDone { get; set; } = [];
    public List<GridFightUIntPairSnapshot> SpotLightNodesVisited { get; set; } = [];
    public bool SpotLightCountersDirty { get; set; }
    public Dictionary<uint, uint> TraitTemplateIds { get; set; } = [];
    public Dictionary<uint, List<uint>> TemplateEquipmentUniqueIds { get; set; } = [];
    public uint DanHengChosenTraitId { get; set; }
    public uint DanHengSubUniqueId { get; set; }
    public uint DanHengMainUniqueId { get; set; }
    public uint PhainonKindling { get; set; }
    public uint PhainonConsumedCount { get; set; }
    public List<uint> RangerRoleIds { get; set; } = [];
    public List<uint> MemberLinkUniqueIds { get; set; } = [];
    public GridFightSeasonTwoPendingSnapshot? SeasonTwoPending { get; set; }
    public List<uint> FinishedSeasonTwoQueuePositions { get; set; } = [];

    // ---- Manager state ----
    // GridFightDamageSttInfo of the last settled battle (protobuf bytes).
    public byte[] LastBattleDamageStatistics { get; set; } = [];

    // ---- Appended members ----
    // 0 in blobs written before the Pam King modifier existed; restored as a fresh id when the train is active.
    public uint PamKingModifierUniqueId { get; set; }
    public bool SpentGoldThisNode { get; set; }
    public List<uint> BonusInvestmentQueue { get; set; } = [];
    public uint BonusInvestmentSource { get; set; }
    public uint BonusInvestmentRerolls { get; set; }
    public int BonusInvestmentReturnPhase { get; set; }
    public List<GridFightNpcSnapshot> OwlbertNpcs { get; set; } = [];
    public uint AdvancedRefreshVisionRefreshCount { get; set; }
    public uint GoldOwlbertPaidThisNode { get; set; }
}

[MemoryPackable]
public partial class GridFightRoleSnapshot
{
    public uint UniqueId { get; set; }
    public uint RoleId { get; set; }
    public uint Star { get; set; }
    public uint Position { get; set; }
    public List<uint> EquippedEquipmentUniqueIds { get; set; } = [];
    // Appended so older snapshots retain their original member order and default to no growth.
    public bool HasPowerGrowth { get; set; }
    public Dictionary<uint, uint> PermanentPropertyBonuses { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightEquipmentSnapshot
{
    public uint UniqueId { get; set; }
    public uint EquipmentId { get; set; }
    public uint Source { get; set; }
    public bool IsLocked { get; set; }
    // Appended: older snapshots read as permanent items.
    public bool IsTemporary { get; set; }
}

[MemoryPackable]
public partial class GridFightOrbSnapshot
{
    public uint UniqueId { get; set; }
    public uint OrbItemId { get; set; }
}

[MemoryPackable]
public partial class GridFightConsumableSnapshot
{
    public uint GroupId { get; set; }
    public uint ItemId { get; set; }
    public uint Count { get; set; }
}

[MemoryPackable]
public partial class GridFightForgeItemSnapshot
{
    public uint UniqueId { get; set; }
    public uint ForgeItemId { get; set; }
    public uint Position { get; set; }
    public List<uint> EquipmentChoices { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightShopSlotSnapshot
{
    public uint Index { get; set; }
    public uint RoleId { get; set; }
    public uint SpecialGoodsId { get; set; }
    public uint Star { get; set; }
    public uint Price { get; set; }
    public bool SoldOut { get; set; }
}

[MemoryPackable]
public partial class GridFightNpcSnapshot
{
    public uint UniqueId { get; set; }
    public uint NpcId { get; set; }
    public uint Position { get; set; }
    public List<uint> EquippedEquipmentUniqueIds { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightSupplyOfferSnapshot
{
    public uint RoleId { get; set; }
    public uint EquipmentId { get; set; }
    public uint Star { get; set; }
    public List<uint> ExtraEquipmentIds { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightRewardEntrySnapshot
{
    public int Type { get; set; }
    public uint ItemId { get; set; }
    public uint Count { get; set; }
    public uint RoleStar { get; set; }
}

[MemoryPackable]
public partial class GridFightMonsterPlanSnapshot
{
    public uint MonsterId { get; set; }
    public uint RoleStar { get; set; }
    public List<GridFightRewardEntrySnapshot> Drops { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightMonsterWaveSnapshot
{
    public uint WaveId { get; set; }
    public uint FormationWaveId { get; set; }
    public List<GridFightMonsterPlanSnapshot> Monsters { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightBattlePlanSnapshot
{
    public uint CampId { get; set; }
    public uint EliteGroupId { get; set; }
    public uint PenaltyRuleId { get; set; }
    public uint DifficultyAdd { get; set; }
    public List<GridFightMonsterWaveSnapshot> Waves { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightEncounterDropSnapshot
{
    public int Type { get; set; }
    public uint ItemId { get; set; }
    public uint Num { get; set; }
}

[MemoryPackable]
public partial class GridFightEncounterOptionSnapshot
{
    public uint EliteBranchId { get; set; }
    public uint EncounterId { get; set; }
    public uint Quality { get; set; }
    public GridFightBattlePlanSnapshot BattlePlan { get; set; } = new();

    // Null rewards (no reward table) differ from an empty list on the option card.
    public List<GridFightEncounterDropSnapshot>? Rewards { get; set; }
}

[MemoryPackable]
public partial class GridFightHackConsoleSnapshot
{
    public uint GameItemUniqueId { get; set; }
    public uint RemainingGoldActions { get; set; }
    public uint RemainingHealActions { get; set; }
    public uint RemainingOrbActions { get; set; }
    public uint EnemyHpPercent { get; set; }
}

[MemoryPackable]
public partial class GridFightBattleDamageSnapshot
{
    public uint Id { get; set; }
    public double Damage { get; set; }
    public double SecondaryDamage { get; set; }
}

[MemoryPackable]
public partial class GridFightKilledMonsterSnapshot
{
    public uint WaveIndex { get; set; }
    public uint MonsterIndex { get; set; }
    public uint MonsterId { get; set; }
}

[MemoryPackable]
public partial class GridFightBattleStatisticsSnapshot
{
    public uint ProgressPercent { get; set; }
    public List<GridFightBattleDamageSnapshot> RoleDamage { get; set; } = [];
    public List<GridFightBattleDamageSnapshot> TraitDamage { get; set; } = [];
    public List<GridFightBattleDamageSnapshot> AugmentDamage { get; set; } = [];
    public List<GridFightKilledMonsterSnapshot> KilledMonsters { get; set; } = [];
    public uint RoundCount { get; set; }
    public Dictionary<string, uint> SavedValues { get; set; } = [];
    public bool IsPresent { get; set; }
}

[MemoryPackable]
public partial class GridFightEquipmentRecommendationSnapshot
{
    public uint QueuePosition { get; set; }
    public uint RoleUniqueId { get; set; }
    public List<uint> EquipmentIds { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightEquipmentTrackSnapshot
{
    public uint RoleId { get; set; }
    public List<uint> TargetEquipmentIds { get; set; } = [];
    public List<uint> CompletedEquipmentIds { get; set; } = [];
    public int Source { get; set; }
    public uint Priority { get; set; }
}

[MemoryPackable]
public partial class GridFightFateDivinerPendingSnapshot
{
    public uint QueuePosition { get; set; }
    public List<uint> OptionIds { get; set; } = [];
    public bool IsInitial { get; set; }
}

[MemoryPackable]
public partial class GridFightHeadPlayerPendingSnapshot
{
    public uint QueuePosition { get; set; }
    public uint RoleUniqueId { get; set; }
    public uint RoleId { get; set; }
    public uint EffectId { get; set; }
    public List<uint> OptionIds { get; set; } = [];
    public bool IsFinalHack { get; set; }
}

[MemoryPackable]
public partial class GridFightSeasonTwoPendingSnapshot
{
    public int Kind { get; set; }
    public uint QueuePosition { get; set; }
    public uint TraitId { get; set; }
    public uint EffectId { get; set; }
    public List<uint> Options { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightMortenaxBladeEffectSnapshot
{
    public uint RoleUniqueId { get; set; }
    public uint EquipmentId { get; set; }
    public uint ReplacedEquipmentUniqueId { get; set; }
    public uint TemporaryInstanceId { get; set; }
    public bool NeedsTraitEffectSync { get; set; }
}

[MemoryPackable]
public partial class GridFightTalentRewardSnapshot
{
    public uint TalentId { get; set; }
    public uint EffectId { get; set; }
    public int Trigger { get; set; }
    public uint ChapterId { get; set; }
}

[MemoryPackable]
public partial class GridFightSavedValueUpdateSnapshot
{
    public uint AugmentId { get; set; }
    public uint EffectId { get; set; }
    public string Key { get; set; } = "";
    public uint Value { get; set; }
}

[MemoryPackable]
public partial class GridFightTwinPrimesRewardSnapshot
{
    public uint EffectId { get; set; }
    public uint ExpAmount { get; set; }
}

[MemoryPackable]
public partial class GridFightUIntPairSnapshot
{
    public uint First { get; set; }
    public uint Second { get; set; }
}

[MemoryPackable]
public partial class GridFightUIntTripleSnapshot
{
    public uint First { get; set; }
    public uint Second { get; set; }
    public uint Third { get; set; }
}

[MemoryPackable]
public partial class GridFightBonfireNodeSnapshot
{
    public uint ChapterId { get; set; }
    public uint SectionId { get; set; }
    public uint EventId { get; set; }
    public uint PetPalEffectId { get; set; }
}

[MemoryPackable]
public partial class GridFightBonfireOptionSnapshot
{
    public uint ShowType { get; set; }
    public uint RoleId { get; set; }
    public uint EventId { get; set; }
    public uint OptionId { get; set; }
}

[MemoryPackable]
public partial class GridFightElationSlotSnapshot
{
    public uint PresetEquipmentId { get; set; }
    public uint EquipmentUniqueId { get; set; }
}

[MemoryPackable]
public partial class GridFightChimeraSnapshot
{
    public uint UniqueId { get; set; }
    public uint ChimeraId { get; set; }
    public uint Star { get; set; }
}

[MemoryPackable]
public partial class GridFightJadeGoodSnapshot
{
    public uint SlotId { get; set; }
    public uint GoodId { get; set; }
    public List<GridFightRewardEntrySnapshot> Rewards { get; set; } = [];
}

[MemoryPackable]
public partial class GridFightSpotLightSnapshot
{
    public uint UniqueId { get; set; }
    public uint SpotLightId { get; set; }
    public uint RoleId { get; set; }
    public uint RoleUniqueId { get; set; }
    public bool IsLoss { get; set; }
}
