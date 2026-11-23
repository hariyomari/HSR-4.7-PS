using System.Diagnostics;
using System.Runtime.InteropServices;
using March7thHoney.Command;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using March7thHoney.WebServer.Pages;
using March7thHoney.WebServer.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

/// <summary>
///     Admin console API. Every /admin/api/* endpoint requires a session token issued by /admin/api/login
///     (Bearer header or X-Admin-Session). The feature areas live in the partial files next to this one.
/// </summary>
public static partial class AdminRoutes
{
    private static readonly DateTime ProcessStartTime = Process.GetCurrentProcess().StartTime;

    public static void MapAdminRoutes(this IEndpointRouteBuilder app)
    {
        // One route: the matcher treats "/admin" and "/admin/" as equivalent, so registering both
        // is an ambiguous match (500). This single template serves both.
        app.MapGet("/admin", () => Results.Redirect("/admin/index.html"));
        app.MapGet("/admin/index.html",
            () => Results.Text(AdminPageRenderer.Render(ConfigManager.Config.GameServer.GameServerName),
                "text/html; charset=utf-8"));

        app.MapPost("/admin/api/login", Login);
        app.MapPost("/admin/api/logout", Logout);
        app.MapGet("/admin/api/session", GetSession);
        app.MapGet("/admin/api/overview", GetOverview);
        app.MapPost("/admin/api/registration", SetRegistration);
        app.MapPost("/admin/api/registration-identity-limit", SetRegistrationIdentityLimit);

        MapAccountRoutes(app);
        MapStoryRoutes(app);
        MapPlayerRoutes(app);
        MapPlayerEditorRoutes(app);
        MapMailRoutes(app);
        MapAnnounceRoutes(app);
        MapAccessRoutes(app);
        MapCommandRoutes(app);
        MapGridFightRoutes(app);
        MapSettingsRoutes(app);
        MapHotfixRoutes(app);
        MapLogRoutes(app);
        MapMaintenanceRoutes(app);
        MapWarpRoutes(app);
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
            return Error("Not authorized", statusCode: StatusCodes.Status401Unauthorized);

        return Success(new AdminSessionData(
            session.ExpiresAt.ToUnixTimeSeconds(),
            ConfigManager.Config.GameServer.GameServerName,
            GameConstants.GAME_VERSION,
            ConfigManager.Config.ServerOption.Language,
            AdminSessionManager.ActiveSessionCount));
    }

    private static IResult GetOverview(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var accounts = GetAllAccounts()
            .Select(ToAccountSummary)
            .OrderBy(account => account.uid)
            .ToList();
        var onlinePlayers = GetOnlinePlayers();
        using var process = Process.GetCurrentProcess();
        var option = ConfigManager.Config.ServerOption;
        var (machineTotalMb, machineAvailableMb) = MuipManager.GetMachineMemory();

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
            option.ServerTime.FixedDate,
            ConfigManager.IsPublicMode);

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

    private static IResult SetRegistration(HttpContext ctx, AdminRegistrationRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        request ??= new AdminRegistrationRequest();
        ConfigManager.Config.ServerOption.Auth.EnableRegistration = request.enabled;
        ConfigManager.SaveConfig();

        return Success(new RegistrationData(ConfigManager.Config.ServerOption.Auth.EnableRegistration));
    }

    private static IResult SetRegistrationIdentityLimit(HttpContext ctx, AdminRegistrationRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        request ??= new AdminRegistrationRequest();
        ConfigManager.Config.ServerOption.Auth.AntiAbuse.LimitRegistrationPerIdentity = request.enabled;
        ConfigManager.SaveConfig();

        return Success(new IdentityLimitData(
            ConfigManager.Config.ServerOption.Auth.AntiAbuse.LimitRegistrationPerIdentity));
    }

    #region Shared helpers

    private static bool IsAuthorized(HttpContext ctx, out IResult error)
    {
        if (AdminSessionManager.IsAuthorized(GetAdminToken(ctx)))
        {
            error = Success(new EmptyData());
            return true;
        }

        error = Error("Not authorized", statusCode: StatusCodes.Status401Unauthorized);
        return false;
    }

    private static string? GetAdminToken(HttpContext ctx)
    {
        var authorization = ctx.Request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authorization["Bearer ".Length..].Trim();

        var header = ctx.Request.Headers["X-Admin-Session"].FirstOrDefault();
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
        return March7thHoneyListener.GetSnapshot()
            .OfType<Connection>()
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

    private static IResult Error(string message, int retcode = -201, int statusCode = StatusCodes.Status400BadRequest)
    {
        return Results.Json(new StatusResult(retcode, message), statusCode: statusCode);
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
