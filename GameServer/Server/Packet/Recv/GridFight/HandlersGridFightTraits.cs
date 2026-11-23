using Google.Protobuf;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.GridFight;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.GridFight;

/// <summary>
///     GridFight request handler that, after the request itself, pushes whatever season-2 trait state the request
///     changed indirectly (Tenglong summon, template equipment, effect snapshots, trait prompts).
/// </summary>
public abstract class GridFightHandler<TReq> : Handler<TReq> where TReq : IMessage<TReq>, new()
{
    protected sealed override async Task OnHandle(Connection connection, PlayerInstance player, TReq req)
    {
        var previousSession = player.GridFightManager?.Session;
        var previousInterestCap = previousSession?.CurrentMaxInterestGold;
        try
        {
            if (player.GridFightManager?.BuildOwlbertGoldTick() is { } gold)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(gold));
            await OnGridFightHandle(connection, player, req);
            if (player.GridFightManager?.BuildSeasonTwoFollowUpSync() is { } followUp)
                await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(followUp));
            if (player.GridFightManager?.BuildInterestDisplayRefresh(previousSession, previousInterestCap) is { } refresh)
                await connection.SendPacket(BasePacket.Create(CmdIds.GridFightGetDataScRsp, refresh));
        }
        finally
        {
            // Whatever the request changed (even when it failed half-way) is the state the run continues from.
            player.GridFightManager?.Persist();
        }
    }

    protected abstract Task OnGridFightHandle(Connection connection, PlayerInstance player, TReq req);
}

/// <summary>
///     Season-2 trait interactions the client starts itself (GridFightTraitUpdateCsReq 8657). The client blocks its
///     input until GridFightTraitUpdateScRsp arrives, so every request is answered; state goes in the sync first.
/// </summary>
[Opcode(CmdIds.GridFightTraitUpdateCsReq)]
public sealed class HandlerGridFightTraitUpdateCsReq : GridFightHandler<GridFightTraitUpdateCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightTraitUpdateCsReq request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        var sync = session == null ? null : Handle(manager, session, request);
        if (sync != null) await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(sync));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightTraitUpdateScRsp, new GridFightTraitUpdateScRsp
        {
            Retcode = sync != null ? 0 : (uint)Retcode.RetFail,
        }));
    }

    private static GridFightSyncUpdateResultScNotify? Handle(
        GridFightManager manager,
        GridFightSession session,
        GridFightTraitUpdateCsReq request)
    {
        var traitId = request.TraitId;
        var effectId = request.EffectId;
        switch (request.NEBNJDMFELOCase)
        {
            case GridFightTraitUpdateCsReq.NEBNJDMFELOOneofCase.GDAMHENIIDJ:
            {
                // Jade's shop (黑市主理人): buy a slot or refresh the rotating goods.
                if (request.GDAMHENIIDJ.AHAIBDAJAIB)
                {
                    if (!session.RefreshJadeGoods(out var goldChanged)) return null;
                    var refresh = new GridFightRewardApplication { Success = true, GoldChanged = goldChanged };
                    return manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0, refresh, includeShop: true);
                }
                var purchase = session.BuyJadeGood(request.GDAMHENIIDJ.EAKAGIKFENM);
                return purchase.Success ? manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0, purchase, includeShop: true) : null;
            }
            case GridFightTraitUpdateCsReq.NEBNJDMFELOOneofCase.KDBKIPPHMFI:
            {
                var purchase = session.BuyTrailblazeGood(request.KDBKIPPHMFI.Pos);
                if (!purchase.Success) return null;
                var sync = manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0, purchase);
                manager.AppendPamKingUnlock(sync);
                return sync;
            }
            case GridFightTraitUpdateCsReq.NEBNJDMFELOOneofCase.MCCMPFENGCD:
            {
                var change = session.ConsumeRoleForPhainon(request.MCCMPFENGCD.MDNAGANGNKK, out _);
                return change.Success ? manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0, change: change) : null;
            }
            case GridFightTraitUpdateCsReq.NEBNJDMFELOOneofCase.GEPGMIEPBMB:
                session.SelectDanHengTrait(0, request.GEPGMIEPBMB.KJMFHCDACEK, out var chosen);
                return chosen ? manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0) : null;
            case GridFightTraitUpdateCsReq.NEBNJDMFELOOneofCase.DPKFJEEJABC:
                session.AppointRanger(0, request.DPKFJEEJABC.RoleId, out var appointed);
                return appointed ? manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0) : null;
            case GridFightTraitUpdateCsReq.NEBNJDMFELOOneofCase.FLDEMEJEODJ:
                return session.SetMemberLink(request.FLDEMEJEODJ.LKNGKJCFFBH.ToList())
                    ? manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0)
                    : null;
            case GridFightTraitUpdateCsReq.NEBNJDMFELOOneofCase.IFDICIIOHHI
                when request.IFDICIIOHHI.HCDIBHEDDEC != null:
            {
                // Elation (欢愉): drag equipment onto Aha. (Aha consumables, HJJPILGEFKK, are not implemented.)
                var dress = session.DressElationEquipment(request.IFDICIIOHHI.HCDIBHEDDEC.AMIFDFAHMDM, out var consumed);
                return dress.Success
                    ? manager.BuildSeasonTwoSelectionSync(traitId, effectId, 0, dress, removedEquipment: consumed)
                    : null;
            }
            default:
                return null;
        }
    }
}

