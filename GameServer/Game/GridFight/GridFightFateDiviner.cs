using March7thHoney.GameServer.Game.Battle;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightFateDivinerPendingState(
    uint QueuePosition,
    IReadOnlyList<uint> OptionIds,
    bool IsInitial);

public sealed record GridFightFateDivinerEnhanceResult(
    bool Success,
    uint FinishedQueuePosition,
    uint OptionId,
    bool IsInitial,
    uint EffectUniqueId,
    bool RefreshShop,
    bool UpdateAllRoles,
    bool UpdateBlackSwan,
    bool CreatedGameItem)
{
    public static GridFightFateDivinerEnhanceResult Failed { get; } =
        new(false, 0, 0, false, 0, false, false, false, false);
}

/// <summary>
/// Trait 3002, Fate Diviner (命运卜者). Capture: Black Swan gets an immediate first divination,
/// then another one after every two battles in which she is in an active (1..13) slot.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint FateDivinerTraitId = 3002;
    public const uint FateDivinerEffectId = 30021;
    public const uint BlackSwanRoleId = 1307;
    public const uint FateBuffOption1 = 35300211;
    public const uint FateBuffOption2 = 35300221;
    public const uint FateBuffOption3 = 35300231;

    private GridFightFateDivinerPendingState? _fateDivinerPending;
    private bool _fateDivinerInitialized;
    private bool _fateDivinerReady;
    private bool _fateDivinerInitialActivationSyncSent;
    private uint _fateDivinerBattleCount;
    private readonly HashSet<uint> _fateConsumedOptions = [];
    private readonly HashSet<uint> _fateBattleBuffOptions = [];
    private uint _fateOption4Count;
    private uint _fateOption6Count;
    private uint _fateOption5GameItemUniqueId;

    public GridFightFateDivinerPendingState? FateDivinerPending => _fateDivinerPending;
    public uint FateOption5GameItemUniqueId => _fateOption5GameItemUniqueId;

    public bool TryPrepareFateDivinerInitialActivation(
        out GridFightFateDivinerPendingState? pending,
        out uint blackSwanUniqueId)
    {
        pending = null;
        blackSwanUniqueId = 0;
        RefreshFateDivinerAfterRoleStateChange();
        if (_fateDivinerInitialActivationSyncSent) return false;

        EnsureFateDivinerPending();
        if (_fateDivinerPending is not { IsInitial: true } fate) return false;
        var blackSwan = Roles
            .Where(role => role.RoleId == BlackSwanRoleId && IsActivePosition(role.Position))
            .OrderBy(role => role.Position)
            .ThenBy(role => role.UniqueId)
            .FirstOrDefault();
        if (blackSwan == null) return false;

        _fateDivinerInitialActivationSyncSent = true;
        pending = fate;
        blackSwanUniqueId = blackSwan.UniqueId;
        return true;
    }

    private bool HasActiveBlackSwan() => Roles.Any(role =>
        role.RoleId == BlackSwanRoleId && IsActivePosition(role.Position));

    private void RefreshFateDivinerAfterRoleStateChange()
    {
        if (!_fateDivinerInitialized && HasActiveBlackSwan())
        {
            _fateDivinerInitialized = true;
            _fateDivinerReady = true;
        }
    }

    public bool EnsureFateDivinerPending()
    {
        RefreshFateDivinerAfterRoleStateChange();
        if (!_fateDivinerReady || _fateDivinerPending != null || _headPlayerPending != null ||
            EquipmentRecommendation != null || !HasActiveBlackSwan() ||
            Phase is not (GridFightSessionPhase.Preparing or GridFightSessionPhase.AwaitingBackToPrepare)) return false;

        // Official rule: option 6 is the only permanent card and is always the right-most choice.
        // Options 1..5 remain in the draw pool until selected, then are permanently consumed for this run.
        var consumablePool = new uint[] { 1, 2, 3, 4, 5 }
            .Where(option => !_fateConsumedOptions.Contains(option))
            .OrderBy(_ => Random.Shared.Next())
            .Take(2)
            .Order()
            .ToList();
        var options = new List<uint>(consumablePool);
        options.Add(6);
        var isInitial = _fateDivinerBattleCount == 0 && _fateConsumedOptions.Count == 0 &&
                        _fateBattleBuffOptions.Count == 0 && _fateOption4Count == 0 && _fateOption6Count == 0;
        _fateDivinerPending = new GridFightFateDivinerPendingState(PendingQueuePosition, options, isInitial);
        _fateDivinerReady = false;
        return true;
    }

    private void RecordFateDivinerBattleParticipation()
    {
        if (!_fateDivinerInitialized || !HasActiveBlackSwan()) return;
        _fateDivinerBattleCount++;
        if (_fateDivinerBattleCount < 2) return;
        _fateDivinerBattleCount -= 2;
        _fateDivinerReady = true;
    }

    public bool QueueRecurringFateDivinerAfterReturnPreparation()
    {
        // Recurring divinations are not injected at battle end. Official 4.5 queues them only
        // after the node's ReturnPreparation pending action has finished.
        if (!_fateDivinerInitialized || !_fateDivinerReady || _fateDivinerPending != null) return false;
        return EnsureFateDivinerPending();
    }

    public GridFightFateDivinerEnhanceResult ResolveFateDivinerEnhance(uint queuePosition, uint optionId)
    {
        var pending = _fateDivinerPending;
        if (pending == null || pending.QueuePosition != queuePosition || !pending.OptionIds.Contains(optionId))
            return GridFightFateDivinerEnhanceResult.Failed;

        if (optionId is >= 1 and <= 5)
            _fateConsumedOptions.Add(optionId);
        if (optionId is 1 or 2 or 3)
            _fateBattleBuffOptions.Add(optionId);

        var effectUniqueId = 0u;
        var refreshShop = false;
        var updateAll = false;
        var updateBlackSwan = false;
        var createdItem = false;
        switch (optionId)
        {
            case 4:
                _fateOption4Count++;
                effectUniqueId = _nextEntityUniqueId++;
                updateAll = true;
                break;
            case 5:
                if (_fateOption5GameItemUniqueId == 0)
                    _fateOption5GameItemUniqueId = _nextEntityUniqueId++;
                effectUniqueId = _fateOption5GameItemUniqueId;
                RefreshShop(false);
                refreshShop = true;
                createdItem = true;
                break;
            case 6:
                _fateOption6Count++;
                effectUniqueId = _nextEntityUniqueId++;
                updateBlackSwan = true;
                break;
        }

        if (PendingQueuePosition == pending.QueuePosition)
            PendingQueuePosition++;
        _fateDivinerPending = null;
        return new GridFightFateDivinerEnhanceResult(
            true, pending.QueuePosition, optionId, pending.IsInitial, effectUniqueId,
            refreshShop, updateAll, updateBlackSwan, createdItem);
    }

    public void ApplyFateDivinerPropertyScales(IDictionary<uint, uint> properties, uint roleId)
    {
        if (_fateOption4Count > 0)
        {
            // Capture option 4: every role receives property 1020=160 and +200 on 1030.
            properties[1020] = 160;
            properties[1030] = (properties.TryGetValue(1030, out var current1030) ? current1030 : 0u) + 200 * _fateOption4Count;
        }
        if (roleId == BlackSwanRoleId && _fateOption6Count > 0)
            properties[1030] = (properties.TryGetValue(1030, out var current1030) ? current1030 : 0u) + 250 * _fateOption6Count;
    }

    public void ApplyFateDivinerBattleBuffs(BattleInstance battle)
    {
        foreach (var option in _fateBattleBuffOptions)
        {
            var buffId = option switch { 1 => FateBuffOption1, 2 => FateBuffOption2, 3 => FateBuffOption3, _ => 0u };
            if (buffId == 0 || battle.Buffs.Any(buff => buff.BuffID == (int)buffId)) continue;
            battle.Buffs.Add(new MazeBuff((int)buffId, 1, -1) { WaveFlag = -1, IncludeOwnerInTargetList = false });
        }
    }
}

