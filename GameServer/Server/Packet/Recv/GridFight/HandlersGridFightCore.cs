using March7thHoney.Data;
using March7thHoney.Enums.Mission;
using March7thHoney.Data.Custom;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.GridFight.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.GridFight;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.GridFight;

[Opcode(CmdIds.GridFightGetDataCsReq)]
public sealed class HandlerGridFightGetDataCsReq : GridFightHandler<GridFightGetDataCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightGetDataCsReq request)
    {
        await FinishTutorialStageQuests(player);
        await connection.SendPacket(new PacketGridFightGetDataScRsp(player));
    }

    /// <summary>
    ///     GetData reports every tutorial stage as finished (GridFightManager.BuildSystemInfo), but the stage reward
    ///     quests 7303101/7303102 (FinishType GridFightDivisionStageFinish) were never completed, and the resident
    ///     map entry keeps the "start" button on the tutorial page until they are. Finish them to match.
    /// </summary>
    private static async ValueTask FinishTutorialStageQuests(PlayerInstance player)
    {
        if (player.QuestManager is not { } quests || player.GridFightManager is not { } manager) return;
        var finishedStages = manager.Catalog.CompletedTutorialStages.Select(stage => (int)stage.DivisionId).ToHashSet();
        foreach (var quest in GameData.QuestDataData.Values)
        {
            if (!GameData.FinishWayData.TryGetValue(quest.FinishWayID, out var finishWay) ||
                finishWay.FinishType != MissionFinishTypeEnum.GridFightDivisionStageFinish ||
                !finishedStages.Contains(finishWay.ParamInt1))
                continue;
            await quests.FinishQuest(quest.QuestID);
        }
    }
}

[Opcode(CmdIds.GridFightStartGamePlayCsReq)]
public sealed class HandlerGridFightStartGamePlayCsReq : GridFightHandler<GridFightStartGamePlayCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightStartGamePlayCsReq request)
    {
        var manager = player.GridFightManager!;
        var success = manager.Start(request.Season, request.DivisionId, request.IsOverlock);
        var response = new GridFightStartGamePlayScRsp
        {
            Retcode = success ? 0 : (uint)Retcode.RetFail,
        };
        if (success) response.CurrentInfo = manager.BuildCurrentInfo();
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightStartGamePlayScRsp, response));
    }
}

[Opcode(CmdIds.GridFightResumeGamePlayCsReq)]
public sealed class HandlerGridFightResumeGamePlayCsReq : GridFightHandler<GridFightResumeGamePlayCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightResumeGamePlayCsReq request)
    {
        var manager = player.GridFightManager!;
        var success = manager.Session?.UniqueId == request.UniqueId;
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightResumeGamePlayScRsp,
            new GridFightResumeGamePlayScRsp
            {
                Retcode = success ? 0 : (uint)Retcode.RetFail,
            }));
    }
}

[Opcode(CmdIds.GridFightQuitSettleCsReq)]
public sealed class HandlerGridFightQuitSettleCsReq : GridFightHandler<GridFightQuitSettleCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightQuitSettleCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        if (session == null)
        {
            // A run the server already settled (victory, or squad HP reached 0) has nothing left to settle.
            await connection.SendPacket(BasePacket.Create(CmdIds.GridFightQuitSettleScRsp,
                new GridFightQuitSettleScRsp
                {
                    Retcode = manager.HasSettledSession ? 0 : (uint)Retcode.RetFail,
                }));
            return;
        }

        var settle = manager.BuildSettleNotify(GridFightSettleReason.Quit);
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightSyncKeepWinCntNotify,
            new GridFightSyncKeepWinCntNotify { KeepWinCount = session.KeepWinCount }));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightSettleNotify, settle));
        manager.ClearSession();
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightQuitSettleScRsp,
            new GridFightQuitSettleScRsp()));
        await manager.RecordGameFinished(session);
    }
}

