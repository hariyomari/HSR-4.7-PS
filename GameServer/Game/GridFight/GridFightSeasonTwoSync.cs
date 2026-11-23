using Google.Protobuf;
using March7thHoney.Data;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Protocol side of the season-2 trait effects: GridFightTraitEffectInfo snapshots (lobby), pending actions,
///     battle BattleGridFightTraitEffectInfo members and the post-request diff sync.
/// </summary>
public sealed partial class GridFightManager
{
    // Effects whose client OnUpdate keeps state; the client treats every update as a full snapshot.
    private static readonly HashSet<uint> SeasonTwoStatefulEffects =
    [
        GridFightSession.DanHengChooseTraitEffectId,
        GridFightSession.DanHengTwoInOneEffectId,
        GridFightSession.PhainonEffectId,
        GridFightSession.RangerEffectId,
        GridFightSession.StellaronHunterEffectId,
        GridFightSession.GoldenHotelEffectId,
        GridFightSession.MemberLinkEffectId,
        GridFightSession.CounterKingEffectId,
        GridFightSession.JadeEffectId,
        GridFightSession.TrailblazeEffectId,
        GridFightSession.ChimeraEffectId,
        GridFightSession.MoonGameWishEffectId,
        GridFightSession.ClaraEffectId,
        GridFightSession.ElationEffectId,
    ];

    private readonly Dictionary<(uint TraitId, uint EffectId), ByteString> _sentSeasonTwoEffects = [];

    /// <summary>The GridFightTraitEffectInfo the client expects for a season-2 effect, or null if stateless.</summary>
    private GridFightTraitEffectInfo? BuildSeasonTwoTraitEffectInfo(uint traitId, uint effectId, bool consumeOneShotFlags)
    {
        var session = Session;
        if (session == null || !session.IsSeasonTwoRules) return null;
        var info = new GridFightTraitEffectInfo { EffectId = effectId };
        switch (effectId)
        {
            case GridFightSession.DanHengChooseTraitEffectId:
                info.GEPGMIEPBMB = new LCMAKEBBBDL { RoleId = GridFightSession.DanHengMainRoleId };
                if (session.DanHengChosenTraitId != 0) info.GEPGMIEPBMB.CKHHAPGAAOH.Add(session.DanHengChosenTraitId);
                return info;
            case GridFightSession.DanHengTwoInOneEffectId:
                info.AMOIMOHMBEM = new GGHAADPCOAA
                {
                    CAAKCKEPMEK = session.DanHengMainUniqueId,
                    OKFEMIMJLME = session.DanHengSubUniqueId,
                };
                return info;
            case GridFightSession.PhainonEffectId:
                info.MCCMPFENGCD = new MCEDNANGIJJ
                {
                    GFFGMNFADBE = session.PhainonCanConsume,
                    EKEDBOFGNII = session.PhainonConsumedCount,
                };
                return info;
            case GridFightSession.RangerEffectId:
                info.DPKFJEEJABC = new IKGOIPNFDBB();
                info.DPKFJEEJABC.HLFIOPJFHDJ.Add(session.ResolveRangerRoleIds());
                return info;
            case GridFightSession.StellaronHunterEffectId:
            case GridFightSession.GoldenHotelEffectId:
                info.NMHCBKFEOMM = new DFJMFJHEHLN();
                info.NMHCBKFEOMM.PNJIPHJJONG.Add(session.GetTemplateEquipmentUniqueIds(traitId));
                return info;
            case GridFightSession.MemberLinkEffectId:
                info.FLDEMEJEODJ = new CLKKNDMGLPP();
                info.FLDEMEJEODJ.LKNGKJCFFBH.Add(session.MemberLinkUniqueIds);
                return info;
            case GridFightSession.CounterKingEffectId:
                info.AvatarCoreInfo = new NBPEJFHIKJL();
                if (session.ResolveCounterKing() is { } king) info.AvatarCoreInfo.OLPPMEAMFFO.Add(king.UniqueId);
                return info;
            case GridFightSession.JadeEffectId:
                info.GDAMHENIIDJ = new JADHHGHGMPH
                {
                    BCDFCFMBCCG = session.JadeRefreshUnlocked,
                    // The auto-open flag makes the client insert its own shop action in front of the queue; with a
                    // server pending action open at the same time the client never closes that prompt, so the flag
                    // waits until nothing is pending.
                    KPMKFGCFMHC = consumeOneShotFlags && !HasOpenPendingAction(session) && session.TakeJadeAutoOpen(),
                };
                info.GDAMHENIIDJ.OGDKJPDFFAI.Add(session.JadeGoods.Select(good =>
                {
                    var entry = new LNDHKMMGBGC { SlotId = good.SlotId, DNJCAMKKLPB = good.GoodId };
                    if (good.GoodId != 0)
                    {
                        entry.NMGIABHIJJB = new GridFightDropInfo();
                        entry.NMGIABHIJJB.DropList.Add(good.Rewards.Select(BuildSeasonTwoDropEntry));
                    }
                    return entry;
                }));
                return info;
            case GridFightSession.TrailblazeEffectId:
                info.KDBKIPPHMFI = new GGFHGKGJOOB { MJIGJEGMCML = session.TrailblazeCoin };
                info.KDBKIPPHMFI.OGDKJPDFFAI.Add(session.TrailblazeGoods.Select(good =>
                    new AMNAMDLMADK { DNJCAMKKLPB = good.GoodId, Cost = good.Cost }));
                foreach (var (goodId, count) in session.TrailblazeBought)
                    info.KDBKIPPHMFI.OBHKKOFDEFG[goodId] = count;
                return info;
            case GridFightSession.ChimeraEffectId:
                info.GIKNCJMJLFE = new CGNAHGDKAFC { EMLBKOKEAOH = session.ChimeraEnergy };
                info.GIKNCJMJLFE.BCGOAIPKGIK.Add(session.Chimeras.Select(chimera => new MLGNKOCLDCO
                {
                    UniqueId = chimera.UniqueId,
                    FGECDDFBFOJ = chimera.ChimeraId,
                    RoleStar = chimera.Star,
                }));
                return info;
            case GridFightSession.MoonGameWishEffectId:
                if (session.MoonGameSubTraitId != GridFightSession.MoonGameWishSubTraitId) return null;
                info.ECEMNCJBFOP = session.MoonGameWishPower;
                return info;
            case GridFightSession.ClaraEffectId:
                return BuildClaraEffectInfo(info);
            case GridFightSession.ElationEffectId:
                return BuildElationEffectInfo(info);
            default:
                return null;
        }
    }

