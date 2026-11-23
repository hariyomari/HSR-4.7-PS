using March7thHoney.Data;
using March7thHoney.Enums.GridFight;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightConsumableInventoryChange(
    uint GroupId,
    uint ItemId,
    uint RemainingCount,
    bool Removed);

public sealed record GridFightEquipDressResult(
    bool Success,
    IReadOnlyList<GridFightEquipmentState> RemovedEquipments,
    IReadOnlyList<GridFightEquipmentState> AddedEquipments,
    IReadOnlyList<GridFightRoleState> UpdatedRoles,
    bool AutoCrafted,
    bool MaxActiveRolesChanged)
{
    public static GridFightEquipDressResult Failed { get; } = new(false, [], [], [], false, false);
}

public sealed record GridFightEquipCraftResult(
    bool Success,
    IReadOnlyList<GridFightEquipmentState> RemovedEquipments,
    GridFightEquipmentState? AddedEquipment,
    bool MaxActiveRolesChanged)
{
    public static GridFightEquipCraftResult Failed { get; } = new(false, [], null, false);
}

public sealed record GridFightConsumableUseResult(
    bool Success,
    uint ItemId,
    GridFightConsumeTypeEnum Rule,
    GridFightConsumableInventoryChange? InventoryChange,
    IReadOnlyList<GridFightEquipmentState> RemovedEquipments,
    IReadOnlyList<GridFightEquipmentState> AddedEquipments,
    IReadOnlyList<GridFightRoleState> UpdatedRoles,
    GridFightRoleAcquisitionResult? RoleAcquisition,
    bool RecommendationStarted,
    bool MaxActiveRolesChanged)
{
    public static GridFightConsumableUseResult Failed { get; } =
        new(false, 0, GridFightConsumeTypeEnum.Remove, null, [], [], [], null, false, false);
    public uint TargetRoleUniqueId { get; init; }
}

public sealed record GridFightRecommendEquipmentSelectionResult(
    bool Success,
    uint FinishedQueuePosition,
    GridFightEquipmentState? Equipment)
{
    public static GridFightRecommendEquipmentSelectionResult Failed { get; } = new(false, 0, null);
}

public sealed record GridFightEquipmentRecommendationState(
    uint QueuePosition,
    uint RoleUniqueId,
    IReadOnlyList<uint> EquipmentIds);

public sealed partial class GridFightSession
{
    private const uint CraftedEquipmentSource = 5;
    private const uint RolledEquipmentSource = 0;
    private const uint RecommendedEquipmentSource = 0;
    private const int NormalEquipmentSlotCount = 3;

    public GridFightEquipmentRecommendationState? EquipmentRecommendation { get; private set; }

    public GridFightEquipDressResult DressEquipment(uint roleUniqueId, uint equipmentUniqueId)
    {
        if (!CanModifyEquipment()) return GridFightEquipDressResult.Failed;
        var role = Roles.FirstOrDefault(candidate => candidate.UniqueId == roleUniqueId);
        var equipment = Equipments.FirstOrDefault(candidate => candidate.UniqueId == equipmentUniqueId);
        if (role == null || equipment == null || equipment.IsLocked ||
            role.EquippedEquipmentUniqueIds.Contains(equipmentUniqueId) || IsEquipmentOnAha(equipmentUniqueId))
            return GridFightEquipDressResult.Failed;
        var previousWearer = FindEquipmentWearer(equipmentUniqueId);
        if (!CanDress(role, equipment)) return GridFightEquipDressResult.Failed;

        // GridFightEquipComponent.HackEquip shows only the first implant, but dress constraints allow more.
        // Replace it so hidden implants cannot affect battle; UpdateEquips returns it to the bag.
        List<GridFightEquipmentState> replacedImplants = IsImplant(equipment.EquipmentId)
            ? GetEquippedEquipment(role).Where(candidate => IsImplant(candidate.EquipmentId)).ToList()
            : [];
        if (replacedImplants.Any(candidate => candidate.IsLocked)) return GridFightEquipDressResult.Failed;

        var previousMaxActiveRoles = MaxActiveRoles;
        var updatedRoles = new List<GridFightRoleState>();
        if (previousWearer != null)
        {
            previousWearer.EquippedEquipmentUniqueIds.Remove(equipmentUniqueId);
            if (IsDiceEquipment(equipment.EquipmentId)) DestroyDiceFilledOnRole(previousWearer);
            updatedRoles.Add(previousWearer);
        }
        foreach (var replaced in replacedImplants)
            role.EquippedEquipmentUniqueIds.Remove(replaced.UniqueId);
        role.EquippedEquipmentUniqueIds.Add(equipmentUniqueId);
        if (!updatedRoles.Contains(role)) updatedRoles.Add(role);

        var removed = new List<GridFightEquipmentState>();
        var added = new List<GridFightEquipmentState>();
        var crafted = TryAutoCraftOnRole(role, equipmentUniqueId, removed, added);
        ApplyDiceEquipmentToRole(role);
        foreach (var updatedRole in updatedRoles.Distinct())
            RefreshEquipmentTrackCompletionForRole(updatedRole);
        RecalculateRoleSwitches();
        return new GridFightEquipDressResult(
            true,
            removed,
            added,
            updatedRoles,
            crafted,
            previousMaxActiveRoles != MaxActiveRoles);
    }

