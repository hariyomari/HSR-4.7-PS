using March7thHoney.Kcp;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.Packet.Recv.Player;

[Opcode(CmdIds.PlayerLogoutCsReq)]
public class HandlerPlayerLogoutCsReq : PlayerHandler
{
    private readonly Logger _logger = new("GameServer");

    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        var uid = player?.Uid.ToString() ?? "<none>";
        _logger.Debug($"Player logout requested. remote={connection.RemoteEndPoint} uid={uid}");

        // TODO: the connection is closed without March7thHoneyListener.SendDisconnectPacket. That call was dropped while
        // the server ran over TCP; decide whether a KCP client should get the disconnect packet again.
        await Task.Delay(50);
        connection.Stop();
    }
}
