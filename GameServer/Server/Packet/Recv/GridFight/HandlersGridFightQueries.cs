using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Server.Packet.Send.GridFight;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.GridFight;

[Opcode(CmdIds.GridFightGetFormationCsReq)]
public sealed class HandlerGridFightGetFormationCsReq : PlayerHandler
{
    protected override Task OnHandle(Connection connection, PlayerInstance player)
    {
        return connection.SendPacket(BasePacket.Create(
            CmdIds.GetGameFormationScRsp,
            new GetGameFormationScRsp()));
    }
}

[Opcode(CmdIds.GridFightFinishTutorialCsReq)]
public sealed class HandlerGridFightFinishTutorialCsReq : PlayerHandler
{
    protected override Task OnHandle(Connection connection, PlayerInstance player)
    {
        return Task.CompletedTask;
    }
}

[Opcode(CmdIds.GridFightTrackTraitCsReq)]
public sealed class HandlerGridFightTraitTrackCsReq : GridFightHandler<GridFightTrackTraitCsReq>
{
    protected override async Task OnGridFightHandle(
        Connection connection,
        PlayerInstance player,
        GridFightTrackTraitCsReq request)
    {
        var manager = player.GridFightManager!;
        var retcode = manager.Session?.UpdateTraitTrack(request.COPNJMBGNDO, request.BJDOINAHDLN) ??
                      (uint)Retcode.RetFail;
        if (retcode == 0)
            await connection.SendPacket(new PacketGridFightSyncUpdateResultScNotify(
                manager.BuildTraitTrackSync()));
        await connection.SendPacket(BasePacket.Create(
            CmdIds.GridFightTrackTraitScRsp,
            new GridFightTrackTraitScRsp { Retcode = retcode }));
    }
}
