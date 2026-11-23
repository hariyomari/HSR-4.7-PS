using System.Diagnostics;
using System.Runtime.InteropServices;
using March7thHoney.Command;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using March7thHoney.Util;
using March7thHoney.WebServer.Http;
using March7thHoney.WebServer.Pages;
using March7thHoney.WebServer.Server;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

/// <summary>
///     Admin console API. Every /admin/api/* endpoint except login, logout and the session check requires a session
///     token issued by /admin/api/login (Bearer header or X-Admin-Session); <see cref="RequireAdminSession" /> enforces
///     it for the whole route group. The feature areas live in the partial files next to this one.
/// </summary>
public static partial class AdminRoutes
{
    private static readonly DateTime ProcessStartTime = Process.GetCurrentProcess().StartTime;
    private static readonly object AdminSessionItemKey = new();

    public static void MapAdminRoutes(this IEndpointRouteBuilder app)
    {
        // One route: the matcher treats "/admin" and "/admin/" as equivalent, so registering both
        // is an ambiguous match (500). This single template serves both.
        app.MapGet("/admin", () => Results.Redirect("/admin/index.html"));
        app.MapGet("/admin/index.html",
            () => Results.Text(AdminPageRenderer.Render(ConfigManager.Config.GameServer.GameServerName),
                "text/html; charset=utf-8"));

        // These three handle a missing or stale session themselves.
        app.MapPost("/admin/api/login", Login);
        app.MapPost("/admin/api/logout", Logout);
        app.MapGet("/admin/api/session", GetSession);

        var api = app.MapGroup("/admin/api").AddEndpointFilter(RequireAdminSession);
        api.MapGet("/overview", GetOverview);
        api.MapPost("/registration", SetRegistration);
        api.MapPost("/registration-identity-limit", SetRegistrationIdentityLimit);

        MapAccountRoutes(api);
        MapStoryRoutes(api);
        MapPlayerRoutes(api);
        MapPlayerEditorRoutes(api);
        MapMailRoutes(api);
        MapAnnounceRoutes(api);
        MapAccessRoutes(api);
        MapCommandRoutes(api);
        MapGridFightRoutes(api);
        MapSettingsRoutes(api);
        MapHotfixRoutes(api);
        MapLogRoutes(api);
        MapMaintenanceRoutes(api);
        MapWarpRoutes(api);
    }

    /// <summary>
    ///     Endpoint filter of the /admin/api group: refuses a request without a live session before its handler runs and
    ///     hands the session to the handler through <see cref="GetAdminSession" />.
    /// </summary>
    private static async ValueTask<object?> RequireAdminSession(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (!AdminSessionManager.TryGetSession(GetAdminToken(context.HttpContext), out var session))
            return NotAuthorized();

        context.HttpContext.Items[AdminSessionItemKey] = session;
        return await next(context);
    }

    /// <summary>The session <see cref="RequireAdminSession" /> admitted the current request with.</summary>
    private static AdminSessionManager.AdminSession GetAdminSession(HttpContext ctx)
    {
        return ctx.Items[AdminSessionItemKey] is AdminSessionManager.AdminSession session
            ? session
            : throw new InvalidOperationException("Only endpoints in the /admin/api route group have an admin session.");
    }

    private static IResult Login(HttpContext ctx, AdminLoginRequest? request)
    {
        request ??= new AdminLoginRequest();
        var clientKey = ctx.Connection.RemoteIpAddress?.ToString();
        if (!AdminSessionManager.TryCreateSession(request.admin_key ?? string.Empty, clientKey, out var session,
                out var message))
            return Error(message, statusCode: StatusCodes.Status401Unauthorized);

        return Success(new AdminLoginData(session.Token, session.ExpiresAt.ToUnixTimeSeconds()));
    }

    private static IResult Logout(HttpContext ctx)
    {
        AdminSessionManager.Revoke(GetAdminToken(ctx));
        return Success(new EmptyData());
    }

