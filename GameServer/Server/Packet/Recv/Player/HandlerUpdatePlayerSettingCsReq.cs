using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Player;

[Opcode(CmdIds.UpdatePlayerSettingCsReq)]
public class HandlerUpdatePlayerSettingCsReq : Handler<UpdatePlayerSettingCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, UpdatePlayerSettingCsReq req)
    {
        // Head-frame display switch (oneof 1600, true = hidden). The client applies the echoed value on the rsp.
        var setting = req.PlayerSetting;
        if (setting is { BKAGLMFNKKACase: UpdatePlayerSetting.BKAGLMFNKKAOneofCase.BOMGNMDOGLM })
            HeadFrameService.SetHidden(player, setting.BOMGNMDOGLM);

        await connection.SendPacket(new PacketUpdatePlayerSettingScRsp(req.PlayerSetting));
    }
}
