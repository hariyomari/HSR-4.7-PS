using March7thHoney.GameServer.Server.Packet.Send.MiscModule;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.ChenLingBattle;

// ChenLingBattleModule._OnChenLingGetDataScRsp passes DJOLGKGKCHE to _OnStageProgressChange, which reads its stage
// id; the empty-body reply left it null. The official capture ships it as stage 1, unlocked, next to KFAIGIIPLOF.
[Opcode(CmdIds.ChenLingGetDataCsReq)]
public class HandlerChenLingGetDataCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await connection.SendPacket(new PacketPresentEmptyScRsp(CmdIds.ChenLingGetDataScRsp, new ChenLingGetDataScRsp
        {
            DJOLGKGKCHE = new FDDNKPJEMJE { StageId = 1, IsUnlock = true }
        }));
    }
}
