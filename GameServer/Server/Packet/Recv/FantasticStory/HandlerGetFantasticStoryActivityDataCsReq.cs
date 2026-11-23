using March7thHoney.GameServer.Server.Packet.Send.MiscModule;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.FantasticStory;

// FantasticStoryActivityModule._OnGetFantasticStoryActivityDataScRsp reads ONIEMLICONB straight away; the empty-body
// reply left it null. The official capture ships it present with OFHBPNGDEGO = 1.
[Opcode(CmdIds.GetFantasticStoryActivityDataCsReq)]
public class HandlerGetFantasticStoryActivityDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketPresentEmptyScRsp(CmdIds.GetFantasticStoryActivityDataScRsp,
            new GetFantasticStoryActivityDataScRsp { ONIEMLICONB = new GCFDJCPFOGG { OFHBPNGDEGO = 1 } }));
    }
}