[Opcode(CmdIds.GridFightQuitLeaveGamePlayCsReq)]
public sealed class HandlerGridFightQuitLeaveGamePlayCsReq : GridFightHandler<GridFightQuitLeaveGamePlayCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightQuitLeaveGamePlayCsReq request)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightQuitLeaveGamePlayScRsp,
            new GridFightQuitLeaveGamePlayScRsp()));
    }
}

[Opcode(CmdIds.GridFightHandlePendingActionCsReq)]
public sealed class HandlerGridFightHandlePendingActionCsReq : GridFightHandler<GridFightHandlePendingActionCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightHandlePendingActionCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        if (session != null && request.RecommendEquipmentSelectAction != null)
        {
            var selection = session.SelectRecommendedEquipment(
                request.QueuePosition,
                request.RecommendEquipmentSelectAction.EquipmentId);
            if (selection.Success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                    manager.BuildRecommendationSelectionSync(selection)));
            await SendResponse(connection, request.QueuePosition, selection.Success);
            return;
        }
        if (session != null && request.TraitEffectSelectEnhanceAction != null)
        {
            if (session.FateDivinerPending != null)
            {
                var fate = session.ResolveFateDivinerEnhance(
                    request.QueuePosition,
                    request.TraitEffectSelectEnhanceAction.EnhanceOptionId);
                if (fate.Success)
                    await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                        manager.BuildFateDivinerEnhanceSync(fate)));
                await SendResponse(connection, request.QueuePosition, fate.Success);
                return;
            }
            var enhancement = session.ResolveHeadPlayerEnhance(
                request.QueuePosition,
                request.TraitEffectSelectEnhanceAction.EnhanceOptionId);
            if (enhancement.Success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                    manager.BuildHeadPlayerEnhanceSync(enhancement)));
            await SendResponse(connection, request.QueuePosition, enhancement.Success);
            return;
        }
        if (session != null && request.EliteBranchRerollAction != null)
        {
            var success = session.RerollEncounter(request.QueuePosition);
            if (success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                    manager.BuildEncounterRerollSync()));
            await SendResponse(connection, request.QueuePosition, success);
            return;
        }
        if (session != null && request.EliteBranchSelectAction != null)
        {
            var eliteBranchId = request.EliteBranchSelectAction.EliteBranchId;
            var success = session.SelectEncounter(request.QueuePosition, eliteBranchId);
            if (success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                    manager.BuildEncounterSelectionSync(request.QueuePosition, eliteBranchId)));
            await SendResponse(connection, request.QueuePosition, success);
            return;
        }
        if (session != null && request.InvestmentRerollAction != null)
        {
            var success = session.RerollInvestment(
                request.QueuePosition,
                request.InvestmentRerollAction.AugmentId);
            if (success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSync(
                    GridFightUpdateSrcType.KgridFightSrcNone,
                    includePending: true)));
            await SendResponse(connection, request.QueuePosition, success);
            return;
        }
        if (session != null && request.InvestmentSelectAction != null)
        {
            var augmentId = request.InvestmentSelectAction.AugmentId;
            var success = session.SelectInvestment(request.QueuePosition, augmentId);
            if (success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                    manager.BuildInvestmentSelectionSync(request.QueuePosition, augmentId)));
            await SendResponse(connection, request.QueuePosition, success);
            return;
        }
        if (session != null && request.SupplyRerollAction != null)
        {
            var success = session.RerollSupply(request.QueuePosition);
            if (success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSync(
                    GridFightUpdateSrcType.KgridFightSrcNone,
                    includePending: true)));
            await SendResponse(connection, request.QueuePosition, success);
            return;
        }
        if (session != null && request.SupplySelectAction != null)
        {
            var indices = request.SupplySelectAction.SelectSupplyIndexes.ToList();
            var selection = session.SelectSupply(request.QueuePosition, indices);
            if (selection.Success)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                    manager.BuildSupplySelectionSync(request.QueuePosition, indices[0], selection)));
            await SendResponse(connection, request.QueuePosition, selection.Success);
            return;
        }
        if (session != null && await TryHandleSeasonTwoPending(connection, manager, session, request)) return;

        var beforeIds = session?.Roles.Select(role => role.UniqueId).ToHashSet() ?? [];
        var kind = ResolveKind(request);
        var portalId = request.PortalSelectAction?.PortalId ?? 0;
        var pendingHandled = session != null && kind != null &&
                             session.HandlePending(request.QueuePosition, kind.Value, portalId);
        GridFightSessionChange? change = null;
        if (pendingHandled)
        {
            var added = session!.Roles.Where(role => !beforeIds.Contains(role.UniqueId)).ToList();
            change = new GridFightSessionChange(true, added, [], [], []);
        }

        if (pendingHandled && kind == GridFightPendingRequestKind.PortalSelect)
        {
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                session!.Phase == GridFightSessionPhase.SelectingNestedPortal
                    ? manager.BuildBlueSeaSelectionSync(request.QueuePosition)
                    : manager.BuildPortalSelectionSync(request.QueuePosition, change!)));
        }
        else if (pendingHandled && kind == GridFightPendingRequestKind.RoundBegin &&
                 session!.Phase == GridFightSessionPhase.AwaitingInitialOrb)
        {
            var rewards = session.GrantOfficialInitialRoundRewards(out var initialConsumable);
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildInitialRoundSync(request.QueuePosition, rewards, initialConsumable)));
        }
        else if (pendingHandled && kind is GridFightPendingRequestKind.RoundBegin or GridFightPendingRequestKind.ReturnPreparation)
        {
            IReadOnlyList<GridFightTalentOrbReward> rewards = kind == GridFightPendingRequestKind.RoundBegin
                ? session!.TakeTalentOrbRewards(GridFightTalentRewardTrigger.PlaneEnter, session.ChapterId)
                : [];
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                kind == GridFightPendingRequestKind.RoundBegin
                    ? manager.BuildBattleRoundSync(request.QueuePosition, rewards)
                    : manager.BuildPendingAdvanceSync(request.QueuePosition)));
        }
        else if (pendingHandled)
        {
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSync(
                GridFightUpdateSrcType.KgridFightSrcNone,
                change,
                includePending: true)));
        }

        await SendResponse(connection, request.QueuePosition, pendingHandled);
    }

    /// <summary>Season-2 trait prompts; returns false if the request is not one.</summary>
    private static async Task<bool> TryHandleSeasonTwoPending(
        Connection connection,
        GridFightManager manager,
        GridFightSession session,
        GridFightHandlePendingActionCsReq request)
    {
        var queue = request.QueuePosition;
        GridFightSyncUpdateResultScNotify? sync = null;
        switch (request.FGLIODAJFMLCase)
        {
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.GEPGMIEPBMB:
            {
                var finished = session.SelectDanHengTrait(queue, request.GEPGMIEPBMB.KJMFHCDACEK, out var success);
                if (success)
                    sync = manager.BuildSeasonTwoSelectionSync(
                        GridFightSession.DanHengTraitId, GridFightSession.DanHengChooseTraitEffectId, finished);
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.DPKFJEEJABC:
            {
                var finished = session.AppointRanger(queue, request.DPKFJEEJABC.RoleId, out var success);
                if (success)
                    sync = manager.BuildSeasonTwoSelectionSync(
                        GridFightSession.RangerTraitId, GridFightSession.RangerEffectId, finished);
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.PFEBKFJOGKO:
            {
                var finished = session.SelectClaraEnhance(queue, request.PFEBKFJOGKO.HAFOKMHCGFM, out var success);
                if (success)
                    sync = manager.BuildSeasonTwoSelectionSync(
                        GridFightSession.ClaraTraitId, GridFightSession.ClaraEffectId, finished);
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.JMJOAOCJAAG:
            {
                if (request.JMJOAOCJAAG.MLKBOBIEAPL)
                {
                    if (session.RerollChimeraGoods(queue)) sync = manager.BuildSeasonTwoPendingRefreshSync();
                    break;
                }
                var good = request.JMJOAOCJAAG.OBPJLMEMFHA < session.ChimeraGoods.Count
                    ? session.ChimeraGoods[(int)request.JMJOAOCJAAG.OBPJLMEMFHA]
                    : null;
                var application = session.SelectChimeraGood(queue, request.JMJOAOCJAAG.OBPJLMEMFHA, out var finished);
                if (application.Success)
                    // Gold picked in the chimera dialog has its own fly animation (update_source 48).
                    sync = manager.BuildSeasonTwoSelectionSync(
                        GridFightSession.ChimeraTraitId,
                        GridFightSession.ChimeraEffectId,
                        finished,
                        application,
                        gainSource: good?.Type == GridFightSession.ChimeraGoodGold
                            ? GridFightUpdateSrcType.Ldobccjegnf
                            : null);
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.IIDBLCFAAPC:
            {
                // Bonfire node (幕后茶歇): choose an option / rest, then take the rewards and leave.
                var bonfire = request.IIDBLCFAAPC;
                if (bonfire.ECDACDNDHGO != null)
                {
                    var finished = session.LeaveBonfire(queue, out _);
                    if (finished != 0) sync = manager.BuildBonfireLeaveSync(finished);
                    break;
                }
                var choice = bonfire.NCGOOEMOBIC;
                if (choice == null) break;
                if (choice.LGAJNMNOAEM)
                {
                    if (session.HealAtBonfire(queue))
                        sync = manager.BuildBonfireChoiceSync(new GridFightRewardApplication { Success = true, LineupHpChanged = true });
                    break;
                }
                if (choice.ALEGIFEHGLH != null)
                {
                    var gains = session.SelectBonfireOption(queue, choice.ALEGIFEHGLH.EventId, choice.ALEGIFEHGLH.HAFOKMHCGFM);
                    if (gains.Success) sync = manager.BuildBonfireChoiceSync(gains);
                }
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.DKDFDGJGIHM:
            {
                // Spotlight (主角光环) select { spotlight id, role config id }.
                var changes = session.SelectSpotLight(queue, request.DKDFDGJGIHM.IHHNLLAJAGB, request.DKDFDGJGIHM.RoleId,
                    out var finished);
                if (changes != null) sync = manager.BuildSpotLightSync(changes, finished);
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.PBEJCFAJLGE:
                if (session.RerollSpotLight(queue, request.PBEJCFAJLGE.IHHNLLAJAGB, out var paid))
                    sync = manager.BuildSpotLightSync(new GridFightSpotLightChanges { CountersChanged = !paid }, 0, paid);
                break;
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.MHBGJCOIMPA:
            {
                var finished = session.AbandonSpotLightReplace(queue);
                if (finished != 0)
                    sync = manager.BuildSpotLightSync(new GridFightSpotLightChanges { CountersChanged = true }, finished);
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.OKPMJOCEKBC:
            {
                // Ultra block of the 3-star 7-cost Aha (星神★阿哈): the confirm applies both GridFightAhaMaxStar rows
                // (幻月之主, 欢愉之主).
                var finished = session.ConfirmAhaUltra(queue);
                if (finished != 0) sync = manager.BuildSpotLightSync(null, finished);
                break;
            }
            case GridFightHandlePendingActionCsReq.FGLIODAJFMLOneofCase.FKEAHOKGDPO:
                if (session.SetMemberLink(request.FKEAHOKGDPO.MLNHFIKMAMP.ToList()))
                    sync = manager.BuildSeasonTwoSelectionSync(
                        GridFightSession.MemberLinkTraitId, GridFightSession.MemberLinkEffectId, 0);
                break;
            default:
                return false;
        }
        if (sync == null && session.IsFinishedSeasonTwoPending(queue))
        {
            // The client kept the prompt open after the answer was accepted; confirm the finish again
            // instead of failing ("消息获取失败") so it can close.
            var finish = new GridFightSyncResultData();
            finish.UpdateDynamicList.Add(new GridFightSyncData { FinishPendingActionPos = queue });
            finish.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = new GridFightLockInfo() });
            sync = new GridFightSyncUpdateResultScNotify();
            sync.ResultList.Add(finish);
        }
        if (sync != null) await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(sync));
        await SendResponse(connection, queue, sync != null);
        return true;
    }

    private static Task SendResponse(Connection connection, uint queuePosition, bool success)
    {
        return connection.SendPacket(BasePacket.Create(CmdIds.GridFightHandlePendingActionScRsp,
            new GridFightHandlePendingActionScRsp
            {
                Retcode = success ? 0 : (uint)Retcode.RetFail,
                QueuePosition = queuePosition,
            }));
    }

    private static GridFightPendingRequestKind? ResolveKind(GridFightHandlePendingActionCsReq request)
    {
        if (request.PortalRerollAction != null) return GridFightPendingRequestKind.PortalReroll;
        if (request.PortalSelectAction != null) return GridFightPendingRequestKind.PortalSelect;
        if (request.RoundBeginAction != null) return GridFightPendingRequestKind.RoundBegin;
        if (request.ReturnPreparationAction != null) return GridFightPendingRequestKind.ReturnPreparation;
        return null;
    }
}

[Opcode(CmdIds.GridFightUpdateEliteBranchSelectCsReq)]
public sealed class HandlerGridFightUpdateEliteBranchSelectCsReq : GridFightHandler<GridFightUpdateEliteBranchSelectCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightUpdateEliteBranchSelectCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        var success = session != null && session.UpdateEncounterSelection(request.EliteBranchId);
        if (success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildEncounterSelectionUpdateSync(request.EliteBranchId)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightUpdateEliteBranchSelectScRsp,
            new GridFightUpdateEliteBranchSelectScRsp
            {
                Retcode = success ? 0 : (uint)Retcode.RetFail,
            }));
    }
}