    public GridFightEquipCraftResult CraftEquipment(
        uint targetEquipmentId,
        IReadOnlyList<uint> materialUniqueIds,
        GridFightEquipCraftSource source,
        uint trackId)
    {
        if (!CanModifyEquipment() || materialUniqueIds.Count != 2 || materialUniqueIds.Distinct().Count() != 2)
            return GridFightEquipCraftResult.Failed;
        var materials = materialUniqueIds
            .Select(uniqueId => Equipments.FirstOrDefault(equipment => equipment.UniqueId == uniqueId))
            .ToList();
        if (materials.Any(equipment => equipment == null) ||
            materials.Any(equipment => equipment!.IsRestricted || FindEquipmentWearer(equipment.UniqueId) != null))
            return GridFightEquipCraftResult.Failed;
        var craftedEquipmentId = ResolveCraftedEquipmentId(
            materials[0]!.EquipmentId, materials[1]!.EquipmentId, targetEquipmentId);
        if (craftedEquipmentId == 0 || !GameData.GridFightEquipmentData.ContainsKey(craftedEquipmentId))
            return GridFightEquipCraftResult.Failed;

        var previousMaxActiveRoles = MaxActiveRoles;
        var removed = materials.Cast<GridFightEquipmentState>().ToList();
        foreach (var material in removed) Equipments.Remove(material);
        var added = CreateEquipment(craftedEquipmentId, CraftedEquipmentSource);
        TryAutoEquipCraftedEquipment(added, source, trackId);
        ApplyPetPalCraftRefund(removed);
        RecalculateRoleSwitches();
        return new GridFightEquipCraftResult(
            true,
            removed,
            added,
            previousMaxActiveRoles != MaxActiveRoles);
    }

    public GridFightConsumableUseResult UseConsumable(
        uint groupId,
        uint roleUniqueId,
        uint equipmentUniqueId,
        uint chimeraUniqueId = 0)
    {
        // The group id is the stack's unique id; since 4.7 the request no longer repeats the item id.
        var consumable = Consumables.FirstOrDefault(item => item.GroupId == groupId);
        if (!CanModifyEquipment() || consumable == null || consumable.Count == 0 ||
            !GameData.GridFightConsumablesData.TryGetValue(consumable.ItemId, out var config))
            return GridFightConsumableUseResult.Failed;
        var itemId = consumable.ItemId;

        var previousMaxActiveRoles = MaxActiveRoles;
        var removed = new List<GridFightEquipmentState>();
        var added = new List<GridFightEquipmentState>();
        var updatedRoles = new List<GridFightRoleState>();
        GridFightRoleAcquisitionResult? acquisition = null;
        var recommendationStarted = false;
        var success = config.ConsumableRule switch
        {
            GridFightConsumeTypeEnum.Remove => RemoveEquipmentFromRole(roleUniqueId, updatedRoles),
            GridFightConsumeTypeEnum.Roll => RollEquipment(roleUniqueId, equipmentUniqueId, removed, added, updatedRoles),
            GridFightConsumeTypeEnum.Upgrade => UpgradeEquipment(equipmentUniqueId, removed, added, updatedRoles),
            // The chimera star-up consumable (奇巧零食, 350119): target oneof 1338 { chimera unique id }.
            GridFightConsumeTypeEnum.UpgradeChimera => UpgradeChimeraByConsumable(chimeraUniqueId),
            GridFightConsumeTypeEnum.Copy => CopyRole(roleUniqueId, config.ConsumableParamList, out acquisition),
            GridFightConsumeTypeEnum.GainRecommendEquip => StartEquipmentRecommendation(
                roleUniqueId,
                config.ConsumableParamList,
                out recommendationStarted),
            _ => false,
        };
        if (!success) return GridFightConsumableUseResult.Failed;
        foreach (var role in updatedRoles) RefreshEquipmentTrackCompletionForRole(role);

        RecalculateRoleSwitches();
        var inventoryChange = ConsumeConsumable(consumable, config.IfConsume);
        return new GridFightConsumableUseResult(
            true,
            itemId,
            config.ConsumableRule,
            inventoryChange,
            removed,
            added,
            updatedRoles,
            acquisition,
            recommendationStarted,
            previousMaxActiveRoles != MaxActiveRoles)
        {
            TargetRoleUniqueId = roleUniqueId,
        };
    }

