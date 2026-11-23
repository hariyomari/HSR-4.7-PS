using March7thHoney.Data;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed partial class GridFightManager
{
    public GridFightGameTraitInfo BuildGameTraitInfo()
    {
        var result = new GridFightGameTraitInfo();
        var session = Session;
        if (session == null) return result;
        var activeRoles = session.Roles
            .Where(role => role.Position is >= 1 and <= 13)
            .OrderBy(role => role.Position)
            .ToList();
        var roleTraitMembers = activeRoles
            .SelectMany(role => session.ResolveRoleTraitIds(role)
                .Select(traitId => (TraitId: traitId, Role: role)))
            .ToList();
        var pepeTraitMembers = session.ResolvePepeTraitMembers();
        foreach (var traitId in roleTraitMembers.Select(entry => entry.TraitId)
                     .Concat(pepeTraitMembers.Select(entry => entry.TraitId))
                     .Distinct()
                     .Order())
        {
            var members = roleTraitMembers
                .Where(entry => entry.TraitId == traitId)
                .Select(entry => entry.Role)
                .Distinct()
                .ToList();
            var hasPepeMember = session.PepeNpc != null && pepeTraitMembers.Any(entry => entry.TraitId == traitId);
            var memberCount = (uint)members.Count +
                              (hasPepeMember ? 1u : 0u) +
                              session.ResolveTraitExtraMemberCount(traitId, activeRoles);
            var trait = new GridGameTraitInfo
            {
                TraitId = traitId,
                Layer = Battle.GridFightBattleProtoBuilder.ResolveTraitLayer(traitId, memberCount),
            };
            trait.MemberRoleUniqueIdList.Add(members.Select(role => role.UniqueId));
            if (hasPepeMember) trait.MemberRoleUniqueIdList.Add(session.PepeNpc!.UniqueId);
            if (trait.Layer > 0 &&
                !GridFightTraitCompatibility.UsesLayerOnlyWireShape(traitId) &&
                GameData.GridFightTraitBasicInfoData.TryGetValue(traitId, out var config))
                trait.TraitEffectList.Add(config.TraitEffectList.Select(effectId =>
                    session.IsSeasonTwoRules
                        ? BuildGameTraitEffectInfo(traitId, effectId)
                        : new GridFightTraitEffectInfo
                        {
                            EffectId = effectId,
                            ECEMNCJBFOP = traitId == GridFightSession.GalaxyScholarTraitId &&
                                           effectId == GridFightSession.GalaxyScholarTraitEffectId
                                ? session.GalaxyScholarDna
                                : 0,
                        }));
            // Moon Game (幻月游戏) sub-trait effects are addressed under trait 100302 by their own effect ids.
            if (trait.Layer > 0 && traitId == GridFightSession.MoonGameTraitId &&
                session.MoonGameSubTraitId == GridFightSession.MoonGameWishSubTraitId)
                trait.TraitEffectList.Add(BuildGameTraitEffectInfo(traitId, GridFightSession.MoonGameWishEffectId));
            if (trait.Layer > 0 && traitId == GridFightSession.FateDivinerTraitId &&
                trait.TraitEffectList.All(effect => effect.EffectId != GridFightSession.FateDivinerEffectId))
                trait.TraitEffectList.Add(new GridFightTraitEffectInfo { EffectId = GridFightSession.FateDivinerEffectId });
            result.GridFightTraitInfo.Add(trait);
        }
        foreach (var entry in session.RoleSwitchEntries)
        {
            var switchEntry = new GridFightRoleSwitchEntry
            {
                CurrentRoleId = entry.CurrentRoleId,
            };
            switchEntry.RoleIdList.Add(entry.RoleIdList);
            result.RoleSwitchList.Add(switchEntry);
        }
        return result;
    }

    public GridFightSyncUpdateResultScNotify BuildEquipDressSync(GridFightEquipDressResult dress)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !dress.Success) return notify;
        if (dress.AutoCrafted)
        {
            var craftResult = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcCraftEquip,
            };
            AppendEquipmentChanges(craftResult, dress.RemovedEquipments, dress.AddedEquipments);
            var craftedCrown = dress.AddedEquipments.FirstOrDefault(equipment =>
                equipment.EquipmentId == GridFightSession.WealthDiamondEquipmentId);
            if (dress.MaxActiveRolesChanged && craftedCrown == null)
                craftResult.UpdateDynamicList.Add(new GridFightSyncData
                {
                    MaxActiveRoleCount = Session.MaxActiveRoles,
                });
            notify.ResultList.Add(craftResult);
            if (craftedCrown != null) AppendWealthDiamondCapacityResult(notify, craftedCrown);
        }

        var dressResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcDressEquip,
        };
        foreach (var role in dress.UpdatedRoles.Distinct())
            dressResult.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        if (Session.EquipmentTracks.Count > 0)
            AppendEquipmentTrackState(dressResult);
        if (!dress.AutoCrafted && dress.MaxActiveRolesChanged)
            dressResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                MaxActiveRoleCount = Session.MaxActiveRoles,
            });
        notify.ResultList.Add(dressResult);
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcDressEquip);
        AppendOotdRefreshResult(notify);
        notify.ResultList.Add(new GridFightSyncResultData());
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildEquipCraftSync(GridFightEquipCraftResult craft)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !craft.Success || craft.AddedEquipment == null) return notify;
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcCraftEquip,
        };
        AppendEquipmentChanges(result, craft.RemovedEquipments, [craft.AddedEquipment]);
        var isWealthDiamond = craft.AddedEquipment.EquipmentId == GridFightSession.WealthDiamondEquipmentId;
        if (craft.MaxActiveRolesChanged && !isWealthDiamond)
            result.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = Session.MaxActiveRoles });
        notify.ResultList.Add(result);
        // Official craft auto-dress is a second CraftEquip result. Keeping it
        // separate is important for the recommended-equipment UI to consume the
        // freshly-created item first and then apply the dress update.
        AppendPendingTrackedAutoEquipResult(notify, GridFightUpdateSrcType.KgridFightSrcCraftEquip);
        if (isWealthDiamond) AppendWealthDiamondCapacityResult(notify, craft.AddedEquipment);
        AppendDiceEquipmentRefreshSync(notify);
        AppendOotdRefreshResult(notify);
        notify.ResultList.Add(new GridFightSyncResultData());
        return notify;
    }

    private void AppendWealthDiamondCapacityResult(
        GridFightSyncUpdateResultScNotify notify,
        GridFightEquipmentState crown)
    {
        if (Session == null) return;
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcAddCrown,
        };
        result.EffectParamList.Add(crown.UniqueId);
        result.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = Session.MaxActiveRoles });
        result.UpdateDynamicList.Add(new GridFightSyncData { JLOCMDIDPCI = Session.OffFieldMaxCount });
        notify.ResultList.Add(result);
    }

    public GridFightSyncUpdateResultScNotify BuildConsumableUseSync(GridFightConsumableUseResult use)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !use.Success) return notify;
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcUseConsumable,
        };
        result.EffectParamList.Add(use.ItemId);
        AppendConsumableChange(result, use.InventoryChange);
        AppendEquipmentChanges(result, use.RemovedEquipments, use.AddedEquipments);
        AppendPendingTrackedAutoEquipRoles(result);
        foreach (var role in use.UpdatedRoles.Distinct())
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        if (use.MaxActiveRolesChanged)
        {
            result.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = Session.MaxActiveRoles });
            if (use.RemovedEquipments.Concat(use.AddedEquipments).Any(equipment =>
                    equipment.EquipmentId == GridFightSession.WealthDiamondEquipmentId))
                result.UpdateDynamicList.Add(new GridFightSyncData { JLOCMDIDPCI = Session.OffFieldMaxCount });
        }
        // Copy-role consumables emit the copied role in a dedicated CopyRole result below.
        // Putting it in UseConsumable suppresses the official copy animation on the client.
        if (use.RecommendationStarted && BuildPendingAction() is { } pending)
        {
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                LockInfo = new GridFightLockInfo
                {
                    LockType = GridFightLockType.Gameplay,
                    LockReason = GridFightLockReason.PendingAction,
                },
            });
            result.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        }
        notify.ResultList.Add(result);

        if (use.RoleAcquisition is { Success: true } roleAcquisition)
        {
            var copyResult = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcCopyRole,
            };
            copyResult.EffectParamList.Add(new[] { use.TargetRoleUniqueId, use.ItemId });
            AppendAcquiredRoles(copyResult, roleAcquisition);
            notify.ResultList.Add(copyResult);
            AppendRoleMergeSteps(notify, roleAcquisition);
        }
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcUseConsumable);
        if (use.RoleAcquisition is { Success: true } finalizedAcquisition)
            AppendUnlockEffects(notify, finalizedAcquisition);
        notify.ResultList.Add(new GridFightSyncResultData());
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildRecommendationSelectionSync(
        GridFightRecommendEquipmentSelectionResult selection)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null || !selection.Success || selection.Equipment == null) return notify;
        var addResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcUseConsumable,
        };
        addResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            AddGameItemInfo = BuildEquipmentItems([selection.Equipment]),
        });
        AppendPendingTrackedAutoEquipRoles(addResult);
        notify.ResultList.Add(addResult);

        var finishResult = new GridFightSyncResultData();
        finishResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            FinishPendingActionPos = selection.FinishedQueuePosition,
        });
        finishResult.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finishResult);
        notify.ResultList.Add(new GridFightSyncResultData());
        AppendOotdRefreshResult(notify);
        AppendDiceEquipmentRefreshSync(notify);
        AppendNextTraitEnhancement(notify);
        return notify;
    }

    private static GridFightGameItemSyncInfo BuildEquipmentItems(
        IEnumerable<GridFightEquipmentState> equipments)
    {
        var items = new GridFightGameItemSyncInfo();
        items.GridFightEquipmentList.Add(equipments.Select(BuildEquipment));
        return items;
    }

    private static void AppendEquipmentChanges(
        GridFightSyncResultData result,
        IReadOnlyList<GridFightEquipmentState> removed,
        IReadOnlyList<GridFightEquipmentState> added)
    {
        foreach (var equipment in removed)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                RemoveGameItemInfo = BuildEquipmentItems([equipment]),
            });
        if (added.Count > 0)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                AddGameItemInfo = BuildEquipmentItems(added),
            });
    }

    private static void AppendConsumableChange(
        GridFightSyncResultData result,
        GridFightConsumableInventoryChange? change)
    {
        if (change == null) return;
        var items = new GridFightGameItemSyncInfo();
        items.UpdateGridFightConsumableList.Add(new GridFightConsumableUpdateInfo
        {
            GroupId = change.GroupId,
            ItemId = change.ItemId,
            ItemStackCount = -1,
            Num = change.RemainingCount,
            MCKHGIEJLHM = new GridFightConsumableInfo
            {
                GroupId = change.GroupId,
                ItemId = change.ItemId,
                Num = change.RemainingCount,
            },
        });
        result.UpdateDynamicList.Add(change.Removed
            ? new GridFightSyncData { RemoveGameItemInfo = items }
            : new GridFightSyncData { UpdateGameItemInfo = items });
    }
}
