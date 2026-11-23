using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.TrainCakeCatch;

/// <summary>
///     A shared cake-catch social play room, keyed by the uid of the player who owns it.
///     Every member is read and written only while <see cref="TrainCakeCatchRoomRegistry.Lock" /> is held.
/// </summary>
internal sealed class TrainCakeCatchRoom
{
    public required uint OwnerUid { get; init; }
    public HashSet<int> Members { get; } = [];
    public Dictionary<int, JOLAHHGIFHA> Motions { get; } = [];
    public Dictionary<int, List<JOLAHHGIFHA>> MotionHistory { get; } = [];
    public List<PEEEEGAPCOK> ArrivalHistory { get; } = [];
    public TrainCakeCatchManager.SocialPlaySceneContext? SceneContext { get; set; }
}

/// <summary>
///     Server-wide registry of cake-catch rooms. Rooms span several players' sessions, so they live here rather than
///     on any one player's <see cref="TrainCakeCatchManager" />. Callers hold <see cref="Lock" /> around every
///     lookup together with the room changes that depend on it.
/// </summary>
internal static class TrainCakeCatchRoomRegistry
{
    private static readonly ConcurrentDictionary<uint, TrainCakeCatchRoom> Rooms = new();

    public static object Lock { get; } = new();

    public static bool TryGet(uint ownerUid, [NotNullWhen(true)] out TrainCakeCatchRoom? room)
    {
        return Rooms.TryGetValue(ownerUid, out room);
    }

    public static TrainCakeCatchRoom GetOrCreate(uint ownerUid)
    {
        return Rooms.GetOrAdd(ownerUid, static uid => new TrainCakeCatchRoom { OwnerUid = uid });
    }

    /// <summary>Removes a player from a room and drops the room once nobody is left in it.</summary>
    public static void RemoveMember(uint ownerUid, int uid)
    {
        if (!Rooms.TryGetValue(ownerUid, out var room))
            return;

        room.Members.Remove(uid);
        room.Motions.Remove(uid);
        room.MotionHistory.Remove(uid);
        if (room.Members.Count == 0)
            Rooms.TryRemove(ownerUid, out _);
    }
}
