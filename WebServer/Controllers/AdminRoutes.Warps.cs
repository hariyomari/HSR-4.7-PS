using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Enums;
using March7thHoney.GameServer.Game.Gacha;
using March7thHoney.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

/// <summary>
///     Warp scheduling (ServerOption.Warps): which limited banners players are offered. Every write replaces the
///     config values as a whole and persists Config.json; players see the change on their next warp-screen open.
/// </summary>
public static partial class AdminRoutes
{
    private static void MapWarpRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/api/warps", GetWarps);
        app.MapGet("/admin/api/warps/pool", GetWarpPool);
        app.MapPut("/admin/api/warps/manual", UpdateWarpManual);
        app.MapPost("/admin/api/warps/mode", SetWarpMode);
        app.MapPost("/admin/api/warps/rotation", RestartWarpRotation);
    }

    private static IResult GetWarps(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        return Success(BuildWarpsData());
    }

    private static IResult GetWarpPool(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var pool = WarpSchedule.Pool;
        var phases = pool.Phases
            .Select(phase => new WarpPhaseView(phase.Key, phase.Version, phase.Phase, phase.StartTime,
                phase.Banners.Select(banner => banner.GachaId).ToList()))
            .ToList();
        var banners = pool.Banners.Select(banner => ToWarpBannerView(banner, pool, null)).ToList();
        return Success(new WarpPoolData(phases, banners));
    }

    private static IResult UpdateWarpManual(HttpContext ctx, AdminWarpManualRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        if (request?.entries == null)
            return Error("Missing entries");

        var entries = request.entries.Select(entry => new WarpManualEntry
        {
            GachaId = entry?.gacha_id ?? 0,
            BeginTime = entry?.begin_time ?? 0,
            EndTime = entry?.end_time ?? 0,
            RateUpItems5 = entry?.rate_up5?.ToList(),
            RateUpItems4 = entry?.rate_up4?.ToList(),
            PairedGachaId = entry?.paired_gacha_id ?? 0
        }).ToList();
        if (WarpSchedule.ValidateManual(entries) is { } error)
            return Error(error);

        var option = ConfigManager.Config.ServerOption.Warps;
        option.Manual = entries;
        option.Mode = nameof(WarpScheduleModeEnum.Manual);
        ConfigManager.SaveConfig();
        return Success(BuildWarpsData());
    }

    private static IResult SetWarpMode(HttpContext ctx, AdminWarpModeRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        if (!WarpSchedule.TryParseMode(request?.mode, out var mode))
            return Error("Unknown mode (expected Static, Rotation or Manual)");

        WarpSchedule.SetMode(mode, WarpSchedule.Now());
        ConfigManager.SaveConfig();
        return Success(BuildWarpsData());
    }

    private static IResult RestartWarpRotation(HttpContext ctx, AdminWarpRotationRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        if (WarpSchedule.RestartRotation(request?.phase, WarpSchedule.Now()) is { } error)
            return Error(error);

        ConfigManager.SaveConfig();
        return Success(BuildWarpsData());
    }

    private static WarpsData BuildWarpsData()
    {
        var option = ConfigManager.Config.ServerOption.Warps;
        var now = WarpSchedule.Now();
        var pool = WarpSchedule.Pool;
        var rotation = WarpSchedule.GetRotation(now);

        var permanent = GameData.BannersConfig.Banners
            .Where(banner => !WarpSchedule.IsLimited(banner.GachaType))
            .Select(banner => ToWarpBannerView(banner, pool, now))
            .ToList();
        var scheduled = WarpSchedule.GetScheduledBanners(now)
            .Select(banner => ToWarpBannerView(banner, pool, now))
            .ToList();
        var manual = (option.Manual ?? [])
            .Select(entry => new WarpManualEntryView(entry.GachaId, entry.BeginTime, entry.EndTime,
                entry.RateUpItems5?.ToList(), entry.RateUpItems4?.ToList(), entry.PairedGachaId))
            .ToList();

        return new WarpsData(
            WarpSchedule.Mode.ToString(),
            now,
            new WarpRotationView(option.RotationDays, option.RotationStart, option.RotationStartPhase,
                rotation.PeriodBegin, rotation.PeriodEnd, rotation.Phase?.Key, rotation.NextPhase?.Key),
            permanent,
            scheduled,
            manual,
            pool.Banners.Count,
            pool.Phases.Count,
            pool.IneligibleIds.ToList());
    }

    // now == null: a history row shown as data, never "running".
    private static WarpBannerView ToWarpBannerView(BannerConfig banner, WarpPool pool, long? now)
    {
        return new WarpBannerView(
            banner.GachaId,
            banner.GachaType.ToString(),
            banner.Version,
            banner.Phase,
            banner.RateUpItems5.ToList(),
            banner.RateUpItems4.ToList(),
            banner.BeginTime,
            banner.EndTime,
            now.HasValue && banner.IsActiveAt(now.Value),
            pool.DefaultPairOf(banner.GachaId));
    }

    public class AdminWarpManualRequest
    {
        public List<AdminWarpManualEntry?>? entries { get; set; }
    }

    public class AdminWarpManualEntry
    {
        public int? gacha_id { get; set; }
        public long? begin_time { get; set; }
        public long? end_time { get; set; }
        public List<int>? rate_up5 { get; set; }
        public List<int>? rate_up4 { get; set; }
        public int? paired_gacha_id { get; set; }
    }

    public class AdminWarpModeRequest
    {
        public string? mode { get; set; }
    }

    public class AdminWarpRotationRequest
    {
        public string? phase { get; set; }
    }
}
