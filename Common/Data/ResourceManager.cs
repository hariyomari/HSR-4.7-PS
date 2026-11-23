using System.Reflection;
using March7thHoney.Data.Activity;
using March7thHoney.Data.Avatar;
using March7thHoney.Data.Challenge;
using March7thHoney.Data.Config;
using March7thHoney.Data.Config.AdventureAbility;
using March7thHoney.Data.Config.Character;
using March7thHoney.Data.Config.Rogue;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Config.SummonUnit;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Data.GridFight;
using March7thHoney.Data.Rogue;
using March7thHoney.Enums;
using March7thHoney.Enums.Rogue;
using March7thHoney.Enums.RogueMagic;
using March7thHoney.Enums.TournRogue;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data;

public class ResourceManager
{
    public static Logger Logger { get; } = new("ResMgr");

    private static void LogPartialMissingSummary(string itemName, int missingCount, List<string> missingFiles)
    {
        if (missingCount <= 0) return;

        var examplePath = missingFiles.Count > 0 ? missingFiles[0] : string.Empty;
        var summary = I18NManager.Translate("Server.ServerInfo.PartialConfigMissing", itemName, missingCount.ToString(),
            examplePath);

        Logger.Debug(summary);
        Logger.WriteToFile($"[{DateTime.Now:HH:mm:ss}] [ResMgr] WARN {summary}");
        foreach (var file in missingFiles) Logger.WriteToFile(file);
    }

    /// <summary>Marks the cache incomplete and logs a resource file that could not be read.</summary>
    private static void ReportReadFailure(string itemName, Exception ex)
    {
        ResourceCache.IsComplete = false;
        Logger.Error(
            I18NManager.Translate("Server.ServerInfo.FailedToReadItem", itemName,
                I18NManager.Translate("Word.Error")), ex);
    }

    /// <summary>
    ///     Reads <paramref name="file" /> and hands its text to <paramref name="load" />. Any failure, in reading or in
    ///     <paramref name="load" />, is reported under <paramref name="itemName" /> and marks the cache incomplete.
    /// </summary>
    /// <param name="normalizeTypeKey">
    ///     Rewrites the client's <c>$type</c> discriminator to <c>Type</c>, which the polymorphic config models read.
    /// </param>
    private static void LoadJsonFile(FileInfo file, string itemName, bool normalizeTypeKey, Action<string> load)
    {
        try
        {
            var text = File.ReadAllText(file.FullName);
            load(normalizeTypeKey ? text.Replace("$type", "Type") : text);
        }
        catch (Exception ex)
        {
            ReportReadFailure(itemName, ex);
        }
    }

    public static void LoadGameData()
    {
        // Route Newtonsoft polymorphic Task/Predicate deserialization through the manual LoadFromJsonObject
        // dispatch (the bases are abstract for MemoryPack). Only needed for offline cache generation.
        Config.Task.PolymorphicJson.EnsureRegistered();

        LoadExcel();

        var t1 = Task.Run(LoadFloorInfo);
        var t2 = Task.Run(LoadMazeSkill);
        var t3 = Task.Run(LoadSummonUnit);
        var t4 = Task.Run(() =>
        {
            LoadMissionInfo();
            LoadSubMissionInfo();
        });
        var t5 = Task.Run(LoadPerformanceInfo);
        var t6 = Task.Run(LoadDialogueInfo);
        var t7 = Task.Run(LoadRogueChestMapInfo);
        var t8 = Task.Run(LoadAdventureModifier);
        var t9 = Task.Run(LoadLocalPlayer);
        var t10 = Task.Run(FinalityBattleTurnLimit.Load);
        CustomConfigFiles.Load(CustomConfigPass.ResourceLoad);

        Task.WaitAll(t1, t2, t3, t4, t5, t6, t7, t8, t9, t10);

        // After the floors: seasons missing from ChallengePeak.json are derived from their placeholder groups.
        ChallengePeakOverrides.Apply();

        // copy modifiers
        foreach (var value in GameData.AdventureAbilityConfigListData.Values)
        foreach (var adventureModifierConfig in value?.GlobalModifiers ?? [])
        {
            if (GameData.AdventureModifierData.TryAdd(adventureModifierConfig.Key, adventureModifierConfig.Value)) continue;
            if (JToken.DeepEquals(JToken.FromObject(GameData.AdventureModifierData[adventureModifierConfig.Key]),
                    JToken.FromObject(adventureModifierConfig.Value))) continue;
            ResourceCache.IsComplete = false;
            Logger.Debug($"Conflicting shared adventure modifier: {adventureModifierConfig.Key}");
        }
    }

    private static readonly List<string> MissingExcelFiles = [];

    // Excel tables belonging to paused / uncompiled gameplay modules. Verified to have no live reader
    // (only their own self-populating Loaded() hooks touch them), so they are skipped unless
    // ServerOption.LoadPausedModuleData is enabled — see that flag for the rationale.
    private static readonly string[] PausedModuleResourcePrefixes = ["MatchThree", "Marble", "Cake"];

