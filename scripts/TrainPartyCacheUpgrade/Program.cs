using System.IO.Compression;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using MemoryPack;
using Newtonsoft.Json.Linq;

// Offline, targeted migration: no configuration, database, network, global GameData, or load hooks.
// Keeps every table of the existing cache byte-for-byte and only adds what the party-car build reads:
// each TrainPartyAreaConfig row's ShowBlockList (room names behind the floor saved values) and ProgressBonusList
// (stars per progress threshold), and the TrainPartyRewardConfig build-level table.
if (args.Length != 3)
{
    Console.Error.WriteLine("Usage: TrainPartyCacheUpgrade <existing Resource.bin> <Resources directory> <new output file>");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var resourceRoot = Path.GetFullPath(args[1]);
var outputPath = Path.GetFullPath(args[2]);
if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) || File.Exists(outputPath))
    throw new InvalidOperationException("Choose a new output file; this tool never overwrites the input or an existing file.");

MemoryPackFormatterProvider.Register(new JsonObjectMemoryPackFormatter());
MemoryPackFormatterProvider.Register(new BigIntegerMemoryPackFormatter());

GameDataSnapshot Load(string path)
{
    using var input = File.OpenRead(path);
    using var gzip = new GZipStream(input, CompressionMode.Decompress);
    using var decoded = new MemoryStream();
    gzip.CopyTo(decoded);
    return MemoryPackSerializer.Deserialize<GameDataSnapshot>(decoded.ToArray())
           ?? throw new InvalidDataException("Empty resource snapshot.");
}

List<T> ReadExcel<T>(string name)
{
    var path = Path.Combine(resourceRoot, "ExcelOutput", name);
    if (!File.Exists(path)) throw new FileNotFoundException($"Missing raw table {path}");
    return JArray.Parse(File.ReadAllText(path)).Select(row => row.ToObject<T>()!).ToList();
}

var snapshot = Load(inputPath);
var activity = snapshot.ActivityRuntimeData ?? throw new InvalidDataException("Cache has no activity runtime snapshot.");
var areas = snapshot.TrainPartyAreaConfigData;
if (areas.Count == 0) throw new InvalidDataException("Cache has no TrainPartyAreaConfig rows.");

// Older caches deserialize these members as null.
foreach (var area in areas.Values)
{
    area.ShowBlockList ??= [];
    area.ProgressBonusList ??= [];
}

activity.TrainPartyRewardConfigData ??= [];
if (activity.TrainPartyRewardConfigData.Count > 0 ||
    areas.Values.Any(area => area.ShowBlockList.Count > 0 || area.ProgressBonusList.Count > 0))
    throw new InvalidDataException("This cache already carries the TrainParty room and reward data.");
var baseline = MemoryPackSerializer.Serialize(snapshot);

var rawAreas = ReadExcel<TrainPartyAreaConfigExcel>("TrainPartyAreaConfig.json").ToDictionary(row => row.ID);
foreach (var area in areas.Values)
{
    if (!rawAreas.TryGetValue(area.ID, out var raw))
        throw new InvalidDataException($"TrainPartyAreaConfig.json has no row for cached area {area.ID}.");
    area.ShowBlockList = raw.ShowBlockList;
    area.ProgressBonusList = raw.ProgressBonusList;
}

var rewards = new Dictionary<int, TrainPartyRewardConfigExcel>();
foreach (var row in ReadExcel<TrainPartyRewardConfigExcel>("TrainPartyRewardConfig.json"))
{
    if (!snapshot.RewardDataData.ContainsKey(row.RewardID))
        throw new InvalidDataException($"TrainPartyRewardConfig level {row.Level} names reward {row.RewardID}, which the cache lacks.");
    rewards.TryAdd(row.Level, row);
}

// Removing the additions must restore the original serialized cache.
var added = areas.Values.ToDictionary(area => area, area => (area.ShowBlockList, area.ProgressBonusList));
foreach (var area in areas.Values)
{
    area.ShowBlockList = [];
    area.ProgressBonusList = [];
}

if (!MemoryPackSerializer.Serialize(snapshot).AsSpan().SequenceEqual(baseline))
    throw new InvalidDataException("Existing cache content changed during the upgrade.");
foreach (var (area, (blocks, bonuses)) in added)
{
    area.ShowBlockList = blocks;
    area.ProgressBonusList = bonuses;
}

activity.TrainPartyRewardConfigData = rewards;

var serialized = MemoryPackSerializer.Serialize(snapshot);
using (var output = File.Create(outputPath))
using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
    gzip.Write(serialized, 0, serialized.Length);

// Re-read the written file and verify the additions survived.
var reloaded = Load(outputPath);
var reloadedAreas = reloaded.TrainPartyAreaConfigData.Values.OrderBy(area => area.ID).ToList();
if (reloadedAreas.Any(area => area.ShowBlockList is not { Count: > 0 } || area.ProgressBonusList is not { Count: > 0 }) ||
    reloaded.ActivityRuntimeData?.TrainPartyRewardConfigData?.Count != rewards.Count)
    throw new InvalidDataException("Written cache lost the TrainParty additions.");
foreach (var area in reloadedAreas)
    Console.WriteLine($"TrainPartyAreaConfig {area.ID}: blocks {string.Join(',', area.ShowBlockList)}; " +
                      $"stars {string.Join(',', area.ProgressBonusList.Select(bonus => $"{bonus.Progress}%+{bonus.AddStar}"))}");
Console.WriteLine($"TrainPartyRewardConfig: {rewards.Count} levels, {rewards.Values.Sum(row => row.RequireStar)} stars in total");
Console.WriteLine($"Wrote {outputPath}");
return 0;
