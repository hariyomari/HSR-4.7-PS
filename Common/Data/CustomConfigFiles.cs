using March7thHoney.Data.Avatar;
using March7thHoney.Data.Challenge;
using March7thHoney.Data.Custom;
using March7thHoney.Util;

namespace March7thHoney.Data;

/// <summary>The startup passes of <see cref="CustomConfigFiles.Load" />.</summary>
public enum CustomConfigPass
{
    /// <summary>
    ///     Part of <see cref="ResourceManager.LoadGameData" />, before the floors finish loading: the files a generated
    ///     Resource.bin carries a copy of, so a cache stays complete for builds that still read those copies.
    /// </summary>
    ResourceLoad,

    /// <summary>After a raw-resource load: the files <see cref="ResourceLoad" /> did not read, then the derived steps.</summary>
    AfterResourceLoad,

    /// <summary>
    ///     After Resource.bin was read: every file, replacing the cached copies, then the derived steps. An edited file
    ///     therefore reaches the players with a restart, without regenerating the cache.
    /// </summary>
    AfterCacheLoad
}

/// <summary>Operator files that /reload and the admin console re-read while the server runs.</summary>
public enum ReloadableCustomConfig
{
    Banners,
    Activity,
    VideoKeys
}

/// <summary>
///     One operator file. <see cref="Load" /> is null for a file its consumer reads on first use rather than at
///     startup, or that another entry's loader reads together with its own file.
/// </summary>
public sealed class CustomConfigFile(string fileName, Action? load, bool readByResourceLoad = false,
    bool inConfigRoot = false, bool optional = false)
{
    public string FileName { get; } = fileName;

    /// <summary>Lives in Config/ instead of Config/Custom/.</summary>
    public bool InConfigRoot { get; } = inConfigRoot;

    /// <summary>Not shipped with the repository; the server runs without it.</summary>
    public bool Optional { get; } = optional;

    /// <summary>Path below Config/ of the shipped copy.</summary>
    public string RelativePath => InConfigRoot ? FileName : Path.Combine("Custom", FileName);

    internal Action? Load { get; } = load;
    internal bool ReadByResourceLoad { get; } = readByResourceLoad;
}

/// <summary>
///     Operator-authored JSON under Config/Custom (or the legacy Config root), as opposed to dumped game data. Every file
///     is listed once in <see cref="Files" />; each boot path reads all of them from disk, so Resource.bin never decides
///     their content. A missing or unreadable file leaves the type's empty configuration and a warning.
/// </summary>
public static class CustomConfigFiles
{
    private static readonly CustomConfigFile Activity =
        Json<ActivityConfig>("ActivityConfig", "Activity", value => GameData.ActivityConfig = value, true);

    // Banners.json is the served list. The warp history pool and the Story-filter data (story gates, admin item names)
    // feed the same warp schedule, so ResourceManager.ReloadBanners reads all four files at startup and on /reload.
    private static readonly CustomConfigFile Banners = new("Banners.json", ResourceManager.ReloadBanners, true);

    private static readonly CustomConfigFile VideoKeys =
        Json<VideoKeysConfig>("VideoKeysConfig", "VideoKeys", value => GameData.VideoKeysConfig = value, true);

    /// <summary>Every operator file, in load order.</summary>
    public static IReadOnlyList<CustomConfigFile> Files { get; } =
    [
        Activity,
        Banners,
        // Read together with Banners.json by ResourceManager.ReloadBanners.
        new("Banners.history.json", null),
        new("WarpStoryGates.json", null),
        new("WarpItemNames.json", null),
        VideoKeys,
        Json<Dictionary<int, List<int>>>("RogueMapGen", "Rogue Map", value => GameData.RogueMapGenData = value, true),
        Json<QueryProductInfoConfig>("QueryProductInfoConfig", "QueryProductInfo",
            value => GameData.QueryProductInfoConfig = value, true),
        Json<DiceyDungeonConfig>("DiceyDungeon", "DiceyDungeon", value => GameData.DiceyDungeonConfig = value, true),
        Json<RealityShowConfig>("RealityShow", "RealityShow", value => GameData.RealityShowConfig = value, true),
        Json<SceneRainbowGroupPropertyConfig>("SceneRainbowGroupProperty", "Scene Rainbow Group Property",
            value => GameData.SceneRainbowGroupPropertyData = value, true),
        Json<ChallengePeakOverrideConfig>("ChallengePeak", "ChallengePeak Override",
            value => GameData.ChallengePeakOverrideConfig = value, true),
        Json<GridFightRewardRulesConfig>("GridFightRewardRules", "GridFight Reward Rules",
            value => GameData.GridFightRewardRulesConfig = value, true),
        Json<GridFightBasicOrbRewardsConfig>("GridFightBasicOrbRewards", "GridFight Orb Rewards",
            value => GameData.GridFightBasicOrbRewardsConfig = value, true),
        Json<GridFightRuntimeConfig>("GridFightRuntime", "GridFight Runtime Config",
            value => GameData.GridFightRuntimeConfig = value, true),
        new("RogueMagicRoomGen.json", () =>
        {
            GameData.RogueMagicRoomGenData = [];
            ResourceManager.LoadRogueMagicRoomData();
        }, true, inConfigRoot: true),
        new("ChessRogueRoomGen.json", () =>
        {
            GameData.ChessRogueRoomData.Clear();
            ResourceManager.LoadChessRogueRoomData();
        }, true),
        new("ChessRogueDiceSurfaceEffect.json", () =>
        {
            GameData.ChessRogueDiceSurfaceEffectData = [];
            ResourceManager.LoadChessRogueDiceSurfaceEffectData();
        }, true),
        // Needs the complete mission graph, so it is never part of the resource load.
        new("MissionInfoOverride.json", ResourceManager.LoadMissionInfoOverrides, optional: true),
        Json<ActivityBangbooConfig>("ActivityBangboo", "ActivityBangboo",
            value => GameData.ActivityBangbooConfig = value),
        // ExRank (奇缘星魂) materials and definitions.
        new("AvatarExRankMaterials.json", AvatarExRankMaterials.Apply),
        // Read by the admin console's GridFight catalog when it is first opened.
        new("GridFightNames.json", null),
        // Read by RogueTournResources on the first Divergent Universe run.
        new("RogueTournRoomPlan.json", null)
    ];

