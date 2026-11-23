using March7thHoney.Data.Avatar;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Data;

namespace March7thHoney.GameServer.Server.Packet.Recv.Avatar;

[Opcode(CmdIds.RankUpAvatarCsReq)]
public class HandlerRankUpAvatarCsReq : Handler<RankUpAvatarCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, RankUpAvatarCsReq req)
    {
        if (req.CostData == null || req.CostData.ItemList.Any(cost => cost.PileItem != null &&
                AvatarExRankMaterials.IsMaterial((int)cost.PileItem.ItemId)))
        {
            var rejected = BasePacket.Create(CmdIds.RankUpAvatarScRsp, new RankUpAvatarScRsp { PLDKENJLKME = (uint)Retcode.RetAvatarRankNotMatch });
            await connection.SendPacket(rejected);
            return;
        }
        await player.AvatarManager!.RankUpAvatar((int)req.AvatarId, req.CostData);
        await connection.SendPacket(CmdIds.RankUpAvatarScRsp);
    }
}

// Kept in this existing file so a source-overlay rollback removes the entire new handler.
// Verified 4.6.53: IEBELJFAFDM/HIGLJHLNODG = 400, field 5 = ExRank config ID.
// ENKEIJOLCNO/LPOLGDLAADM = 395, field 2 = retcode. Existing 4.7 definitions match.
[Opcode(CmdIds.IEBELJFAFDM)]
public class HandlerActivateAvatarExRankCsReq : Handler<HIGLJHLNODG>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, HIGLJHLNODG req)
    {
        var result = await player.AvatarManager!.ActivateExRank(unchecked((int)req.OJFIGABCKLG));
        var response = BasePacket.Create(CmdIds.ENKEIJOLCNO, new LPOLGDLAADM { Retcode = (uint)result });
        await connection.SendPacket(response);
    }
}