    private static bool IsPausedModuleResource(Type cls)
    {
        return PausedModuleResourcePrefixes.Any(prefix => cls.Name.StartsWith(prefix, StringComparison.Ordinal));
    }

    public static void LoadExcel()
    {
        var classes = Assembly.GetExecutingAssembly().GetTypes(); // Get all classes in the assembly
        List<ExcelResource> resList = [];
        lock (MissingExcelFiles)
        {
            MissingExcelFiles.Clear();
        }

        var loadPaused = ConfigManager.Config.ServerOption.LoadPausedModuleData;
        foreach (var cls in classes.Where(x => x.IsSubclassOf(typeof(ExcelResource))))
        {
            if (!loadPaused && IsPausedModuleResource(cls)) continue;
            var res = LoadSingleExcelResource(cls);
            if (res != null) resList.AddRange(res);
        }

        foreach (var cls in resList) cls.AfterAllDone();
        GridFightTableCompatibility.Apply(resList);

        lock (MissingExcelFiles)
        {
            LogPartialMissingSummary("Excel", MissingExcelFiles.Count, MissingExcelFiles);
        }
    }

    /// <summary>Apply the runtime overlay for an explicitly identified resource source, not the server version.</summary>
    public static void ApplyResourceCompatibility(string? resourceVersion) => ResourceCompatibility.Apply(resourceVersion);

    /// <summary>True for donor-cache keys whose original provenance is not recoverable from the row alone.</summary>
    public static bool IsVersionScopedCompatibilityRow(string memberName, int id) =>
        ResourceCompatibility.ContainsOverlayKey(memberName, id);

