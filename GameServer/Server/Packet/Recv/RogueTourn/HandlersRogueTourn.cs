using March7thHoney.Data;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.RogueTourn;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.RogueTourn;

// The Divergent Universe home pull; an unpopulated body NREs the client.
[Opcode(CmdIds.RogueTournQueryCsReq)]
public sealed class HandlerRogueTournQueryCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.RogueTournManager!;
        var rsp = new RogueTournQueryScRsp { LFLCDNGALME = RogueTournProtoBuilder.BuildQueryInfo(manager.Data) };
        if (manager.RunInstance != null) rsp.PHGJIJELKDB = RogueTournProtoBuilder.BuildRunInfo(manager.RunInstance);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournQueryScRsp, rsp));
    }
}

[Opcode(CmdIds.RogueTournStartCsReq)]
public sealed class HandlerRogueTournStartCsReq : Handler<RogueTournStartCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, RogueTournStartCsReq req)
    {
        var manager = player.RogueTournManager!;
        var avatarIds = req.AvatarList.Select(avatar => (int)avatar.Id).ToList();
        var (ret, run) = await manager.StartRun((int)req.AreaId, avatarIds);
        if (ret != Retcode.RetSucc || run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournStartScRsp,
                new RogueTournStartScRsp { Retcode = (uint)ret }));
            return;
        }

        // The run's opening notifies precede the scene change and the scene itself.
        await manager.SendStartPrelude();
        // A new run always builds its scene fresh, even from a previous run's room on the same plane.
        if (!await manager.EnterRoomScene(run.CurRoom(), true))
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournStartScRsp,
                new RogueTournStartScRsp { Retcode = (uint)Retcode.RetFail }));
            return;
        }

        // Official order: level notify, the opening pending action, then the response mirroring it.
        await manager.SendLevelNotify();
        await manager.SendPendingNotify();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournStartScRsp, new RogueTournStartScRsp
        {
            IDCMACCPCBH = RogueTournProtoBuilder.BuildEnterInfo(player),
            PHGJIJELKDB = RogueTournProtoBuilder.BuildRunInfo(run)
        }));
        // The run's rooms register in ONE result notify; the room chooser's ids resolve against it.
        await manager.SendRoomRegistrations();
    }
}

// Resume path: re-enters the room the run stands in and re-sends the live run.
[Opcode(CmdIds.RogueTournEnterCsReq)]
public sealed class HandlerRogueTournEnterCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.RogueTournManager!;
        var run = manager.RunInstance;
        if (run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournEnterScRsp,
                new RogueTournEnterScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        // Without a scene the client has nothing to load and keeps asking before quitting the run.
        if (!RogueTournEntityLoader.IsTournRoomScene(player)) await manager.EnterRoomScene(run.CurRoom());
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournEnterScRsp, new RogueTournEnterScRsp
        {
            IDCMACCPCBH = RogueTournProtoBuilder.BuildEnterInfo(player),
            PHGJIJELKDB = RogueTournProtoBuilder.BuildRunInfo(run)
        }));
    }
}

// The response must carry the lobby scene, or the client keeps the dead run scene and retries forever.
[Opcode(CmdIds.RogueTournLeaveCsReq)]
public sealed class HandlerRogueTournLeaveCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        if (!await player.RogueTournManager!.LeaveRunToLobby())
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournLeaveScRsp,
                new RogueTournLeaveScRsp { Retcode = (uint)Retcode.RetFail }));
            return;
        }

        // Tears the client's tourn maze state down before the entrance scene arrives.
        await connection.SendPacket(new PacketSyncServerSceneChangeNotify());
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournLeaveScRsp,
            new RogueTournLeaveScRsp { IDCMACCPCBH = RogueTournProtoBuilder.BuildEnterInfo(player) }));
    }
}

