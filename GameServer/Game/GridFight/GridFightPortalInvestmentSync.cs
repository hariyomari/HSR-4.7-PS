using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed partial class GridFightManager
{
    private void AppendPortalInitialResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null) return;
        if (session.SelectedPortalId == GridFightSession.GunpowderPortalId && session.GunpowderInitialForgeItems.Count > 0)
        {
            var result = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcPreInitialSupplySelect };
            result.EffectParamList.Add(new[] { GridFightSession.GunpowderPortalId, 101u });
            foreach (var forge in session.GunpowderInitialForgeItems)
                result.UpdateDynamicList.Add(new GridFightSyncData { AddForgeItemInfo = BuildForgeItem(forge) });
            notify.ResultList.Add(result);
        }
        else if (session.SelectedPortalId == GridFightSession.TalentRecruitmentPortalId && session.TalentRecruitmentInitialOrb is { } orb)
        {
            var result = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcPreInitialSupplySelect };
            result.EffectParamList.Add(new[] { GridFightSession.TalentRecruitmentPortalId, 101u });
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                OrbInfo = new GridFightOrbSyncInfo { UniqueId = orb.UniqueId, OrbItemId = orb.OrbItemId },
            });
            notify.ResultList.Add(result);
        }
    }

    private void AppendMarketVitalitySavedValueResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null) return;
        foreach (var update in session.TakeMarketVitalitySavedValueUpdates())
        {
            var result = NewAugmentResult(GridFightSession.MarketVitalityAugmentId, update.EffectId);
            var saved = new GridFightSavedValueUpdate();
            saved.SavedValues[GridFightSession.MarketVitalitySavedValueKey] = update.Value;
            result.UpdateDynamicList.Add(new GridFightSyncData { SavedValueUpdate = saved });
            notify.ResultList.Add(result);
        }
    }

    private void AppendTwinPrimesResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null) return;
        foreach (var reward in session.TakeTwinPrimesRewards())
        {
            var result = NewAugmentResult(GridFightSession.TwinPrimesAugmentId, reward.EffectId);
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                PlayerLevel = new GridFightPlayerLevelSyncInfo
                {
                    Level = session.PlayerLevel,
                    Exp = session.PlayerExp,
                    MaxLevel = Catalog.MaxPlayerLevel,
                },
            });
            notify.ResultList.Add(result);
        }
        AppendMarketVitalitySavedValueResults(notify);
    }

    private void AppendInvestmentSelectionResults(GridFightSyncUpdateResultScNotify notify, uint augmentId)
    {
        var session = Session;
        if (session == null) return;

        if (augmentId == GridFightSession.PeriodicBenefitsAugmentId)
        {
            var result = NewAugmentResult(augmentId, 106);
            result.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
            notify.ResultList.Add(result);
        }

        if (augmentId == GridFightSession.OotdGoldAugmentId)
        {
            var count = session.OotdDistinctEquipmentCount;
            foreach (var (effectId, stage) in new (uint EffectId, int Stage)[] { (119, 1), (120, 2), (121, 3) })
            {
                var result = NewAugmentResult(augmentId, effectId);
                foreach (var role in session.Roles)
                {
                    var info = BuildRole(role);
                    if (stage < 2) RemovePropertyDelta(info, 1027, count * 60u);
                    if (stage < 3) RemovePropertyDelta(info, 1030, count * 60u);
                    result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = info });
                }
                notify.ResultList.Add(result);
            }
        }

        AppendMarketVitalitySavedValueResults(notify);
    }

    private static void RemovePropertyDelta(GridGameRoleInfo info, uint propertyId, uint delta)
    {
        if (delta == 0 || !info.ConvertPropertyToFixpoint.TryGetValue(propertyId, out var value)) return;
        if (value <= delta) info.ConvertPropertyToFixpoint.Remove(propertyId);
        else info.ConvertPropertyToFixpoint[propertyId] = value - delta;
    }

    private void AppendOotdRefreshResult(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null || !session.ActiveInvestmentIds.Contains(GridFightSession.OotdGoldAugmentId)) return;
        var result = NewAugmentResult(GridFightSession.OotdGoldAugmentId, 121);
        foreach (var role in session.Roles)
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        notify.ResultList.Add(result);
    }

    public GridFightSyncUpdateResultScNotify BuildShopLockSync()
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var result = new GridFightSyncResultData();
        result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        notify.ResultList.Add(result);
        return notify;
    }
}