    private static IResult GetSession(HttpContext ctx)
    {
        if (!AdminSessionManager.TryGetSession(GetAdminToken(ctx), out var session))
            return NotAuthorized();

        return Success(new AdminSessionData(
            session.ExpiresAt.ToUnixTimeSeconds(),
            ConfigManager.Config.GameServer.GameServerName,
            GameConstants.GAME_VERSION,
            ConfigManager.Config.ServerOption.Language,
            AdminSessionManager.ActiveSessionCount));
    }

    private static IResult GetOverview()
    {
        var accounts = GetAllAccounts()
            .Select(ToAccountSummary)
            .OrderBy(account => account.uid)
            .ToList();
        var onlinePlayers = GetOnlinePlayers();
        using var process = Process.GetCurrentProcess();
        var option = ConfigManager.Config.ServerOption;
        var (machineTotalMb, machineAvailableMb) = HostMetrics.GetMachineMemory();

        var stats = new OverviewStats(
            accounts.Count,
            onlinePlayers.Count,
            accounts.Count(account => account.banned),
            accounts.Count(account => !account.email_verified),
            GetConfiguredRoles().Count,
            March7thHoneyListener.GetSnapshot().Count,
            accounts.Count(account => !account.has_password));

        var server = new OverviewServer(
            ConfigManager.Config.GameServer.GameServerName,
            ConfigManager.Config.GameServer.GetDisplayAddress(),
            ConfigManager.Config.Database.DatabaseType,
            GetUptimeSeconds(),
            Math.Round(process.WorkingSet64 / 1024d / 1024d, 1),
            Extensions.GetUnixSec(),
            option.Auth.EnableRegistration,
            option.Auth.AntiAbuse.LimitRegistrationPerIdentity,
            GameConstants.GAME_VERSION,
            Math.Round(machineTotalMb, 1),
            Math.Round(machineAvailableMb, 1),
            GetLastSaveUnix(),
            DatabaseHelper.DirtyCount,
            RuntimeInformation.OSDescription,
            option.ServerAnnounce.EnableAnnounce,
            option.Auth.Email.Enabled,
            option.ServerTime.EnableFakeServerTime,
            option.ServerTime.FixedDate);

        var roleBreakdown = accounts
            .GroupBy(account => account.role)
            .Select(group => new RoleCount(group.Key, group.Count()))
            .OrderBy(item => item.role)
            .ToList();

        var attention = accounts
            .Where(account => account.banned || !account.email_verified || !account.has_password)
            .Select(account => new AttentionItem(
                account.uid,
                account.username,
                account.email,
                account.role,
                account.banned
                    ? "banned"
                    : !account.email_verified
                        ? "unverified"
                        : "password"))
            .Take(12)
            .ToList();

        return Success(new OverviewData(stats, server, onlinePlayers, roleBreakdown, attention));
    }

    private static IResult SetRegistration(AdminRegistrationRequest? request)
    {
        request ??= new AdminRegistrationRequest();
        ConfigManager.Config.ServerOption.Auth.EnableRegistration = request.enabled;
        ConfigManager.SaveConfig();

        return Success(new RegistrationData(ConfigManager.Config.ServerOption.Auth.EnableRegistration));
    }

    private static IResult SetRegistrationIdentityLimit(AdminRegistrationRequest? request)
    {
        request ??= new AdminRegistrationRequest();
        ConfigManager.Config.ServerOption.Auth.AntiAbuse.LimitRegistrationPerIdentity = request.enabled;
        ConfigManager.SaveConfig();

        return Success(new IdentityLimitData(
            ConfigManager.Config.ServerOption.Auth.AntiAbuse.LimitRegistrationPerIdentity));
    }

    #region Shared helpers

