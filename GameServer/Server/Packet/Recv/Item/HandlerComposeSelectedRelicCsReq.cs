using March7thHoney.GameServer.Server.Packet.Send.Item;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Item;

[Opcode(CmdIds.ComposeSelectedRelicCsReq)]
public class HandlerComposeSelectedRelicCsReq : Handler<ComposeSelectedRelicCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, ComposeSelectedRelicCsReq req)
    {
        var (retcode, items) = req.ComposeId > int.MaxValue
            ? (Retcode.RetItemConfigNotExist, [])
            : await player.InventoryManager!.ComposeRelic(req);
        if (items.Count == 0)
        {
            await connection.SendPacket(new PacketComposeSelectedRelicScRsp(req.ComposeId,
                retcode == Retcode.RetSucc ? Retcode.RetFail : retcode));
            return;
        }

        await connection.SendPacket(new PacketComposeSelectedRelicScRsp(req.ComposeId, items));
    }
}