[Opcode(CmdIds.RogueTournEnterRoomCsReq)]
public sealed class HandlerRogueTournEnterRoomCsReq : Handler<RogueTournEnterRoomCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, RogueTournEnterRoomCsReq req)
    {
        var manager = player.RogueTournManager!;
        var ret = manager.EnterRoom((int)req.KHBLMAOPLLN);
        if (ret == Retcode.RetSucc && manager.RunInstance == null) ret = Retcode.RetRogueStatusNotMatch;
        if (ret != Retcode.RetSucc)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournEnterRoomScRsp,
                new RogueTournEnterRoomScRsp { Retcode = (uint)ret }));
            return;
        }

        await manager.EnterRoomScene(manager.RunInstance!.CurRoom());
        await manager.SendLevelNotify();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournEnterRoomScRsp,
            new RogueTournEnterRoomScRsp { IDCMACCPCBH = RogueTournProtoBuilder.BuildEnterInfo(player) }));
    }
}

[Opcode(CmdIds.RogueTournEnterLayerCsReq)]
public sealed class HandlerRogueTournEnterLayerCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.RogueTournManager!;
        var ret = manager.EnterLayer();
        if (ret == Retcode.RetSucc && manager.RunInstance == null) ret = Retcode.RetRogueStatusNotMatch;
        if (ret != Retcode.RetSucc)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournEnterLayerScRsp,
                new RogueTournEnterLayerScRsp { Retcode = (uint)ret }));
            return;
        }

        await manager.EnterRoomScene(manager.RunInstance!.CurRoom());
        await manager.SendLevelNotify();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournEnterLayerScRsp,
            new RogueTournEnterLayerScRsp { IDCMACCPCBH = RogueTournProtoBuilder.BuildEnterInfo(player) }));
    }
}

/// <summary>
///     RogueTournTriggerRoomSelectCsReq: fired after every room load and again on the far side of an opened door,
///     answered with RogueTournTriggerRoomSelectScRsp. The Persona style state goes out first, then whatever the
///     cleared room still owes (gift, then the chooser); the trigger walks the run only when nothing is pending,
///     finishes the run at its last room, and announces the room's Event talk.
/// </summary>
[Opcode(CmdIds.RogueTournTriggerRoomSelectCsReq)]
public sealed class HandlerRogueTournTriggerRoomSelectCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.RogueTournManager!;
        var run = manager.RunInstance;
        if (run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournTriggerRoomSelectScRsp,
                new RogueTournTriggerRoomSelectScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        await manager.SendActionResults(manager.TakeStyleRows());
        manager.DealClearedRoomAction();

        var moved = false;
        if (run.Pending == null)
        {
            var (ret, next, walked) = manager.SelectNextRoom();
            if (ret != Retcode.RetSucc)
            {
                await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournTriggerRoomSelectScRsp,
                    new RogueTournTriggerRoomSelectScRsp { Retcode = (uint)ret }));
                return;
            }

            moved = walked;
            if (walked && next != null)
            {
                await manager.SendActionResults(manager.TakeStyleRows());
                if (await manager.EnterRoomScene(next))
                {
                    await manager.PushSceneChange();
                    await manager.SendSceneResult();
                }
            }
        }

        // The last room of the last layer: the run finishes and is pushed three times, opening the settle page.
        if (!moved) await manager.FinishRunIfLastRoom();

        await manager.SendLevelNotify();
        await manager.SendPendingNotify();
        await manager.SendDialogueSnapshot();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournTriggerRoomSelectScRsp,
            new RogueTournTriggerRoomSelectScRsp()));
    }
}

[Opcode(CmdIds.RogueTournGetPermanentTalentInfoCsReq)]
public sealed class HandlerRogueTournGetPermanentTalentInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetPermanentTalentInfoScRsp,
            new RogueTournGetPermanentTalentInfoScRsp
            {
                DPABCOHLPLH = RogueTournProtoBuilder.BuildPermanentTalentInfo(player.RogueTournManager!.Data)
            }));
    }
}

[Opcode(CmdIds.RogueTournGetSeasonTalentInfoCsReq)]
public sealed class HandlerRogueTournGetSeasonTalentInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetSeasonTalentInfoScRsp,
            new RogueTournGetSeasonTalentInfoScRsp
            {
                HANICMMHJGI = RogueTournProtoBuilder.BuildSeasonTalentInfo(player.RogueTournManager!.Data)
            }));
    }
}

