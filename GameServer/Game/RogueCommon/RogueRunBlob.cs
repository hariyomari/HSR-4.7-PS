using MemoryPack;

namespace March7thHoney.GameServer.Game.RogueCommon;

/// <summary>
///     A live rogue run is saved as one base64 MemoryPack blob in its mode's save row. The run types change between
///     versions, so a blob that no longer reads is reported as an empty run instead of failing the player load.
/// </summary>
public static class RogueRunBlob
{
    /// <summary>The blob to store for <paramref name="run" />; null when there is no run.</summary>
    public static string? Serialize<TRun>(TRun? run) where TRun : class
    {
        return run == null ? null : Convert.ToBase64String(MemoryPackSerializer.Serialize(run));
    }

    /// <summary>
    ///     False when nothing is stored. Otherwise true, with <paramref name="run" /> null when the blob is corrupt or
    ///     was written by an incompatible run layout.
    /// </summary>
    public static bool TryLoad<TRun>(string? blob, out TRun? run) where TRun : class
    {
        run = null;
        if (string.IsNullOrEmpty(blob)) return false;

        try
        {
            run = MemoryPackSerializer.Deserialize<TRun>(Convert.FromBase64String(blob));
        }
        catch
        {
            run = null;
        }

        return true;
    }
}