/// <summary>
///     The client asks to enter a bonfire (幕后茶歇) node (8559, sent from Lua). The bonfire pending action is pushed on
///     node entry already; resend it here. The 4.7 protocol has no matching response command, so none is sent.
/// </summary>
[Opcode(CmdIds.GridFightEnterBonfireCsReq)]
public sealed class HandlerGridFightEnterBonfireCsReq : GridFightHandler<GridFightEnterBonfireCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightEnterBonfireCsReq request)
    {
        var manager = player.GridFightManager!;
        if (manager.Session?.SeasonTwoPending?.Kind == GridFightSeasonTwoPendingKind.Bonfire)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSeasonTwoPendingRefreshSync()));
    }
}

/// <summary>
///     GridFightSpotLightEnterCsReq (8508): sent by the HUD and by the battle button while the current node still has
///     HasSpotLight. Opens the node's due selection (or re-pushes an open one); with none due, settles the node so the
///     client clears the flag.
/// </summary>
[Opcode(CmdIds.NLMAAHDFLGC)]
public sealed class HandlerGridFightSpotLightEnterCsReq : GridFightHandler<NLMAAHDFLGC>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, NLMAAHDFLGC request)
    {
        var manager = player.GridFightManager!;
        var session = manager.Session;
        var open = session?.SeasonTwoPending?.Kind == GridFightSeasonTwoPendingKind.SpotLightSelect ||
                   session?.StartSpotLightFromEnter() == true;
        var settled = open ? null : session?.ResolveStaleSpotLightNode();
        if (open || settled != null)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(manager.BuildSpotLightSync(settled, 0)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightSpotLightEnterScRsp,
            new GridFightSpotLightEnterScRsp { Retcode = open || settled != null ? 0 : (uint)Retcode.RetFail }));
    }
}

/// <summary>GridFightSpotLightReplaceCsReq (8740 { unique_id }): spend a replace charge and reselect that spotlight.</summary>
[Opcode(CmdIds.LLIKPBNDNAG)]
public sealed class HandlerGridFightSpotLightReplaceCsReq : GridFightHandler<LLIKPBNDNAG>
{
    protected override async Task OnGridFightHandle(Connection connection, PlayerInstance player, LLIKPBNDNAG request)
    {
        var manager = player.GridFightManager!;
        var success = manager.Session?.StartSpotLightReplace(request.UniqueId) == true;
        if (success)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildSpotLightSync(new GridFightSpotLightChanges { CountersChanged = true }, 0)));
        await connection.SendPacket(BasePacket.Create(CmdIds.GridFightSpotLightReplaceScRsp,
            new GridFightSpotLightReplaceScRsp { Retcode = success ? 0 : (uint)Retcode.RetFail }));
    }
}

/// <summary>GridFightUpdateSpotLightTrackCsReq (8636): spotlight tracking is UI-only; acknowledge (reply 8727).</summary>
[Opcode(CmdIds.GridFightUpdateSpotLightTrackCsReq)]
public sealed class HandlerGridFightUpdateSpotLightTrackCsReq : GridFightHandler<GridFightUpdateSpotLightTrackCsReq>
{
    protected override Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightUpdateSpotLightTrackCsReq request)
    {
        return connection.SendPacket(BasePacket.Create(CmdIds.OCNEDLGLLEA, new OCNEDLGLLEA()));
    }
}
