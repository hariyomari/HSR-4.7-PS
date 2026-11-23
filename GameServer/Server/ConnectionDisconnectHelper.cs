using March7thHoney.Database.Account;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server;

public static class ConnectionDisconnectHelper
{
    private static readonly Logger Logger = new("GameServer");
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    // PlayerSqueezedScNotify kick types. The proto enum names are obfuscated, so the values the client expects for
    // each kick are named here.
    private const JNFDGPDHENJ GmKickType = (JNFDGPDHENJ)5;
    private const JNFDGPDHENJ BannedKickType = (JNFDGPDHENJ)1;

    public static void KickByGm(Connection? connection)
    {
        _ = Task.Run(() => KickByGmAsync(connection));
    }

    public static Task KickByGmAsync(Connection? connection)
    {
        return DisconnectAsync(connection,
            new PacketPlayerSqueezedScNotify(GmKickType));
    }

    public static void KickBanned(Connection? connection, AccountBanStatus banStatus)
    {
        _ = Task.Run(() => KickBannedAsync(connection, banStatus));
    }

    public static Task KickBannedAsync(Connection? connection, AccountBanStatus banStatus)
    {
        return DisconnectAsync(connection, new PacketPlayerSqueezedScNotify(
            BannedKickType,
            AccountBanHelper.ToBlackInfo(banStatus)));
    }

    private static async Task DisconnectAsync(Connection? connection, BasePacket packet)
    {
        if (connection == null)
            return;

        try
        {
            var sendTask = connection.SendPacket(packet);
            var completedTask = await Task.WhenAny(sendTask, Task.Delay(SendTimeout));
            if (completedTask == sendTask)
                await sendTask;
            else
                Logger.Debug($"Timed out sending disconnect packet to {connection.RemoteEndPoint}.");
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to send disconnect packet to {connection.RemoteEndPoint}.", ex);
        }
        finally
        {
            await StopWithTimeout(connection);
        }
    }

    private static async Task StopWithTimeout(Connection connection)
    {
        try
        {
            var stopTask = Task.Run(connection.Stop);
            var completedTask = await Task.WhenAny(stopTask, Task.Delay(StopTimeout));
            if (completedTask == stopTask)
                await stopTask;
            else
                Logger.Debug($"Timed out stopping connection {connection.RemoteEndPoint}.");
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to stop connection {connection.RemoteEndPoint}.", ex);
        }
    }
}