    public GridFightRecommendEquipmentSelectionResult SelectRecommendedEquipment(
        uint queuePosition,
        uint equipmentId)
    {
        var recommendation = EquipmentRecommendation;
        if (recommendation == null || recommendation.QueuePosition != queuePosition ||
            !recommendation.EquipmentIds.Contains(equipmentId) ||
            !GameData.GridFightEquipmentData.ContainsKey(equipmentId))
            return GridFightRecommendEquipmentSelectionResult.Failed;
        var equipment = CreateEquipment(equipmentId, RecommendedEquipmentSource);
        TryAutoEquipTrackedEquipment(equipment);
        EquipmentRecommendation = null;
        PendingQueuePosition++;
        return new GridFightRecommendEquipmentSelectionResult(true, queuePosition, equipment);
    }

    public IReadOnlyList<uint> ResolveRoleTraitIds(GridFightRoleState role)
    {
        var result = GameData.GridFightRoleBasicInfoData.GetValueOrDefault(role.RoleId)?.TraitList.ToHashSet() ?? [];
        foreach (var equipment in GetEquippedEquipment(role))
        {
            var config = GameData.GridFightEquipmentData.GetValueOrDefault(equipment.EquipmentId);
            if (config?.EquipFunc is GridFightEquipFuncTypeEnum.OriginEmblem or
                GridFightEquipFuncTypeEnum.ClassEmblem)
            {
                var traitId = config.EquipFuncParamList.FirstOrDefault();
                if (traitId != 0) result.Add(traitId);
            }
        }
        AddSeasonTwoGrantedTraitIds(role, result);
        return result.Order().ToList();
    }

    public uint ResolveTraitExtraMemberCount(uint traitId, IEnumerable<GridFightRoleState> roles)
    {
        return (uint)roles.Sum(role => GetEquippedEquipment(role).Count(equipment =>
        {
            var config = GameData.GridFightEquipmentData.GetValueOrDefault(equipment.EquipmentId);
            return config?.EquipFunc == GridFightEquipFuncTypeEnum.AddTraitLayer &&
                   config.EquipFuncParamList.FirstOrDefault() == traitId;
        }));
    }

    public IReadOnlyList<GridFightEquipmentState> GetEquippedEquipment(GridFightRoleState role)
    {
        return role.EquippedEquipmentUniqueIds
            .Select(uniqueId => Equipments.FirstOrDefault(equipment => equipment.UniqueId == uniqueId))
            .Where(equipment => equipment != null)
            .Cast<GridFightEquipmentState>()
            .ToList();
    }

    private bool CanModifyEquipment()
    {
        return Phase == GridFightSessionPhase.Preparing && !HasBlockingPendingSelection;
    }

    private GridFightRoleState? FindEquipmentWearer(uint equipmentUniqueId)
    {
        return Roles.FirstOrDefault(role => role.EquippedEquipmentUniqueIds.Contains(equipmentUniqueId));
    }

    private static bool IsImplant(uint equipmentId) =>
        GameData.GridFightEquipmentData.TryGetValue(equipmentId, out var config) &&
        config.EquipType == GridFightEquipmentTypeEnum.Implants;

