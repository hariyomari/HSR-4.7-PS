using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.TrainParty;

[Opcode(CmdIds.TrainPartyHandlePendingActionCsReq)]
public class HandlerTrainPartyHandlePendingActionCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        var req = TrainPartyHandlePendingActionCsReq.Parser.ParseFrom(data);
        var manager = connection.Player!.TrainPartyManager!;
        var ret = manager.HandlePendingAction(req, out var rspProto);
        rspProto.Retcode = ret;

        var rsp = BasePacket.Create(CmdIds.TrainPartyHandlePendingActionScRsp, rspProto);
        await connection.SendPacket(rsp);

        // Push the resulting deltas (including the next pending action) on the sync channel.
        var sync = manager.BuildGameplaySyncNotify();
        if (sync != null)
        {
            var notify = BasePacket.Create(CmdIds.TrainPartySyncUpdateScNotify, sync);
            await connection.SendPacket(notify);
        }

        // Once the run has settled, follow up with the settle notifies and clear it.
        if (manager.GameplayRun is { Finished: true })
        {
            var settle = BasePacket.Create(CmdIds.TrainPartySettleNotify, manager.BuildSettleNotify());
            await connection.SendPacket(settle);

            var gamePlaySettle = BasePacket.Create(CmdIds.TrainPartyGamePlaySettleNotify, manager.BuildGamePlaySettleNotify());
            await connection.SendPacket(gamePlaySettle);

            manager.ResetGameplay();
        }
    }
}
