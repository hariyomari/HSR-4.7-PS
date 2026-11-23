using System.Text;
using System.Text.Json.Nodes;
using March7thHoney.Command;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using March7thHoney.WebServer.Http;
using March7thHoney.WebServer.Pages;
using March7thHoney.WebServer.Server;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

/// <summary>
///     The player self-service console: a page at /player plus the endpoints behind it. Everything runs with
///     the exact permissions of in-game chat (resolved through <see cref="AccountData.HasPermission" />), so a
///     player can only do what their role already lets them type in the game.
///     <para>
///         Two credential styles are accepted everywhere: a session token from /player/login (used by the page,
///         so the password travels once) and the original uid+password body the launcher's console tab sends.
///     </para>
/// </summary>
public static class ConsoleRoutes
{
    private static readonly Logger Logger = new("PlayerConsole");

    public static void MapConsoleRoutes(this IEndpointRouteBuilder app)
    {
        // One template serves "/player" and "/player/"; registering both is an ambiguous match.
        app.MapGet("/player", () => Results.Redirect("/player/index.html"));
        app.MapGet("/player/index.html", RenderPage);

        // Cast to Delegate: a Func<HttpContext, Task<IResult>> otherwise binds as a RequestDelegate, which
        // discards the returned result and answers every call with an empty 200.
        app.MapPost("/player/login", (Delegate)Login);
        app.MapPost("/player/logout", (Delegate)Logout);
        app.MapPost("/player/session", (Delegate)Session);
        app.MapPost("/player/catalog", (Delegate)Catalog);
        app.MapPost("/player/exec", (Delegate)Exec);
        app.MapPost("/player/self-reset", (Delegate)SelfReset);

        // Keep the original launcher integration working while /player is the canonical browser route.
        app.MapGet("/console", () => Results.Redirect("/player/index.html"));
        app.MapGet("/console/index.html", () => Results.Redirect("/player/index.html"));
        app.MapPost("/console/login", (Delegate)Login);
        app.MapPost("/console/logout", (Delegate)Logout);
        app.MapPost("/console/session", (Delegate)Session);
        app.MapPost("/console/catalog", (Delegate)Catalog);
        app.MapPost("/console/exec", (Delegate)Exec);
        app.MapPost("/console/self-reset", (Delegate)SelfReset);
    }

    private static IResult RenderPage()
    {
        var config = ConfigManager.Config;
        return Results.Text(
            PlayerConsolePage.Render(
                config.GameServer.GameServerName,
                config.ServerOption.Auth.RegistrationPageIcon,
                config.ServerOption.Language ?? "EN"),
            "text/html; charset=utf-8");
    }

    // ------------------------------------------------------------------ auth

    private static async Task<IResult> Login(HttpContext ctx)
    {
        var body = await ReadBodyAsync(ctx.Request);
        var uidText = Text(body, "username", "uid");

        // Count every sign-in attempt, wrong or right: this is the one endpoint that trades a password
        // for a long-lived token, so it is the one worth throttling per IP/account.
        var identityKeys = AccountBanHelper.BuildIdentityKeys([], ctx.Connection.RemoteIpAddress?.ToString());
        if (AuthRateLimiter.TryReject(ctx, AuthRateLimitAction.Login, identityKeys, uidText, out var limited))
            return limited;

        if (!TryResolveCredentials(ctx, uidText, Text(body, "password"), true, out var account,
                out var credentialFingerprint, out var error))
            return error;
        if (!TryRejectBan(account, out var banned))
            return banned;

        var session = PlayerConsoleSessionManager.Create(account, credentialFingerprint);
        return Results.Json(new ConsoleEnvelope<ConsoleLoginData>(0, "OK",
            new ConsoleLoginData(session.Token, BuildPlayerData(account, session.ExpiresAt.ToUnixTimeSeconds()))));
    }

    private static async Task<IResult> Logout(HttpContext ctx)
    {
        PlayerConsoleSessionManager.Revoke(await ResolveTokenAsync(ctx));
        return ConsoleJson(0, "OK", "Signed out.");
    }

    private static async Task<IResult> Session(HttpContext ctx)
    {
        var body = await ReadBodyAsync(ctx.Request);
        if (!TryAuthenticate(ctx, body, out var account, out var expiresAt, out var error))
            return error;

        return Results.Json(new ConsoleEnvelope<ConsolePlayerData>(0, "OK", BuildPlayerData(account, expiresAt)));
    }

