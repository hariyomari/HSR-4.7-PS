using March7thHoney.Internationalization;
using March7thHoney.Util;

namespace March7thHoney.Data;

/// <summary>Loads the game data a server boot needs: the resource cache or the raw tree, then the operator files.</summary>
public static class GameDataStartup
{
    private static readonly Logger Logger = new("GameData");

    /// <summary>
    ///     Reads Config/Resource.bin when it exists; otherwise reads the raw resource tree and, with
    ///     ServerOption.UseCache, writes Resource.bin when every resource loaded. Then reads the operator files.
    /// </summary>
    /// <returns>True when the raw resource tree was read.</returns>
    /// <exception cref="InvalidDataException">Resource.bin exists but is unreadable or stale.</exception>
    public static async Task<bool> LoadAsync()
    {
        var loadedFromResources = false;
        if (File.Exists(ResourceCache.CachePath))
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.Cache")));
            if (!ResourceCache.LoadCache(ConfigManager.Config.Path.ResourceVersion))
            {
                ResourceCache.ClearGameData();
                throw new InvalidDataException(
                    "Resource cache is invalid. Delete it before restarting to rebuild from Resources.");
            }
        }
        else
        {
            Logger.Info(I18NManager.Translate("Server.ServerInfo.LoadingItem", I18NManager.Translate("Word.GameData")));
            ResourceManager.LoadGameData();
            loadedFromResources = true;

            // Finish writing the cache before startup completes.
            if (ConfigManager.Config.ServerOption.UseCache && ResourceCache.IsComplete)
            {
                Logger.Warn(I18NManager.Translate("Server.ServerInfo.WaitingItem", I18NManager.Translate("Word.Cache")));
                await ResourceCache.SaveCache();
            }
            else if (ConfigManager.Config.ServerOption.UseCache)
            {
                // A single unparseable record anywhere in the tree clears IsComplete; say why no cache appears, or
                // every later boot re-reads the raw tree without the operator knowing.
                Logger.Warn(I18NManager.Translate("Server.Log.CacheNotWritten"));
            }

            // Keep version-specific runtime overlays out of newly generated caches.
            ResourceManager.ApplyResourceCompatibility(ConfigManager.Config.Path.ResourceVersion);
        }

        CustomConfigFiles.Load(loadedFromResources ? CustomConfigPass.AfterResourceLoad : CustomConfigPass.AfterCacheLoad);
        return loadedFromResources;
    }
}