[Opcode(CmdIds.GridFightGetOrbCsReq)]
public sealed class HandlerGridFightGetOrbCsReq : GridFightHandler<GridFightGetOrbCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightGetOrbCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        if (session == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.GridFightGetOrbScRsp,
                new GridFightGetOrbScRsp { Retcode = (uint)Retcode.RetFail }));
            return;
        }

        if (!request.TakeAll && request.OrbUniqueIdList.Count == 0)
        {
            var initialSuccess = session.ClaimInitialOrb();
            if (initialSuccess)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSync(
                    GridFightUpdateSrcType.KgridFightSrcUseOrb,
                    includeGold: true,
                    includeShop: true,
                    includePending: true)));
            await connection.SendPacket(BasePacket.Create(CmdIds.GridFightGetOrbScRsp,
                new GridFightGetOrbScRsp { Retcode = initialSuccess ? 0 : (uint)Retcode.RetFail }));
            return;
        }

        var applications = session.OpenOrbs(request.OrbUniqueIdList.ToList(), request.TakeAll);
        foreach (var application in applications)
        {
            await connection.SendPacket(BasePacket.Create(
                CmdIds.GridFightOpenOrbResultScNotify,
                manager.BuildOpenOrbResult(application)));
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildOrbOpenSync(application)));
        }
        var success = applications.Count > 0;
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightGetOrbScRsp,
            new GridFightGetOrbScRsp { Retcode = success ? 0 : (uint)Retcode.RetFail }));
    }
}

