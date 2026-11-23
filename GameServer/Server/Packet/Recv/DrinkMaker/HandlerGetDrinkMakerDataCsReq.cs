using March7thHoney.GameServer.Server.Packet.Send.DrinkMaker;
using March7thHoney.Kcp;

namespace March7thHoney.GameServer.Server.Packet.Recv.DrinkMaker;

[Opcode(CmdIds.GetDrinkMakerDataCsReq)]
public class HandlerGetDrinkMakerDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketGetDrinkMakerDataScRsp());
    }
}