    private bool CanDress(GridFightRoleState role, GridFightEquipmentState equipment)
    {
        if (!GameData.GridFightEquipmentData.TryGetValue(equipment.EquipmentId, out var config)) return false;
        // Client constraint BCBCBPPHCBC: implants cannot be dressed on a role standing on the bench.
        if (config.EquipType == GridFightEquipmentTypeEnum.Implants &&
            role.Position >= 14 && role.Position <= 13 + _catalog.BenchSize)
            return false;
        var equipped = GetEquippedEquipment(role)
            .Where(candidate => candidate.UniqueId != equipment.UniqueId)
            .ToList();
        var normalEquipped = equipped
            .Where(candidate => GameData.GridFightEquipmentData[candidate.EquipmentId].EquipType !=
                                GridFightEquipmentTypeEnum.Implants)
            .ToList();
        if (config.EquipType != GridFightEquipmentTypeEnum.Implants)
        {
            var willAutoCraft = normalEquipped.Any(candidate =>
                ResolveCraftedEquipmentId(equipment.EquipmentId, candidate.EquipmentId) != 0);
            if (normalEquipped.Count >= NormalEquipmentSlotCount && !willAutoCraft) return false;
            if (normalEquipped.Any(candidate =>
                    GameData.GridFightEquipmentData[candidate.EquipmentId].DressRule ==
                    GridFightEquipDressTypeEnum.DressRuleAllSlotEmpty))
                return false;
        }

        return config.DressRule switch
        {
            GridFightEquipDressTypeEnum.DressRuleNotUnique => true,
            GridFightEquipDressTypeEnum.DressRuleUnique =>
                equipped.All(candidate => candidate.EquipmentId != equipment.EquipmentId),
            GridFightEquipDressTypeEnum.DressRuleAllSlotEmpty => normalEquipped.Count == 0,
            GridFightEquipDressTypeEnum.DressRuleLeader => role.Position == 1,
            GridFightEquipDressTypeEnum.DressRuleTraitOnly =>
                config.DressRuleParamList.Any(ResolveRoleTraitIds(role).Contains),
            GridFightEquipDressTypeEnum.DressRuleUniqueAndExclusiveTrait =>
                equipped.All(candidate => candidate.EquipmentId != equipment.EquipmentId) &&
                config.DressRuleParamList.All(traitId => !ResolveRoleTraitIds(role).Contains(traitId)),
            GridFightEquipDressTypeEnum.DressRuleRoleOnly => config.DressRuleParamList.Contains(role.RoleId),
            _ => false,
        };
    }

    private bool TryAutoCraftOnRole(
        GridFightRoleState role,
        uint newlyEquippedUniqueId,
        ICollection<GridFightEquipmentState> removed,
        ICollection<GridFightEquipmentState> added)
    {
        var newlyEquipped = Equipments.First(equipment => equipment.UniqueId == newlyEquippedUniqueId);
        if (newlyEquipped.IsRestricted) return false;
        var craftCandidates = GetEquippedEquipment(role)
            .Where(equipment => equipment.UniqueId != newlyEquippedUniqueId)
            .Where(equipment => !equipment.IsRestricted)
            .Where(equipment => GameData.GridFightEquipmentData[equipment.EquipmentId].EquipType !=
                                GridFightEquipmentTypeEnum.Implants)
            .ToList();
        foreach (var other in craftCandidates)
        {
            var craftedEquipmentId = ResolveCraftedEquipmentId(newlyEquipped.EquipmentId, other.EquipmentId);
            if (craftedEquipmentId == 0) continue;

            var insertIndex = Math.Min(
                role.EquippedEquipmentUniqueIds.IndexOf(newlyEquipped.UniqueId),
                role.EquippedEquipmentUniqueIds.IndexOf(other.UniqueId));
            role.EquippedEquipmentUniqueIds.Remove(newlyEquipped.UniqueId);
            role.EquippedEquipmentUniqueIds.Remove(other.UniqueId);
            Equipments.Remove(newlyEquipped);
            Equipments.Remove(other);
            removed.Add(newlyEquipped);
            removed.Add(other);
            var crafted = CreateEquipment(craftedEquipmentId, CraftedEquipmentSource);
            role.EquippedEquipmentUniqueIds.Insert(Math.Max(0, insertIndex), crafted.UniqueId);
            added.Add(crafted);
            return true;
        }
        return false;
    }