[Opcode(CmdIds.GridFightBuyGoodsCsReq)]
public sealed class HandlerGridFightBuyGoodsCsReq : GridFightHandler<GridFightBuyGoodsCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightBuyGoodsCsReq request)
    {
        var manager = player.GridFightManager!;
        var indices = request.BuyAll
            ? manager.Session?.Shop.Where(slot => !slot.SoldOut).Select(slot => slot.Index).ToList() ?? []
            : request.GoodsIndexList.ToList();
        if (manager.Session?.IsSpecialGoodsPurchase(indices) == true)
        {
            var purchases = manager.Session.BuySpecialGoods(indices);
            if (purchases.Count > 0)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                    manager.BuildSpecialGoodsSync(purchases)));
            var specialResponse = new GridFightBuyGoodsScRsp
            {
                Retcode = purchases.Count > 0 ? 0 : (uint)Retcode.RetFail,
            };
            specialResponse.GoodsIndexList.Add(indices);
            await connection.SendPacket(BasePacket.Create(CmdIds.GridFightBuyGoodsScRsp, specialResponse));
            return;
        }
        var acquisition = manager.Session?.BuyGoods(indices) ?? GridFightRoleAcquisitionResult.Failed;
        if (acquisition.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildBuyGoodsSync(indices, acquisition)));
        var response = new GridFightBuyGoodsScRsp { Retcode = acquisition.Success ? 0 : (uint)Retcode.RetFail };
        response.GoodsIndexList.Add(indices);
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightBuyGoodsScRsp, response));
    }
}

