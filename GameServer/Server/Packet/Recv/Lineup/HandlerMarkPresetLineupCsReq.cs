using March7thHoney.GameServer.Server.Packet.Send.Lineup;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Lineup;

/// <summary>
///     Marks a lineup as favourite. The reply is cmd 773 in 4.6 and cmd 780 <see cref="SetTeamFavourite" /> in 4.7
///     (its fields index / _is_favourite / retcode mirror the request one to one; yuukisr_go 6bb4fd1 named it
///     MarkPresetLineupScRsp). That reply name cannot be derived from the request name, so without this handler the
///     client gets no reply at all.
/// </summary>
[Opcode(CmdIds.MarkPresetLineupCsReq)]
public class HandlerMarkPresetLineupCsReq : Handler<MarkPresetLineupCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, MarkPresetLineupCsReq req)
    {
        var ok = player.LineupManager!.SetLineupFavourite((int)req.Index, req.IsFavourite);
        await connection.SendPacket(new PacketSetTeamFavourite(req.Index, req.IsFavourite,
            ok ? Retcode.RetSucc : Retcode.RetLineupNotExist));
    }
}
