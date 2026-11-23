using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using March7thHoney.Data;
using March7thHoney.Data.Config.AdventureAbility;
using March7thHoney.Enums;
using March7thHoney.Enums.GridFight;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using MemoryPack;

// Production resource loader in an isolated process. No database or network.
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ResourceRegression <resource directory> <new report directory> [--cache-out <new file>] [--stale-cache <old cache>]");
    return 2;
}
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var resourceRoot = Path.GetFullPath(args[0]);
var reportRoot = Path.GetFullPath(args[1]);
Directory.CreateDirectory(reportRoot);
Environment.CurrentDirectory = reportRoot;
var config = ConfigManager.Config;
config.Path.ResourcePath = resourceRoot;
config.Path.ConfigPath = Path.Combine(repository, "Config"); // reads operator config; never saves cache here
config.Path.LanguagePath = Path.Combine(repository, "Config", "Languages");
config.Path.DatabasePath = Path.Combine(reportRoot, "UnusedDatabase");
config.Path.LogPath = reportRoot;
config.ServerOption.Language = "EN";
config.ServerOption.UseCache = false;
Logger.SetLogFile(new FileInfo(Path.Combine(reportRoot, "loader.log")));
// Per-file resource misses and loader diagnostics are logged by ResMgr/ResCache at debug level, so they only reach
// the debug log and are read back from there; operator-facing warnings arrive through OnLogEntry.
var debugLog = new FileInfo(Path.Combine(reportRoot, "loader-debug.log"));
Logger.SetDebugLogFile(debugLog);
var warningsRead = 0;
List<string> NewLoaderWarnings()
{
    debugLog.Refresh();
    if (!debugLog.Exists) return [];
    var warnings = File.ReadAllLines(debugLog.FullName)
        .Where(line => line.Contains("][ResMgr][DEBUG] ", StringComparison.Ordinal) ||
                       line.Contains("][ResCache][DEBUG] ", StringComparison.Ordinal))
        .ToList();
    var fresh = warnings.Skip(warningsRead).ToList();
    warningsRead = warnings.Count;
    return fresh;
}
var issues = new ConcurrentQueue<LogIssue>();
PluginEventCommon.OnLogEntry += (module, level, message) =>
{
    if (level is LoggerLevel.WARN or LoggerLevel.ERROR or LoggerLevel.FATAL)
        issues.Enqueue(new(module, level.ToString(), message));
};
I18NManager.LoadLanguage();
var failures = new List<string>();
var checks = 0;
void Check(string name, bool pass)
{
    checks++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures.Add(name);
}

var fixture = Path.Combine(reportRoot, "Fixtures");
var staleIndex = Array.IndexOf(args, "--stale-cache");
if (staleIndex >= 0)
{
    var operatorConfigPath = config.Path.ConfigPath;
    config.Path.ConfigPath = Path.Combine(reportRoot, "StaleCache");
    Directory.CreateDirectory(config.Path.ConfigPath);
    File.Copy(Path.GetFullPath(args[staleIndex + 1]), Path.Combine(config.Path.ConfigPath, "Resource.bin"));
    // CachePath is first initialized against this isolated copy, never production.
    Check("pre-adaptation cache is rejected when split raw tables are available", !ResourceCache.LoadCache() &&
        (issues.Any(issue => issue.Message.Contains("GridFight split season tables", StringComparison.Ordinal)) ||
         NewLoaderWarnings().Any(line => line.Contains("GridFight split season tables", StringComparison.Ordinal))));
    ResourceCache.ClearGameData();
    config.Path.ConfigPath = operatorConfigPath;
    issues.Clear();
}
Directory.CreateDirectory(Path.Combine(fixture, "ExcelOutput"));
config.Path.ResourcePath = fixture;
ResourceCache.IsComplete = true;
ResourceManager.LoadSingleExcelResource(typeof(OptionalFixture));
Check("absent optional table has no warning and permits cache generation",
    issues.IsEmpty && NewLoaderWarnings().Count == 0 && ResourceCache.IsComplete);
ResourceManager.LoadSingleExcelResource(typeof(RequiredFixture));
Check("absent required table warns and prevents cache generation",
    NewLoaderWarnings().Any(line => line.Contains("Required.json", StringComparison.Ordinal)) && !ResourceCache.IsComplete);
issues.Clear();
ResourceCache.IsComplete = true;
File.WriteAllText(Path.Combine(fixture, "ExcelOutput/Optional.json"), "[{\"Value\":\"invalid integer\"}]");
ResourceManager.LoadSingleExcelResource(typeof(OptionalFixture));
Check("existing invalid optional table still reports a parse failure", issues.Any(x => x.Level == "ERROR") && !ResourceCache.IsComplete);
issues.Clear();

var abilityDirectory = Path.Combine(fixture, "Config", "ConfigAdventureAbility");
Directory.CreateDirectory(abilityDirectory);
File.WriteAllText(Path.Combine(abilityDirectory, "Shared.json"), "{\"AbilityList\":[{\"Name\":\"Shared_Default\",\"OnStart\":[]}],\"GlobalModifiers\":{}}");
File.WriteAllText(Path.Combine(fixture, "Character.json"), "{\"AbilityList\":[\"Shared_Default\"]}");
var catalog = new AdventureAbilityResourceCatalog(fixture);
Check("shared ability resolves from declared name without a model-specific file",
    catalog.TryResolve("Character.json", out var shared) && shared.AbilityList.Single().Name == "Shared_Default");