[Opcode(CmdIds.GridFightUseHackOptionCsReq)]
public sealed class HandlerGridFightUseHackOptionCsReq : GridFightHandler<GridFightUseHackOptionCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightUseHackOptionCsReq request)
    {
        var manager = player.GridFightManager!;
        var result = manager.Session?.UseHackOption(request.OptionId, request.EnemyHpPercent) ??
                     GridFightHackOptionResult.Failed;
        if (result.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildHackOptionSync(result)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightUseHackOptionScRsp,
            new GridFightUseHackOptionScRsp
            {
                Retcode = result.Success ? 0 : (uint)Retcode.RetFail,
            }));
    }
}

[Opcode(CmdIds.GridFightRefreshShopCsReq)]
public sealed class HandlerGridFightRefreshShopCsReq : GridFightHandler<GridFightRefreshShopCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightRefreshShopCsReq request)
    {
        var manager = player.GridFightManager!;
        var success = manager.Session?.RefreshShop(true) == true;
        if (success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildRefreshShopSync()));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightRefreshShopScRsp,
            new GridFightRefreshShopScRsp { Retcode = success ? 0 : (uint)Retcode.RetFail }));
    }
}

[Opcode(CmdIds.GridFightRecycleRoleCsReq)]
public sealed class HandlerGridFightRecycleRoleCsReq : GridFightHandler<GridFightRecycleRoleCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightRecycleRoleCsReq request)
    {
        var manager = player.GridFightManager!;
        var change = manager.Session?.RecycleRole(request.UniqueId) ?? GridFightSessionChange.Failed;
        if (change.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSync(
                GridFightUpdateSrcType.KgridFightSrcRecycleRole,
                change,
                includeGold: true)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightRecycleRoleScRsp,
            new GridFightRecycleRoleScRsp { Retcode = change.Success ? 0 : (uint)Retcode.RetFail }));
    }
}

