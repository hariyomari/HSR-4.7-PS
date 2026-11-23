using March7thHoney.Configuration;
using March7thHoney.Enums;
using March7thHoney.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static void MapHotfixRoutes(IEndpointRouteBuilder api)
    {
        api.MapGet("/hotfix", GetHotfix);
        api.MapPut("/hotfix", UpdateHotfix);
    }

    private static IResult GetHotfix()
    {
        return Success(BuildHotfixData());
    }

    private static IResult UpdateHotfix(AdminHotfixRequest? request)
    {
        request ??= new AdminHotfixRequest();

        if (request.region != null)
        {
            var region = request.region.Trim() switch
            {
                "CN" or "cn" => BaseRegionEnum.CN,
                "OS" or "os" => BaseRegionEnum.OS,
                "None" or "none" or "" => BaseRegionEnum.None,
                _ => (BaseRegionEnum?)null
            };
            if (region == null)
                return Error("Region must be None, CN or OS");

            ConfigManager.SetHotfixRegion(region.Value);
        }

        var versions = (request.versions ?? [])
            .Select(version => (version ?? string.Empty).Trim())
            .Where(version => version.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (versions.Count > 0)
        {
            foreach (var version in versions)
            {
                if (version.Length > 64 || version.Any(char.IsWhiteSpace))
                    return Error($"'{version}' is not a valid version key");
            }

            var urls = new DownloadUrlConfig
            {
                AssetBundleUrl = (request.asset_bundle_url ?? string.Empty).Trim(),
                ExAssetBundleUrl = (request.ex_asset_bundle_url ?? string.Empty).Trim(),
                ExResourceUrl = (request.ex_resource_url ?? string.Empty).Trim(),
                LuaUrl = (request.lua_url ?? string.Empty).Trim(),
                IfixUrl = (request.ifix_url ?? string.Empty).Trim()
            };
            foreach (var url in new[] { urls.AssetBundleUrl, urls.ExAssetBundleUrl, urls.ExResourceUrl, urls.LuaUrl, urls.IfixUrl })
            {
                if (url.Length > 0 && !Uri.TryCreate(url, UriKind.Absolute, out _))
                    return Error($"'{url}' is not an absolute URL");
            }

            ConfigManager.ReplaceHotfixData(versions, urls);
        }

        if (request.send_hotfix.HasValue || request.fetch_remote.HasValue)
        {
            if (request.send_hotfix.HasValue)
                ConfigManager.Config.HttpServer.SendHotfix = request.send_hotfix.Value;
            if (request.fetch_remote.HasValue)
                ConfigManager.Config.HttpServer.UseFetchRemoteHotfix = request.fetch_remote.Value;
            ConfigManager.SaveConfig();
        }

        return Success(BuildHotfixData());
    }

    private static HotfixData BuildHotfixData()
    {
        var hotfix = ConfigManager.Hotfix;
        var entries = hotfix.HotfixData
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new HotfixEntry(
                pair.Key,
                pair.Value.AssetBundleUrl ?? "",
                pair.Value.ExAssetBundleUrl ?? "",
                pair.Value.ExResourceUrl ?? "",
                pair.Value.LuaUrl ?? "",
                pair.Value.IfixUrl ?? ""))
            .ToList();

        return new HotfixData(
            hotfix.Region.ToString(),
            ConfigManager.Config.HttpServer.SendHotfix,
            ConfigManager.Config.HttpServer.UseFetchRemoteHotfix,
            entries,
            ["None", "CN", "OS"]);
    }

    public class AdminHotfixRequest
    {
        public List<string>? versions { get; set; }
        public string? asset_bundle_url { get; set; }
        public string? ex_asset_bundle_url { get; set; }
        public string? ex_resource_url { get; set; }
        public string? lua_url { get; set; }
        public string? ifix_url { get; set; }
        public string? region { get; set; }
        public bool? send_hotfix { get; set; }
        public bool? fetch_remote { get; set; }
    }
}
