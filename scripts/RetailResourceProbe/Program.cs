using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: RetailResourceProbe <4.7 game directory> <reference JSON directory> <new output directory>");
    return 1;
}

try
{
    return Run(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
}
catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OverflowException or ArgumentException)
{
    Console.Error.WriteLine($"Probe failed: {ex.Message}");
    return 1;
}

static int Run(string game, string reference, string output)
{
    if (Directory.Exists(output) || File.Exists(output) || Inside(output, game) || Inside(output, reference))
        throw new IOException("Output must be a new directory outside both input trees");
    if (!File.ReadLines(Path.Combine(game, "config.ini")).Any(line => line.Trim() == "game_version=4.7.0"))
        throw new InvalidDataException("This probe was designed for the retail 4.7.0 installation");
    var layers = new[] { DesignManifest.Open(game, "StreamingAssets"), DesignManifest.Open(game, "Persistent") };
    var indices = new JsonArray();
    foreach (var layer in layers)
    {
        indices.Add(new JsonObject { ["layer"] = layer.Layer, ["revision"] = layer.Revision, ["digest"] = layer.Digest,
            ["blocks"] = layer.BlockCount, ["entries"] = layer.Entries.Count });
        Console.WriteLine($"{layer.Layer}: design revision {layer.Revision}, {layer.Entries.Count:N0} indexed entries");
    }
    var reader = new BlockReader();
    var findings = new JsonArray();
    var files = new Dictionary<string, JsonNode>();
    bool ok = true;

    // Retail keys independently identified by the payload contents, not inferred from filesystem order.
    foreach (var (name, key) in new[] { ("GridFightBasicBonusPoolV2", 0xd8ca67135327b392UL), ("GridFightTraitEffect", 0x95933781f5b87baeUL) })
    {
        string path = $"ExcelOutput/{name}.json";
        var (record, bytes) = Find(path, key);
        var result = Tables.Analyze(name, bytes, Path.Combine(reference, path));
        var rows = result["rows"] as JsonArray;
        var match = result["matchesReference"]?.GetValue<bool>() == true;
        if (rows != null)
        {
            result.Remove("rows");
            files.Add(path, rows);
        }
        record["comparison"] = result;
        ok &= match && rows != null;
        Console.WriteLine($"{name}: decoded rows={rows?.Count.ToString() ?? "unavailable"}, matches reference={match}");
    }

    // Each reference control uses the exact same presence mask as the following target.
    foreach (var (control, controlKey, target, targetKey) in new[] {
        (3050320, 0x3c4ded6d48bf3c1cUL, 3050321, 0x63a56845371ae33aUL),
        (4011800, 0xdeaefde2ed074220UL, 4011900, 0xbf135c772186de95UL) })
    {
        foreach (var (id, key) in new[] { (control, controlKey), (target, targetKey) })
        {
            var path = $"Config/Level/Mission/{id}/MissionInfo_{id}.json";
            var (record, bytes) = Find(path, key);
            var mission = MissionProbe.Decode(bytes, id);
            var local = Path.Combine(reference, path);
            var exists = File.Exists(local);
            bool? matches = exists ? JsonNode.DeepEquals(mission, ParseFile(local)) : null;
            record["referencePresent"] = exists;
            record["matchesReference"] = matches;
            record["controlMissionId"] = control;
            if (id == control && matches != true) throw new InvalidDataException($"Mission {control} failed its reference control");
            if (matches == false) ok = false;
            if (id != control) files.Add(path, mission);
            Console.WriteLine($"Mission {id}: decoded; start={mission["StartSubMissionList"]![0]}, finish={mission["FinishSubMissionList"]![0]}; reference={(exists ? (matches == true ? "match" : "different") : "absent")}");
        }
    }

    files.Add("report.json", new JsonObject { ["clientVersion"] = "4.7.0", ["manifests"] = indices,
        ["findings"] = findings, ["passed"] = ok });
    foreach (var (name, node) in files)
    {
        var destination = Path.Combine(output, name);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        node.WriteTo(writer);
    }
    Console.WriteLine($"{(ok ? "PASS" : "MISMATCH")}: {Path.Combine(output, "report.json")}");
    return ok ? 0 : 2;

    (JsonObject, byte[]) Find(string path, ulong key)
    {
        var matches = new JsonArray();
        DesignSlice? selected = null;
        foreach (var layer in layers)
        {
            if (!layer.Entries.TryGetValue(key, out var slice)) continue;
            selected = slice;
            matches.Add(new JsonObject { ["layer"] = layer.Layer, ["block"] = slice.Block.Digest,
                ["offset"] = slice.Offset, ["length"] = slice.Length });
        }
        if (selected == null) throw new InvalidDataException($"Neither manifest contains the known retail key {key:x16} for {path}");
        var record = new JsonObject { ["expectedPath"] = path, ["key"] = key.ToString("x16"), ["layersBaseFirst"] = matches };
        findings.Add(record);
        return (record, reader.Read(selected.Value));
    }
}

static bool Inside(string path, string parent)
{
    var relative = Path.GetRelativePath(parent, path);
    return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
}

static JsonNode? ParseFile(string path)
{
    using var stream = File.OpenRead(path);
    return JsonNode.Parse(stream);
}
