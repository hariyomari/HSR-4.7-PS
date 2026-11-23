using System.Collections.Concurrent;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.Lobby;

/// <summary>
///     Server-wide registry of lobby rooms, created once at startup. Packet handlers of different players call it
///     concurrently, so room ids come from an atomic counter and the room table is a concurrent map.
/// </summary>
public class LobbyServerManager
{
    private readonly ConcurrentDictionary<long, LobbyRoomInstance> _rooms = new();
    private long _lastRoomId;

    public async ValueTask<LobbyRoomInstance> CreateLobbyRoom(PlayerInstance ownerPlayer, FightGameMode gameMode,
        int lobbyMode, List<int> sealList)
    {
        var roomId = Interlocked.Increment(ref _lastRoomId);
        var room = new LobbyRoomInstance(ownerPlayer, roomId, gameMode, lobbyMode);
        _rooms[roomId] = room;
        await room.AddPlayer(ownerPlayer, sealList, LobbyProtoValues.CharacterLeader);

        return room;
    }

    public LobbyRoomInstance? GetLobbyRoom(long roomId)
    {
        return _rooms.GetValueOrDefault(roomId);
    }

    public void RemoveLobbyRoom(long roomId)
    {
        _rooms.TryRemove(roomId, out _);
    }

    public LobbyRoomInstance? GetPlayerJoinedRoom(int uid)
    {
        foreach (var room in _rooms.Values)
            if (room.Players.Any(x => x.Player.Uid == uid))
                return room;

        return null;
    }
}