[Opcode(CmdIds.RogueTournGetAllArchiveCsReq)]
public sealed class HandlerRogueTournGetAllArchiveCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var rsp = new RogueTournGetAllArchiveScRsp();
        foreach (var entry in player.RogueTournManager!.Data.Archives)
            rsp.MCGGPONOCKC.Add(RogueTournProtoBuilder.BuildArchive(entry));
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetAllArchiveScRsp, rsp));
    }
}

// No collection tracker yet: both catalogue lists stay empty (a fresh account's shape).
[Opcode(CmdIds.RogueTournGetArchiveRepositoryCsReq)]
public sealed class HandlerRogueTournGetArchiveRepositoryCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetArchiveRepositoryScRsp,
            new RogueTournGetArchiveRepositoryScRsp()));
    }
}

// The official capture answers an empty list; build-ref sharing is partly an HTTP feature.
[Opcode(CmdIds.RogueTournGetAllBuildRefCsReq)]
public sealed class HandlerRogueTournGetAllBuildRefCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetAllBuildRefScRsp,
            new RogueTournGetAllBuildRefScRsp()));
    }
}

[Opcode(CmdIds.RogueTournGetMiscRealTimeDataCsReq)]
public sealed class HandlerRogueTournGetMiscRealTimeDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var rsp = new RogueTournGetMiscRealTimeDataScRsp();
        RogueTournProtoBuilder.FillMiscRealTimeData(rsp);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetMiscRealTimeDataScRsp, rsp));
    }
}

// The official answers {} when the player has no cocoon run.
[Opcode(CmdIds.RogueTournGetCurRogueCocoonInfoCsReq)]
public sealed class HandlerRogueTournGetCurRogueCocoonInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetCurRogueCocoonInfoScRsp,
            new RogueTournGetCurRogueCocoonInfoScRsp()));
    }
}

/// <summary>
///     Boss Rush entry. No cocoon run is modelled, so it must answer a FAILURE: an empty response is success with no
///     scene, which the client waits on forever. A refused entry releases its UI instead.
/// </summary>
[Opcode(CmdIds.RogueTournEnterRogueCocoonSceneCsReq)]
public sealed class HandlerRogueTournEnterRogueCocoonSceneCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournEnterRogueCocoonSceneScRsp,
            new RogueTournEnterRogueCocoonSceneScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
    }
}

// The settle page, asked after the run was marked Finished: live counters, score/exp and the run snapshot.
[Opcode(CmdIds.RogueTournGetSettleInfoCsReq)]
public sealed class HandlerRogueTournGetSettleInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.RogueTournManager!;
        var run = manager.RunInstance;
        if (run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetSettleInfoScRsp,
                new RogueTournGetSettleInfoScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        var (score, gain, total) = manager.SettleScore();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournGetSettleInfoScRsp,
            new RogueTournGetSettleInfoScRsp
            {
                BFLLJJBHLDF = RogueTournProtoBuilder.BuildSettlePayload(run, score, gain, total)
            }));
    }
}

/// <summary>
///     The client's own settle path: the settle page plus the scene pair. It deliberately does NOT settle the run —
///     the reward and the archive belong to RogueTournConfirmSettleCsReq, or a confirm would grant twice.
/// </summary>
[Opcode(CmdIds.RogueTournSettleCsReq)]
public sealed class HandlerRogueTournSettleCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.RogueTournManager!;
        var run = manager.RunInstance;
        if (run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournSettleScRsp,
                new RogueTournSettleScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        var (score, gain, total) = manager.SettleScore();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournSettleScRsp, new RogueTournSettleScRsp
        {
            BFLLJJBHLDF = RogueTournProtoBuilder.BuildSettlePayload(run, score, gain, total),
            IDCMACCPCBH = RogueTournProtoBuilder.BuildEnterInfo(player)
        }));
    }
}