    private static IResult NotAuthorized()
    {
        return Error("Not authorized", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static string? GetAdminToken(HttpContext ctx)
    {
        if (AuthHeaders.TryGetBearerToken(ctx.Request, out var bearer))
            return bearer;

        var header = ctx.Request.Headers[WebConstants.AdminSessionHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header))
            return header;

        // Downloads open in a new tab cannot set headers; accept the token as a query parameter there.
        return ctx.Request.Query["token"].FirstOrDefault();
    }

    private static List<AccountData> GetAllAccounts()
    {
        return (DatabaseHelper.GetAllInstanceFromMap<AccountData>() ?? DatabaseHelper.GetAllInstance<AccountData>() ?? [])
            .Where(account => account.Uid > 0)
            .GroupBy(account => account.Uid)
            .Select(group => group.First())
            .ToList();
    }

    private static AccountSummary ToAccountSummary(AccountData account)
    {
        var player = DatabaseHelper.Instance?.GetInstance<PlayerData>(account.Uid);
        var role = account.GetRole();
        var permissionOverrides = account.GetPermissionOverrides();
        var basePermissions = account.GetBasePermissions();
        var effectivePermissions = account.GetEffectivePermissions();
        var banStatus = account.GetBanStatus();
        return new AccountSummary(
            account.Uid,
            account.Username ?? "",
            account.Email ?? "",
            account.IsEmailVerified,
            account.EmailVerifiedAt,
            player?.Name ?? "",
            player?.Level,
            account.HasPassword(),
            role,
            permissionOverrides,
            basePermissions,
            effectivePermissions,
            account.DispatchTokenExpireAt,
            account.ComboTokenExpireAt,
            Listener.GetActiveConnection(account.Uid) != null,
            banStatus.IsActive,
            banStatus.Reason,
            banStatus.ExpireAt,
            banStatus.CreatedAt,
            banStatus.IsPermanent,
            banStatus.RemainingSeconds,
            AccountBanHelper.ParseIdentityKeys(account.KnownIdentityKeys).Count,
            player?.WorldLevel ?? 0,
            account.RegistrationIp ?? "", account.LastIp ?? "",
            AccountBanHelper.NormalizeIpAddress(Listener.GetActiveConnection(account.Uid)?.RemoteEndPoint.Address.ToString()) ?? "",
            account.CreatedAt, account.LastSeenAt,
            account.KnownIpAddresses?.ToList() ?? [], account.KnownDeviceIds?.ToList() ?? [],
            AccountBanHelper.ParseIdentityKeys(account.KnownIdentityKeys).Where(k => k.StartsWith("device:")).ToList(),
            account.DeleteOnLogout);
    }

    private static List<OnlinePlayer> GetOnlinePlayers()
    {
        return Listener.GetConnections()
            .Where(connection => connection.Player != null && connection.State == SessionStateEnum.ACTIVE)
            .Select(connection => new OnlinePlayer(
                connection.Player!.Uid,
                connection.Player.Data.Name ?? "",
                connection.Player.Data.Level,
                connection.RemoteEndPoint.ToString(),
                connection.State.ToString()))
            .ToList();
    }

    private static long GetUptimeSeconds()
    {
        return Math.Max(0, (long)(DateTime.Now - ProcessStartTime).TotalSeconds);
    }

    private static long GetLastSaveUnix()
    {
        try
        {
            return new DateTimeOffset(new DateTime(DatabaseHelper.LastSaveTick, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }
        catch
        {
            return 0;
        }
    }

    private static string NormalizeRole(string? role)
    {
        return CommandRoles.NormalizeRole(role, ConfigManager.Config.ServerOption.DefaultPermissionRole,
            GetConfiguredRoles());
    }

    private static List<string> GetRoleNames()
    {
        return GetConfiguredRoles().Keys
            .OrderBy(role => role == CommandRoles.User ? 0 :
                role == CommandRoles.Moderator ? 1 :
                role == CommandRoles.Administrator ? 2 :
                role == CommandRoles.Developer ? 3 : 4)
            .ThenBy(role => role, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyDictionary<string, HashSet<string>> GetConfiguredRoles()
    {
        return ConfigManager.Config.ServerOption.PermissionRoles ?? CommandRoles.DefaultRoles();
    }

    private static IResult Success<T>(T data)
    {
        return Results.Json(new AdminResult<T>(0, "OK", data));
    }

    #endregion

    public class AdminLoginRequest
    {
        public string? admin_key { get; set; }
    }

    public class AdminRegistrationRequest
    {
        public bool enabled { get; set; }
    }
}
