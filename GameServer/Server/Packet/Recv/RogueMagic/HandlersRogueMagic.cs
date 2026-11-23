using March7thHoney.Enums.RogueMagic;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.RogueMagic;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.RogueMagic;

[Opcode(CmdIds.RogueMagicQueryCsReq)]
public sealed class HandlerRogueMagicQueryCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var rsp = new RogueMagicQueryScRsp { LFLCDNGALME = RogueMagicProtoBuilder.BuildQueryInfo() };
        var run = player.RogueMagicManager!.RunInstance;
        if (run != null) rsp.PHGJIJELKDB = RogueMagicProtoBuilder.BuildCurInfo(run);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicQueryScRsp, rsp));
    }
}

// OCEGEKKFLAL (repeated) carries the difficulty modifiers, IEOFONJHFDJ the style type.
[Opcode(CmdIds.RogueMagicStartCsReq)]
public sealed class HandlerRogueMagicStartCsReq : Handler<RogueMagicStartCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, RogueMagicStartCsReq req)
    {
        var manager = player.RogueMagicManager!;
        var (ret, run) = await manager.StartRun((int)req.AreaId, (int)req.IEOFONJHFDJ,
            req.OCEGEKKFLAL.Select(id => (int)id).ToList(), req.NOEPBNMJFGL.Select(id => (int)id).ToList());
        if (ret == Retcode.RetSucc && run != null && !await manager.EnterRoomScene(run.CurRoom(), false))
        {
            await manager.LeaveRun(false);
            ret = Retcode.RetFail;
        }

        if (ret != Retcode.RetSucc || run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicStartScRsp,
                new RogueMagicStartScRsp { Retcode = (uint)ret }));
            return;
        }

        await manager.SendLevelNotify();
        await manager.SendPendingNotify();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicStartScRsp, new RogueMagicStartScRsp
        {
            IDCMACCPCBH = RogueMagicProtoBuilder.BuildEnterInfo(player),
            PHGJIJELKDB = RogueMagicProtoBuilder.BuildCurInfo(run)
        }));
    }
}

// Resume path: re-sends the live run.
[Opcode(CmdIds.RogueMagicEnterCsReq)]
public sealed class HandlerRogueMagicEnterCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var run = player.RogueMagicManager!.RunInstance;
        var rsp = run == null
            ? new RogueMagicEnterScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }
            : new RogueMagicEnterScRsp
            {
                PHGJIJELKDB = RogueMagicProtoBuilder.BuildCurInfo(run),
                IDCMACCPCBH = RogueMagicProtoBuilder.BuildEnterInfo(player)
            };
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicEnterScRsp, rsp));
    }
}

// JOJCPKNIJOH is the room being left; KHBLMAOPLLN the door's room type.
[Opcode(CmdIds.RogueMagicEnterRoomCsReq)]
public sealed class HandlerRogueMagicEnterRoomCsReq : Handler<RogueMagicEnterRoomCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, RogueMagicEnterRoomCsReq req)
    {
        var manager = player.RogueMagicManager!;
        var roomIndex = (int)req.JOJCPKNIJOH + 1;
        var ret = manager.EnterRoom(roomIndex, (RogueMagicRoomTypeEnum)req.KHBLMAOPLLN);
        if (ret == Retcode.RetSucc && !await manager.EnterRoomScene(manager.RunInstance!.CurRoom(), false))
            ret = Retcode.RetFail;
        if (ret != Retcode.RetSucc)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicEnterRoomScRsp,
                new RogueMagicEnterRoomScRsp { Retcode = (uint)ret }));
            return;
        }

        await manager.SendLevelNotify([roomIndex, roomIndex + 1]);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicEnterRoomScRsp,
            new RogueMagicEnterRoomScRsp { IDCMACCPCBH = RogueMagicProtoBuilder.BuildEnterInfo(player) }));
        await connection.SendPacket(new PacketEnterSceneByServerScNotify(player.SceneInstance!));
    }
}

[Opcode(CmdIds.RogueMagicEnterLayerCsReq)]
public sealed class HandlerRogueMagicEnterLayerCsReq : Handler<RogueMagicEnterLayerCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, RogueMagicEnterLayerCsReq req)
    {
        var manager = player.RogueMagicManager!;
        var ret = manager.EnterLayer((RogueMagicRoomTypeEnum)req.KHBLMAOPLLN);
        if (ret == Retcode.RetSucc && !await manager.EnterRoomScene(manager.RunInstance!.CurRoom(), false))
            ret = Retcode.RetFail;
        if (ret != Retcode.RetSucc)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicEnterLayerScRsp,
                new RogueMagicEnterLayerScRsp { Retcode = (uint)ret }));
            return;
        }

        await manager.SendLevelNotify();
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicEnterLayerScRsp,
            new RogueMagicEnterLayerScRsp { IDCMACCPCBH = RogueMagicProtoBuilder.BuildEnterInfo(player) }));
    }
}

// The response must carry the lobby scene, or the client keeps the dead run scene.
[Opcode(CmdIds.RogueMagicLeaveCsReq)]
public sealed class HandlerRogueMagicLeaveCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        if (!await player.RogueMagicManager!.LeaveRunToLobby())
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicLeaveScRsp,
                new RogueMagicLeaveScRsp { Retcode = (uint)Retcode.RetFail }));
            return;
        }

        await connection.SendPacket(new PacketSyncServerSceneChangeNotify());
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicLeaveScRsp,
            new RogueMagicLeaveScRsp { IDCMACCPCBH = RogueMagicProtoBuilder.BuildEnterInfo(player) }));
    }
}

// Settle ends the run and returns to the lobby; the finish info carries the run as it ended.
[Opcode(CmdIds.RogueMagicSettleCsReq)]
public sealed class HandlerRogueMagicSettleCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var manager = player.RogueMagicManager!;
        var run = manager.RunInstance;
        if (run == null)
        {
            await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicSettleScRsp,
                new RogueMagicSettleScRsp { Retcode = (uint)Retcode.RetRogueStatusNotMatch }));
            return;
        }

        var finish = new FKFOIDMNDFG
        {
            PHGJIJELKDB = RogueMagicProtoBuilder.BuildCurInfo(run),
            KDDGKLNGNEA = new EJHDAJAGFIJ(),
            BNLLEJPCIGD = new NOAKCDKAAFB()
        };

        await manager.LeaveRunToLobby();
        await connection.SendPacket(new PacketSyncServerSceneChangeNotify());
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicSettleScRsp, new RogueMagicSettleScRsp
        {
            IDCMACCPCBH = RogueMagicProtoBuilder.BuildEnterInfo(player),
            BFLLJJBHLDF = finish
        }));
    }
}

[Opcode(CmdIds.RogueMagicGetMiscRealTimeDataCsReq)]
public sealed class HandlerRogueMagicGetMiscRealTimeDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var rsp = new RogueMagicGetMiscRealTimeDataScRsp();
        RogueMagicProtoBuilder.FillMiscRealTimeData(rsp);
        await connection.SendPacket(BasePacket.Create(CmdIds.RogueMagicGetMiscRealTimeDataScRsp, rsp));
    }
}