    private static uint ResolveCraftedEquipmentId(uint firstEquipmentId, uint secondEquipmentId, uint targetEquipmentId = 0)
    {
        // Both manual crafting and on-role crafting accept every red/blue diamond pair.
        if (firstEquipmentId is RawDiamondEquipmentId or BlueDiamondEquipmentId &&
            secondEquipmentId is RawDiamondEquipmentId or BlueDiamondEquipmentId &&
            targetEquipmentId is 0 or WealthDiamondEquipmentId &&
            GameData.GridFightEquipmentData.ContainsKey(WealthDiamondEquipmentId))
            return WealthDiamondEquipmentId;
        var materialIds = new[] { firstEquipmentId, secondEquipmentId }.Order().ToList();
        var recipe = GridFightSeasonScope.Crafts(GridFightSeasonScope.CurrentSeason)
            .Where(candidate => targetEquipmentId == 0 || candidate.CraftEquipID == targetEquipmentId)
            .OrderBy(candidate => candidate.CraftID)
            .FirstOrDefault(candidate => candidate.CostEquipList.Order().SequenceEqual(materialIds));
        return recipe != null && GameData.GridFightEquipmentData.ContainsKey(recipe.CraftEquipID)
            ? recipe.CraftEquipID
            : 0;
    }

    private bool RemoveEquipmentFromRole(uint roleUniqueId, ICollection<GridFightRoleState> updatedRoles)
    {
        var role = Roles.FirstOrDefault(candidate => candidate.UniqueId == roleUniqueId);
        if (role == null || role.EquippedEquipmentUniqueIds.Count == 0) return false;
        DestroyDiceFilledOnRole(role);
        role.EquippedEquipmentUniqueIds.Clear();
        updatedRoles.Add(role);
        return true;
    }

    private bool RollEquipment(
        uint roleUniqueId,
        uint equipmentUniqueId,
        ICollection<GridFightEquipmentState> removed,
        ICollection<GridFightEquipmentState> added,
        ICollection<GridFightRoleState> updatedRoles)
    {
        if (equipmentUniqueId != 0)
        {
            var equipment = Equipments.FirstOrDefault(candidate => candidate.UniqueId == equipmentUniqueId);
            if (equipment == null || equipment.IsRestricted || FindEquipmentWearer(equipmentUniqueId) != null) return false;
            return RollOneEquipment(equipment, removed, added);
        }

        var role = Roles.FirstOrDefault(candidate => candidate.UniqueId == roleUniqueId);
        if (role == null) return false;
        var replacements = GetEquippedEquipment(role)
            .Where(equipment => !equipment.IsRestricted)
            .Select(equipment =>
            {
                var config = GameData.GridFightEquipmentData[equipment.EquipmentId];
                var replacementId = config.EquipType == GridFightEquipmentTypeEnum.Implants
                    ? 0
                    : _catalog.RollEquipment(config.EquipCategory, equipment.EquipmentId);
                return (Equipment: equipment, ReplacementId: replacementId);
            })
            .Where(replacement => replacement.ReplacementId != 0)
            .ToList();
        if (replacements.Count == 0) return false;
        if (replacements.Any(replacement => IsDiceEquipment(replacement.Equipment.EquipmentId)))
            DestroyDiceFilledOnRole(role);
        foreach (var replacement in replacements)
        {
            role.EquippedEquipmentUniqueIds.Remove(replacement.Equipment.UniqueId);
            Equipments.Remove(replacement.Equipment);
            removed.Add(replacement.Equipment);
            added.Add(CreateEquipment(replacement.ReplacementId, RolledEquipmentSource));
        }
        updatedRoles.Add(role);
        return true;
    }

    private bool RollOneEquipment(
        GridFightEquipmentState equipment,
        ICollection<GridFightEquipmentState> removed,
        ICollection<GridFightEquipmentState> added)
    {
        if (equipment.IsRestricted) return false;
        var config = GameData.GridFightEquipmentData[equipment.EquipmentId];
        if (config.EquipType == GridFightEquipmentTypeEnum.Implants) return false;
        var replacementId = _catalog.RollEquipment(config.EquipCategory, equipment.EquipmentId);
        if (replacementId == 0) return false;
        Equipments.Remove(equipment);
        removed.Add(equipment);
        added.Add(CreateEquipment(replacementId, RolledEquipmentSource));
        return true;
    }

