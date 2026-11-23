using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed partial class GridFightManager
{
    private void AppendInvestmentItemReward(
        GridFightSyncUpdateResultScNotify notify,
        GridFightInvestmentItemReward reward)
    {
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcAugment,
        };
        result.EffectParamList.Add(new[] { reward.AugmentId, reward.EffectId });
        if (reward.Equipment != null)
        {
            var items = new GridFightGameItemSyncInfo();
            items.GridFightEquipmentList.Add(BuildEquipment(reward.Equipment));
            result.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        }
        if (reward.ForgeItem != null)
            result.UpdateDynamicList.Add(new GridFightSyncData { AddForgeItemInfo = BuildForgeItem(reward.ForgeItem) });
        if (reward.GoldValue.HasValue)
            result.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = reward.GoldValue.Value });
        if (reward.Consumable != null)
        {
            var items = new GridFightGameItemSyncInfo();
            items.UpdateGridFightConsumableList.Add(BuildConsumableUpdate(reward.Consumable));
            result.UpdateDynamicList.Add(reward.Consumable.IsNew
                ? new GridFightSyncData { AddGameItemInfo = items }
                : new GridFightSyncData { UpdateGameItemInfo = items });
        }
        AppendPendingTrackedAutoEquipRoles(result);
        notify.ResultList.Add(result);
    }

    private void AppendRoundBeginInvestmentRewards(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null) return;

        foreach (var removal in session.TakeLizhiNpcRemovals())
        {
            var unbind = NewAugmentResult(GridFightSession.LizhiProcedureAugmentId, 110);
            unbind.UpdateDynamicList.Add(new GridFightSyncData { DHLEOLKNAMI = BuildPepeNpc(removal.Npc, 0) });
            notify.ResultList.Add(unbind);

            var remove = NewAugmentResult(GridFightSession.LizhiProcedureAugmentId, 110);
            remove.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
            remove.UpdateDynamicList.Add(new GridFightSyncData { NJCEFEJBEOP = removal.Npc.UniqueId });
            var items = new GridFightGameItemSyncInfo();
            items.GridFightEquipmentList.Add(BuildEquipment(removal.Equipment));
            remove.UpdateDynamicList.Add(new GridFightSyncData { UpdateGameItemInfo = items });
            var reset = new GridFightSavedValueUpdate();
            reset.SavedValues["Augment204001_SavedValue01"] = removal.SavedValueAfterReset;
            remove.UpdateDynamicList.Add(new GridFightSyncData { SavedValueUpdate = reset });
            notify.ResultList.Add(remove);
        }

        foreach (var reward in session.TakeRoundBeginInvestmentRewards())
            AppendInvestmentItemReward(notify, reward);
        AppendAugmentEffectResults(notify, session.TakeAugmentNodeEffects());
        AppendEconomyEffects(notify);
        foreach (var uniqueId in session.TakePowerGrowthRoleUpdates())
        {
            var role = session.Roles.FirstOrDefault(entry => entry.UniqueId == uniqueId);
            if (role == null) continue;
            var growth = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcSelectPortalBuff };
            growth.EffectParamList.Add(GridFightSession.PowerGrowthPortalId);
            growth.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
            notify.ResultList.Add(growth);
        }

        foreach (var saved in session.TakeRoundBeginSavedValueUpdates())
        {
            var result = NewAugmentResult(saved.AugmentId, saved.EffectId);
            var update = new GridFightSavedValueUpdate();
            update.SavedValues[saved.Key] = saved.Value;
            result.UpdateDynamicList.Add(new GridFightSyncData { SavedValueUpdate = update });
            // Prismatic Upstream (打通上下游·彩) immediately changes every role's front/back strength display.
            if (saved.AugmentId == GridFightSession.UpstreamPrismaticAugmentId)
                foreach (var role in session.Roles)
                    result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
            notify.ResultList.Add(result);
        }
    }

    private void AppendSpecialInvestmentSelectionResults(
        GridFightSyncUpdateResultScNotify notify,
        uint augmentId)
    {
        var session = Session;
        if (session == null) return;

        AppendAstaDogSelectionResults(notify, augmentId);

        if (augmentId == GridFightSession.UpstreamPrismaticAugmentId)
        {
            // Official selection packet splits front/back initialization into effect 103 and 104.
            foreach (var (effectId, propertyId) in new (uint EffectId, uint PropertyId)[] { (103, 1027), (104, 1030) })
            {
                var result = NewAugmentResult(augmentId, effectId);
                foreach (var role in session.Roles)
                {
                    var info = BuildRole(role);
                    info.ConvertPropertyToFixpoint[propertyId] = 150;
                    result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = info });
                }
                notify.ResultList.Add(result);
            }
        }

        if (augmentId != GridFightSession.LizhiProcedureAugmentId) return;
        var first = true;
        var addedNpcCount = 0u;
        foreach (var npc in session.LizhiNpcs)
        {
            var equipment = npc.EquippedEquipmentUniqueIds
                .Select(uid => session.Equipments.FirstOrDefault(item => item.UniqueId == uid))
                .FirstOrDefault(item => item != null);
            if (equipment == null) continue;

            var capacity = NewAugmentResult(augmentId, 109);
            capacity.UpdateDynamicList.Add(new GridFightSyncData
            {
                MaxActiveRoleCount = Math.Min(GridFightSession.TeamPopulationLimit,
                    session.MaxActiveRoles - (uint)session.LizhiNpcs.Count + addedNpcCount),
            });
            if (first)
            {
                capacity.UpdateDynamicList.Add(new GridFightSyncData { JLOCMDIDPCI = session.OffFieldMaxCount });
                first = false;
            }
            var items = new GridFightGameItemSyncInfo();
            items.GridFightEquipmentList.Add(BuildEquipment(equipment));
            capacity.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
            notify.ResultList.Add(capacity);

            var bind = NewAugmentResult(augmentId, 109);
            bind.UpdateDynamicList.Add(new GridFightSyncData { EJMDHDJCGDI = BuildPepeNpc(npc, 0) });
            bind.UpdateDynamicList.Add(new GridFightSyncData { DHLEOLKNAMI = BuildPepeNpc(npc) });
            notify.ResultList.Add(bind);
            addedNpcCount++;
        }
        var final = NewAugmentResult(augmentId, 109);
        final.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
        notify.ResultList.Add(final);
    }

    /// <summary>Syncs data-driven investment effects (GridFightAugmentEffects) with the same payloads as orb rewards.</summary>
    private void AppendAugmentEffectResults(
        GridFightSyncUpdateResultScNotify notify,
        IReadOnlyList<GridFightAugmentEffectResult> effects)
    {
        if (Session == null) return;
        foreach (var effect in effects)
        {
            var application = effect.Application;
            var source = effect.IsPortal
                ? GridFightUpdateSrcType.KgridFightSrcSelectPortalBuff
                : GridFightUpdateSrcType.KgridFightSrcAugment;
            var result = new GridFightSyncResultData { UpdateSource = source };
            result.EffectParamList.Add(effect.AugmentId);
            AppendRewardApplication(result, application);
            AppendLeadingAcquiredRoles(result, application.RoleAcquisitions);
            if (effect.ShopChanged)
                result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
            if (effect.WinStreakChanged)
                result.UpdateDynamicList.Add(new GridFightSyncData { KeepWinCount = Session.KeepWinCount });
            AppendPendingTrackedAutoEquipRoles(result);
            notify.ResultList.Add(result);
            AppendRoleAcquisitionMerges(notify, source, [effect.AugmentId], application.RoleAcquisitions, unlockEffects: true);
            if (application.RoleAcquisitions.Count > 0)
                AppendRoleDerivedStateResult(notify, source);
        }
    }

    private static GridFightSyncResultData NewAugmentResult(uint augmentId, uint effectId)
    {
        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcAugment,
        };
        result.EffectParamList.Add(new[] { augmentId, effectId });
        return result;
    }
}
