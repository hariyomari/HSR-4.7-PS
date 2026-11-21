using System.Diagnostics;
using System.Runtime.InteropServices;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Database;
using March7thHoney.Kcp;
using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using March7thHoney.WebServer.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static readonly object SaveLock = new();

    private static void MapMaintenanceRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/api/maintenance", GetMaintenance);
        app.MapPost("/admin/api/maintenance/save", SaveDatabaseNow);
        app.MapPost("/admin/api/maintenance/reload", ReloadResource);
        app.MapGet("/admin/api/maintenance/rate-limits", GetRateLimits);
        app.MapPost("/admin/api/maintenance/rate-limits/clear", ClearRateLimits);
        app.MapPost("/admin/api/maintenance/rotate-admin-key", RotateAdminKey);
    }

    private static IResult GetMaintenance(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var config = ConfigManager.Config;
        using var process = Process.GetCurrentProcess();
        var (machineTotalMb, machineAvailableMb) = MuipManager.GetMachineMemory();
        var rateLimits = AuthRateLimiter.GetSnapshot();
        var now = Extensions.GetUnixSec();

        long dbSize = 0;
        try
        {
            if (string.Equals(config.Database.DatabaseType, "sqlite", StringComparison.OrdinalIgnoreCase))
            {
                var file = new FileInfo(Path.Combine(config.Path.DatabasePath, config.Database.DatabaseName));
                if (file.Exists) dbSize = file.Length;
            }
        }
        catch
        {
            // Diagnostics only.
        }

        return Success(new MaintenanceData(
            DatabaseHelper.UidInstanceMap.Count,
            DatabaseHelper.DirtyCount,
            GetLastSaveUnix(),
            config.Database.DatabaseType,
            config.Database.DatabaseName,
            dbSize,
            rateLimits.Count,
            rateLimits.Count(entry => entry.BlockedUntil > now),
            now,
            GameConstants.GAME_VERSION,
            RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
            Math.Round(machineTotalMb, 1),
            Math.Round(machineAvailableMb, 1),
            GetUptimeSeconds(),
            process.Threads.Count,
            March7thHoneyListener.GetSnapshot().Count,
            AdminSessionManager.ActiveSessionCount,
            ConfigManager.IsPublicMode,
            HandbookRoutes.GetLanguages(),
            GC.CollectionCount(2),
            Math.Round(GC.GetTotalMemory(false) / 1024d / 1024d, 1)));
    }

    private static IResult SaveDatabaseNow(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var database = DatabaseHelper.Instance;
        if (database == null)
            return Error("Database is not initialized");

        var dirty = DatabaseHelper.DirtyCount;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            lock (SaveLock)
            {
                database.SaveDatabase();
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        stopwatch.Stop();
        return Success(new SaveDatabaseData(true, Math.Round(stopwatch.Elapsed.TotalSeconds, 2), dirty));
    }

    private static IResult ReloadResource(HttpContext ctx, AdminReloadRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var target = (request?.target ?? string.Empty).Trim().ToLowerInvariant();
        try
        {
            switch (target)
            {
                case "banner":
                case "banners":
                    ResourceManager.ReloadBanners();
                    return Success(new ReloadData("banner", "Banners reloaded"));
                case "activity":
                    GameData.ActivityConfig = ResourceManager.LoadCustomFile<ActivityConfig>("Activity", "ActivityConfig") ??
                                              new ActivityConfig();
                    return Success(new ReloadData("activity", "Activity config reloaded"));
                case "videokey":
                case "videokeys":
                    GameData.VideoKeysConfig = ResourceManager.LoadCustomFile<VideoKeysConfig>("VideoKeys", "VideoKeysConfig") ??
                                               new VideoKeysConfig();
                    return Success(new ReloadData("videokey", "Video keys reloaded"));
                default:
                    return Error("Unknown reload target (banner, activity, videokey)");
            }
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static IResult GetRateLimits(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var now = Extensions.GetUnixSec();
        var entries = AuthRateLimiter.GetSnapshot()
            .Select(entry => new RateLimitEntry(entry.Key, entry.Action, entry.Count, entry.WindowStart,
                entry.BlockedUntil, entry.UpdatedAt))
            .ToList();

        return Success(new RateLimitData(entries, entries.Count, entries.Count(entry => entry.blocked_until > now), now));
    }

    private static IResult ClearRateLimits(HttpContext ctx, AdminRateLimitClearRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        request ??= new AdminRateLimitClearRequest();
        if (!string.IsNullOrWhiteSpace(request.key))
            return Success(new ClearedData(AuthRateLimiter.Clear(request.key.Trim()) ? 1 : 0));

        return Success(new ClearedData(AuthRateLimiter.ClearAll(request.blocked_only.GetValueOrDefault())));
    }

    private static IResult RotateAdminKey(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var newKey = Guid.NewGuid().ToString();
        ConfigManager.Config.MuipServer.AdminKey = newKey;
        ConfigManager.SaveConfig();
        var revoked = AdminSessionManager.RevokeOthers(GetAdminToken(ctx)) + MuipManager.RevokeAdminSessions();

        return Success(new AdminKeyRotateData(newKey, revoked));
    }

    public class AdminReloadRequest
    {
        public string? target { get; set; }
    }

    public class AdminRateLimitClearRequest
    {
        public string? key { get; set; }
        public bool? blocked_only { get; set; }
    }
}
