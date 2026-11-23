using March7thHoney.GameServer.Game.Player;
using March7thHoney.Kcp;

namespace March7thHoney.GameServer.Server;

/// <summary>
///     Typed lookups over the live KCP connections tracked by <see cref="March7thHoneyListener" />, so callers work
///     with game <see cref="Connection" />s and players instead of re-casting raw connections.
/// </summary>
public static class Listener
{
    /// <summary>Snapshot of every live game connection, whether or not a player has logged in on it.</summary>
    public static IEnumerable<Connection> GetConnections()
    {
        return March7thHoneyListener.GetSnapshot().OfType<Connection>();
    }

    /// <summary>Snapshot of every connection that carries a logged-in player, for status and command lookups.</summary>
    public static IEnumerable<(Connection Connection, PlayerInstance Player)> GetPlayerConnections()
    {
        foreach (var con in March7thHoneyListener.GetSnapshot())
            if (con is Connection { Player: { } player } gameConnection)
                yield return (gameConnection, player);
    }

    /// <summary>Players of every connection in <see cref="SessionStateEnum.ACTIVE" /> state, in connection order.</summary>
    public static IEnumerable<PlayerInstance> GetActivePlayers()
    {
        return GetPlayerConnections()
            .Where(t => t.Connection.State == SessionStateEnum.ACTIVE)
            .Select(t => t.Player);
    }

    /// <summary>Connection of an online player by uid, in any session state past login.</summary>
    public static Connection? GetPlayerConnection(int uid)
    {
        return GetPlayerConnections().FirstOrDefault(t => t.Player.Uid == uid).Connection;
    }

    /// <summary>Connection of an online player by uid in <see cref="SessionStateEnum.ACTIVE" /> state.</summary>
    public static Connection? GetActiveConnection(int uid)
    {
        return GetPlayerConnections()
            .FirstOrDefault(t => t.Player.Uid == uid && t.Connection.State == SessionStateEnum.ACTIVE)
            .Connection;
    }
}
