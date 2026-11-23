using System.Text.Json;
using March7thHoney.Command;
using March7thHoney.Configuration;
using March7thHoney.Enums;
using March7thHoney.Internationalization;

namespace March7thHoney.Util;

public static class ConfigManager
{
    public static readonly Logger Logger = new("ConfigManager");
    private static readonly object HotfixDataLock = new();
    private static readonly string ConfigFilePath = "Config.json";
    private static string HotfixFilePath => Config.Path.ConfigPath + "/Hotfix.json";
    public static ConfigContainer Config { get; private set; } = new();
    public static HotfixContainer Hotfix { get; private set; } = new();

    // Set when LoadConfig created Config.json; its admin key is printed by LogStartupNotices.
    private static bool _configGenerated;

    public static void LoadConfig()
    {
        LoadConfigData();
        LoadHotfixData();
    }

    /// <summary>
    ///     Prints what the owner has to know about the loaded configuration. Called once the language pack is loaded,
    ///     since <see cref="LoadConfig" /> runs before it; also called when loading the language fails, so a freshly
    ///     generated admin key is never lost.
    /// </summary>
    public static void LogStartupNotices()
    {
        if (_configGenerated)
        {
            _configGenerated = false;
            Logger.Info(TranslateOr("Server.Log.ConfigGenerated", $"Generated {ConfigFilePath}.", ConfigFilePath));
            Logger.Info(TranslateOr("Server.Log.ConfigLanguage", $"Current language: {Config.ServerOption.Language}",
                Config.ServerOption.Language));
            Logger.Info(TranslateOr("Server.Log.MuipAdminKey", $"Muipserver Admin key: {Config.MuipServer.AdminKey}",
                Config.MuipServer.AdminKey));
        }

        if (Config.ServerOption.NetStatus is { Enabled: true } netStatus && !netStatus.TryGetColor(out _))
            Logger.Warn(TranslateOr("Server.Log.NetStatusInvalid",
                "ServerOption.NetStatus requires a non-negative LatencyMs and a #RRGGBB Color; signal override is disabled."));
        WarnIfDefaultPermissionsTooBroad();
    }

    // Falls back to English when the language pack is unavailable, so the line keeps its information.
    private static string TranslateOr(string key, string english, params string[] args)
    {
        var text = I18NManager.Translate(key, args);
        return text == key ? english : text;
    }

    /// <summary>
    ///     Entries of <c>ServerOption.DefaultPermissions</c> that grant a permission the built-in User role does not
    ///     have (for example <c>*</c>, <c>command.*</c>, <c>command.account</c> or <c>command.others</c>).
    /// </summary>
    public static List<string> GetPrivilegedDefaultPermissions()
    {
        var roles = CommandRoles.DefaultRoles();
        var userPermissions = CommandRoles.GetPermissions(CommandRoles.User, CommandRoles.User, roles);
        var privileged = CommandPermissions.KnownPermissions()
            .Concat(CommandRoles.GetPermissions(CommandRoles.Developer, CommandRoles.User, roles))
            .Where(permission => permission is not (CommandPermissions.All or CommandPermissions.AllCommands) &&
                                 !userPermissions.Contains(permission))
            .ToList();

        return Config.ServerOption.DefaultPermissions
            .Where(granted => granted == CommandPermissions.All ||
                              privileged.Any(permission => CommandPermissions.Allows(granted, permission)))
            .OrderBy(granted => granted, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void WarnIfDefaultPermissionsTooBroad()
    {
        var privileged = GetPrivilegedDefaultPermissions();
        if (privileged.Count == 0) return;

        var list = string.Join(", ", privileged);
        Logger.Warn(TranslateOr("Server.Log.DefaultPermissionsTooBroad",
            $"ServerOption.DefaultPermissions grants every account [{list}]. Any registered player can use these, " +
            "including taking over or deleting other accounts. Remove them and grant them through PermissionRoles instead.",
            list));
    }

    public static void SaveConfig()
    {
        SaveData(Config, ConfigFilePath);
    }

    public static bool TryGetHotfixData(string version, out DownloadUrlConfig? urls)
    {
        lock (HotfixDataLock)
        {
            if (!Hotfix.HotfixData.TryGetValue(version, out var current))
            {
                urls = null;
                return false;
            }

            urls = new DownloadUrlConfig
            {
                AssetBundleUrl = current.AssetBundleUrl,
                ExAssetBundleUrl = current.ExAssetBundleUrl,
                ExResourceUrl = current.ExResourceUrl,
                LuaUrl = current.LuaUrl,
                IfixUrl = current.IfixUrl
            };
            return true;
        }
    }

    public static bool UpdateHotfixData(string version, DownloadUrlConfig remote)
    {
        return UpdateHotfixData([version], remote) > 0;
    }

    /// <summary>
    ///     Overwrite every URL of the given versions with the supplied values (empty strings clear the URL, unlike
    ///     <see cref="UpdateHotfixData(string, DownloadUrlConfig)" /> which only fills in non-empty ones) and persist.
    /// </summary>
    public static int ReplaceHotfixData(IEnumerable<string> versions, DownloadUrlConfig urls)
    {
        lock (HotfixDataLock)
        {
            var count = 0;
            foreach (var version in versions.Distinct(StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(version)) continue;
                Hotfix.HotfixData[version] = new DownloadUrlConfig
                {
                    AssetBundleUrl = urls.AssetBundleUrl ?? "",
                    ExAssetBundleUrl = urls.ExAssetBundleUrl ?? "",
                    ExResourceUrl = urls.ExResourceUrl ?? "",
                    LuaUrl = urls.LuaUrl ?? "",
                    IfixUrl = urls.IfixUrl ?? ""
                };
                count++;
            }

            if (count > 0) SaveHotfixDataAtomic();
            return count;
        }
    }

    public static void SetHotfixRegion(BaseRegionEnum region)
    {
        lock (HotfixDataLock)
        {
            Hotfix.Region = region;
            SaveHotfixDataAtomic();
        }
    }

    public static int UpdateHotfixData(IEnumerable<string> versions, DownloadUrlConfig remote)
    {
        lock (HotfixDataLock)
        {
            var changedCount = 0;
            foreach (var version in versions.Distinct(StringComparer.Ordinal))
            {
                if (UpdateHotfixDataCore(version, remote)) changedCount++;
            }

            if (changedCount > 0) SaveHotfixDataAtomic();
            return changedCount;
        }
    }

    private static void LoadConfigData()
    {
        var file = new FileInfo(ConfigFilePath);
        if (!file.Exists)
        {
            Config = new ConfigContainer
            {
                MuipServer =
                {
                    AdminKey = Guid.NewGuid().ToString()
                },
                ServerOption =
                {
                    Language = UtilTools.GetCurrentLanguage()
                }
            };

            _configGenerated = true;
            SaveData(Config, ConfigFilePath);
        }

        using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            Config = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ConfigContainer)!;
        }

        SaveData(Config, ConfigFilePath);
    }

