using March7thHoney.Util;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Activity;

/// <summary>
///     ActivityVoracityInvasionBuf.json: the ally buff ("Aha's Big Help") an invaded stage adds next to its corruption
///     buff. Read from the raw table on first use so Resource.bin rows stay unchanged; without the table no ally buff
///     is added.
/// </summary>
public static class VoracityInvasionAllyBuff
{
    private static readonly object Gate = new();
    private static bool _loaded;
    private static int _topBuffId;

    /// <summary>The highest-level ally buff id, or 0 when the table is absent.</summary>
    public static int TopBuffId
    {
        get
        {
            EnsureLoaded();
            return _topBuffId;
        }
    }

    private static void EnsureLoaded()
    {
        if (Volatile.Read(ref _loaded)) return;
        lock (Gate)
        {
            if (_loaded) return;
            _topBuffId = ReadTopBuffId();
            Volatile.Write(ref _loaded, true);
        }
    }

    private static int ReadTopBuffId()
    {
        var file = ResourcePaths.FindExcel("ActivityVoracityInvasionBuf.json");
        if (file == null) return 0;

        try
        {
            return JArray.Parse(File.ReadAllText(file.FullName))
                .OfType<JObject>()
                .Select(row => (Id: row["BuffID"]?.Value<int>() ?? 0, Level: row["BuffLevel"]?.Value<int>() ?? 0))
                .Where(row => row.Id > 0)
                .OrderByDescending(row => row.Level)
                .Select(row => row.Id)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            new Logger("VoracityInvasion").Warn("ActivityVoracityInvasionBuf.json could not be read.", ex);
            return 0;
        }
    }
}
