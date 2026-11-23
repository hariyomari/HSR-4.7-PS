using March7thHoney.Data;
using March7thHoney.Enums.GridFight;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed partial class GridFightSession
{
    public const uint BlueSeaPortalId = 113;
    public const uint JoyContractPortalId = 1201;
    private const uint BlueSeaBonusGold = 6;
    // The Joy Contract opens with the 3-cost Silver Wolf.
    private const uint JoyContractInitialSilverWolfRoleId = GridFightSpecialFeatures.SilverWolfThreeCostRoleId;
    private const uint JoyContractTrailblazerRoleId = 8009;
    private const uint JoyContractSparxieRoleId = 1501;
    private const string JoyContractSavedValueKey = "Portal1201_SavedValue01";

    // Capture 21.24.22:
    // initial portal queue=1 -> Blue Sea nested portal queue=3 -> normal round-begin queue=2.
    private readonly List<uint> _activePortalIds = [];
    private readonly Dictionary<string, uint> _portalSavedValues = new(StringComparer.Ordinal);
    private bool _initialRolesMaterialized;
    private bool _joyContractInitialized;
    private uint _joyContractInitialRoleUniqueId;

    public IReadOnlyDictionary<string, uint> PortalSavedValues => _portalSavedValues;
    public uint JoyContractInitialRoleUniqueId => _joyContractInitialRoleUniqueId;
    public bool HasBlueSeaPortal => _activePortalIds.Contains(BlueSeaPortalId);
    public bool HasJoyContractPortal => _activePortalIds.Contains(JoyContractPortalId);

    private void BeginBlueSeaNestedPortal()
    {
        SelectedPortalId = BlueSeaPortalId;
        AddActivePortal(BlueSeaPortalId);
        Gold = (uint)Math.Min(uint.MaxValue, (ulong)Gold + BlueSeaBonusGold);

        var nestedPortalId = _catalog.RollBlueSeaPortal();
        PortalOffer = nestedPortalId == 0 ? Array.Empty<uint>() : new[] { nestedPortalId };
        Phase = GridFightSessionPhase.SelectingNestedPortal;
        PendingQueuePosition = 3;
    }

    private void FinalizeInitialPortalSelection(uint portalId, uint nextQueuePosition)
    {
        SelectedPortalId = portalId;
        AddActivePortal(portalId);

        if (SelectedPortalId == 119 && CurrentNode.NodeType == GridFightNodeTypeEnum.Monster)
            BattlePlan = _catalog.RollBattlePlan(
                CurrentNode,
                CampIds,
                RewardCampId,
                Bosses,
                SelectedPortalId,
                _temporarilyUnlockedRestrictedRoleIds);

        InitializeInvitationPortal();
        InitializeLaborDispatchPortal();
        InitializePepePortal();
        InitializeJoyContractPortal();
        InitializeDotContractPortal();
        InitializeRotationHeroPortal();
        InitializeConceptStockPortal();
        InitializePortalRewards();
        MaterializeInitialRoles();
        ApplyPortalOpeningEffects();

        Gold = (uint)Math.Min(uint.MaxValue, (ulong)Gold + OfficialInitialGold);
        Phase = GridFightSessionPhase.AwaitingInitialRound;
        PendingQueuePosition = nextQueuePosition;
    }

    private void AddActivePortal(uint portalId)
    {
        if (portalId != 0 && !_activePortalIds.Contains(portalId))
            _activePortalIds.Add(portalId);
    }

    private void InitializeJoyContractPortal()
    {
        if (_joyContractInitialized || SelectedPortalId != JoyContractPortalId) return;
        if (!GameData.GridFightRoleBasicInfoData.ContainsKey(JoyContractInitialSilverWolfRoleId) ||
            !GameData.GridFightRoleStarData.ContainsKey(JoyContractInitialSilverWolfRoleId << 4 | 1u))
            return;

        var role = new GridFightRoleState
        {
            UniqueId = _nextEntityUniqueId++,
            RoleId = JoyContractInitialSilverWolfRoleId,
            Star = 1,
            Position = GetFreeBenchPosition(),
        };
        Roles.Add(role);
        _joyContractInitialRoleUniqueId = role.UniqueId;
        _portalSavedValues[JoyContractSavedValueKey] = JoyContractInitialSilverWolfRoleId;
        _joyContractInitialized = true;

        // Do not activate Trait 3006 while the role is on the bench. Only remember the
        // current Silver Wolf cost tier so the first shop refresh can use the right form.
        ObserveSilverWolfProgression();
        RecalculateRoleSwitches();
    }

    private GridFightHeadPlayerEnhanceResult AttachJoyContractReward(GridFightHeadPlayerEnhanceResult result)
    {
        if (!result.Success || !HasJoyContractPortal) return result;
        if (!TryGrantJoyContractHeadPlayerReward(result.EffectId, out var acquisition, out var effectParam))
            return result;

        return result with
        {
            JoyContractReward = acquisition,
            JoyContractEffectParam = effectParam,
        };
    }

    private bool TryGrantJoyContractHeadPlayerReward(
        uint headPlayerEffectId,
        out GridFightRoleAcquisitionResult acquisition,
        out uint effectParam)
    {
        acquisition = GridFightRoleAcquisitionResult.Failed;

        // The two capture-confirmed 1201 trigger parameter IDs are:
        // 30061 completion -> [1201,103], 30062 completion -> [1201,105].
        // 30063/final also belongs to the same contract rule, but its second parameter has
        // not yet been captured, so only the known portal ID is emitted for that stage.
        effectParam = headPlayerEffectId switch
        {
            GridFightSpecialFeatures.SilverWolfThreeCostTraitEffectId => 103u,
            GridFightSpecialFeatures.SilverWolfFourCostTraitEffectId => 105u,
            _ => 0u,
        };

        // Capture proves both rewards are valid but not their exact weights. Keep the
        // uncertainty isolated here; equal choice is deliberately easy to replace later.
        var first = Random.Shared.Next(2) == 0
            ? JoyContractSparxieRoleId
            : JoyContractTrailblazerRoleId;
        var second = first == JoyContractSparxieRoleId
            ? JoyContractTrailblazerRoleId
            : JoyContractSparxieRoleId;

        return TryAddRole(first, 1, out acquisition) ||
               TryAddRole(second, 1, out acquisition);
    }
}

