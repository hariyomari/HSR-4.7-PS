using March7thHoney.Enums.Avatar;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Avatar;

[Opcode(CmdIds.SetMultipleAvatarPathsCsReq)]
public class HandlerSetMultipleAvatarPathsCsReq : Handler<SetMultipleAvatarPathsCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, SetMultipleAvatarPathsCsReq req)
    {
        foreach (var targetAvatarType in req.AvatarIdList)
        {
            // Any variant is accepted; Trailblazer ids are normalized to the player's gender inside, and paths the
            // player's character does not own a base for are ignored.
            var avatarId = (int)targetAvatarType;
            await player.AvatarManager!.ChangePathType(avatarId, (MultiPathAvatarTypeEnum)avatarId);
        }

        await connection.SendPacket(CmdIds.SetMultipleAvatarPathsScRsp);
    }
}