    private bool UpgradeEquipment(
        uint equipmentUniqueId,
        ICollection<GridFightEquipmentState> removed,
        ICollection<GridFightEquipmentState> added,
        ICollection<GridFightRoleState> updatedRoles)
    {
        var equipment = Equipments.FirstOrDefault(candidate => candidate.UniqueId == equipmentUniqueId);
        if (equipment == null || equipment.IsRestricted ||
            !GameData.GridFightEquipUpgradeData.TryGetValue(equipment.EquipmentId, out var upgrade) ||
            !GameData.GridFightEquipmentData.ContainsKey(upgrade.UpgradeID))
            return false;
        var wearer = FindEquipmentWearer(equipmentUniqueId);
        if (wearer != null)
        {
            if (IsDiceEquipment(equipment.EquipmentId)) DestroyDiceFilledOnRole(wearer);
            wearer.EquippedEquipmentUniqueIds.Remove(equipmentUniqueId);
            updatedRoles.Add(wearer);
        }
        Equipments.Remove(equipment);
        removed.Add(equipment);
        added.Add(CreateEquipment(upgrade.UpgradeID, equipment.Source));
        return true;
    }

    private bool CopyRole(
        uint roleUniqueId,
        IReadOnlyList<uint> parameters,
        out GridFightRoleAcquisitionResult? acquisition)
    {
        acquisition = null;
        var sourceRole = Roles.FirstOrDefault(role => role.UniqueId == roleUniqueId);
        if (sourceRole == null || parameters.Count < 2 ||
            !GameData.GridFightRoleBasicInfoData.TryGetValue(sourceRole.RoleId, out var roleConfig) ||
            roleConfig.Rarity > parameters[0] || parameters[1] != 1 || GetFreeBenchPosition() == 0)
            return false;
        // ConsumableParamList[2] is a required trait: the Trailblaze Employee/Perfect Projectors
        // (开拓·员工/完美投影仪, 350113/350114) are [3|5, 1, 100502] and only copy members of the Trailblaze trait
        // (开拓银轨). The client does not check it, so the server must.
        var requiredTraitId = parameters.Count > 2 ? parameters[2] : 0u;
        if (requiredTraitId != 0 && !ResolveRoleTraitIds(sourceRole).Contains(requiredTraitId))
            return false;
        var acquired = new List<GridFightRoleState>();
        var mergeSteps = new List<GridFightRoleMergeStep>();
        for (var index = 0u; index < parameters[1]; index++)
        {
            var role = new GridFightRoleState
            {
                UniqueId = _nextEntityUniqueId++,
                RoleId = sourceRole.RoleId,
                Star = 1,
                Position = GetFreeBenchPosition(),
            };
            if (role.Position == 0) return false;
            Roles.Add(role);
            acquired.Add(role);
            mergeSteps.AddRange(MergeRoles(role.RoleId));
        }
        acquisition = FinalizeRoleAcquisition(new GridFightRoleAcquisitionResult(
            true,
            acquired,
            mergeSteps,
            GetFinalMergedRoles(mergeSteps)));
        return true;
    }

    private bool StartEquipmentRecommendation(
        uint roleUniqueId,
        IReadOnlyList<uint> parameters,
        out bool started)
    {
        started = false;
        var role = Roles.FirstOrDefault(candidate => candidate.UniqueId == roleUniqueId);
        if (role == null || parameters.Count == 0 || parameters[0] == 0 ||
            !GameData.GridFightRoleBasicInfoData.TryGetValue(role.RoleId, out var roleConfig))
            return false;
        var equipmentIds = _catalog.RollRecommendedEquipment(
            role.RoleId,
            roleConfig.FrontBackType,
            checked((int)parameters[0]));
        if (equipmentIds.Count != parameters[0] ||
            equipmentIds.Any(id => !GameData.GridFightEquipmentData.ContainsKey(id)))
            return false;
        EquipmentRecommendation = new GridFightEquipmentRecommendationState(
            PendingQueuePosition,
            roleUniqueId,
            equipmentIds);
        started = true;
        return true;
    }

    private GridFightConsumableInventoryChange? ConsumeConsumable(
        GridFightConsumableState consumable,
        bool shouldConsume)
    {
        if (!shouldConsume) return null;
        consumable.Count--;
        var removed = consumable.Count == 0;
        if (removed) Consumables.Remove(consumable);
        return new GridFightConsumableInventoryChange(
            consumable.GroupId,
            consumable.ItemId,
            consumable.Count,
            removed);
    }

    private GridFightEquipmentState CreateEquipment(uint equipmentId, uint source)
    {
        var equipment = new GridFightEquipmentState
        {
            UniqueId = _nextEntityUniqueId++,
            EquipmentId = equipmentId,
            Source = source,
        };
        Equipments.Add(equipment);
        return equipment;
    }
}
