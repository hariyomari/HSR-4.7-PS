using System.Collections.Concurrent;
using March7thHoney.Database;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Player;

public enum PlayerEditErrorEnum
{
    Invalid,
    NotFound,
    Conflict,
    Busy
}

/// <summary>An admin edit of a save that cannot be carried out; the message is safe to show.</summary>
public sealed class PlayerEditException(string message, PlayerEditErrorEnum kind = PlayerEditErrorEnum.Invalid)
    : Exception(message)
{
    public PlayerEditErrorEnum Kind { get; } = kind;
}

public sealed record OfflineEditResult<T>(T Value, bool WasOnline);

/// <summary>
///     Admin-side changes to a save happen with the player offline: the live session shares the save's objects and
///     would neither see the change nor stop writing over it. A <see cref="Lease" /> marks a uid as taken (logins
///     are refused, the logoff of a kicked session is not the player's own) while a tool rewrites its save.
/// </summary>
public static class OfflinePlayerSession
{
    private static readonly Logger Logger = new("PlayerEdit");
    private static readonly ConcurrentDictionary<int, byte> Held = new();

    public static bool IsHeld(int uid) => Held.ContainsKey(uid);

    /// <summary>Takes <paramref name="uid" /> for an offline rewrite; dispose to give it back.</summary>
    public static Lease Acquire(int uid)
    {
        if (!Held.TryAdd(uid, 0))
            throw new PlayerEditException($"UID {uid} is being changed by another tool; retry shortly",
                PlayerEditErrorEnum.Busy);
        return new Lease(uid);
    }

    /// <summary>Stops every session of <paramref name="uid" />, logged in or still logging in.</summary>
    /// <returns>Whether one was running.</returns>
    public static async Task<bool> StopSessionsAsync(int uid)
    {
        var connections = March7thHoneyListener.GetSnapshot().OfType<Connection>()
            .Where(connection => connection.Player?.Uid == uid)
            .ToList();
        if (connections.Count == 0) return false;

        await System.Threading.Tasks.Task.WhenAll(connections.Select(ConnectionDisconnectHelper.KickByGmAsync));
        if (connections.Any(connection => connection.IsOnline))
            throw new PlayerEditException("Session could not be stopped; retry", PlayerEditErrorEnum.Busy);
        return true;
    }

    /// <summary>
    ///     Loads <paramref name="uid" />'s save into a session-less player, as a login would, runs
    ///     <paramref name="edit" /> on it and saves. <paramref name="verify" /> runs on the stored save before it is
    ///     loaded (the place to refuse a stale edit). A failing edit puts the previous save back; a successful one
    ///     keeps it as the uid's undo point.
    /// </summary>
    public static async Task<OfflineEditResult<T>> EditAsync<T>(int uid, string label,
        Func<PlayerInstance, ValueTask<T>> edit, Action? verify = null)
    {
        var database = DatabaseHelper.Instance ??
                       throw new PlayerEditException("Database is not initialized", PlayerEditErrorEnum.Busy);
        using var lease = Acquire(uid);
        var wasOnline = await StopSessionsAsync(uid);
        var data = database.GetInstance<PlayerData>(uid) ??
                   throw new PlayerEditException($"UID {uid} has no gameplay data", PlayerEditErrorEnum.NotFound);
        verify?.Invoke();

        var backup = DatabaseHelper.CloneUidGameplayData(uid);
        T value;
        try
        {
            var player = new PlayerInstance(data);
            await player.InitializeAsync();
            value = await edit(player);
            DatabaseHelper.MarkDirty(uid);
            database.SaveUidData(uid);
        }
        catch
        {
            DatabaseHelper.ReplaceUidGameplayData(uid, backup);
            throw;
        }

        KeepUndoPoint(uid, label, backup);
        return new OfflineEditResult<T>(value, wasOnline);
    }

    /// <summary>
    ///     Replaces <paramref name="uid" />'s save with <paramref name="rows" /> (one row per gameplay table) and keeps
    ///     the previous save as the undo point.
    /// </summary>
    public static Task<bool> ReplaceAsync(int uid, string label, IReadOnlyCollection<BaseDatabaseDataHelper> rows) =>
        ReplaceAsync(uid, label, _ => rows);

    /// <summary>
    ///     Replaces <paramref name="uid" />'s save with the rows <paramref name="build" /> makes from a copy of the
    ///     stored save, taken once the session is stopped.
    /// </summary>
    public static async Task<bool> ReplaceAsync(int uid, string label,
        Func<List<BaseDatabaseDataHelper>, IReadOnlyCollection<BaseDatabaseDataHelper>> build)
    {
        if (DatabaseHelper.Instance == null)
            throw new PlayerEditException("Database is not initialized", PlayerEditErrorEnum.Busy);
        using var lease = Acquire(uid);
        var wasOnline = await StopSessionsAsync(uid);
        var backup = DatabaseHelper.CloneUidGameplayData(uid);
        var rows = build(DatabaseHelper.CloneUidGameplayData(uid));
        DatabaseHelper.ReplaceUidGameplayData(uid, rows);
        KeepUndoPoint(uid, label, backup);
        return wasOnline;
    }

    /// <summary>Puts back the save from before the last editor change; the replaced save becomes the undo point.</summary>
    public static async Task<bool> UndoAsync(int uid)
    {
        var meta = AccountSnapshotService.GetUndoPoint(uid) ??
                   throw new PlayerEditException($"UID {uid} has nothing to undo", PlayerEditErrorEnum.NotFound);
        var rows = AccountSnapshotService.ReadUndoPoint(uid);
        return await ReplaceAsync(uid, $"undo {meta.Name}", rows);
    }

    /// <summary>Keeps <paramref name="rows" /> (a save as it was before a tool replaced it) as the uid's undo point.</summary>
    public static void KeepUndoPoint(int uid, string label, List<BaseDatabaseDataHelper> rows)
    {
        if (!rows.OfType<PlayerData>().Any()) return;
        try
        {
            AccountSnapshotService.WriteUndoPoint(uid, label, rows);
        }
        catch (Exception e)
        {
            Logger.Warn($"uid {uid}: the undo point for '{label}' could not be written", e);
        }
    }

    public sealed class Lease(int uid) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) Held.TryRemove(uid, out _);
        }
    }
}
