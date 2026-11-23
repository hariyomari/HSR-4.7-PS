using System.IO.Compression;
using MemoryPack;
using March7thHoney.Data.Avatar;
using March7thHoney.Data.Challenge;
using March7thHoney.Data.Excel;
using March7thHoney.Internationalization;
using March7thHoney.Util;

namespace March7thHoney.Data;

/// <summary>
///     Binary resource cache. The full game data is serialized to a single MemoryPack blob (Resource.bin)
///     via <see cref="GameData.CreateSnapshot" /> and restored via <see cref="GameData.ApplySnapshot" /> —
///     no reflection, no JSON. Generated offline from the raw JSON resources, read at runtime for fast startup.
/// </summary>
public static class ResourceCache
{
    public static Logger Logger { get; } = new("ResCache");
    public static string CachePath { get; } = ConfigManager.Config.Path.ConfigPath + "/Resource.bin";
    public static bool IsComplete { get; set; } = true; // cleared on partial load errors during generation

    static ResourceCache()
    {
        // Enable serializing the untyped `object` JSON values used by some resource models.
        MemoryPackFormatterProvider.Register(new JsonObjectMemoryPackFormatter());
        // BigInteger has no built-in MemoryPack formatter; without this every cached name hash (HashName.Hash)
        // round-trips to 0. Registered here so it is in place before the first cache Save/Load.
        MemoryPackFormatterProvider.Register(new BigIntegerMemoryPackFormatter());
    }

    public static Task SaveCache()
    {
        return Task.Run(() =>
        {
            // The snapshot is dominated by highly repetitive level-graph / mission trees, so the raw
            // MemoryPack blob (~120 MB) shrinks several-fold under Deflate. GZip(Optimal) keeps both
            // generation (cache-miss only) and per-boot decompression fast without an extra dependency.
            var raw = MemoryPackSerializer.Serialize(GameData.CreateSnapshot());
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                gzip.Write(raw, 0, raw.Length);
            File.WriteAllBytes(CachePath, output.ToArray());
            Logger.Info(I18NManager.Translate("Server.ServerInfo.GeneratedItem",
                I18NManager.Translate("Word.Cache")));
        });
    }

    public static bool LoadCache(string? resourceVersion = null)
    {
        GameDataSnapshot? snapshot;
        try
        {
            var bytes = File.ReadAllBytes(CachePath);

            // Reject uncompressed or truncated caches before deserializing.
            if (bytes.Length < 2 || bytes[0] != 0x1F || bytes[1] != 0x8B) return false;

            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            snapshot = MemoryPackSerializer.Deserialize<GameDataSnapshot>(output.ToArray());
        }
        catch (Exception ex)
        {
            Logger.Error(I18NManager.Translate("Server.ServerInfo.FailedToLoadItem",
                I18NManager.Translate("Word.Cache")), ex);
            return false;
        }

        if (snapshot == null) return false;

        GameData.ApplySnapshot(snapshot);

        // Loaded()/AfterAllDone() post-load hooks are skipped on the cache path, and the get-only
        // `ChallengeMonsters` dictionaries they populate can't round-trip through MemoryPack — re-derive
        // them here, otherwise endgame bosses fail to spawn: ChallengePeak normal mode, and the 3rd/tierce
        // node of MOC / Pure Fiction / Apocalyptic Shadow.
        ChallengePeakOverrides.Apply();
        foreach (var tierce in GameData.ChallengeMazeTierceConfigData.Values)
            tierce.RebuildChallengeMonsters();

        Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadedItem", I18NManager.Translate("Word.Cache")));

        var valid = ValidateLoadedCache();
        if (valid) ResourceManager.ApplyResourceCompatibility(resourceVersion);
        return valid;
    }

    public static void ClearGameData()
    {
        GameData.ApplySnapshot(new GameDataSnapshot());
    }

    /// <summary>
    ///     A check that rejects a loaded cache built before a resource model or loader change.
    /// </summary>
    /// <param name="RawTable">
    ///     The raw excel table to rebuild from. When set, the rule only applies while that table is available, so
    ///     deployments that intentionally ship a cache without the raw resources keep working.
    /// </param>
    /// <param name="IsStale">True when the loaded data shows the cache predates the change.</param>
    /// <param name="Reason">What is missing; logged through the Server.Log.CacheStale language key.</param>
    private sealed record StaleCacheRule(string? RawTable, Func<bool> IsStale, string Reason);