File.WriteAllText(Path.Combine(fixture, "Character.json"), "{\"AbilityList\":[\"Unavailable\"]}");
Check("unresolved declared ability fails instead of becoming an empty config", !catalog.TryResolve("Character.json", out _));
File.WriteAllText(Path.Combine(abilityDirectory, "Conflicting.json"), "{\"AbilityList\":[{\"Name\":\"Shared_Default\",\"OnStart\":[{\"Type\":\"Other\"}]}]}");
File.WriteAllText(Path.Combine(fixture, "Character.json"), "{\"AbilityList\":[\"Shared_Default\"]}");
Check("conflicting definitions are rejected", !new AdventureAbilityResourceCatalog(fixture).TryResolve("Character.json", out _));
Check("beta bonus enum values match the client", (int)GridFightBonusTypeEnum.SpecificAvatarWithEquip == 16 &&
    (int)GridFightBonusTypeEnum.SpecificAvatarWithRandomEquip == 17 && (int)GridFightBonusTypeEnum.GlobalHp == 23);
Check("beta consume, equipment and trait values match the client", (int)GridFightConsumeTypeEnum.UpgradeByRefresh == 5 &&
    (int)GridFightEquipCategoryEnum.Intrinsic == 15 && (int)GridFightTraitEffectTypeEnum.TempEquipFromTemplate == 16);

config.Path.ResourcePath = resourceRoot;
ResourceCache.IsComplete = true;
issues.Clear();
NewLoaderWarnings();
string? fatal = null;
try
{
    ResourceManager.LoadGameData();
    ResourceManager.ReportTruncatedMissionGraphs();
}
catch (Exception exception) { fatal = exception.ToString(); }
var loaderWarnings = NewLoaderWarnings();
Check("production loader completes without warnings, errors or an exception",
    issues.IsEmpty && loaderWarnings.Count == 0 && fatal == null && ResourceCache.IsComplete);
Check("production loader registers character and scene data", GameData.AvatarConfigData.Count > 0 && GameData.FloorInfoData.Count > 0);
Check("NPC shared abilities contain their actual shared definitions", GameData.AdventureAbilityConfigListData.TryGetValue(1004010, out var npc) &&
    npc.AbilityList.Any(x => x.Name == "MazeMonster_CommonAbility_Default"));

var cacheIndex = Array.IndexOf(args, "--cache-out");
if (cacheIndex >= 0 && failures.Count == 0)
{
    var cachePath = Path.GetFullPath(args[cacheIndex + 1]);
    // CreateNew guarantees an existing production cache or prior audit is never replaced.
    var bytes = MemoryPackSerializer.Serialize(GameData.CreateSnapshot());
    var roundTrip = MemoryPackSerializer.Deserialize<GameDataSnapshot>(bytes);
    Check("cache round trip preserves loaded avatar and NPC ability data", roundTrip != null &&
        roundTrip.AvatarConfigData.Count == GameData.AvatarConfigData.Count &&
        roundTrip.AdventureAbilityConfigListData.GetValueOrDefault(1004010)?.AbilityList.Any(x => x.Name == "MazeMonster_CommonAbility_Default") == true);
    Check("cache round trip preserves split camps and native role layout", roundTrip != null &&
        roundTrip.GridFightCampData.Count == GameData.GridFightCampData.Count &&
        roundTrip.GridFightCampData.All(pair => pair.Value.SeasonID == GameData.GridFightCampData[pair.Key].SeasonID &&
            pair.Value.MonsterList.SequenceEqual(GameData.GridFightCampData[pair.Key].MonsterList)) &&
        roundTrip.GridFightRoleBasicInfoData.All(pair =>
            pair.Value.UsesNativeSkillOverrides == GameData.GridFightRoleBasicInfoData[pair.Key].UsesNativeSkillOverrides &&
            pair.Value.IsFakeAvatar == GameData.GridFightRoleBasicInfoData[pair.Key].IsFakeAvatar));
    if (failures.Count == 0)
    {
        using var output = new FileStream(cachePath, FileMode.CreateNew, FileAccess.Write);
        using var gzip = new GZipStream(output, CompressionLevel.Optimal);
        gzip.Write(bytes);
    }
}
var report = new { ResourceRoot = resourceRoot, Checks = checks, Failures = failures, Fatal = fatal,
    ResourceCache.IsComplete, Issues = issues.ToArray(), Warnings = loaderWarnings, AvatarCount = GameData.AvatarConfigData.Count,
    HasAvatar1511 = GameData.AvatarConfigData.ContainsKey(1511), FloorCount = GameData.FloorInfoData.Count };
File.WriteAllText(Path.Combine(reportRoot, "audit.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Resource regression checks: {checks}, failures: {failures.Count}, loader issues: {issues.Count + loaderWarnings.Count}");
return failures.Count == 0 ? 0 : 1;

record LogIssue(string Module, string Level, string Message);
[ResourceEntity("Optional.json", OptionalFileNames = "Optional.json")]
class OptionalFixture : ExcelResource { public int Value { get; set; } }
[ResourceEntity("Required.json")]
class RequiredFixture : ExcelResource { public int Value { get; set; } }