    /// <summary>
    ///     Lock sent together with a pending action. The client refuses every board drag while the gameplay lock is
    ///     set (GridFightPlayer.Blocked, PlayerBlockConstrait and the Lua drag handlers), which includes dragging a
    ///     role onto the Spotlight (主角光环) stage, so that selection is sent unlocked.
    /// </summary>
    private GridFightLockInfo BuildPendingLockInfo() =>
        Session?.IsSpotLightSelecting == true
            ? new GridFightLockInfo()
            : new GridFightLockInfo
            {
                LockType = GridFightLockType.Gameplay,
                LockReason = GridFightLockReason.PendingAction,
            };

    private bool HasOpenPendingAction(GridFightSession session) =>
        session.HasPendingSelection || session.Phase != GridFightSessionPhase.Preparing || BuildPendingAction() != null;

    private static GridFightDropEntry BuildSeasonTwoDropEntry(GridFightRewardEntry reward)
    {
        if (reward.Type == Enums.GridFight.GridFightBonusTypeEnum.Refresh)
            return new GridFightDropEntry { DropType = GridFightDropType.Refresh, Num = reward.Count };
        return BuildDropEntry(reward);
    }

    /// <summary>Effect ids (with their owning trait) that currently need a state snapshot on the client.</summary>
    private IEnumerable<(uint TraitId, uint EffectId)> EnumerateSeasonTwoStatefulEffects()
    {
        var session = Session;
        if (session == null || !session.IsSeasonTwoRules) yield break;
        foreach (var (traitId, state) in session.ResolveTraitLayers())
        {
            // GridFightGameTrait.UpdateTraitEffect NREs for a trait the client does not know; only active traits.
            if (state.Layer == 0 || !GameData.GridFightTraitBasicInfoData.TryGetValue(traitId, out var config)) continue;
            foreach (var effectId in config.TraitEffectList.Where(SeasonTwoStatefulEffects.Contains))
                yield return (traitId, effectId);
            if (traitId == GridFightSession.MoonGameTraitId)
                yield return (traitId, GridFightSession.MoonGameWishEffectId);
        }
    }