    // Checked in order; the first stale rule rejects the cache.
    private static readonly StaleCacheRule[] StaleCacheRules =
    [
        // The split camp table also identifies the native role-skill layout. Older caches have unscoped camps
        // and no native-skill marker.
        new("GridFightCampSeason.json",
            () => GameData.GridFightCampData.Values.Any(row => row.SeasonID == 0) ||
                  GameData.GridFightRoleBasicInfoData.Values.Any(row =>
                      row.RoleSavedValueList.Count == 0 && !row.UsesNativeSkillOverrides),
            "GridFight split season tables and native role skills require a rebuild."),

        // Caches built before the 4.5 RogueBuffGroup key mapping collapse every classic Simulated Universe group
        // onto id 0, and caches built before the buff/group unions lose the classic RogueBuff subtype, so no
        // blessing draft pool resolves.
        new("RogueBuffGroup.json",
            () => GameData.RogueBuffGroupData.ContainsKey(0) ||
                  !GameData.RogueBuffData.Values.Any(buff => buff is RogueBuffExcel) ||
                  !GameData.RogueBuffGroupData.Values.Any(group =>
                      group is RogueBuffGroupExcel && group.BuffList.Any(buff => buff is RogueBuffExcel)),
            "classic Simulated Universe blessing groups are missing."),

        // Caches built before AvatarDemoConfig.StageType/SPList were modelled deserialize with empty values,
        // which would make every Role Trial stage look like a non-trial row.
        new("AvatarDemoConfig.json",
            () => GameData.AvatarDemoConfigData.Count > 0 &&
                  GameData.AvatarDemoConfigData.Values.All(stage => string.IsNullOrEmpty(stage.StageType)),
            "AvatarDemoConfig StageType/SPList are missing."),

        // Caches built before ItemComposeConfig.FormulaType/RelicList/SpecialMaterialCost were modelled: without
        // them relic and special-material synthesis would be rejected.
        new("ItemComposeConfig.json",
            () => GameData.ItemComposeConfigData.Count > 0 &&
                  GameData.ItemComposeConfigData.Values.All(compose => string.IsNullOrEmpty(compose.FormulaType)),
            "ItemComposeConfig formula columns are missing."),

        // Caches built before the daily-training tables (DailyQuest / DailyMissionData / DailyMissionCount) and
        // the CocoonConfig type columns were loaded.
        new("DailyQuest.json",
            () => GameData.DailyQuestData.Count == 0 ||
                  (GameData.CocoonConfigData.Count > 0 &&
                   GameData.CocoonConfigData.Values.All(cocoon => cocoon.FarmType == 0)),
            "daily training tables are missing."),

        // Caches built before the Divergent Universe Persona tables / blessing-pool TournMode were loaded;
        // scripts/RogueTournCacheUpgrade adds them in place.
        new("RoguePersonaLayerRoom.json",
            () => GameData.RoguePersonaLayerRoomData.Count == 0 ||
                  !GameData.RogueBuffGroupData.Values.OfType<RogueTournBuffGroupExcel>()
                      .Any(group => group.TournMode != Enums.TournRogue.RogueTournModeEnum.Permanent),
            "Divergent Universe Persona tables are missing."),

        new(null, () => GameData.EquipmentConfigData.Count == 0, "EquipmentConfigData is missing."),

        new(null,
            () => GameData.EquipmentConfigData.Values
                .Any(config => config.Release && (config.ExpProvide <= 0 || config.CoinCost <= 0)),
            "EquipmentConfigData is missing Light Cone EXP/cost fields."),

        new("CityShopConfig.json",
            () => GameData.CityShopConfigData.Count == 0 ||
                  GameData.CityShopRewardListData.Count == 0 ||
                  (GameData.ShopConfigData.Count > 0 &&
                   GameData.ShopConfigData.Values.SelectMany(shop => shop.Goods)
                       .All(goods => goods.LimitTimes == 0 && string.IsNullOrEmpty(goods.RefreshType))),
            "CityShop tables and ShopGoodsConfig LimitTimes/RefreshType fields are missing.")
    ];

    private static bool ValidateLoadedCache()
    {
        foreach (var rule in StaleCacheRules)
        {
            if (rule.RawTable != null && ResourcePaths.FindExcel(rule.RawTable) == null) continue;
            if (!rule.IsStale()) continue;

            Logger.Warn(I18NManager.Translate("Server.Log.CacheStale", rule.Reason.TrimEnd('.')));
            return false;
        }

        return true;
    }
}