    // --------------------------------------------------------------- catalog

    private static async Task<IResult> Catalog(HttpContext ctx)
    {
        var body = await ReadBodyAsync(ctx.Request);
        if (!TryAuthenticate(ctx, body, out var account, out _, out var error))
            return error;

        var entries = MuipManager.CommandCatalogProvider?.Invoke() ?? [];
        var commands = entries
            .Select(entry => new ConsoleCommandItem(
                entry.Name,
                entry.Description,
                entry.Usage,
                entry.Permission,
                entry.Aliases.ToList(),
                entry.Subcommands.ToList(),
                account.HasPermission(entry.Permission)))
            .OrderBy(entry => entry.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var permissions = SplitPermissions(account.GetEffectivePermissions());
        return Results.Json(new ConsoleEnvelope<ConsoleCatalogData>(0, "OK", new ConsoleCatalogData(
            commands, commands.Count, commands.Count(command => command.allowed), account.GetRole(), permissions)));
    }

    // ------------------------------------------------------------ execution

    // Runs a command with the EXACT permissions of in-game chat. An empty command is a credential check
    // only, which is what the launcher's "Connect" button sends.
    private static async Task<IResult> Exec(HttpContext ctx)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request);
            if (!TryAuthenticate(ctx, body, out var account, out _, out var error))
                return error;

            var command = Text(body, "command").Trim();
            if (command.Length > WebConstants.MaxCommandLength)
                return ConsoleJson(1, "Command too long.", null, 400);

            // Use the same active-connection lookup the command pipeline uses.
            var online = Listener.GetActiveConnection(account.Uid)?.Player != null;

            // Signing in must succeed while offline: resetting save data does not need a live session,
            // so only real command execution is gated on being online.
            if (string.IsNullOrWhiteSpace(command))
            {
                var status = online
                    ? "Status: online."
                    : "Status: offline - commands require you to be in-game, but resetting save data does not.";
                return ConsoleJson(0, "OK", $"Connected as UID {account.Uid} (role: {account.GetRole()}). {status}");
            }

            if (!online)
                return ConsoleJson(1, "player offline", null, 400);

            // A non-admin session bound to the player's account and targeting their own uid:
            // MuipCommandSender then resolves permissions via Account.HasPermission, identical to chat.
            var result = await MuipManager.RunCommandAsync(
                MuipManager.CreateLocalSession("console", isAdmin: false, account), command, account.Uid);
            if (result.Code != 0)
                return ConsoleJson(1, result.Message, null, 400);