    public static List<ExcelResource>? LoadSingleExcelResource(Type cls)
    {
        var attribute = (ResourceEntity?)Attribute.GetCustomAttribute(cls, typeof(ResourceEntity));

        if (attribute == null) return null;
        var resource = (ExcelResource)Activator.CreateInstance(cls)!;
        var count = 0;
        List<ExcelResource> resList = [];
        foreach (var fileName in attribute.FileName)
            try
            {
                var file = ResourcePaths.FindExcel(fileName);
                if (file == null)
                {
                    if (attribute.OptionalFileNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Contains(fileName, StringComparer.Ordinal)) continue;
                    ResourceCache.IsComplete = false;
                    Logger.Debug(I18NManager.Translate("Server.ServerInfo.FailedToReadItem", fileName,
                        I18NManager.Translate("Word.NotFound")));
                    lock (MissingExcelFiles)
                    {
                        MissingExcelFiles.Add(fileName);
                    }

                    continue;
                }

                var badRecords = 0;
                Exception? firstError = null;
                var json = File.ReadAllText(file.FullName);
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.Read();
                    switch (reader.TokenType)
                    {
                        case JsonToken.StartArray:
                        {
                            // array
                            var jArray = JArray.Parse(json);
                            foreach (var item in jArray)
                                try
                                {
                                    var res = JsonConvert.DeserializeObject(item.ToString(), cls);
                                    resList.Add((ExcelResource)res!);
                                    ((ExcelResource?)res)?.Loaded();
                                    count++;
                                }
                                catch (Exception ex)
                                {
                                    badRecords++;
                                    firstError ??= ex;
                                }

                            break;
                        }
                        case JsonToken.StartObject:
                        {
                            // dictionary
                            var jObject = JObject.Parse(json);
                            foreach (var (_, obj) in jObject)
                                try
                                {
                                    var instance = JsonConvert.DeserializeObject(obj!.ToString(), cls);

                                    if (((ExcelResource?)instance)?.GetId() == 0 || (ExcelResource?)instance == null)
                                    {
                                        // Deserialize as JObject to handle nested dictionaries
                                        var nestedObject = JsonConvert.DeserializeObject<JObject>(obj.ToString());

                                        foreach (var nestedItem in nestedObject ?? [])
                                            try
                                            {
                                                var nestedInstance =
                                                    JsonConvert.DeserializeObject(nestedItem.Value!.ToString(), cls);
                                                resList.Add((ExcelResource)nestedInstance!);
                                                ((ExcelResource?)nestedInstance)?.Loaded();
                                                count++;
                                            }
                                            catch (Exception ex)
                                            {
                                                badRecords++;
                                                firstError ??= ex;
                                            }
                                    }
                                    else
                                    {
                                        resList.Add((ExcelResource)instance);
                                        ((ExcelResource)instance).Loaded();
                                    }

                                    count++;
                                }
                                catch (Exception ex)
                                {
                                    badRecords++;
                                    firstError ??= ex;
                                }

                            break;
                        }
                    }
                }

                if (badRecords > 0)
                {
                    ResourceCache.IsComplete = false;
                    Logger.Error(I18NManager.Translate("Server.ServerInfo.FailedToReadItem",
                        $"{fileName} ({badRecords} records)", I18NManager.Translate("Word.Error")), firstError!);
                }

                resource.Finalized();
            }
            catch (Exception ex)
            {
                ReportReadFailure(fileName, ex);
            }

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(), cls.Name));

        return resList;
    }

    public static void LoadFloorInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.FloorInfo")));
        DirectoryInfo directory = new(ResourcePaths.Resource("Config/LevelOutput/RuntimeFloor/"));
        var missingGroupInfos = false;

        if (!directory.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.FloorInfo"),
                ResourcePaths.Resource("Config/LevelOutput/RuntimeFloor"),
                I18NManager.Translate("Word.FloorMissingResult")));
            return;
        }

        var files = directory.GetFiles();

        // Load floor infos in parallel
        Parallel.ForEach(files, file =>
        {
            LoadJsonFile(file, file.Name, false, text =>
            {
                var info = JsonConvert.DeserializeObject<FloorInfo>(text);
                var name = file.Name[..file.Name.IndexOf('.')];
                if (info == null) return;
                GameData.FloorInfoData[name] = info;

                // Load navmap infos
                FileInfo navmapFile = new(ResourcePaths.Resource(info.NavmapConfigPath));
                if (navmapFile.Exists)
                    LoadJsonFile(navmapFile, navmapFile.Name, false, navmapText =>
                    {
                        var navmap = JsonConvert.DeserializeObject<MapInfo>(navmapText);
                        if (navmap != null)
                            foreach (var area in navmap.AreaList)
                            foreach (var section in area.MinimapVolume.Sections)
                                info.MapSections.Add(section.ID);
                    });

                // Load group infos sequentially to maintain order
                foreach (var groupInfo in info.GroupInstanceList)
                {
                    if (groupInfo.IsDelete) continue;
                    if (groupInfo.GroupPath.Contains("_D100")) continue;
                    FileInfo groupFile = new(ResourcePaths.Resource(groupInfo.GroupPath));
                    if (!groupFile.Exists) continue;

                    LoadJsonFile(groupFile, groupFile.Name, false, groupText =>
                    {
                        var group = JsonConvert.DeserializeObject<GroupInfo>(groupText);
                        if (group == null) return;

                        group.Id = groupInfo.ID;
                        // The common map carries the authoritative per-instance SaveType/Category.
                        info.StampCommonMapFields(group);
                        info.Groups[groupInfo.ID] = group;

                        // A group graph failure is reported against the group file.
                        var graphFile = new FileInfo(ResourcePaths.Resource(group.LevelGraph));
                        if (graphFile.Exists)
                        {
                            var graphText = File.ReadAllText(graphFile.FullName).Replace("$type", "Type");
                            group.LevelGraphConfig = LevelGraphConfigInfo.LoadFromJsonObject(JObject.Parse(graphText));
                        }

                        group.Load();
                        // Prop InitLevelGraph fan-out (PropReqInteract) - must run while the props still
                        // hold their ValueSource / InitLevelGraph (FloorInfo.OnLoad clears both).
                        PropInteractGraphLoader.Populate(group, ConfigManager.Config.Path.ResourcePath);
                    });
                }

                if (info.Groups.Count == 0) missingGroupInfos = true;

                info.OnLoad();
            });
        });

        if (missingGroupInfos)
            Logger.Debug(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.FloorGroupInfo"),
                ResourcePaths.Resource("Config/LevelOutput/SharedRuntimeGroup"),
                I18NManager.Translate("Word.FloorGroupMissingResult")));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", GameData.FloorInfoData.Count.ToString(),
            I18NManager.Translate("Word.FloorInfo")));
    }

    public static void LoadMissionInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.MissionInfo")));
        DirectoryInfo directory = new(ResourcePaths.Resource("Config/Level/Mission"));
        if (!directory.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.MissionInfo"),
                ResourcePaths.Resource("Config/Level/Mission"),
                I18NManager.Translate("Word.Mission")));
            return;
        }

        var missingMissionInfos = false;
        var missingCount = 0;
        var missingFiles = new List<string>();
        var count = 0;
        Parallel.ForEach(GameData.MainMissionData, missionExcel =>
        {
            try
            {
                var path =
                    ResourcePaths.Resource($"Config/Level/Mission/{missionExcel.Key}/MissionInfo_{missionExcel.Key}.json");
                if (!File.Exists(path))
                {
                    missingMissionInfos = true;
                    Interlocked.Increment(ref missingCount);
                    lock (missingFiles)
                    {
                        missingFiles.Add(path);
                    }
                    return;
                }

                var json = File.ReadAllText(path);
                var missionInfo = JsonConvert.DeserializeObject<MissionInfo>(json);
                var partialPath =
                    ResourcePaths.Resource($"Config/Level/Mission/{missionExcel.Key}/MissionInfo_{missionExcel.Key}.partial.json");
                if (missionInfo != null && File.Exists(partialPath))
                    LoadJsonFile(new FileInfo(partialPath), $"MissionInfo_{missionExcel.Key}.partial.json", false,
                        partialJson =>
                        {
                            var partialMissionInfo = JsonConvert.DeserializeObject<MissionInfo>(partialJson);
                            if (partialMissionInfo?.SubMissionList is { Count: > 0 })
                                missionInfo.SubMissionList = partialMissionInfo.SubMissionList;
                        });
                if (missionInfo != null)
                {
                    GameData.MainMissionData[missionExcel.Key].SetMissionInfo(missionInfo);
                    Interlocked.Increment(ref count);
                }
                else
                {
                    missingMissionInfos = true;
                }
            }
            catch (Exception ex)
            {
                missingMissionInfos = true;
                ReportReadFailure($"MissionInfo_{missionExcel.Key}.json", ex);
            }
        });

        if (missingMissionInfos)
            LogPartialMissingSummary(I18NManager.Translate("Word.MissionInfo"), missingCount, missingFiles);
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.MissionInfo")));
    }

    /// <summary>
    ///     Applies Config/Custom/MissionInfoOverride.json on top of the loaded mission graphs.
    /// </summary>
    /// <remarks>
    ///     The dump ships MissionInfo_&lt;id&gt;.json with only a Start/Finish list for a large number of
    ///     mains; their SubMissionList lives in a MissionInfo_&lt;id&gt;.partial.json that is not always
    ///     present. A main in that state registers NONE of its sub-missions, so AcceptSubMission finds
    ///     nothing to accept, the finish gate can never turn Finish, the main never completes and every
    ///     successor stays out of the player's mission list. This file lets an operator restore those
    ///     lists without a Resources tree and without regenerating Resource.bin — it is re-read after the
    ///     cache branch, exactly like Banners.json.
    ///     Shape: { "&lt;mainMissionId&gt;": { "StartSubMissionList": [...], "FinishSubMissionList": [...],
    ///     "SubMissionList": [ ...SubMissionInfo... ] } }.
    ///     Note the level graphs (SubMissionTaskInfo) still come from the Resources tree, so on a
    ///     cache-only deployment a restored PropState sub is accepted and finishable but its
    ///     ChangePropState task cannot run.
    /// </remarks>
    public static void LoadMissionInfoOverrides()
    {
        var path = Path.Combine(ConfigManager.Config.Path.ConfigPath, "Custom", "MissionInfoOverride.json");
        if (!File.Exists(path)) return;

        Dictionary<int, Config.MissionInfo>? overrides;
        try
        {
            overrides = JsonConvert.DeserializeObject<Dictionary<int, Config.MissionInfo>>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToReadItem", "MissionInfoOverride.json",
                    I18NManager.Translate("Word.Error")), ex);
            return;
        }

        var applied = 0;
        foreach (var (mainId, info) in overrides ?? [])
        {
            if (info == null) continue;
            if (!GameData.MainMissionData.TryGetValue(mainId, out var main)) continue;

            // Re-registering would append duplicates to SubMissionIds, so start from a clean list.
            main.SubMissionIds = [];
            info.MainMissionID = mainId;
            foreach (var sub in info.SubMissionList) sub.MainMissionID = mainId;
            main.SetMissionInfo(info);
            applied++;
        }

        if (applied > 0)
            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", applied.ToString(),
                "MissionInfoOverride"));
    }

    /// <summary>
    ///     Warns about mains whose start/finish gate points at a sub-mission that is not registered.
    ///     Those mains can be accepted but never completed, which silently truncates the story chain, so
    ///     the gap is worth naming at boot instead of leaving it to be discovered in-game.
    /// </summary>
    public static void ReportTruncatedMissionGraphs()
    {
        var broken = new List<int>();
        foreach (var main in GameData.MainMissionData.Values)
        {
            var info = main.MissionInfo;
            var gate = info.StartSubMissionList.Concat(info.FinishSubMissionList);
            if (gate.Any(x => !GameData.SubMissionInfoData.ContainsKey(x))) broken.Add(main.MainMissionID);
        }

        if (broken.Count == 0) return;

        broken.Sort();
        Logger.Debug($"{broken.Count} main missions reference sub-missions that are not registered and can " +
                    "therefore never be completed - their successors will never appear in the mission log. " +
                    "Restore their SubMissionList via Config/Custom/MissionInfoOverride.json or a " +
                    $"MissionInfo_<id>.partial.json. Affected: {string.Join(", ", broken.Take(20))}" +
                    (broken.Count > 20 ? $", ... (+{broken.Count - 20} more)" : ""));
    }

    /// <summary>
    ///     Re-reads the operator warp files: the served list (Banners.json), the history pool, and the Story-filter
    ///     data generated by scripts/generate-warp-data.py (story gates and admin item names).
    /// </summary>
    public static void ReloadBanners()
    {
        GameData.BannersConfig = LoadBanners();
        GameData.BannerHistoryConfig = LoadBannerHistory();
        GameData.WarpStoryGatesConfig =
            LoadCustomFile<WarpStoryGatesConfig>("Warp story gates", "WarpStoryGates") ?? new WarpStoryGatesConfig();
        GameData.WarpItemNamesConfig =
            LoadCustomFile<WarpItemNamesConfig>("Warp item names", "WarpItemNames") ?? new WarpItemNamesConfig();
    }

    /// <summary>
    ///     Reads Config/Custom/Banners.json.
    ///     Parsed entry by entry on purpose: a single bad row (most often a gachaType the server does not
    ///     know) used to abort the whole file and leave the player with no banners at all, standard pool
    ///     included. A broken row is now reported and skipped, and the rest still load.
    ///     This is also NOT served from Resource.bin — see CustomConfigFiles, which re-reads it after the cache
    ///     path so operators can edit the banner list without regenerating the resource cache.
    /// </summary>
    public static BannersConfig LoadBanners()
    {
        return LoadBannerFile("Banner", "Banners.json", true);
    }

    /// <summary>
    ///     Reads Config/Custom/Banners.history.json, the pool of past limited banners that the Rotation and
    ///     Manual warp modes draw from. Its dates are historical, so no time-window warning; a missing file
    ///     only matters when one of those modes is on, and the scheduler says so then.
    /// </summary>
    public static BannersConfig LoadBannerHistory()
    {
        return LoadBannerFile("Banner history", "Banners.history.json", false);
    }

    private static BannersConfig LoadBannerFile(string filetype, string filename, bool isServedFile)
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", filetype));

        var customPath = Path.Combine(ConfigManager.Config.Path.ConfigPath, "Custom", filename);
        var legacyPath = Path.Combine(ConfigManager.Config.Path.ConfigPath, filename);
        FileInfo file = new(File.Exists(customPath) || !isServedFile ? customPath : legacyPath);
        var config = new BannersConfig();

        if (!file.Exists)
        {
            if (isServedFile)
                Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing", filetype,
                    $"{ConfigManager.Config.Path.ConfigPath}/{filename}", filetype));
            return config;
        }

        JToken token;
        try
        {
            token = JToken.Parse(File.ReadAllText(file.FullName));
        }
        catch (Exception ex)
        {
            Logger.Error(
                I18NManager.Translate("Server.ServerInfo.FailedToReadItem", file.Name,
                    I18NManager.Translate("Word.Error")), ex);
            return config;
        }

        var entries = token.Type == JTokenType.Array
            ? token.Children().ToList()
            : (token["Banners"] ?? token["banners"])?.Children().ToList() ?? [];

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        foreach (var entry in entries)
        {
            BannerConfig? banner;
            try
            {
                banner = entry.ToObject<BannerConfig>();
            }
            catch (Exception ex)
            {
                Logger.Warn(I18NManager.Translate("Server.Log.BannerEntryInvalid", file.Name, ex.Message));
                continue;
            }

            if (banner == null) continue;

            if (banner.GachaType == GachaTypeEnum.Unknown)
            {
                Logger.Warn(I18NManager.Translate("Server.Log.BannerTypeInvalid", banner.GachaId.ToString(), file.Name));
                continue;
            }

            // The client hides a pool outside its window, so a stale beginTime/endTime is by far the most
            // common reason a freshly added event banner "does not show up". Say so instead of silently
            // handing the client a banner it will never render.
            if (isServedFile && !banner.IsActiveAt(now))
                Logger.Warn(I18NManager.Translate("Server.Log.BannerOutOfWindow", banner.GachaId.ToString(),
                    banner.BeginTime.ToString(), banner.EndTime.ToString(), now.ToString()));

            // The client keys pools by gacha id, so a second row with the same id could never be shown.
            if (config.Banners.Exists(x => x.GachaId == banner.GachaId))
            {
                Logger.Warn($"Banner {banner.GachaId} appears more than once in {file.Name}; later rows skipped.");
                continue;
            }

            config.Banners.Add(banner);
        }

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", config.Banners.Count.ToString(), filetype));
        return config;
    }

    public static T? LoadCustomFile<T>(string filetype, string filename)
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", filetype));
        FileInfo file = new(CustomConfigFiles.ResolvePath($"{filename}.json"));
        T? customFile = default;
        if (!file.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing", filetype,
                $"{ConfigManager.Config.Path.ConfigPath}/{filename}.json", filetype));
            return customFile;
        }

        LoadJsonFile(file, file.Name, false, text => customFile = JsonConvert.DeserializeObject<T>(text));

        switch (customFile)
        {
            case Dictionary<int, int> d:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", d.Count.ToString(), filetype));
                break;
            case Dictionary<int, List<int>> di:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", di.Count.ToString(), filetype));
                break;
            case BannersConfig c:
                Logger.Info(
                    I18NManager.Translate("Server.ServerInfo.LoadedItems", c.Banners.Count.ToString(), filetype));
                break;
            case RogueMiracleEffectConfig r:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", r.Miracles.Count.ToString(),
                    filetype));
                break;
            case ActivityConfig a:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", a.ScheduleData.Count.ToString(),
                    filetype));
                break;
            case VideoKeysConfig a:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", a.TotalCount.ToString(),
                    filetype));
                break;
            case SceneRainbowGroupPropertyConfig c:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", c.FloorProperty.Count.ToString(),
                    filetype));
                break;
            default:
                Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem", filetype));
                break;
        }

        return customFile;
    }

    public static void LoadMazeSkill()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.MazeSkillInfo")));
        var count = 0;
        var missingCount = 0;
        var missingFiles = new List<string>();
        var adventureAbilityLock = new object();
        var npcAbilityCatalog = new Lazy<AdventureAbilityResourceCatalog>(() =>
            new AdventureAbilityResourceCatalog(ConfigManager.Config.Path.ResourcePath));
        void LoadAbilityFile(int id, FileInfo file, string adventurePath) =>
            LoadJsonFile(file, adventurePath, true, text =>
            {
                var info = AdventureAbilityConfigListInfo.LoadFromJsonObject(JObject.Parse(text));

                lock (adventureAbilityLock)
                {
                    GameData.AdventureAbilityConfigListData.TryAdd(id, info);
                }

                Interlocked.Increment(ref count);
            });

        Parallel.ForEach(GameData.AdventurePlayerData.Values, adventure =>
        {
            var adventurePath = adventure.PlayerJsonPath.Replace("_Config.json", "_Ability.json")
                .Replace("ConfigCharacter", "ConfigAdventureAbility");
            var path = ResourcePaths.Resource(adventurePath);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                Interlocked.Increment(ref missingCount);
                lock (missingFiles)
                {
                    missingFiles.Add(path);
                }

                return;
            }

            LoadAbilityFile(adventure.ID, file, adventurePath);
        });

        Parallel.ForEach(GameData.NpcMonsterDataData.Values, adventure =>
        {
            // The client retains unused development rows whose WhiteBox
            // character/ability assets are not shipped. Do not derive a skill
            // filename from an empty path or require those development assets.
            if (adventure.JsonPath.StartsWith("Config/ConfigCharacter/WhiteBox/", StringComparison.Ordinal)
                && !File.Exists(ResourcePaths.Resource(adventure.JsonPath))) return;
            var adventurePath = adventure.ConfigEntityPath.Replace("_Entity.json", "_Ability.json")
                .Replace("_Config.json", "_Ability.json")
                .Replace("ConfigEntity", "ConfigAdventureAbility");

            var path = ResourcePaths.Resource(adventurePath);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                // NPC variants and common-only characters have no per-model
                // ability file. Their character config references shared names.
                try
                {
                    if (npcAbilityCatalog.Value.TryResolve(adventure.JsonPath, out var shared))
                    {
                        lock (adventureAbilityLock)
                            GameData.AdventureAbilityConfigListData.TryAdd(adventure.ID, shared);
                        Interlocked.Increment(ref count);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    ResourceCache.IsComplete = false;
                    Logger.Debug($"Failed to resolve shared NPC abilities for {adventure.ID} ({adventure.JsonPath}).", ex);
                }
                Interlocked.Increment(ref missingCount);
                lock (missingFiles)
                {
                    missingFiles.Add(path);
                }

                return;
            }

            LoadAbilityFile(adventure.ID, file, adventurePath);
        });

        if (missingCount > 0)
            LogPartialMissingSummary(I18NManager.Translate("Word.MazeSkillInfo"), missingCount, missingFiles);

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.MazeSkillInfo")));
    }

    public static void LoadSummonUnit()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.SummonUnitInfo")));
        var count = 0;
        var missingCount = 0;
        var missingFiles = new List<string>();
        Parallel.ForEach(GameData.SummonUnitDataData.Values, summonUnit =>
        {
            var path = ResourcePaths.Resource(summonUnit.JsonPath);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                Interlocked.Increment(ref missingCount);
                lock (missingFiles)
                {
                    missingFiles.Add(path);
                }

                return;
            }

            LoadJsonFile(file, summonUnit.JsonPath, true, text =>
            {
                summonUnit.ConfigInfo = SummonUnitConfigInfo.LoadFromJsonObject(JObject.Parse(text));
                Interlocked.Increment(ref count);
            });
        });

        if (missingCount > 0)
            LogPartialMissingSummary(I18NManager.Translate("Word.SummonUnitInfo"), missingCount, missingFiles);

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.SummonUnitInfo")));
    }

    public static void LoadDialogueInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.DialogueInfo")));
        var count = 0;
        var missingCount = 0;
        var missingFiles = new List<string>();
        Parallel.ForEach(GameData.RogueNPCData.Values, dialogue =>
        {
            var path = ResourcePaths.Resource(dialogue.NPCJsonPath);
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                Interlocked.Increment(ref missingCount);
                lock (missingFiles)
                {
                    missingFiles.Add(path);
                }

                return;
            }

            LoadJsonFile(file, file.Name, true, text =>
            {
                var dialogueInfo = JsonConvert.DeserializeObject<RogueNPCConfigInfo>(text);
                if (dialogueInfo == null) return;
                dialogue.RogueNpcConfig = dialogueInfo;
                Interlocked.Increment(ref count);
                dialogueInfo.Loaded();
                LoadRogueTournDialogueGraph(dialogue.RogueNPCID, dialogueInfo);
            });
        });

        if (missingCount > 0)
            LogPartialMissingSummary(I18NManager.Translate("Word.DialogueInfo"), missingCount, missingFiles);

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.DialogueInfo")));
    }

    /// <summary>
    ///     Divergent Universe event NPCs (RogueTournNPC, RogueEventTourn* dialogues) are played by a client-side Act
    ///     graph. The generic LevelGraph model drops the option/event lists the server answers with, so the graph is
    ///     flattened into <see cref="RogueTournDialogueGraph" /> as well. A missing graph file is a legitimate state.
    /// </summary>
    public static void LoadRogueTournDialogueGraph(int npcId, RogueNPCConfigInfo config)
    {
        var entry = config.DialogueList.FirstOrDefault();
        if (entry == null || !entry.DialoguePath.Contains("RogueEventTourn", StringComparison.Ordinal)) return;

        // Some official builds pad their resource paths with trailing whitespace.
        var file = new FileInfo(ResourcePaths.Resource(entry.DialoguePath.Trim()));
        if (!file.Exists) return;
        try
        {
            var graph = RogueTournDialogueGraph.Parse(npcId, entry.TalkNameID, File.ReadAllText(file.FullName));
            if (graph == null) return;
            lock (GameData.RogueTournDialogueData)
            {
                GameData.RogueTournDialogueData[npcId] = graph;
            }
        }
        catch (Exception ex)
        {
            ReportReadFailure(file.Name, ex);
        }
    }

    public static void LoadPerformanceInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.PerformanceInfo")));
        var count = 0;

        // An empty path is a performance without an act graph and still counts as loaded.
        void LoadPerformance(string performancePath, Action<LevelGraphConfigInfo> assign)
        {
            if (performancePath == "")
            {
                Interlocked.Increment(ref count);
                return;
            }

            var file = new FileInfo(ResourcePaths.Resource(performancePath));
            if (!file.Exists) return;
            LoadJsonFile(file, file.Name, true, text =>
            {
                assign(LevelGraphConfigInfo.LoadFromJsonObject(JObject.Parse(text)));
                Interlocked.Increment(ref count);
            });
        }

        Parallel.ForEach(GameData.PerformanceEData.Values,
            performance => LoadPerformance(performance.PerformancePath, info => performance.ActInfo = info));
        Parallel.ForEach(GameData.PerformanceDData.Values,
            performance => LoadPerformance(performance.PerformancePath, info => performance.ActInfo = info));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.PerformanceInfo")));
    }

    public static void LoadSubMissionInfo()
    {
        Logger.Info(
            I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.SubMissionInfo")));
        var count = 0;
        Parallel.ForEach(GameData.SubMissionInfoData.Values, subMission =>
        {
            if (subMission.SubMissionInfo == null || subMission.SubMissionInfo.MissionJsonPath == "") return;

            var path = ResourcePaths.Resource(subMission.SubMissionInfo.MissionJsonPath);
            var file = new FileInfo(path);
            if (!file.Exists) return;
            LoadJsonFile(file, file.Name, true, text =>
            {
                subMission.SubMissionTaskInfo = LevelGraphConfigInfo.LoadFromJsonObject(JObject.Parse(text));
                Interlocked.Increment(ref count);
            });
        });

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.SubMissionInfo")));
    }

    public static void LoadRogueChestMapInfo()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.RogueChestMapInfo")));
        var count = 0;
        var boardList = new List<RogueDLCChessBoardExcel>();
        foreach (var nousMap in GameData.RogueNousChessBoardData.Values) boardList.AddRange(nousMap);

        foreach (var nousMap in GameData.RogueSwarmChessBoardData.Values) boardList.AddRange(nousMap);

        foreach (var board in boardList)
        {
            if (board.ChessBoardConfiguration == "")
            {
                count++;
                continue;
            }

            var path = ResourcePaths.Resource(board.ChessBoardConfiguration);

            var file = new FileInfo(path);
            if (!file.Exists) continue;
            LoadJsonFile(file, file.Name, false, text =>
            {
                var map = JsonConvert.DeserializeObject<RogueChestMapInfo>(text);
                if (map == null) return;
                board.MapInfo = map;
                count++;
            });
        }

        if (count < boardList.Count)
            Logger.Debug(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.RogueChestMapInfo"),
                ResourcePaths.Resource("Config/Gameplays/RogueDLC"),
                I18NManager.Translate("Word.RogueChestMap")));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.RogueChestMapInfo")));
    }

    public static void LoadAdventureModifier()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.AdventureModifierInfo")));
        var count = 0;

        // list the files in folder
        var directory = new DirectoryInfo(ResourcePaths.Resource("Config/ConfigAdventureModifier"));
        if (!directory.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.AdventureModifierInfo"),
                ResourcePaths.Resource("Config/ConfigAdventureModifier"),
                I18NManager.Translate("Word.Buff")));

            return;
        }

        var files = directory.GetFiles();

        foreach (var file in files)
            LoadJsonFile(file, file.Name, true, text =>
            {
                var info = AdventureModifierLookupTableConfig.LoadFromJObject(JObject.Parse(text));

                foreach (var config in info.ModifierMap)
                {
                    GameData.AdventureModifierData.Add(config.Key, config.Value);
                    count++;
                }
            });

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.AdventureModifierInfo")));
    }

    public static void LoadLocalPlayer()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.LocalPlayerCharacter")));
        var count = 0;
        Parallel.ForEach(GameData.AdventurePlayerData.Values, excel =>
        {
            var path = ResourcePaths.Resource(excel.PlayerJsonPath);
            var file = new FileInfo(path);
            if (!file.Exists) return;
            LoadJsonFile(file, excel.PlayerJsonPath, true, text =>
            {
                var info = JsonConvert.DeserializeObject<CharacterConfigInfo>(text);
                if (info == null) return;

                GameData.CharacterConfigInfoData.TryAdd(excel.ID, info);
                Interlocked.Increment(ref count);
            });
        });

        if (count < GameData.AdventurePlayerData.Count)
            Logger.Debug(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.LocalPlayerCharacterInfo"),
                ResourcePaths.Resource("Config/ConfigCharacter"),
                I18NManager.Translate("Word.LocalPlayerCharacter")));

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.LocalPlayerCharacterInfo")));
    }

    public static void LoadChessRogueRoomData()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.ChessRogueRoomInfo")));
        var count = 0;

        var customRoomGen = Path.Combine(ConfigManager.Config.Path.ConfigPath, "Custom", "ChessRogueRoomGen.json");
        FileInfo file = new(File.Exists(customRoomGen)
            ? customRoomGen
            : ConfigManager.Config.Path.ConfigPath + "/ChessRogueRoomGen.json");
        if (!file.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.ChessRogueRoomInfo"),
                $"{ConfigManager.Config.Path.ConfigPath}/ChessRogueRoomGen.json",
                I18NManager.Translate("Word.ChessRogueRoom")));

            return;
        }

        LoadJsonFile(file, file.Name, false, text =>
        {
            var rooms = JsonConvert.DeserializeObject<List<ChessRogueRoomConfig>>(text) ??
                        throw new InvalidDataException("Failed to deserialize ChessRogueRoomGen.json");
            count = ChessRogueRoomPools.Rebuild(rooms);
        });

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.ChessRogueRoomInfo")));
    }

    public static void LoadRogueMagicRoomData()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.RogueMagicRoomInfo")));
        var count = 0;

        FileInfo file = new(ConfigManager.Config.Path.ConfigPath + "/RogueMagicRoomGen.json");

        if (!file.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.RogueMagicRoomInfo"),
                $"{ConfigManager.Config.Path.ConfigPath}/RogueMagicRoomGen.json",
                I18NManager.Translate("Word.RogueMagicRoom")));

            return;
        }

        LoadJsonFile(file, file.Name, false, text =>
        {
            var json = JsonConvert.DeserializeObject<List<RogueMagicRoomConfig>>(text);
            if (json == null) throw new Exception("Failed to deserialize RogueMagicRoomGen.json");

            RogueMagicRoomPools.AddEventRoomVariants(json);
            GameData.RogueMagicRoomGenData = json;
            count = json.Count;
        });

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.RogueMagicRoomInfo")));
    }

    public static void LoadChessRogueDiceSurfaceEffectData()
    {
        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem",
            I18NManager.Translate("Word.RogueDiceSurfaceInfo")));
        var count = 0;

        var customDiceEffect =
            Path.Combine(ConfigManager.Config.Path.ConfigPath, "Custom", "ChessRogueDiceSurfaceEffect.json");
        FileInfo file = new(File.Exists(customDiceEffect)
            ? customDiceEffect
            : ConfigManager.Config.Path.ConfigPath + "/ChessRogueDiceSurfaceEffect.json");

        if (!file.Exists)
        {
            Logger.Warn(I18NManager.Translate("Server.ServerInfo.ConfigMissing",
                I18NManager.Translate("Word.RogueDiceSurfaceInfo"),
                $"{ConfigManager.Config.Path.ConfigPath}/ChessRogueDiceSurfaceEffect.json",
                I18NManager.Translate("Word.RogueDiceSurface")));

            return;
        }

        LoadJsonFile(file, file.Name, false, text =>
        {
            var json = JsonConvert.DeserializeObject<Dictionary<int, ChessRogueDiceSurfaceEffectConfig>>(text);
            if (json == null) throw new Exception("Failed to deserialize ChessRogueDiceSurfaceEffect.json");

            GameData.ChessRogueDiceSurfaceEffectData = json;
            count = json.Count;
        });

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItems", count.ToString(),
            I18NManager.Translate("Word.RogueDiceSurfaceInfo")));
    }
}