    // Banners, the activity schedule and the video keys are looked up per request, so swapping them is safe while
    // players are online. The other files feed state that is derived once (ChallengePeak rows, GridFight sessions,
    // room pools, mission graphs) and only change with a restart.
    private static readonly Dictionary<ReloadableCustomConfig, CustomConfigFile> Reloadable = new()
    {
        [ReloadableCustomConfig.Banners] = Banners,
        [ReloadableCustomConfig.Activity] = Activity,
        [ReloadableCustomConfig.VideoKeys] = VideoKeys
    };

    /// <summary>
    ///     Where an operator file is read from: Config/Custom/&lt;name&gt;, or the legacy Config/&lt;name&gt; when there is
    ///     no Config/Custom copy.
    /// </summary>
    public static string ResolvePath(string fileName)
    {
        var configPath = ConfigManager.Config.Path.ConfigPath;
        var customPath = Path.Combine(configPath, "Custom", fileName);
        return File.Exists(customPath) ? customPath : Path.Combine(configPath, fileName);
    }

    /// <summary>Reads the operator files of one startup pass.</summary>
    public static void Load(CustomConfigPass pass)
    {
        switch (pass)
        {
            case CustomConfigPass.ResourceLoad:
                LoadFiles(file => file.ReadByResourceLoad);
                LinkGridFightRewards();
                // ResourceManager.LoadGameData applies the ChallengePeak overrides once the floors are loaded.
                break;
            case CustomConfigPass.AfterResourceLoad:
                LoadFiles(file => !file.ReadByResourceLoad);
                FinishLoad();
                break;
            case CustomConfigPass.AfterCacheLoad:
                LoadFiles(_ => true);
                LinkGridFightRewards();
                // Seasons ChallengePeak.json leaves out are derived from the floors in the cache.
                ChallengePeakOverrides.Apply();
                // Chessboard maps come from the raw tree; a cache-only deployment keeps the cached ones.
                ResourceManager.LoadRogueChestMapInfo();
                FinishLoad();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(pass), pass, null);
        }
    }

    /// <summary>Re-reads one operator file; a missing or unreadable file installs an empty configuration.</summary>
    public static void Reload(ReloadableCustomConfig file)
    {
        Reloadable[file].Load?.Invoke();
    }

    private static void LoadFiles(Func<CustomConfigFile, bool> include)
    {
        foreach (var file in Files)
            if (file.Load != null && include(file))
                file.Load();
    }

    private static void FinishLoad()
    {
        ResourceManager.ReportTruncatedMissionGraphs();
        // Story-trial skill limits and IsUseWorldLevel live on the raw excel rows, not in Resource.bin.
        StoryTrialTables.Load();
    }

    // Resource.bin stores the reward rules and the runtime settings inside the orb-reward configuration, and the
    // GridFight code reads them from there.
    private static void LinkGridFightRewards()
    {
        GameData.GridFightBasicOrbRewardsConfig.RewardRules = GameData.GridFightRewardRulesConfig;
        GameData.GridFightBasicOrbRewardsConfig.RuntimeConfig = GameData.GridFightRuntimeConfig;
    }

    private static CustomConfigFile Json<T>(string name, string label, Action<T> assign, bool readByResourceLoad = false)
        where T : class, new()
    {
        return new CustomConfigFile($"{name}.json",
            () => assign(ResourceManager.LoadCustomFile<T>(label, name) ?? new T()), readByResourceLoad);
    }
}
