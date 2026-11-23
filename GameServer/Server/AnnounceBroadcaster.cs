using March7thHoney.GameServer.Server.Packet.Send.Player;

namespace March7thHoney.GameServer.Server;

/// <summary>Sends one server announcement to a set of connections; shared by the anno command and the admin API.</summary>
public static class AnnounceBroadcaster
{
    /// <summary>Delivers the announcement to every recipient with a logged-in player and returns how many got it.</summary>
    public static async ValueTask<int> SendAsync(IEnumerable<Connection> recipients, string content,
        long durationSeconds, uint bannerFrequencySeconds)
    {
        var sent = 0;
        foreach (var connection in recipients)
        {
            if (!connection.IsOnline || connection.Player == null) continue;
            await connection.SendPacket(new PacketServerAnnounceNotify(content, durationSeconds,
                bannerFrequencySeconds));
            sent++;
        }

        return sent;
    }
}
