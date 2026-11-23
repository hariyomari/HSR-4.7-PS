using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using March7thHoney.Data;

// Builds the path list for `demiurge res --namemap`. The client's resource index stores only path hashes and
// the dumper names an asset only when it is given its path, so every asset of a new version is skipped until
// its path is known. This collects candidate paths from what an export already contains; the dumper hashes
// each one and keeps those the index really has, so a wrong guess costs nothing. Read-only apart from the
// output file.
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: ResourceNameHarvest <keys listing from `demiurge res keys`> <exported resource directory> <new path list>");
    return 2;
}
var keysPath = Path.GetFullPath(args[0]);
var resourceRoot = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args[2]);
if (File.Exists(outputPath)) throw new InvalidOperationException("Output must be a new file.");

// Names the listing already resolves. --namemap replaces the dumper's built-in list, so they are written back.
var known = new SortedSet<string>(StringComparer.Ordinal);
var unnamed = 0;
foreach (var line in File.ReadLines(keysPath))
{
    var parts = line.Split('\t');
    if (parts.Length < 2) continue;
    if (parts[1].Length > 0) known.Add(parts[1].Trim());
    else unnamed++;
}
Console.WriteLine($"Listing: {known.Count} named, {unnamed} unnamed.");

var candidates = new HashSet<string>(StringComparer.Ordinal);

// 1. Every resource path quoted inside an exported file.
var quoted = new Regex("\"((?:Config|Story|Stages|ExcelOutput|ExcelOutputGameCore)/[^\"\\\\\\r\\n]{1,220}?\\.json)\"", RegexOptions.Compiled);
var scanned = 0;
foreach (var file in Directory.EnumerateFiles(resourceRoot, "*.json", SearchOption.AllDirectories))
{
    if (Path.GetRelativePath(resourceRoot, file).StartsWith("TextMap", StringComparison.Ordinal)) continue;
    scanned++;
    var text = File.ReadAllText(file);
    if (!text.Contains(".json\"", StringComparison.Ordinal)) continue;
    foreach (Match match in quoted.Matches(text)) candidates.Add(match.Groups[1].Value);
}
Console.WriteLine($"Scanned {scanned} exported files: {candidates.Count} quoted paths.");

// 2. Every table the server loads, in both table directories.
var tables = typeof(GameData).Assembly.GetTypes()
    .Select(type => type.GetCustomAttribute<ResourceEntity>())
    .Where(attribute => attribute != null)
    .SelectMany(attribute => attribute!.FileName.SelectMany(name => name.Split(',', StringSplitOptions.TrimEntries)))
    .Where(name => name.EndsWith(".json", StringComparison.Ordinal))
    .ToHashSet(StringComparer.Ordinal);
foreach (var table in tables)
{
    candidates.Add("ExcelOutput/" + table);
    candidates.Add("ExcelOutputGameCore/" + table);
}
Console.WriteLine($"Server tables: {tables.Count}.");

// 3. The MissionInfo file of every main mission the mission tables mention.
var missions = new HashSet<long>();
foreach (var table in new[] { "MainMission.json", "SubMission.json", "MissionChapterConfig.json" })
{
    var path = Path.Combine(resourceRoot, "ExcelOutput", table);
    if (!File.Exists(path)) continue;
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var rows = document.RootElement.ValueKind == JsonValueKind.Array
        ? document.RootElement.EnumerateArray()
        : document.RootElement.EnumerateObject().Select(property => property.Value);
    foreach (var row in rows)
        if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("MainMissionID", out var id) &&
            id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var value))
            missions.Add(value);
}
foreach (var id in missions) candidates.Add($"Config/Level/Mission/{id}/MissionInfo_{id}.json");
Console.WriteLine($"Main mission ids: {missions.Count}.");

// 4. The floor file of every floor a plane lists. Nothing quotes these paths; a floor file in turn quotes its groups.
var floors = 0;
var planes = Path.Combine(resourceRoot, "ExcelOutput", "MazePlane.json");
if (File.Exists(planes))
{
    using var document = JsonDocument.Parse(File.ReadAllText(planes));
    var rows = document.RootElement.ValueKind == JsonValueKind.Array
        ? document.RootElement.EnumerateArray()
        : document.RootElement.EnumerateObject().Select(property => property.Value);
    foreach (var row in rows)
    {
        if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("PlaneID", out var plane) ||
            !plane.TryGetInt64(out var planeId)) continue;
        var floorIds = new HashSet<long>();
        if (row.TryGetProperty("StartFloorID", out var start) && start.TryGetInt64(out var startId)) floorIds.Add(startId);
        if (row.TryGetProperty("FloorIDList", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var floor in list.EnumerateArray())
                if (floor.TryGetInt64(out var floorId)) floorIds.Add(floorId);
        foreach (var floorId in floorIds.Where(id => id != 0))
        {
            candidates.Add($"Config/LevelOutput/RuntimeFloor/P{planeId}_F{floorId}.json");
            floors++;
        }
    }
}
Console.WriteLine($"Plane floors: {floors}.");

var fresh = candidates.Where(candidate => !known.Contains(candidate)).Order(StringComparer.Ordinal).ToList();
static string Resource(string relative) => "BakedConfig/" + relative[..^".json".Length] + ".bytes";
var output = new StringBuilder();
foreach (var relative in known) output.Append(Resource(relative)).Append('\n');
foreach (var relative in fresh) output.Append(Resource(relative)).Append('\n');
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, output.ToString(), new UTF8Encoding(false));
Console.WriteLine($"Candidates not yet named: {fresh.Count}. Wrote {known.Count + fresh.Count} paths -> {outputPath}");
return 0;
