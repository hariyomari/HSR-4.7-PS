using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Util;
using March7thHoney.Util.Security;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Http;

internal static class BanRegression
{
    public static async Task Run(HttpClient client, HttpClient admin, string password, Action<string, bool> check)
    {
        var auth = ConfigManager.Config.ServerOption.Auth;
        var limitRegistration = auth.AntiAbuse.LimitRegistrationPerIdentity;
        var autoCreate = ConfigManager.Config.ServerOption.AutoCreateUser;
        var packetLogging = ConfigManager.Config.ServerOption.LogOption.EnableGamePacketLog;
        var loginRateLimit = auth.AntiAbuse.LoginRateLimitEnabled;
        var loginRateMax = auth.AntiAbuse.LoginRateLimitMaxRequests;
        ConfigManager.Config.ServerOption.LogOption.EnableGamePacketLog = false;
        var logs = new ConcurrentQueue<string>();
        var tokens = new List<string>();
        void Capture(string module, LoggerLevel level, string message)
        {
            if (module == "Auth" && level == LoggerLevel.WARN) logs.Enqueue(message);
        }
        PluginEventCommon.OnLogEntry += Capture;
        var rules = new List<int>();
        async Task<JsonElement> Send(string path, object body, Dictionary<string, string>? headers = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            foreach (var (key, value) in headers ?? []) request.Headers.Add(key, value);
            using var response = await client.SendAsync(request);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }
        bool Ok(JsonElement json) => json.GetProperty("retcode").GetInt32() == 0;
        async Task<int> Block(string kind, string value)
        {
            using var response = await admin.PostAsJsonAsync("/admin/api/identity-blocks",
                new { kind, value, reason = "independent ban regression", permanent = true });
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var id = json.RootElement.GetProperty("data").GetProperty("block").GetProperty("id").GetInt32();
            rules.Add(id);
            return id;
        }
        async Task Unblock(int id)
        {
            using var response = await admin.DeleteAsync($"/admin/api/identity-blocks/{id}");
            response.EnsureSuccessStatusCode();
            rules.Remove(id);
        }
        var source = AccountHelper.CreateAccount("ban-source", "ban-source@example.test", password, 0, null,
            identityKeys: AccountBanHelper.BuildIdentityKeys(["shared-pc-device", "testdevicefp"], "127.0.0.1"));
        var healthy = AccountHelper.CreateAccount("ban-healthy", "ban-healthy@example.test", password, 0, null,
            identityKeys: AccountBanHelper.BuildIdentityKeys(["healthy-android-device"], "198.51.100.8"));
        source.Ban("UID-only regression ban", 0);
        auth.AntiAbuse.LimitRegistrationPerIdentity = false;
        try
        {
            foreach (var device in new[] { "shared-pc-device", "healthy-android-device" })
            {
                using var response = await client.PostAsJsonAsync("/hkrpg_global/mdk/shield/api/login",
                    new { account = healthy.Username, password, device_id = device, is_crypto = false });
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                check($"UID ban does not reject another account using {device} on the same IP",
                    json.RootElement.GetProperty("retcode").GetInt32() == 0 && !healthy.IsBanned);
            }
            check("sharing a banned account's device alone does not reject another UID on a different IP",
                !AccountBanHelper.TryGetActiveBan(healthy,
                    AccountBanHelper.BuildIdentityKeys(["shared-pc-device"], "198.51.100.44"), out _));
            using var registration = await client.PostAsJsonAsync("/account/register",
                new { username = "ban-shared-registration", email = "ban-shared-registration@example.test", password,
                    device_id = "shared-pc-device" });
            using var registered = JsonDocument.Parse(await registration.Content.ReadAsStringAsync());
            check("UID ban does not prohibit registration from a shared device or IP",
                registered.RootElement.GetProperty("retcode").GetInt32() == 0);

            ConfigManager.Config.ServerOption.AutoCreateUser = true;
            check("UID ban does not prohibit auto-creation from a shared device or IP",
                Ok(await Send("/hkrpg_global/mdk/shield/api/login",
                    new { account = "ban-auto-registration", password, device_id = "shared-pc-device" })));
            ConfigManager.Config.ServerOption.AutoCreateUser = false;

            string[] passwordRoutes = ["/hkrpg_global/mdk/shield/api/login",
                "/hkrpg_cn/account/ma-passport/api/appLoginByPassword", "/account/ma-cn-passport/app/loginByPassword"];
            foreach (var route in passwordRoutes)
            {
                check($"{route}: another UID can share a banned account's device and IP",
                    Ok(await Send(route, new { account = healthy.Username, password, device_id = "shared-pc-device" })));
                var knownBefore = source.KnownIdentityKeys;
                var rejected = await Send(route, new { account = source.Username, password, device_id = "new-android-device" });
                check($"{route}: banned UID remains rejected on a new device without learning its ID",
                    rejected.GetProperty("retcode").GetInt32() == -203 && source.KnownIdentityKeys == knownBefore);
            }

            string[] verifyRoutes = ["/hkrpg_global/mdk/shield/api/verify", "/account/ma-cn-session/app/verify",
                "/hkrpg_cn/account/ma-passport/token/verifySToken", "/hkrpg_cn/combo/granter/login/v2/login"];
            object VerifyBody(string route, AccountData account, string token, string device = "shared-pc-device") => route switch
            {
                "/account/ma-cn-session/app/verify" => new { mid = account.Uid.ToString(), token = new { token }, refresh = false },
                "/hkrpg_cn/account/ma-passport/token/verifySToken" => new { mid = account.Uid.ToString(), stoken = token, refresh = false },
                "/hkrpg_cn/combo/granter/login/v2/login" => new { app_id = 1, channel_id = 1, device,
                    data = JsonSerializer.Serialize(new { uid = account.Uid.ToString(), token }), sign = "" },
                _ => new { uid = account.Uid.ToString(), token }
            };
            foreach (var route in verifyRoutes)
            {
                var healthyToken = healthy.GenerateDispatchToken();
                tokens.Add(healthyToken);
                check($"{route}: shared device and IP do not invalidate another UID's token",
                    Ok(await Send(route, VerifyBody(route, healthy, healthyToken),
                        new() { ["x-rpc-device_id"] = "shared-pc-device" })));
                // Model a still-valid token issued before an account ban is observed.
                var bannedToken = source.GenerateDispatchToken();
                tokens.Add(bannedToken);
                var rejected = await Send(route, VerifyBody(route, source, bannedToken));
                check($"{route}: banned UID is rejected and its token is revoked",
                    rejected.GetProperty("retcode").GetInt32() == -203 && !source.ValidateGameToken(bannedToken));
            }
            var wrongPassword = await Send(passwordRoutes[0], new { account = source.Username, password = "wrong-password" });
            check("invalid credentials are not reported as a ban", wrongPassword.GetProperty("retcode").GetInt32() == -201);

            var deviceRule = await Block("device", "shared-pc-device");
            var deviceRejected = await Send(passwordRoutes[0],
                new { account = healthy.Username, password, device_id = "shared-pc-device" });
            check("independent device rule rejects the current device", deviceRejected.GetProperty("retcode").GetInt32() == -203);
            var omitted = await Send(passwordRoutes[0], new { account = healthy.Username, password });
            check("independent device rule cannot be bypassed by omitting the recorded device",
                omitted.GetProperty("retcode").GetInt32() == -203);
            foreach (var route in verifyRoutes)
            {
                var token = healthy.GenerateDispatchToken();
                var rejected = await Send(route, VerifyBody(route, healthy, token, "healthy-android-device"));
                check($"{route}: independent device rule still rejects token exchange",
                    rejected.GetProperty("retcode").GetInt32() == -203 && !healthy.ValidateGameToken(token));
            }
            await Unblock(deviceRule);
            check("removing an independent device rule restores login despite the other UID's ban",
                Ok(await Send(passwordRoutes[0], new { account = healthy.Username, password })));

            var ipRule = await Block("ip", "127.0.0.1");
            var ipRejected = await Send(passwordRoutes[0], new { account = healthy.Username, password });
            check("independent IP rule still rejects a healthy UID", ipRejected.GetProperty("retcode").GetInt32() == -203);
            await Unblock(ipRule);

            async Task<HttpStatusCode> Exchange(AccountData account)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/validate_account_token");
                request.Headers.Add("X-March7thHoney-Server-Secret",
                    ConfigManager.Config.ServerOption.ServerConfig.ServerExchangeSecret);
                var token = account.GenerateDispatchToken();
                tokens.Add(token);
                request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["accountUid"] = account.Uid.ToString(), ["token"] = token, ["clientIp"] = "127.0.0.1"
                });
                using var response = await client.SendAsync(request);
                return response.StatusCode;
            }
            check("split server token validation permits another UID on a banned account's IP",
                await Exchange(healthy) == HttpStatusCode.OK);
            check("split server token validation still rejects the banned UID",
                await Exchange(source) == HttpStatusCode.Forbidden);

            // Persist the exact hashes that old versions accepted, without using the new normalizer.
            string[] placeholders = ["testdevicefp", "asd9123cfa767", "38d7ecd67b187",
                "00000000-0000-0000-0000-000000000000", "0000000000000000"];
            var legacyKeys = placeholders.Select(value => "device:" + AuthSecurity.HashToken(value)).ToArray();
            healthy.KnownIdentityKeys = string.Join(",", legacyKeys);
            healthy.KnownDeviceIds = placeholders.ToList();
            DatabaseHelper.SaveDatabaseType(healthy);
            for (var i = 0; i < legacyKeys.Length; i++)
                DatabaseHelper.SaveInstance(new IdentityBlockData
                {
                    Uid = -20000 - i, Kind = "device", Value = placeholders[i], IdentityKey = legacyKeys[i],
                    Reason = "legacy public placeholder rule", CreatedAt = Extensions.GetUnixSec()
                });
            DatabaseHelper.Instance!.SaveDatabase();
            DatabaseHelper.UidInstanceMap.Clear();
            DatabaseHelper.Instance.Initialize(startSaveThread: false);
            source = AccountData.GetAccountByUid(source.Uid)!;
            healthy = AccountData.GetAccountByUid(healthy.Uid)!;
            check("persisted placeholder hashes do not activate device rules after reload",
                !IdentityBlockHelper.TryGetActiveBan(legacyKeys, out _) &&
                IdentityBlockHelper.GetSnapshot().Where(rule => legacyKeys.Contains(rule.IdentityKey)).All(rule => !rule.GetStatus().IsActive));

            foreach (var placeholder in placeholders)
            {
                // One valid ID plus placeholders from headers, a body property and nested device JSON.
                var result = await Send(passwordRoutes[0], new
                {
                    account = healthy.Username, password, device_fp = placeholder.ToUpperInvariant(),
                    device = JsonSerializer.Serialize(new { device_id = "valid-device-after-upgrade", fp = placeholder })
                }, new() { ["x-rpc-device_fp"] = placeholder });
                check($"public fingerprint {placeholder} does not reject login or remain in history",
                    Ok(result) && AccountBanHelper.CreateDeviceIdentityKey(placeholder) == null &&
                    !healthy.KnownDeviceIds.Contains(placeholder) &&
                    !AccountBanHelper.ParseIdentityKeys(healthy.KnownIdentityKeys).Intersect(legacyKeys).Any());
                using var rawRule = await admin.PostAsJsonAsync("/admin/api/identity-blocks",
                    new { kind = "device", value = placeholder, permanent = true });
                using var rawJson = JsonDocument.Parse(await rawRule.Content.ReadAsStringAsync());
                using var hashRule = await admin.PostAsJsonAsync("/admin/api/identity-blocks", new
                {
                    kind = "device", identity_key = "device:" + AuthSecurity.HashToken(placeholder), permanent = true
                });
                using var hashJson = JsonDocument.Parse(await hashRule.Content.ReadAsStringAsync());
                check($"public fingerprint {placeholder} cannot be blocked by value or legacy hash",
                    !Ok(rawJson.RootElement) && !Ok(hashJson.RootElement));
            }
            var placeholderOnly = await Send("/account/register", new
            {
                username = "ban-placeholder-only", email = "ban-placeholder-only@example.test", password, device_id = " TESTDEVICEFP "
            });
            check("a public fingerprint cannot satisfy the required registration identity",
                !Ok(placeholderOnly) && AccountData.GetAccountByUserName("ban-placeholder-only") == null);
            var placeholderRegistration = await Send("/account/register", new
            {
                username = "ban-placeholder-real", email = "ban-placeholder-real@example.test", password,
                device_id = "valid-new-registration-device", device_fp = "testdevicefp"
            });
            check("a valid registration identity works alongside an ignored legacy placeholder rule", Ok(placeholderRegistration));

            // The same ignored placeholder must not join unrelated IPs into one rate-limit bucket.
            auth.AntiAbuse.LoginRateLimitEnabled = true;
            auth.AntiAbuse.LoginRateLimitMaxRequests = 1;
            AuthRateLimiter.ClearAll(false);
            var firstContext = new DefaultHttpContext();
            firstContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.81");
            var secondContext = new DefaultHttpContext();
            secondContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.82");
            check("public fingerprints do not merge independent login rate limits",
                !AuthRateLimiter.TryReject(firstContext, AuthRateLimitAction.Login,
                    AccountBanHelper.BuildIdentityKeys(["testdevicefp"], "192.0.2.81"), null, out _) &&
                !AuthRateLimiter.TryReject(secondContext, AuthRateLimitAction.Login,
                    AccountBanHelper.BuildIdentityKeys(["testdevicefp"], "192.0.2.82"), null, out _));
            auth.AntiAbuse.LoginRateLimitEnabled = false;
            AuthRateLimiter.ClearAll(false);

            check("rejection logs identify UID account bans with packet debug disabled",
                logs.Any(line => line.Contains($"uid={source.Uid} ") && line.Contains("source=account_ban match=uid")));
            check("rejection logs identify independent device rules and historical matches",
                logs.Any(line => line.Contains($"rule_id={deviceRule} ") && line.Contains("source=device_block") && line.Contains("match=known_device")));
            check("rejection logs identify independent IP rules and the actual source IP",
                logs.Any(line => line.Contains($"rule_id={ipRule} ") && line.Contains("remote=127.0.0.1 source=ip_block match=current")));
            check("rejection logs omit credentials and raw device identifiers",
                logs.Count > 0 && logs.All(line => !line.Contains(password) && !line.Contains("shared-pc-device") &&
                    !line.Contains("testdevicefp") && !line.Contains("token=") && tokens.All(token => !line.Contains(token))));
        }
        finally
        {
            foreach (var id in rules.ToArray()) await Unblock(id);
            source.ClearBan();
            auth.AntiAbuse.LimitRegistrationPerIdentity = limitRegistration;
            auth.AntiAbuse.LoginRateLimitEnabled = loginRateLimit;
            auth.AntiAbuse.LoginRateLimitMaxRequests = loginRateMax;
            ConfigManager.Config.ServerOption.AutoCreateUser = autoCreate;
            ConfigManager.Config.ServerOption.LogOption.EnableGamePacketLog = packetLogging;
            PluginEventCommon.OnLogEntry -= Capture;
        }
    }
}
