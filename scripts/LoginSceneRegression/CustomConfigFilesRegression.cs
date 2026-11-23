using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Util;
using Newtonsoft.Json.Linq;

// The operator files: the CustomConfigFiles table against the files the repository ships, and the server's game data
// boot (GameDataStartup.LoadAsync) from the cache copy in the scratch Config directory without a raw resource tree,
// where an edited file must win over the copy inside Resource.bin. Runs last: it reloads all game data.
internal static class CustomConfigFilesRegression
{
    public static async Task Run(string repository, Action<string, bool> check)
    {
        var shippedConfig = Path.Combine(repository, "Config");
        var listed = CustomConfigFiles.Files.Where(file => !file.InConfigRoot).Select(file => file.FileName).ToHashSet();
        var unlisted = Directory.GetFiles(Path.Combine(shippedConfig, "Custom"), "*.json")
            .Select(Path.GetFileName).Where(name => !listed.Contains(name!)).ToList();
        check($"every shipped Config/Custom/*.json file is listed in CustomConfigFiles ({string.Join(", ", unlisted)})",
            unlisted.Count == 0);
        var unshipped = CustomConfigFiles.Files
            .Where(file => !file.Optional && !File.Exists(Path.Combine(shippedConfig, file.RelativePath)))
            .Select(file => file.RelativePath).ToList();
        check($"every CustomConfigFiles entry is shipped or marked optional ({string.Join(", ", unshipped)})",
            unshipped.Count == 0);

        var paths = ConfigManager.Config.Path;
        var configPath = paths.ConfigPath;
        var resourcePath = paths.ResourcePath;
        try
        {
            // The directory holding the regression's copy of Resource.bin stands in for a deployment's Config/.
            var deployedConfig = Path.GetDirectoryName(ResourceCache.CachePath)!;
            paths.ConfigPath = deployedConfig;
            paths.ResourcePath = Path.Combine(deployedConfig, "no-raw-resource-json");
            Directory.CreateDirectory(Path.Combine(deployedConfig, "Custom"));
            foreach (var file in CustomConfigFiles.Files.Where(file => File.Exists(Path.Combine(shippedConfig, file.RelativePath))))
                File.Copy(Path.Combine(shippedConfig, file.RelativePath), Path.Combine(deployedConfig, file.RelativePath),
                    overwrite: true);
            EditJson(Path.Combine(deployedConfig, "Custom", "QueryProductInfoConfig.json"),
                json => json["countryCode"] = "ZZ");
            EditJson(Path.Combine(deployedConfig, "Custom", "GridFightRuntime.json"),
                json => json["RotationPortalId"] = 987654);

            var cacheLoaded = ResourceCache.LoadCache();
            var cachedCountry = GameData.QueryProductInfoConfig.CountryCode;
            var cachedPortal = GameData.GridFightRuntimeConfig.RotationPortalId;
            GameData.RealityShowConfig = new RealityShowConfig();
            GameData.DiceyDungeonConfig = new DiceyDungeonConfig();
            GameData.ActivityBangbooConfig = new ActivityBangbooConfig();

            var loadedFromResources = await GameDataStartup.LoadAsync();
            check("game data boot without raw resources reads the cache", cacheLoaded && !loadedFromResources);
            check("cache boot: an edited QueryProductInfoConfig.json wins over the cached copy",
                cachedCountry != "ZZ" && GameData.QueryProductInfoConfig.CountryCode == "ZZ");
            check("cache boot: an edited GridFightRuntime.json wins over the cached copy and is linked for GridFight",
                cachedPortal != 987654 && GameData.GridFightRuntimeConfig.RotationPortalId == 987654 &&
                ReferenceEquals(GameData.GridFightBasicOrbRewardsConfig.RuntimeConfig, GameData.GridFightRuntimeConfig) &&
                ReferenceEquals(GameData.GridFightBasicOrbRewardsConfig.RewardRules, GameData.GridFightRewardRulesConfig));
            check("cache boot installs RealityShow.json, DiceyDungeon.json and ActivityBangboo.json",
                GameData.RealityShowConfig.Challenges.Count > 0 && GameData.DiceyDungeonConfig.Levels.Count > 0 &&
                GameData.ActivityBangbooConfig.Holes.Count > 0);
            check("cache boot installs Banners.json and VideoKeysConfig.json",
                GameData.BannersConfig.Banners.Count > 0 && GameData.VideoKeysConfig.TotalCount > 0);

            // A raw-resource boot read these files inside ResourceManager.LoadGameData; its later pass must not read
            // them again.
            var banners = new BannersConfig();
            var videoKeys = new VideoKeysConfig();
            GameData.BannersConfig = banners;
            GameData.VideoKeysConfig = videoKeys;
            GameData.ActivityBangbooConfig = new ActivityBangbooConfig();
            CustomConfigFiles.Load(CustomConfigPass.AfterResourceLoad);
            check("raw-resource boot reads ActivityBangboo.json after the load and leaves the files LoadGameData read",
                ReferenceEquals(GameData.BannersConfig, banners) && ReferenceEquals(GameData.VideoKeysConfig, videoKeys) &&
                GameData.ActivityBangbooConfig.Holes.Count > 0);

            CustomConfigFiles.Reload(ReloadableCustomConfig.VideoKeys);
            check("reloading the video keys reads VideoKeysConfig.json", GameData.VideoKeysConfig.TotalCount > 0);
        }
        finally
        {
            paths.ConfigPath = configPath;
            paths.ResourcePath = resourcePath;
        }
    }

    private static void EditJson(string path, Action<JObject> edit)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        edit(json);
        File.WriteAllText(path, json.ToString());
    }
}