public sealed partial class GridFightManager
{
    private static GridFightPendingAction BuildFateDivinerPendingAction(GridFightFateDivinerPendingState fate)
    {
        var action = new GridFightTraitEffectSelectEnhanceActionInfo
        {
            TraitId = GridFightSession.FateDivinerTraitId,
            EffectId = GridFightSession.FateDivinerEffectId,
            IsOptional = false,
        };
        action.EnhanceOptionIdList.Add(fate.OptionIds);
        if (fate.OptionIds.Count >= 3) action.EnhanceIndexList.Add(new uint[] { 5, 5, 0 });
        else if (fate.OptionIds.Count == 2) action.EnhanceIndexList.Add(new uint[] { 5, 0 });
        else action.EnhanceIndexList.Add(0);
        return new GridFightPendingAction
        {
            QueuePosition = fate.QueuePosition,
            TraitEffectSelectEnhanceAction = action,
        };
    }

    private void AppendFateDivinerInitialActivationResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null ||
            !session.TryPrepareFateDivinerInitialActivation(out var pending, out var blackSwanUniqueId) ||
            pending == null)
            return;

        // Official 4.5 activation sequence after Black Swan enters an active slot:
        // 1) publish trait 3002/effect 30021;
        // 2) TraitEffectUpdate [3002,30021,<BlackSwan unique id>] + gameplay pending lock + card choices;
        // 3) refresh Black Swan so the client applies the selected-enhance property layer.
        var traitResult = new GridFightSyncResultData();
        traitResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            TraitInfo = new GridFightTraitSyncInfo
            {
                TraitId = GridFightSession.FateDivinerTraitId,
                TraitEffectInfo = new GridFightTraitEffectInfo
                {
                    EffectId = GridFightSession.FateDivinerEffectId,
                },
            },
        });
        notify.ResultList.Add(traitResult);

        var pendingResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectUpdate,
        };
        pendingResult.EffectParamList.Add(new[]
        {
            GridFightSession.FateDivinerTraitId,
            GridFightSession.FateDivinerEffectId,
            blackSwanUniqueId,
        });
        pendingResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LockInfo = new GridFightLockInfo
            {
                LockType = GridFightLockType.Gameplay,
                LockReason = GridFightLockReason.PendingAction,
            },
        });
        pendingResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            PendingAction = BuildFateDivinerPendingAction(pending),
        });
        notify.ResultList.Add(pendingResult);

        var blackSwan = session.Roles.FirstOrDefault(role => role.UniqueId == blackSwanUniqueId);
        if (blackSwan != null)
        {
            var refreshResult = new GridFightSyncResultData();
            refreshResult.UpdateDynamicList.Add(new GridFightSyncData
            {
                UpdateRoleInfo = BuildRole(blackSwan),
            });
            notify.ResultList.Add(refreshResult);
        }
    }

    public GridFightSyncUpdateResultScNotify BuildFateDivinerEnhanceSync(GridFightFateDivinerEnhanceResult enhancement)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null || !enhancement.Success) return notify;

        // Recurring divinations carry TraitEffectUpdate + current gold. The initial free
        // divination in the capture skips this row.
        if (!enhancement.IsInitial)
        {
            var update = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectUpdate };
            update.EffectParamList.Add(new[] { GridFightSession.FateDivinerTraitId, GridFightSession.FateDivinerEffectId });
            update.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
            if (enhancement.CreatedGameItem)
                update.UpdateDynamicList.Add(new GridFightSyncData { GameDataItemInfo = BuildFateDivinerGameItem(session, false) });
            notify.ResultList.Add(update);
        }

        if (enhancement.OptionId is 4 or 5 or 6)
        {
            var result = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectSelectEnhance };
            result.EffectParamList.Add(new[] { enhancement.OptionId, enhancement.EffectUniqueId });
            if (enhancement.UpdateAllRoles)
                foreach (var role in session.Roles)
                    result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(role) });
            if (enhancement.UpdateBlackSwan)
            {
                var swan = session.Roles.FirstOrDefault(role => role.RoleId == GridFightSession.BlackSwanRoleId && role.Position is >= 1 and <= 13);
                if (swan != null) result.UpdateDynamicList.Add(new GridFightSyncData { UpdateRoleInfo = BuildRole(swan) });
            }
            if (enhancement.CreatedGameItem)
                result.UpdateDynamicList.Add(new GridFightSyncData { GameDataItemInfo = BuildFateDivinerGameItem(session, true) });
            if (enhancement.RefreshShop)
                result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
            notify.ResultList.Add(result);
        }

        var finish = new GridFightSyncResultData();
        finish.UpdateDynamicList.Add(new GridFightSyncData { FinishPendingActionPos = enhancement.FinishedQueuePosition });
        finish.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finish);
        AppendNextTraitEnhancement(notify);
        return notify;
    }

    private static GridFightGameItemInfo BuildFateDivinerGameItem(GridFightSession session, bool activated)
    {
        var item = new GridFightGameItemInfo
        {
            UniqueId = session.FateOption5GameItemUniqueId,
            Descriptor_ = BuildGameItemDescriptor(GridFightGameItemCategory.HackConsole, 5),
            FLELIBAJPEC = new FCABIGNMAPL { CKKDAEMFDPA = activated ? 5u : 0u },
        };
        return item;
    }
}
