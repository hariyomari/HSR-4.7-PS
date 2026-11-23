using March7thHoney.Data;
using March7thHoney.Enums.Avatar;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Player;

[Opcode(CmdIds.SetAvatarPathCsReq)]
public class HandlerSetAvatarPathCsReq : Handler<SetAvatarPathCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, SetAvatarPathCsReq req)
    {
        GameData.MultiplePathAvatarConfigData.TryGetValue((int)req.AvatarId, out var avatar);

        // ChangePathType normalizes Trailblazer variants to the player's gender. Echo the id actually stored:
        // echoing the request contradicts the avatar sync and the path panel never marks the chosen row.
        var storedId = avatar == null
            ? 0
            : await player.AvatarManager!.ChangePathType(avatar.BaseAvatarID, (MultiPathAvatarTypeEnum)avatar.AvatarID);

        if (storedId != 0)
            await connection.SendPacket(new PacketSetAvatarPathScRsp(storedId));
        else
            await connection.SendPacket(CmdIds.SetAvatarPathScRsp);
    }
}
