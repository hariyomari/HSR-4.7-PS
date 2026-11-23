using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using March7thHoney.Configuration;
using March7thHoney.Database.Account;
using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using March7thHoney.WebServer.Objects;
using March7thHoney.WebServer.Server;
using March7thHoney.WebServer.Services;

internal static class SecuritySessionChecks
{
    // Uses only the regression runner's scratch database/config and loopback HTTP server.
    public static async Task Run(HttpClient client, int fixtureUid, string fixturePassword,
        Action<string, bool> check)
    {
        await CredentialChecks(client, fixtureUid, fixturePassword, check);

        var config = ConfigManager.Config;
        var rsa = config.WebSecurity.EnableRsaLoginDecrypt;
        try
        {
            foreach (var enabled in new[] { false, true })
            {
                config.WebSecurity.EnableRsaLoginDecrypt = enabled;
                foreach (var password in new[] { "", "not-the-password" })
                {
                    using var response = await client.PostAsJsonAsync("/sr-tools",
                        new { username = fixtureUid.ToString(), password });
                    check($"sr-tools requires password with RSA={enabled} ({(password.Length == 0 ? "empty" : "wrong")})",
                        response.StatusCode == HttpStatusCode.Unauthorized);
                }
                // No data is submitted, and the fixture is offline: valid authentication reaches the
                // expected offline guard, not the import code or a misleading password failure.
                using var valid = await client.PostAsJsonAsync("/sr-tools",
                    new { username = fixtureUid.ToString(), password = fixturePassword });
                check($"sr-tools accepts valid credentials independently of RSA={enabled}",
                    valid.StatusCode == HttpStatusCode.BadRequest &&
                    (await valid.Content.ReadAsStringAsync()).Contains("player offline"));
            }
        }
        finally { config.WebSecurity.EnableRsaLoginDecrypt = rsa; }

        const int uid = 10003;
        AccountHelper.CreateAccount("session-security", "session-security@example.invalid", "fixture-initial-only", uid, null);
        var account = AccountData.GetAccountByUid(uid)!;
        var old = PlayerConsoleSessionManager.Create(account);
        check("new player session resolves", PlayerConsoleSessionManager.TryResolve(old.Token, out _, out _));
        AccountHelper.SetPassword(account, "fixture-changed-only");
        check("password changes invalidate old player console token",
            !PlayerConsoleSessionManager.TryResolve(old.Token, out _, out _));
        var resetSession = PlayerConsoleSessionManager.Create(account);
        account.MarkEmailVerified();
        var resetToken = account.GeneratePasswordResetToken();
        new AccountRecoveryHandler().ResetPassword(new ResetPasswordReqJson
        {
            uid = uid.ToString(), token = resetToken, password = "fixture-reset-only", confirm_password = "fixture-reset-only"
        });
        check("password recovery completed", account.VerifyPassword("fixture-reset-only"));
        check("password recovery invalidates old player console token",
            !PlayerConsoleSessionManager.TryResolve(resetSession.Token, out _, out _));
        var current = PlayerConsoleSessionManager.Create(account);
        check("fresh session works after recovery", PlayerConsoleSessionManager.TryResolve(current.Token, out _, out _));
        PlayerConsoleSessionManager.RevokeForUid(uid);

        var originalKey = config.MuipServer.AdminKey;
        MuipManager.Sessions.Clear();
        try
        {
            var created = MuipManager.CreateSession("XML");
            var id = created.Data!.SessionId;
            check("MUIP new session authenticates", MuipManager.AuthAdmin(id, originalKey).Code == 0);
            var session = MuipManager.GetSession(id)!;
            config.MuipServer.AdminKey = "regression-rotated-only";
            check("MUIP lookup rejects old key generation", MuipManager.GetSession(id) == null);
            MuipManager.Sessions[id] = session;
            check("MUIP authentication rejects session created before key rotation",
                MuipManager.AuthAdmin(id, config.MuipServer.AdminKey).Code != 0);
            MuipManager.Sessions[id] = session;
            check("MUIP command rejects old key generation",
                (await MuipManager.ExecuteCommandAsync(id, "/help", 0)).Code != 0);
            MuipManager.Sessions[id] = session;
            check("MUIP server info rejects old key generation", MuipManager.GetInformation(id).Code != 0);
            MuipManager.Sessions[id] = session;
            check("MUIP player info rejects old key generation", MuipManager.GetPlayerInformation(id, fixtureUid).Code != 0);
            MuipManager.Sessions.Clear();
            config.MuipServer.AdminKey = originalKey;

            var rotationId = MuipManager.CreateSession("XML").Data!.SessionId;
            MuipManager.AuthAdmin(rotationId, originalKey);
            using (var rotated = await client.PostAsJsonAsync("/admin/api/maintenance/rotate-admin-key", new { }))
                check("admin rotation endpoint succeeds", rotated.IsSuccessStatusCode);
            using (var kept = await client.GetAsync("/admin/api/session"))
                check("admin rotation keeps the caller's own session", kept.IsSuccessStatusCode);
            check("admin rotation removes MUIP sessions", !MuipManager.Sessions.ContainsKey(rotationId));
            config.MuipServer.AdminKey = originalKey;

            MuipManager.Sessions.Clear();
            var clientRejected = 0;
            for (var i = 0; i < 40; i++)
            {
                using var response = await client.PostAsJsonAsync("/muip/create_session", new { key_type = "XML" });
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (json.RootElement.GetProperty("code").GetInt32() != 0) clientRejected++;
            }
            check("MUIP HTTP session admission bounds each client", clientRejected > 0 && MuipManager.Sessions.Count <= 32);
            MuipManager.Sessions.Clear();

            Parallel.For(0, 128, _ => MuipManager.CreateSession("XML", "concurrent-regression-client"));
            check("MUIP concurrent admission preserves per-client capacity", MuipManager.Sessions.Count == 32);
            MuipManager.Sessions.Clear();

            var rejected = 0;
            for (var i = 0; i < 1100; i++)
                if (MuipManager.CreateSession("XML").Code != 0) rejected++;
            check("anonymous MUIP session storage is bounded", rejected > 0 && MuipManager.Sessions.Count <= 1024);
            foreach (var pending in MuipManager.Sessions.Values)
                pending.ExpireTimeStamp = DateTime.Now.AddMinutes(-1).ToUnixSec();
            var afterExpiry = MuipManager.CreateSession("XML");
            check("MUIP admission cleans abandoned expired sessions", afterExpiry.Code == 0 && MuipManager.Sessions.Count == 1);

            foreach (var pending in MuipManager.Sessions.Values)
                pending.ExpireTimeStamp = DateTime.Now.AddMinutes(-1).ToUnixSec();
            // Trigger the actual background timer without a minute-long test delay; no session API
            // is invoked while waiting, so lazy cleanup cannot accidentally satisfy this assertion.
            var timer = typeof(MuipManager).GetField("SessionCleanupTimer", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null) as Timer;
            check("MUIP has proactive cleanup timer", timer != null);
            if (timer != null)
            {
                try
                {
                    timer.Change(TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                    for (var i = 0; i < 100 && MuipManager.Sessions.Count > 0; i++) await Task.Delay(20);
                    check("MUIP timer removes abandoned sessions without requests", MuipManager.Sessions.Count == 0);
                }
                finally { timer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)); }
            }

            const string bruteForceClient = "muip-brute-force-regression-client";
            var wrongKeyId = MuipManager.CreateSession("XML", bruteForceClient).Data!.SessionId;
            check("MUIP wrong key is refused", MuipManager.AuthAdmin(wrongKeyId, "not-the-admin-key").Code == 2);
            check("MUIP wrong key consumes the session", !MuipManager.Sessions.ContainsKey(wrongKeyId));
            for (var i = 0; i < 8; i++)
                MuipManager.AuthAdmin(MuipManager.CreateSession("XML", bruteForceClient).Data!.SessionId,
                    "not-the-admin-key", bruteForceClient);
            var blockedId = MuipManager.CreateSession("XML", bruteForceClient).Data!.SessionId;
            var blocked = MuipManager.AuthAdmin(blockedId, originalKey, bruteForceClient);
            check("MUIP refuses the correct key after repeated failures", blocked.Code == 3 &&
                MuipManager.GetSession(blockedId)?.IsAuthorized != true);
            var blockedAdmin = AdminSessionManager.TryCreateSession(originalKey, bruteForceClient, out _, out _);
            check("MUIP failures share the admin page failure counter", !blockedAdmin);
            var otherClientId = MuipManager.CreateSession("XML", "muip-other-regression-client").Data!.SessionId;
            check("MUIP failure block is per client",
                MuipManager.AuthAdmin(otherClientId, originalKey, "muip-other-regression-client").Code == 0);
            MuipManager.Sessions.Clear();

            var enabledSessionId = MuipManager.CreateSession("XML").Data!.SessionId;
            MuipManager.AuthAdmin(enabledSessionId, originalKey);
            config.MuipServer.AdminKey = "";
            check("disabling MUIP revokes existing administrator sessions", MuipManager.GetSession(enabledSessionId) == null);
            check("disabled MUIP rejects anonymous creation", MuipManager.CreateSession("XML").Code != 0);
            var playerOnly = new MuipSession
            {
                SessionId = "disabled-muip-player", IsAdmin = false, IsAuthorized = true,
                ExpireTimeStamp = DateTime.Now.AddMinutes(1).ToUnixSec()
            };
            MuipManager.Sessions[playerOnly.SessionId] = playerOnly;
            check("player console bridge survives disabled MUIP", MuipManager.GetSession(playerOnly.SessionId) != null);
        }
        finally
        {
            config.MuipServer.AdminKey = originalKey;
            MuipManager.Sessions.Clear();
        }
    }

    // The console and sr-tools endpoints share one uid+password verifier; every route must apply the same rules.
    private static async Task CredentialChecks(HttpClient adminClient, int fixtureUid, string fixturePassword,
        Action<string, bool> check)
    {
        var antiAbuse = ConfigManager.Config.ServerOption.Auth.AntiAbuse;
        var (limitEnabled, limitMax) = (antiAbuse.LoginRateLimitEnabled, antiAbuse.LoginRateLimitMaxRequests);
        using var client = new HttpClient { BaseAddress = adminClient.BaseAddress, Timeout = TimeSpan.FromSeconds(60) };

        async Task<(HttpStatusCode Status, string Body)> Post(string path, object body)
        {
            using var response = await client.PostAsJsonAsync(path, body);
            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        static bool ConsoleRejected(string body) =>
            JsonDocument.Parse(body).RootElement.GetProperty("retcode").GetInt32() != 0;

        static bool RateLimited(string body) =>
            body.Contains("\"retcode\":-203") && body.Contains("Too many requests");

        // A uid alone must never be enough: passwordless accounts are refused everywhere.
        const int passwordlessUid = 10004;
        AccountHelper.CreateAccount("passwordless", "passwordless@example.invalid", null, passwordlessUid);
        AuthRateLimiter.ClearAll(false);
        foreach (var path in new[] { "/player/login", "/player/exec", "/player/catalog", "/player/session", "/player/self-reset" })
        {
            var (_, body) = await Post(path, new { username = passwordlessUid.ToString(), password = "", command = "/help" });
            check($"{path} rejects a passwordless account", ConsoleRejected(body));
        }
        foreach (var path in new[] { "/sr-tools", "/sr-tools-export" })
        foreach (var password in new[] { "", "anything" })
        {
            // Each miss counts towards the login limit; reset it so this asserts the password rule itself.
            AuthRateLimiter.ClearAll(false);
            check($"{path} rejects a passwordless account ({(password.Length == 0 ? "empty" : "any")} password)",
                (await Post(path, new { username = passwordlessUid.ToString(), password })).Status ==
                HttpStatusCode.Unauthorized);
        }

        AuthRateLimiter.ClearAll(false);
        foreach (var malformed in new[] { "[]", "42", "\"text\"", "{" })
        {
            using var content = new StringContent(malformed, System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/sr-tools-export", content);
            check($"sr-tools-export answers a non-object body ({malformed}) with 400",
                response.StatusCode == HttpStatusCode.BadRequest);
        }
        check("sr-tools-export rejects a missing password",
            (await Post("/sr-tools-export", new { username = fixtureUid.ToString() })).Status == HttpStatusCode.Unauthorized);
        check("sr-tools-export rejects a wrong password",
            (await Post("/sr-tools-export", new { username = fixtureUid.ToString(), password = "not-the-password" }))
            .Status == HttpStatusCode.Unauthorized);
        var exported = await Post("/sr-tools-export", new { username = fixtureUid.ToString(), password = fixturePassword });
        check("sr-tools-export accepts credentials in the POST body", exported.Status == HttpStatusCode.OK);
        using (var legacy = await client.GetAsync(
                   $"/sr-tools-export?uid={fixtureUid}&password={Uri.EscapeDataString(fixturePassword)}"))
            check("sr-tools-export no longer accepts query-string credentials",
                legacy.StatusCode == HttpStatusCode.MethodNotAllowed);

        try
        {
            antiAbuse.LoginRateLimitEnabled = true;
            antiAbuse.LoginRateLimitMaxRequests = 2;
            foreach (var missPath in new[] { "/player/exec", "/sr-tools", "/sr-tools-export" })
            {
                AuthRateLimiter.ClearAll(false);
                for (var i = 0; i < 3; i++)
                    await Post(missPath, new { username = fixtureUid.ToString(), password = "not-the-password", command = "/help" });

                var import = await Post("/sr-tools", new { username = fixtureUid.ToString(), password = fixturePassword });
                check($"sr-tools refuses the correct password once {missPath} misses block the client",
                    RateLimited(import.Body));
                var export = await Post("/sr-tools-export", new { username = fixtureUid.ToString(), password = fixturePassword });
                check($"sr-tools-export refuses the correct password once {missPath} misses block the client",
                    export.Status == HttpStatusCode.OK && RateLimited(export.Body));
                var console = await Post("/player/exec",
                    new { username = fixtureUid.ToString(), password = fixturePassword, command = "/help" });
                check($"console refuses the correct password once {missPath} misses block the client",
                    RateLimited(console.Body));
            }
        }
        finally
        {
            antiAbuse.LoginRateLimitEnabled = limitEnabled;
            antiAbuse.LoginRateLimitMaxRequests = limitMax;
            AuthRateLimiter.ClearAll(false);
        }

        var ipBlock = IdentityBlockHelper.Block("ip", "127.0.0.1", "regression", 0);
        try
        {
            check("sr-tools refuses a blocked IP before checking the password",
                (await Post("/sr-tools", new { username = fixtureUid.ToString(), password = fixturePassword })).Body
                .Contains("\"retcode\":-203"));
            check("sr-tools-export refuses a blocked IP before checking the password",
                (await Post("/sr-tools-export", new { username = fixtureUid.ToString(), password = fixturePassword }))
                .Body.Contains("\"retcode\":-203"));
        }
        finally
        {
            IdentityBlockHelper.Remove(ipBlock.Uid);
        }

        var fixture = AccountData.GetAccountByUid(fixtureUid)!;
        fixture.Ban("regression", 0);
        try
        {
            check("sr-tools refuses a banned account",
                (await Post("/sr-tools", new { username = fixtureUid.ToString(), password = fixturePassword })).Status ==
                HttpStatusCode.Forbidden);
            check("sr-tools-export refuses a banned account",
                (await Post("/sr-tools-export", new { username = fixtureUid.ToString(), password = fixturePassword }))
                .Status == HttpStatusCode.Forbidden);
        }
        finally
        {
            fixture.ClearBan();
        }

        // The default must not hand every player administrator commands.
        var serverOption = ConfigManager.Config.ServerOption;
        check("DefaultPermissions defaults to empty", new ServerOption().DefaultPermissions.Count == 0);
        var permissions = fixture.GetEffectivePermissions().Split(',');
        check("default-role accounts lack high-privilege permissions under the default config",
            !permissions.Contains("*") && !permissions.Contains("command.*") &&
            !permissions.Contains("command.account") && !permissions.Contains("command.others"));
        var configured = serverOption.DefaultPermissions;
        try
        {
            serverOption.DefaultPermissions = ["*", "command.account", "command.give"];
            var flagged = ConfigManager.GetPrivilegedDefaultPermissions();
            check("startup warning flags * and high-privilege DefaultPermissions",
                flagged.Contains("*") && flagged.Contains("command.account") && !flagged.Contains("command.give"));
            serverOption.DefaultPermissions = [];
            check("startup warning stays quiet for empty DefaultPermissions",
                ConfigManager.GetPrivilegedDefaultPermissions().Count == 0);
        }
        finally
        {
            serverOption.DefaultPermissions = configured;
        }
    }
}