public sealed partial class GridFightManager
{
    public GridFightSyncUpdateResultScNotify BuildBlueSeaSelectionSync(uint finishedQueuePosition)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        var session = Session;
        if (session == null) return notify;

        // Official capture: selecting 113 applies +6 first, before the normal initial +3.
        var bonusResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcPreInitialSupplySelect,
        };
        bonusResult.EffectParamList.Add(GridFightSession.BlueSeaPortalId);
        bonusResult.EffectParamList.Add(102);
        bonusResult.UpdateDynamicList.Add(new GridFightSyncData { ItemValue = session.Gold });
        notify.ResultList.Add(bonusResult);

        var portalResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcSelectPortalBuff,
        };
        portalResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            PortalUpdate = new GridFightPortalBuffUpdateInfo
            {
                PortalBuffId = GridFightSession.BlueSeaPortalId,
            },
        });
        notify.ResultList.Add(portalResult);

        var finishResult = new GridFightSyncResultData();
        finishResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            FinishPendingActionPos = finishedQueuePosition,
        });
        finishResult.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
        notify.ResultList.Add(finishResult);

        var pendingResult = new GridFightSyncResultData();
        pendingResult.UpdateDynamicList.Add(new GridFightSyncData
        {
            LockInfo = new GridFightLockInfo
            {
                LockType = GridFightLockType.Gameplay,
                LockReason = GridFightLockReason.PendingAction,
            },
        });
        if (BuildPendingAction() is { } pending)
            pendingResult.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
        notify.ResultList.Add(pendingResult);
        return notify;
    }

    private void AppendJoyContractInitialResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null ||
            session.SelectedPortalId != GridFightSession.JoyContractPortalId ||
            session.JoyContractInitialRoleUniqueId == 0)
            return;

        var silverWolf = session.Roles.FirstOrDefault(role =>
            role.UniqueId == session.JoyContractInitialRoleUniqueId);
        if (silverWolf == null) return;

        var roleResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcPreInitialSupplySelect,
        };
        roleResult.EffectParamList.Add(GridFightSession.JoyContractPortalId);
        roleResult.EffectParamList.Add(104);
        roleResult.UpdateDynamicList.Add(new GridFightSyncData { AddRoleInfo = BuildRole(silverWolf) });
        notify.ResultList.Add(roleResult);

        var savedResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcPreInitialSupplySelect,
        };
        savedResult.EffectParamList.Add(GridFightSession.JoyContractPortalId);
        savedResult.EffectParamList.Add(104);
        var savedValue = new GridFightSavedValueUpdate();
        foreach (var (key, value) in session.PortalSavedValues)
            savedValue.SavedValues[key] = value;
        savedResult.UpdateDynamicList.Add(new GridFightSyncData { SavedValueUpdate = savedValue });
        notify.ResultList.Add(savedResult);
    }

    private void AppendJoyContractHeadPlayerReward(
        GridFightSyncUpdateResultScNotify notify,
        GridFightHeadPlayerEnhanceResult enhancement)
    {
        var acquisition = enhancement.JoyContractReward;
        if (acquisition == null || !acquisition.Success) return;

        var portalResult = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcPortalBuff,
        };
        portalResult.EffectParamList.Add(GridFightSession.JoyContractPortalId);
        if (enhancement.JoyContractEffectParam != 0)
            portalResult.EffectParamList.Add(enhancement.JoyContractEffectParam);
        AppendAcquiredRoles(portalResult, acquisition);
        notify.ResultList.Add(portalResult);

        IReadOnlyList<uint> effectParameters = enhancement.JoyContractEffectParam == 0
            ? new[] { GridFightSession.JoyContractPortalId }
            : new[] { GridFightSession.JoyContractPortalId, enhancement.JoyContractEffectParam };
        AppendRoleMergeResults(
            notify,
            GridFightUpdateSrcType.KgridFightSrcPortalBuff,
            effectParameters,
            acquisition);
        AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcPortalBuff);
        AppendUnlockEffects(notify, acquisition);
    }
}
