using March7thHoney.Util;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Activity;

/// <summary>One MaterialSubmitter.json row: the materials a "Lab Assistants In Position" slot takes and what it pays.</summary>
public sealed record MaterialSubmitterRow(
    int Id,
    int ActivityModuleId,
    IReadOnlyList<(int ItemId, int Count)> Materials,
    int MissionId,
    int RewardId);

/// <summary>
///     MaterialSubmitter.json, read from the raw table on first use so Resource.bin stays unchanged. Without the table
///     the activity reports no slots.
/// </summary>
public static class MaterialSubmitterTable
{
    private static readonly object Gate = new();
    private static Dictionary<int, MaterialSubmitterRow>? _rows;

    public static IReadOnlyDictionary<int, MaterialSubmitterRow> Rows
    {
        get
        {
            var rows = Volatile.Read(ref _rows);
            if (rows != null) return rows;
            lock (Gate)
            {
                _rows ??= Load();
                return _rows;
            }
        }
    }

    private static Dictionary<int, MaterialSubmitterRow> Load()
    {
        var rows = new Dictionary<int, MaterialSubmitterRow>();
        var file = ResourcePaths.FindExcel("MaterialSubmitter.json");
        if (file == null) return rows;

        try
        {
            foreach (var row in JArray.Parse(File.ReadAllText(file.FullName)).OfType<JObject>())
            {
                var id = row["ID"]?.Value<int>() ?? 0;
                if (id <= 0) continue;
                var materials = (row["MaterialList"] as JArray ?? [])
                    .OfType<JObject>()
                    .Select(m => (m["ItemID"]?.Value<int>() ?? 0, m["ItemNum"]?.Value<int>() ?? 0))
                    .Where(m => m.Item1 > 0 && m.Item2 > 0)
                    .ToList();
                rows[id] = new MaterialSubmitterRow(id, row["ActivityModuleID"]?.Value<int>() ?? 0, materials,
                    row["MissionID"]?.Value<int>() ?? 0, row["RewardID"]?.Value<int>() ?? 0);
            }
        }
        catch (Exception ex)
        {
            new Logger("MaterialSubmit").Warn("MaterialSubmitter.json could not be read.", ex);
        }

        return rows;
    }
}