[Opcode(CmdIds.GridFightBuyExpCsReq)]
public sealed class HandlerGridFightBuyExpCsReq : GridFightHandler<GridFightBuyExpCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightBuyExpCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        var previousLevel = session?.PlayerLevel ?? 0;
        var success = session?.BuyExp() == true;
        if (success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSync(
                GridFightUpdateSrcType.KgridFightSrcBuyExp,
                includeGold: true,
                includeShop: true,
                includeLevel: true,
                levelChanged: session!.PlayerLevel != previousLevel)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightBuyExpScRsp,
            new GridFightBuyExpScRsp { Retcode = success ? 0 : (uint)Retcode.RetFail }));
    }
}

[Opcode(CmdIds.GridFightUpdatePosCsReq)]
public sealed class HandlerGridFightUpdatePosCsReq : GridFightHandler<GridFightUpdatePosCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightUpdatePosCsReq request)
    {
        var manager = player.GridFightManager!;
        var updates = request.PosInfoList.Select(info => (info.UniqueId, info.Pos)).ToList();
        var change = manager.Session?.UpdatePositions(updates) ?? GridFightSessionChange.Failed;
        if (change.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildPositionSync(change)));
        var response = new GridFightUpdatePosScRsp
        {
            Retcode = change.Success ? 0 : change.FailureRetcode,
        };
        response.PosInfoList.Add(request.PosInfoList);
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightUpdatePosScRsp, response));
    }
}