            return ConsoleJson(0, "OK", result.Output.TrimEnd());
        }
        catch (Exception ex)
        {
            Logger.Error(I18NManager.Translate("Server.Log.ConsoleCommandFailed"), ex);
            return ConsoleJson(1, "Internal server error.", null, 500);
        }
    }

    // Resets ONLY the caller's own gameplay data (self-only by construction). Works offline, so a
    // crash-locked account can be reset before logging back in.
    private static async Task<IResult> SelfReset(HttpContext ctx)
    {
        try
        {
            var body = await ReadBodyAsync(ctx.Request);
            if (!TryAuthenticate(ctx, body, out var account, out _, out var error))
                return error;

            if (!string.Equals(Text(body, "confirm"), "true", StringComparison.OrdinalIgnoreCase))
                return ConsoleJson(1, "Reset requires confirmation.", null, 400);

            await PlayerResetHelper.ResetGameplayAsync(account.Uid);
            PlayerConsoleSessionManager.RevokeForUid(account.Uid);

            return ConsoleJson(0, "OK", $"Your save data has been reset (UID {account.Uid}). Please log in again.");
        }
        catch (Exception ex)
        {
            Logger.Error(I18NManager.Translate("Server.Log.ConsoleSelfResetFailed"), ex);
            return ConsoleJson(1, "Internal server error.", null, 500);
        }
    }

    // --------------------------------------------------------------- helpers

    /// <summary>
    ///     Accepts either a console session token (page) or a uid+password body (launcher). Both end at the same
    ///     account, so every endpoint below stays self-only.
    /// </summary>
    private static bool TryAuthenticate(HttpContext ctx, JsonNode? body, out AccountData account, out long expiresAt,
        out IResult error)
    {
        account = null!;
        expiresAt = 0;
        error = default!;

        var token = ResolveToken(ctx, body);
        if (!string.IsNullOrWhiteSpace(token))
        {
            if (!PlayerConsoleSessionManager.TryResolve(token, out var session, out var resolved))
            {
                error = ConsoleJson(1, "Session expired. Please sign in again.", null, 401);
                return false;
            }

            account = resolved;
            expiresAt = session.ExpiresAt.ToUnixTimeSeconds();
        }
        // A password-authenticated call has no session behind it, so nothing to report an expiry for.
        else if (!TryResolveCredentials(ctx, Text(body, "username", "uid"), Text(body, "password"), false,
                     out account, out _, out error))
        {
            return false;
        }

        // Checked on every call, not just at sign-in: a ban handed out mid-session has to bite now rather
        // than when the token happens to expire.
        if (!TryRejectBan(account, out error))
            return false;

        return true;
    }

    private static bool TryResolveCredentials(HttpContext ctx, string uidText, string password, bool attemptCounted,
        out AccountData account, out string credentialFingerprint, out IResult error)
    {
        account = null!;
        credentialFingerprint = "";
        error = default!;

        if (!int.TryParse(uidText, out var uid))
        {
            error = ConsoleJson(1, "Username field must be the player's UID (integer).", null, 400);
            return false;
        }

        var resolved = AccountData.GetAccountByUid(uid);
        if (resolved == null)
        {
            error = ConsoleJson(1, "uid not found on server", null, 400);
            return false;
        }

        credentialFingerprint = PlayerConsoleSessionManager.GetCredentialFingerprint(resolved);
        if (!WebCredentialVerifier.TryVerify(ctx, resolved, password, "player_console", out var failure,
                attemptCounted))
        {
            error = failure.Response ?? ConsoleJson(1, failure.Message, null, failure.StatusCode);
            return false;
        }

        if (!string.Equals(credentialFingerprint, PlayerConsoleSessionManager.GetCredentialFingerprint(resolved),
                StringComparison.Ordinal))
        {
            error = ConsoleJson(1, "Credentials changed. Please sign in again.", null, 401);
            return false;
        }

        account = resolved;
        return true;
    }

    private static bool TryRejectBan(AccountData account, out IResult error)
    {
        error = default!;
        if (!AccountBanHelper.TryGetActiveBan(account, null, out var banStatus, "player_console"))
            return true;

        PlayerConsoleSessionManager.RevokeForUid(account.Uid);
        error = ConsoleJson(1, banStatus.FormatLoginMessage(), null, 403);
        return false;
    }

    private static ConsolePlayerData BuildPlayerData(AccountData account, long expiresAt)
    {
        var player = DatabaseHelper.Instance?.GetInstance<PlayerData>(account.Uid);
        return new ConsolePlayerData(
            account.Uid,
            account.Username ?? "",
            player?.Name ?? "",
            player?.Level ?? 0,
            player?.WorldLevel ?? 0,
            account.GetRole(),
            Listener.GetActiveConnection(account.Uid)?.Player != null,
            account.HasPassword(),
            SplitPermissions(account.GetEffectivePermissions()),
            expiresAt);
    }

    private static List<string> SplitPermissions(string? permissions)
    {
        return (permissions ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(permission => permission, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<string?> ResolveTokenAsync(HttpContext ctx)
    {
        return ResolveToken(ctx, await ReadBodyAsync(ctx.Request));
    }

    private static string? ResolveToken(HttpContext ctx, JsonNode? body)
    {
        if (AuthHeaders.TryGetBearerToken(ctx.Request, out var bearer))
            return bearer;

        if (ctx.Request.Headers.TryGetValue(WebConstants.ConsoleSessionHeader, out var sessionHeader))
        {
            var value = sessionHeader.ToString().Trim();
            if (value.Length > 0) return value;
        }

        var token = Text(body, "token");
        return token.Length > 0 ? token : null;
    }

    private static async Task<JsonNode?> ReadBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(body)) return null;

        try
        {
            return JsonNode.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    private static string Text(JsonNode? body, params string[] keys)
    {
        if (body is not JsonObject obj) return string.Empty;

        foreach (var key in keys)
        {
            if (obj.TryGetPropertyValue(key, out var value) && value != null)
            {
                var text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text)) return text;
            }
        }

        return string.Empty;
    }

    internal static IResult ConsoleJson(int retcode, string message, string? output, int statusCode = 200)
    {
        return Results.Json(new ConsoleResult(retcode, message, output ?? string.Empty), statusCode: statusCode);
    }
}