/// <summary>
///     The settle confirmation {area_id, name, slot}: the run settles into its archive slot and grants its season
///     exp, the Settled status is pushed twice, the exp lands, and the player returns to the Divergent Universe map
///     entrance with the archive row just written.
/// </summary>
[Opcode(CmdIds.RogueTournConfirmSettleCsReq)]
public sealed class HandlerRogueTournConfirmSettleCsReq : Handler<RogueTournConfirmSettleCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player,
        RogueTournConfirmSettleCsReq req)
    {
        var manager = player.RogueTournManager!;
        var slot = manager.SettleRun(req.Name, (int)req.NPHAJEKCBKF);
        if (slot == 0)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournConfirmSettleScRsp,
                new RogueTournConfirmSettleScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        await manager.SendLevelNotify(false);
        await manager.SendLevelNotify(false);
        if (manager.Data.Exp > 0)
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournExpNotify,
                new RogueTournExpNotify { Exp = (uint)manager.Data.Exp }));

        if (!await manager.LeaveSettledRun())
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournConfirmSettleScRsp,
                new RogueTournConfirmSettleScRsp { Retcode = (uint)Retcode.RetFail }));
            return;
        }

        var rsp = new RogueTournConfirmSettleScRsp { IDCMACCPCBH = RogueTournProtoBuilder.BuildEnterInfo(player) };
        if (manager.ArchiveOf(slot) is { } archive) rsp.BLPDBPHMCGA = RogueTournProtoBuilder.BuildArchive(archive);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournConfirmSettleScRsp, rsp));
    }
}

// The Divergent Universe level ladder: RogueTournExpReward(MainTournID, Level) pays RewardData items.
[Opcode(CmdIds.RogueTournTakeExpRewardCsReq)]
public sealed class HandlerRogueTournTakeExpRewardCsReq : Handler<RogueTournTakeExpRewardCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player,
        RogueTournTakeExpRewardCsReq req)
    {
        var manager = player.RogueTournManager!;
        var (levels, items) = await manager.TakeExpRewards((int)req.FABMHMFNMDB, req.HBGAFAFFFAO.Select(level => (int)level));
        var rsp = new RogueTournTakeExpRewardScRsp { Exp = (uint)manager.Data.Exp, Reward = new ItemList() };
        rsp.FKDNANDJLGK.AddRange(levels.Select(level => (uint)level));
        foreach (var item in items) rsp.Reward.ItemList_.Add(item.ToProto());
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueTournTakeExpRewardScRsp, rsp));
    }
}

// The Workbench of Creation's state, asked before every use: the bench's function list, each in its fresh state.
[Opcode(CmdIds.RogueWorkbenchGetInfoCsReq)]
public sealed class HandlerRogueWorkbenchGetInfoCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var rsp = new RogueWorkbenchGetInfoScRsp();
        if (GameData.RogueTournWorkbenchData.TryGetValue(RogueTournProtoBuilder.DefaultWorkbenchId, out var bench))
            foreach (var funcId in bench.FuncList)
                rsp.NJAPFMHHPPA[(uint)funcId] = WorkbenchEntry();
        player.RogueTournManager?.DiscoverWorkbench(RogueTournProtoBuilder.DefaultWorkbenchId);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueWorkbenchGetInfoScRsp, rsp));
    }

    // The official's first GetInfo of a run already reports {DMGJDGJMCJL:1, CLNDMEODHJE:1} for every function.
    public static BCOEGNBGJFC WorkbenchEntry()
    {
        return new BCOEGNBGJFC { KDFGAKDCMIK = new LPODFEEGDOB { DMGJDGJMCJL = 1, CLNDMEODHJE = 1 } };
    }
}

// One workbench function invoked; the enhancement effect itself is not modelled, only the answer.
[Opcode(CmdIds.RogueWorkbenchHandleFuncCsReq)]
public sealed class HandlerRogueWorkbenchHandleFuncCsReq : Handler<RogueWorkbenchHandleFuncCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player,
        RogueWorkbenchHandleFuncCsReq req)
    {
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueWorkbenchHandleFuncScRsp,
            new RogueWorkbenchHandleFuncScRsp
            {
                FuncId = req.FuncId,
                JNPALDKNHKM = HandlerRogueWorkbenchGetInfoCsReq.WorkbenchEntry()
            }));
    }
}