    /// <summary>Snapshot for GridGameTraitInfo.trait_effect_list (GetData / StartGamePlay).</summary>
    private GridFightTraitEffectInfo BuildGameTraitEffectInfo(uint traitId, uint effectId)
    {
        var info = BuildSeasonTwoTraitEffectInfo(traitId, effectId, true);
        if (info == null) return new GridFightTraitEffectInfo { EffectId = effectId };
        _sentSeasonTwoEffects[(traitId, effectId)] = info.ToByteString();
        return info;
    }

    private void ResetSeasonTwoSentEffects() => _sentSeasonTwoEffects.Clear();

    /// <summary>
    ///     Runs the season-2 trait refresh after a GridFight request and returns the sync for everything that changed
    ///     (summoned roles, trait equipment, gold/HP/shop changes, trait effect snapshots, new pending prompt).
    /// </summary>
    public GridFightSyncUpdateResultScNotify? BuildSeasonTwoFollowUpSync()
    {
        var session = Session;
        if (session == null || !session.IsSeasonTwoRules) return null;
        var changes = session.RefreshSeasonTwoTraits();
        var notify = new GridFightSyncUpdateResultScNotify();
        var result = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcNone };
        foreach (var role in changes.AddedRoles)
            result.UpdateDynamicList.Add(new GridFightSyncData { AddRoleInfo = BuildRole(role) });
        foreach (var uniqueId in changes.RemovedRoleIds)
            result.UpdateDynamicList.Add(new GridFightSyncData { RemoveRoleUniqueId = uniqueId });
        foreach (var role in changes.UpdatedRoles)
            result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
        AppendLeadingAcquiredRoles(result, changes.RoleAcquisitions);
        foreach (var equipment in changes.RemovedEquipments)
            result.UpdateDynamicList.Add(new GridFightSyncData { RemoveGameItemInfo = BuildEquipmentItems([equipment]) });
        if (changes.AddedEquipments.Count > 0)
        {
            var items = new GridFightGameItemSyncInfo();
            items.GridFightEquipmentList.Add(changes.AddedEquipments.Select(BuildEquipment));
            result.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        }
        foreach (var change in changes.ConsumableChanges)
        {
            var items = new GridFightGameItemSyncInfo();
            items.UpdateGridFightConsumableList.Add(BuildConsumableUpdate(change));
            result.UpdateDynamicList.Add(change.IsNew
                ? new GridFightSyncData { AddGameItemInfo = items }
                : new GridFightSyncData { UpdateGameItemInfo = items });
        }
        if (changes.GoldChanged && session.TakeChimeraPassiveGold() > 0)
        {
            // Passive chimera gold plays its own fly animation (update_source 47, params[0] = chimera id 402 工作狂).
            var gold = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.Lgdffiomnjo };
            gold.EffectParamList.Add(402u);
            gold.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
            notify.ResultList.Add(gold);
        }
        else if (changes.GoldChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        if (changes.PlayerLevelChanged)
            AppendPlayerLevel(result, session);
        if (changes.LineupHpChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                LineupHp = new GridFightLineupHpSyncInfo { CurrentHp = session.LineupHp, MaxHp = session.MaxLineupHp },
            });
        if (changes.ShopChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        AppendRoleSavedValueUpdates(result, session.RefreshRoleSavedValues());
        AppendSpotLightChanges(result, session, changes.SpotLight);
        AppendSeasonTwoEffectDiffs(result);
        AppendSeasonTwoItemDiffs(result, session);
        AppendAhaModifierDiff(result, session);
        AppendPamKingModifierDiff(result, session);
        if (changes.PendingStarted && BuildPendingAction() is { } pending)
        {
            result.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = BuildPendingLockInfo() });
            result.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        }
        AppendRoleCapacityDiff(result, session);
        if (result.UpdateDynamicList.Count > 0) notify.ResultList.Add(result);
        // Roles gained here run through the same merge pipeline (TryAddRole); without their merge steps the client
        // kept the merged-away copies and never saw the upgraded role.
        AppendRoleAcquisitionMerges(notify, GridFightUpdateSrcType.KgridFightSrcNone, [], changes.RoleAcquisitions);
        return notify.ResultList.Count == 0 ? null : notify;
    }

    private uint _observedMaxActiveRoles;
    private uint _observedOffFieldMaxCount;

    private void ObserveRoleCapacity(GridFightSession session)
    {
        _observedMaxActiveRoles = session.MaxActiveRoles;
        _observedOffFieldMaxCount = session.OffFieldMaxCount;
    }

    /// <summary>
    ///     Population cap (1583 -> GridFightPlayer.UpdateMaxRoleNum) and unlocked back cells (833 -> UpdateMaxBackRoleNum)
    ///     whenever they changed since the client last got them (a crown from any reward, the 双龙吟 summon's own cell...).
    ///     Growth goes first so a cell exists before a role is put into it; shrinking goes last, after the roles left it.
    ///     Re-sending a value the client already has is a no-op there.
    /// </summary>
    private void AppendRoleCapacityDiff(GridFightSyncResultData result, GridFightSession session)
    {
        var maxRoles = session.MaxActiveRoles;
        var backCells = session.OffFieldMaxCount;
        if (backCells != _observedOffFieldMaxCount)
            Put(new GridFightSyncData { JLOCMDIDPCI = backCells }, backCells > _observedOffFieldMaxCount);
        if (maxRoles != _observedMaxActiveRoles)
            Put(new GridFightSyncData { MaxActiveRoleCount = maxRoles }, maxRoles > _observedMaxActiveRoles);
        _observedMaxActiveRoles = maxRoles;
        _observedOffFieldMaxCount = backCells;

        void Put(GridFightSyncData data, bool grows)
        {
            if (grows) result.UpdateDynamicList.Insert(0, data);
            else result.UpdateDynamicList.Add(data);
        }
    }

    private void AppendPlayerLevel(GridFightSyncResultData result, GridFightSession session)
    {
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            PlayerLevel = new GridFightPlayerLevelSyncInfo
            {
                Level = session.PlayerLevel,
                Exp = session.PlayerExp,
                MaxLevel = Catalog.MaxPlayerLevel,
            },
        });
        result.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
    }

    /// <summary>Adds trait_info snapshots for every season-2 effect whose state differs from what the client has.</summary>
    private void AppendSeasonTwoEffectDiffs(GridFightSyncResultData result)
    {
        var current = EnumerateSeasonTwoStatefulEffects().ToList();
        foreach (var stale in _sentSeasonTwoEffects.Keys.Where(key => !current.Contains(key)).ToList())
            _sentSeasonTwoEffects.Remove(stale);
        foreach (var (traitId, effectId) in current)
        {
            var info = BuildSeasonTwoTraitEffectInfo(traitId, effectId, true);
            if (info == null) continue;
            var bytes = info.ToByteString();
            if (_sentSeasonTwoEffects.TryGetValue((traitId, effectId), out var sent) && sent.Equals(bytes)) continue;
            _sentSeasonTwoEffects[(traitId, effectId)] = bytes;
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                TraitInfo = new GridFightTraitSyncInfo { TraitId = traitId, TraitEffectInfo = info },
            });
        }
    }

    /// <summary>A sync result marked as TraitEffectUpdate for the given effect (drives the client's bonus animations).</summary>
    private GridFightSyncResultData BuildTraitEffectUpdateResult(uint traitId, uint effectId)
    {
        var result = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectUpdate };
        // JadeShop's summarizer reads params[1] of every TraitEffectUpdate result without a bound check.
        result.EffectParamList.Add(traitId);
        result.EffectParamList.Add(effectId);
        return result;
    }

    // ---------------------------------------------------------------- pending actions

    private GridFightPendingAction? BuildSeasonTwoPendingAction(GridFightSession session)
    {
        if (session.SeasonTwoPending is not { } pending) return null;
        var action = new GridFightPendingAction { QueuePosition = pending.QueuePosition };
        switch (pending.Kind)
        {
            case GridFightSeasonTwoPendingKind.DanHengChooseTrait:
                action.GEPGMIEPBMB = new AKDPLDPGGKF
                {
                    TraitId = pending.TraitId,
                    EffectId = pending.EffectId,
                    RoleId = GridFightSession.DanHengMainRoleId,
                };
                action.GEPGMIEPBMB.DAHOEGKELCI.Add(pending.Options);
                return action;
            case GridFightSeasonTwoPendingKind.RangerAppoint:
                action.DPKFJEEJABC = new EEODAFPJLFE { TraitId = pending.TraitId, EffectId = pending.EffectId };
                action.DPKFJEEJABC.PNHBEHLICFL.Add(pending.Options);
                return action;
            case GridFightSeasonTwoPendingKind.ChimeraSelect:
                action.JMJOAOCJAAG = new MMFBHOGIAML
                {
                    TraitId = pending.TraitId,
                    EffectId = pending.EffectId,
                    PLPJNCDKMAD = session.ChimeraRerolls,
                };
                action.JMJOAOCJAAG.OGDKJPDFFAI.Add(session.ChimeraGoods.Select(good => new AMFPHALPHMF
                {
                    Type = (FDKAFBBHPFL)good.Type,
                    Param = good.Param,
                }));
                return action;
            case GridFightSeasonTwoPendingKind.ClaraEnhance:
                return BuildClaraPendingAction(session, pending, action);
            case GridFightSeasonTwoPendingKind.SpotLightSelect:
                return BuildSpotLightPendingAction(session, action);
            case GridFightSeasonTwoPendingKind.AhaUltra:
                action.OKPMJOCEKBC = new CHAIHAICBCE { Type = HOIDGPICBOJ.Jphakjkapei };
                return action;
            case GridFightSeasonTwoPendingKind.Bonfire:
            {
                var bonfire = new NCEPOLNLNKH
                {
                    Phase = session.BonfireSelected ? KMJMHDPPAKH.Bgenlpcjgjf : KMJMHDPPAKH.Hllkdffmien,
                    EIFICNFGGHI = session.CurrentNode.BasicGoldReward,
                };
                bonfire.KFNMIEMBJGD.Add(session.BonfireOptions.Select(option => new MHCOIBONODA
                {
                    IMCOGBJLOFP = option.ShowType,
                    GOFDNCIPKFH = option.RoleId,
                    EventId = option.EventId,
                    HAFOKMHCGFM = option.OptionId,
                }));
                // Attribute the bonfire to the partner effect (reason {570 = effect id}).
                action.Reason = new OEJKAOKHPPL { OKGGFNJJLNG = pending.EffectId };
                action.IIDBLCFAAPC = bonfire;
                return action;
            }
            default:
                return null;
        }
    }

    /// <summary>Sync for a finished season-2 selection: effect snapshot diff, finish marker, gains.</summary>
    public GridFightSyncUpdateResultScNotify BuildSeasonTwoSelectionSync(
        uint traitId,
        uint effectId,
        uint finishedQueuePosition,
        GridFightRewardApplication? application = null,
        GridFightSessionChange? change = null,
        GridFightUpdateSrcType? gainSource = null,
        bool includeShop = false,
        GridFightEquipmentState? removedEquipment = null)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var result = gainSource is { } source
            ? new GridFightSyncResultData { UpdateSource = source }
            : BuildTraitEffectUpdateResult(traitId, effectId);
        if (removedEquipment != null)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                RemoveGameItemInfo = BuildEquipmentItems([removedEquipment]),
            });
        if (application != null) AppendRewardApplication(result, application);
        if (application != null) AppendLeadingAcquiredRoles(result, application.RoleAcquisitions);
        if (change != null) AppendSessionChanges(result, change);
        if (includeShop) result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        AppendRoleSavedValueUpdates(result, Session.RefreshRoleSavedValues());
        AppendSeasonTwoEffectDiffs(result);
        notify.ResultList.Add(result);
        if (application != null)
            AppendRoleAcquisitionMerges(notify, result.UpdateSource, [traitId, effectId], application.RoleAcquisitions);
        if (finishedQueuePosition != 0)
        {
            var finish = new GridFightSyncResultData();
            finish.UpdateDynamicList.Add(new GridFightSyncData { FinishPendingActionPos = finishedQueuePosition });
            finish.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
            notify.ResultList.Add(finish);
        }
        return notify;
    }

    /// <summary>After a bonfire choice: the gains, then the same pending action re-sent with phase 2.</summary>
    public GridFightSyncUpdateResultScNotify BuildBonfireChoiceSync(GridFightRewardApplication gains)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var result = new GridFightSyncResultData();
        AppendRewardApplication(result, gains);
        AppendLeadingAcquiredRoles(result, gains.RoleAcquisitions);
        if (BuildPendingAction() is { } pending) result.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        notify.ResultList.Add(result);
        AppendRoleAcquisitionMerges(notify, GridFightUpdateSrcType.KgridFightSrcNone, [], gains.RoleAcquisitions);
        return notify;
    }

    /// <summary>Leaving the bonfire: finish it, pay the node gold and continue like a cleared reward node.</summary>
    public GridFightSyncUpdateResultScNotify BuildBonfireLeaveSync(uint finishedQueuePosition)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var finish = new GridFightSyncResultData();
        finish.UpdateDynamicList.Add(new GridFightSyncData { FinishPendingActionPos = finishedQueuePosition });
        finish.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finish);
        var next = BuildSync(GridFightUpdateSrcType.KgridFightSrcNone, includeGold: true, includeShop: true, includeRoute: true);
        notify.ResultList.Add(next.ResultList);
        if (BuildPendingAction() is { } pending)
        {
            var round = new GridFightSyncResultData();
            round.UpdateDynamicList.Add(new GridFightSyncData
            {
                LockInfo = BuildPendingLockInfo(),
            });
            round.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
            notify.ResultList.Add(round);
        }
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildSeasonTwoPendingRefreshSync()
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (BuildPendingAction() is not { } pending) return notify;
        var result = new GridFightSyncResultData();
        result.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        notify.ResultList.Add(result);
        return notify;
    }

    /// <summary>
    ///     GridFightContextValueInfo: field 6 (named context_value_version) is the Elation template id, field 8 maps
    ///     template-equipment traits to their run template, field 11 is the Moon Game (幻月游戏) sub-trait and the value
    ///     map carries the Aha growth key. Season-1 runs keep the captured template 97.
    /// </summary>
    private static GridFightContextValueInfo BuildContextValueInfo(GridFightSession session)
    {
        var info = new GridFightContextValueInfo
        {
            ContextValueVersion = session.ElationTemplateId != 0 ? session.ElationTemplateId : 97,
        };
        if (session.ActiveInvestmentIds.Contains(GridFightSession.PrismaticProcurementAugmentId))
            info.ContextValueMap[GridFightSession.ProcurementSavedValueKey] = session.ProcurementRefreshCount;
        if (session.ActiveInvestmentIds.Contains(GridFightSession.GoldProcurementAugmentId))
            info.ContextValueMap[GridFightSession.GoldProcurementSavedValueKey] = session.GoldProcurementRefreshCount;
        if (!session.IsSeasonTwoRules) return info;
        foreach (var (traitId, templateId) in session.TraitTemplateIds) info.DKMNOPCJDDC[traitId] = templateId;
        info.FMNPBOJGLFI = session.MoonGameSubTraitId;
        if (session.AhaGrowth > 0) info.ContextValueMap[GridFightSession.AhaGrowthSavedValueKey] = session.AhaGrowth;
        return info;
    }

    // ---------------------------------------------------------------- battle

    /// <summary>BattleGridFightTraitEffectInfo members the battle reads for season-2 effects.</summary>
    internal static void FillSeasonTwoBattleTraitEffect(GridFightSession session, BattleGridFightTraitEffectInfo effect)
    {
        if (!session.IsSeasonTwoRules) return;
        switch (effect.EffectId)
        {
            case GridFightSession.MemberLinkEffectId:
                effect.FLDEMEJEODJ = new MOGMLBIJMEH();
                effect.FLDEMEJEODJ.HLFIOPJFHDJ.Add(session.MemberLinkUniqueIds
                    .Select(uid => session.Roles.FirstOrDefault(role => role.UniqueId == uid)?.RoleId ?? 0)
                    .Where(roleId => roleId != 0));
                break;
            case GridFightSession.CounterKingEffectId:
                effect.AvatarCoreInfo = new BattleGridFigntAvatarCoreRoleInfo();
                if (session.ResolveCounterKing() is { } king)
                {
                    effect.AvatarCoreInfo.GridFightAvatarList.Add(king.RoleId);
                    effect.AvatarCoreInfo.LKNGKJCFFBH.Add(king.UniqueId);
                }
                break;
            case GridFightSession.PhainonEffectId:
                effect.MCCMPFENGCD = new GHCEOIMHKJI { EIMIIHEDKFM = session.PhainonKindling };
                break;
            case GridFightSession.TrailblazeEffectId:
                effect.KDBKIPPHMFI = new LFNNCOMFBMD { MJIGJEGMCML = session.TrailblazeCoin };
                break;
            case GridFightSession.ChimeraEffectId:
                effect.GIKNCJMJLFE = new BDIIMNDEADO();
                effect.GIKNCJMJLFE.BCGOAIPKGIK.Add(session.Chimeras.Select(chimera => new KOCDIMHKAMH
                {
                    FGECDDFBFOJ = chimera.ChimeraId,
                    RoleStar = chimera.Star,
                    RoleId = session.ResolveChimeraRoleId(chimera.ChimeraId),
                }));
                break;
            default:
                FillClaraBattleTraitEffect(session, effect);
                break;
        }
    }
}
