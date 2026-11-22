using March7thHoney.GameServer.Game.Tutorial;
using March7thHoney.GameServer.Server.Packet.Send.Tutorial;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Tutorial;

[Opcode(CmdIds.UnlockTutorialCsReq)]
public class HandlerUnlockTutorialCsReq : Handler<UnlockTutorialCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, UnlockTutorialCsReq req)
    {
        // Input-lock guard: a tutorial whose finish trigger the server cannot guarantee (EnterBattle-only, an
        // unreachable FinishMainMission, an empty finish list without AutoFinish) is closed on arrival so it never
        // grabs input. Everything else opens and is persisted as UNLOCK.
        var status = TutorialHelper.Unlock(player, (int)req.TutorialId);

        await connection.SendPacket(new PacketUnlockTutorialScRsp(req.TutorialId, status));
    }
}