    private static void LoadHotfixData()
    {
        var file = new FileInfo(HotfixFilePath);

        // Client keys use {CN|OS}{BETA|PROD}{Win|Android|iOS}{version}.
        // GAME_VERSION's major.minor selects the client's line: production x.y.0, beta x.y.51–55.
        // Its last component is not a beta switch.
        var verList = new List<string>();
        string[] regions = ["CN", "OS"];
        string[] platforms = ["Win", "Android", "iOS"];

        var verParts = GameConstants.GAME_VERSION.Split('.');
        var branch = $"{verParts[0]}.{verParts[1]}";
        var prodVersion = $"{branch}.0";
        var betaVersions = Enumerable.Range(1, 5).Select(i => $"{branch}.5{i}").ToList();

        foreach (var region in regions)
        {
            foreach (var platform in platforms)
                foreach (var v in betaVersions)
                    verList.Add($"{region}BETA{platform}{v}");
            foreach (var platform in platforms)
                verList.Add($"{region}PROD{platform}{prodVersion}");
        }

        if (!file.Exists)
        {
            Hotfix = new HotfixContainer();
            SaveData(Hotfix, HotfixFilePath);
            file.Refresh();
        }

        using (var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
        {
            var json = reader.ReadToEnd();
            Hotfix = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.HotfixContainer)!;
        }

        foreach (var version in verList)
            if (!Hotfix.HotfixData.TryGetValue(version, out _))
                Hotfix.HotfixData[version] = new DownloadUrlConfig();

        SaveData(Hotfix, HotfixFilePath);
    }

    private static void SaveData(object data, string path)
    {
        var json = data switch
        {
            ConfigContainer c => JsonSerializer.Serialize(c, ConfigJsonContext.Default.ConfigContainer),
            HotfixContainer h => JsonSerializer.Serialize(h, ConfigJsonContext.Default.HotfixContainer),
            _ => throw new ArgumentException($"No source-gen JSON metadata for {data.GetType()}", nameof(data))
        };
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream);
        writer.Write(json);
    }

    private static bool UpdateValue(string value, string current, Action<string> setter)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, current, StringComparison.Ordinal))
            return false;

        setter(value);
        return true;
    }

    private static bool UpdateHotfixDataCore(string version, DownloadUrlConfig remote)
    {
        var changed = false;
        if (!Hotfix.HotfixData.TryGetValue(version, out var current))
        {
            current = new DownloadUrlConfig();
            Hotfix.HotfixData[version] = current;
            changed = true;
        }

        changed |= UpdateValue(remote.AssetBundleUrl, current.AssetBundleUrl,
            value => current.AssetBundleUrl = value);
        changed |= UpdateValue(remote.ExAssetBundleUrl, current.ExAssetBundleUrl,
            value => current.ExAssetBundleUrl = value);
        changed |= UpdateValue(remote.ExResourceUrl, current.ExResourceUrl,
            value => current.ExResourceUrl = value);
        changed |= UpdateValue(remote.LuaUrl, current.LuaUrl,
            value => current.LuaUrl = value);
        changed |= UpdateValue(remote.IfixUrl, current.IfixUrl,
            value => current.IfixUrl = value);
        return changed;
    }

    private static void SaveHotfixDataAtomic()
    {
        var json = JsonSerializer.Serialize(Hotfix, ConfigJsonContext.Default.HotfixContainer);
        var tempPath = $"{HotfixFilePath}.tmp.{Guid.NewGuid():N}";
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, HotfixFilePath, true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}