[Opcode(CmdIds.GridFightUseForgeCsReq)]
public sealed class HandlerGridFightUseForgeCsReq : GridFightHandler<GridFightUseForgeCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightUseForgeCsReq request)
    {
        var manager = player.GridFightManager!;
        var result = manager.Session?.UseForge(request.UniqueId, request.ForgeTargetIndex) ??
                     GridFightForgeUseResult.Failed;
        if (result.Success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildForgeUseSync(result)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightUseForgeScRsp,
            new GridFightUseForgeScRsp { Retcode = result.Success ? 0 : (uint)Retcode.RetFail }));
    }
}

[Opcode(CmdIds.GridFightEnterBattleStageCsReq)]
public sealed class HandlerGridFightEnterBattleStageCsReq : GridFightHandler<GridFightEnterBattleStageCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightEnterBattleStageCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        if (session == null || !session.EnterBattle())
        {
            await connection.SendPacket(new PacketGridFightEnterBattleStageScRsp(null));
            return;
        }
        session.PrepareMortenaxBladeBattleEffects();
        var battle = GridFightBattleModule.StartBattle(player, session, manager.Catalog);
        if (battle == null)
        {
            session.AbortBattleStart();
            await connection.SendPacket(new PacketGridFightEnterBattleStageScRsp(null));
            return;
        }
        await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildBattleEnterSync()));
        await connection.SendPacket(new PacketGridFightEnterBattleStageScRsp(battle.ToProto()));
    }
}

[Opcode(CmdIds.GridFightBackToPrepareCsReq)]
public sealed class HandlerGridFightBackToPrepareCsReq : GridFightHandler<GridFightBackToPrepareCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightBackToPrepareCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        var success = session?.BackToPrepare() == true;
        // Official recurring Fate flow already published the PendingAction immediately after
        // ReturnPreparation. Re-sending the same pending here can reset/close the divination UI.
        if (success && session?.FateDivinerPending == null)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildPreparationSync()));
        // Node-entry prompts (幕后茶歇) are queued before the response. Spotlight (主角光环) is not one of them: the client
        // asks for it with 8508 once its entrance queue has played the spotlight hint.
        if (success && manager.BuildSeasonTwoFollowUpSync() is { } entry)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(entry));

        // 4.5v3 cmd 8680 (LFDIMJCMNDJ) is the empty BackToPrepare response seen in captures.
        await connection.SendPacket(BasePacket.Create(CmdIds.LFDIMJCMNDJ, new LFDIMJCMNDJ()));
    }
}

[Opcode(CmdIds.GridFightLockShopCsReq)]
public sealed class HandlerGridFightShopLockCsReq : GridFightHandler<GridFightLockShopCsReq>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, GridFightLockShopCsReq request)
    {
        var manager = player.GridFightManager!;
        var success = manager.Session?.SetShopLocked(request.IsLock) == true;
        if (success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildShopLockSync()));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightLockShopScRsp, new GridFightLockShopScRsp
        {
            Retcode = success ? 0 : (uint)Retcode.RetFail,
        }));
    }
}
