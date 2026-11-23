using March7thHoney.GameServer.Server.Packet.Send.MiscModule;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.ElfRestaurant;

// ElfRestaurantModule._OnCmdGetElfRestaurantDataScRsp passes FFKCMKJFFCM to ElfFarmInfo.Sync, which dereferences it;
// the empty-body reply left it null. The official capture ships all five sub-messages present.
[Opcode(CmdIds.GetElfRestaurantDataCsReq)]
public class HandlerGetElfRestaurantDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketPresentEmptyScRsp(CmdIds.GetElfRestaurantDataScRsp,
            new GetElfRestaurantDataScRsp()));
    }
}
