using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Mail;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Internationalization;
using March7thHoney.Kcp;
using March7thHoney.Util;
using March7thHoney.WebServer;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// All writes go to a fresh temporary SQLite database. Never load the operator's configuration.
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var scratch = Path.Combine(Path.GetTempPath(), "m7h-auth-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
Environment.CurrentDirectory = scratch;
var config = ConfigManager.Config;
config.Path.LanguagePath = Path.Combine(repository, "Config/Languages");
config.Path.DatabasePath = scratch;
config.Path.LogPath = Path.Combine(scratch, "Logs");
config.Database.DatabaseType = "sqlite";
config.Database.DatabaseName = "regression.db";
config.HttpServer.UseSSL = false;
config.HttpServer.TrustedProxies = [];
config.MuipServer.AdminKey = "local-auth-regression-only";
config.GameServer.GameServerName = "账号安全回归测试";
config.GameServer.UsePacketEncryption = false;
config.ServerOption.Language = "CHS";
config.ServerOption.WelcomeMail.Enabled = false;
config.WebSecurity.EnableRsaLoginDecrypt = false;
var auth = config.ServerOption.Auth;
auth.PasswordHashIterations = 10000;
auth.SendVerificationEmailOnRegister = false;
auth.AntiAbuse.RateLimitEnabled = false;
auth.AntiAbuse.LoginRateLimitEnabled = false;
auth.AntiAbuse.LimitRegistrationPerIdentity = true;
auth.AntiAbuse.RegistrationIdentityMode = "DeviceOnly";
auth.AntiAbuse.RequireRegistrationIdentity = true;
I18NManager.LoadLanguage();

// Start with an old schema to exercise automatic column migration and missing historical IPs.
SQLitePCL.Batteries_V2.Init();
using (var connection = new SqliteConnection($"Data Source={Path.Combine(scratch, config.Database.DatabaseName)}"))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "CREATE TABLE Account (Uid INTEGER PRIMARY KEY, Username TEXT, Email TEXT); INSERT INTO Account VALUES (9001, 'legacy', 'legacy@example.test');";
    command.ExecuteNonQuery();
}
var database = new DatabaseHelper();
database.Initialize(startSaveThread: false);
AuthRateLimiter.Initialize();
var serve = args.Contains("--serve");
await using var app = WebProgram.BuildWebApp([], 0, serve ? "http://127.0.0.1:21001" : "http://127.0.0.1:0");
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(address) };
using var admin = new HttpClient { BaseAddress = new Uri(address) };
const string Password = "Regression-Password-42";
var failures = new List<string>();
void Check(string name, bool pass)
{
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures.Add(name);
}
async Task<JsonElement> Send(HttpClient http, string path, object? body = null, HttpMethod? method = null,
    Dictionary<string, string>? headers = null)
{
    using var request = new HttpRequestMessage(method ?? (body == null ? HttpMethod.Get : HttpMethod.Post), path);
    if (body != null) request.Content = JsonContent.Create(body);
    foreach (var (key, value) in headers ?? []) request.Headers.TryAddWithoutValidation(key, value);
    using var response = await http.SendAsync(request);
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.Clone();
}
bool Ok(JsonElement response) => response.GetProperty("retcode").GetInt32() == 0;
Task<JsonElement> Register(string username, string? device, Dictionary<string, string>? headers = null) =>
    Send(client, "/account/register", new { username, email = $"{username}@example.test", password = Password, device_id = device }, headers: headers);
Task<JsonElement> Login(string username, string? device = null) => Send(client, "/hkrpg_global/mdk/shield/api/login",
    new { account = username, password = Password, device_id = device, is_crypto = false });
Task<JsonElement> Verify(AccountData account, string token) => Send(client, "/hkrpg_global/mdk/shield/api/verify",
    new { uid = account.Uid.ToString(), token });
async Task<int> Block(string kind, string value) => (await Send(admin, "/admin/api/identity-blocks",
    new { kind, value, reason = "regression", permanent = true })).GetProperty("data").GetProperty("block").GetProperty("id").GetInt32();
Task<JsonElement> Unblock(int id) => Send(admin, $"/admin/api/identity-blocks/{id}", method: HttpMethod.Delete);

foreach (var path in new[] { "/admin/api/accounts", "/admin/api/identity-blocks" })
{
    using var response = await client.GetAsync(path);
    Check($"{path} requires authentication", response.StatusCode == HttpStatusCode.Unauthorized);
}
foreach (var path in new[] { "/admin/api/accounts/bulk-delete", "/admin/api/identity-blocks" })
{
    using var response = await client.PostAsJsonAsync(path, new { });
    Check($"{path} requires authentication", response.StatusCode == HttpStatusCode.Unauthorized);
}
var login = await Send(admin, "/admin/api/login", new { admin_key = config.MuipServer.AdminKey });
admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("data").GetProperty("token").GetString());
var legacy = AccountData.GetAccountByUid(9001)!;
Check("old schema migrates without inventing IP history", legacy != null && legacy.LastIp == null);
Check("passwordless account cannot be claimed by first login", !Ok(await Login("legacy")) && !legacy!.HasPassword());
Check("blank registration password is rejected before creation", !Ok(await Send(client, "/account/register",
    new { username = "blank", email = "blank@example.test", password = "", device_id = "blank" })) && AccountData.GetAccountByUserName("blank") == null);
Check("model and app ID do not count as device identity", !Ok(await Register("model-only", null,
    new() { ["x-rpc-device_model"] = "PC", ["x-rpc-app_id"] = "hkrpg" })));

var aliceResult = await Register("alice", "Alice-Device", new() { ["X-Forwarded-For"] = "203.0.113.77", ["x-rpc-app_id"] = "hkrpg" });
var alice = AccountData.GetAccountByUserName("alice")!;
Check("valid registration succeeds and records actual connection IP", Ok(aliceResult) && alice.RegistrationIp == "127.0.0.1" && alice.LastIp == "127.0.0.1");
Check("normal login still works", Ok(await Login("alice", "Alice-Device")));
Check("shared app IDs do not cause unrelated device collisions", Ok(await Register("bob", "Bob-Device", new() { ["x-rpc-app_id"] = "hkrpg" })));
var race = await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Register($"race-{i}", "shared-race-device")));
Check("concurrent registrations on one device create exactly one account", race.Count(Ok) == 1);
auth.AntiAbuse.LimitRegistrationPerIdentity = false;
var duplicates = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Register("duplicate", $"duplicate-{i}")));
Check("concurrent duplicate usernames create exactly one account", duplicates.Count(Ok) == 1);
Check("all allocated account UIDs are unique", DatabaseHelper.GetAllInstance<AccountData>()!.GroupBy(a => a.Uid).All(g => g.Count() == 1));
auth.AntiAbuse.LimitRegistrationPerIdentity = true;
config.ServerOption.AutoCreateUser = true;
auth.EnableRegistration = false;
Check("auto-create respects registration switch", !Ok(await Login("closed-auto", "closed-device")) && AccountData.GetAccountByUserName("closed-auto") == null);
auth.EnableRegistration = true;
Check("auto-create respects identity registration limit", !Ok(await Login("duplicate-auto", "Alice-Device")) && AccountData.GetAccountByUserName("duplicate-auto") == null);
Check("auto-create requires device identity when configured", !Ok(await Login("missing-auto")) && AccountData.GetAccountByUserName("missing-auto") == null);
auth.RequireVerifiedEmailForLogin = true;
Check("auto-create cannot bypass email verification", !Ok(await Login("unverified-auto", "new-auto-device")) && !AccountData.GetAccountByUserName("unverified-auto")!.CanLogin());
auth.RequireVerifiedEmailForLogin = false;
var forgedCombo = await Send(client, "/hkrpg_global/combo/granter/login/v2/login", new
{
    app_id = 1, channel_id = 1, device = "forged", data = JsonSerializer.Serialize(new { uid = "87654321", token = "forged-token" }), sign = ""
});
Check("external combo UID cannot create or enter an account without a server token", !Ok(forgedCombo) && AccountData.GetAccountByUserName("os_87654321") == null);
config.ServerOption.AutoCreateUser = false;
var token = alice.GenerateDispatchToken();
var legitimateCombo = await Send(client, "/hkrpg_global/combo/granter/login/v2/login", new
{
    app_id = 1, channel_id = 1, device = "Alice-Device", data = JsonSerializer.Serialize(new { uid = alice.Uid.ToString(), token }), sign = ""
});
Check("legitimate server-issued combo exchange still succeeds", Ok(legitimateCombo));

var deviceBlock = await Block("device", "ALICE-DEVICE");
Check("device blocks revoke existing tokens", !alice.ValidateDispatchToken(token));
Check("device matching is normalized and blocks registration", !Ok(await Register("blocked-device", "alice-device")));
Check("omitting device ID cannot bypass an existing device block", !Ok(await Login("alice")) && !Ok(await Verify(alice, alice.GenerateDispatchToken())));
Check("unrelated device still logs in", Ok(await Login("bob", "Bob-Device")));
Check("editing same normalized rule does not create duplicates", await Block("device", "alice-device") == deviceBlock);
Check("invalid IP block is rejected", !Ok(await Send(admin, "/admin/api/identity-blocks", new { kind = "ip", value = "not-an-ip" })));
Check("expired and out-of-range expiry values are rejected", !Ok(await Send(admin, "/admin/api/identity-blocks",
    new { kind = "ip", value = "192.0.2.1", permanent = false, expire_at = long.MaxValue })));
var ipBlock = await Block("ip", "::ffff:127.0.0.1");
Check("IPv4 mapped IPv6 block matches IPv4 even with rate limits disabled", !Ok(await Login("bob", "Bob-Device")));
Check("IP block cannot be escaped with a forged forwarding header", !Ok(await Register("spoof-escape", "spoof-escape",
    new() { ["X-Forwarded-For"] = "192.0.2.2" })));
Check("admin can still remove an IP block from the blocked network", Ok(await Unblock(ipBlock)) && Ok(await Login("bob", "Bob-Device")));
Check("admin can remove a device block", Ok(await Unblock(deviceBlock)) && Ok(await Login("alice", "Alice-Device")));
var replacementRule = await Send(admin, "/admin/api/identity-blocks", new { kind = "device", value = "temporary-rule", identity_key = "" });
var replacementRuleId = replacementRule.GetProperty("data").GetProperty("block").GetProperty("id").GetInt32();
Check("new rules do not reuse deleted rule IDs and accept empty optional key", replacementRuleId != deviceBlock && replacementRuleId != ipBlock);
Check("stale unblock request cannot remove a different rule", !Ok(await Unblock(deviceBlock)) && IdentityBlockHelper.GetSnapshot().Any(b => b.Uid == replacementRuleId));
await Unblock(replacementRuleId);

var victim = AccountHelper.CreateAccount("victim", "victim@example.test", Password, 0, null,
    identityKeys: AccountBanHelper.BuildIdentityKeys(["victim-device"], "198.51.100.8"));
var survivor = AccountHelper.CreateAccount("survivor", "survivor@example.test", Password, 0, null);
DatabaseHelper.SaveInstance(new PlayerData { Uid = victim.Uid, Name = "Disposable fixture" });
DatabaseHelper.SaveInstance(new MailData { Uid = victim.Uid });
// A global counter can have the same numeric UID as a player and must survive deletion.
DatabaseHelper.SaveInstance(new AuthAttemptData { Uid = victim.Uid, AttemptKey = "global-counter", Action = "Login" });
var victimToken = victim.GenerateDispatchToken();
var persistentBlock = await Block("device", "victim-device");
Check("bulk deletion requires explicit confirmation", !Ok(await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = new[] { victim.Uid } })) && AccountData.GetAccountByUid(victim.Uid) != null);
DatabaseHelper.LoadAllData = false;
var loadingDelete = (await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = new[] { victim.Uid }, confirm = "DELETE" })).GetProperty("data");
Check("deletion waits until all gameplay data is loaded", loadingDelete.GetProperty("failed").GetArrayLength() == 1 && AccountData.GetAccountByUid(victim.Uid) != null);
DatabaseHelper.LoadAllData = true;
Check("bulk deletion rejects invalid lists without deleting anything", !Ok(await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = new[] { victim.Uid, -1 }, confirm = "DELETE" })) && AccountData.GetAccountByUid(victim.Uid) != null);
Check("bulk deletion enforces batch size", !Ok(await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = Enumerable.Repeat(victim.Uid, 501).ToArray(), confirm = "DELETE" })));
var deleted = (await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = new[] { victim.Uid, victim.Uid, 123456789 }, confirm = "DELETE" })).GetProperty("data");
Check("bulk deletion deduplicates and reports missing UIDs", deleted.GetProperty("deleted_uids").GetArrayLength() == 1 && deleted.GetProperty("missing_uids").GetArrayLength() == 1 && deleted.GetProperty("failed").GetArrayLength() == 0);
Check("bulk deletion removes account and gameplay but preserves unselected accounts", AccountData.GetAccountByUid(victim.Uid) == null && database.GetInstance<PlayerData>(victim.Uid) == null && database.GetInstance<MailData>(victim.Uid) == null && AccountData.GetAccountByUid(survivor.Uid) != null);
Check("account deletion preserves independent anti-abuse counters and blocks", database.GetInstance<AuthAttemptData>(victim.Uid) != null && IdentityBlockHelper.GetSnapshot().Any(b => b.Uid == persistentBlock));
Check("deleted account token cannot be reused", !victim.ValidateGameToken(victimToken) && !Ok(await Verify(victim, victimToken)));
var delayedWriteRejected = false;
try { DatabaseHelper.SaveInstance(new MailData { Uid = victim.Uid }); } catch (InvalidOperationException) { delayedWriteRejected = true; }
Check("delayed insert cannot recreate deleted player data", delayedWriteRejected);
var reuseRejected = false;
try { AccountHelper.CreateAccount("reused", "reused@example.test", Password, victim.Uid, null); } catch { reuseRejected = true; }
Check("deleted UIDs cannot be reassigned", reuseRejected);
Check("deleted account's independently blocked device cannot register again", !Ok(await Register("reentry", "victim-device")));
var summary = (await Send(admin, "/admin/api/accounts")).GetProperty("data").GetProperty("accounts").EnumerateArray().Single(a => a.GetProperty("uid").GetInt32() == alice.Uid);
Check("authenticated account list exposes readable IP and device history", summary.GetProperty("last_ip").GetString() == "127.0.0.1" && summary.GetProperty("device_ids").GetArrayLength() > 0);

var rollbackAccount = AccountHelper.CreateAccount("rollback", "rollback@example.test", Password, 0, null);
DatabaseHelper.SaveInstance(new MailData { Uid = rollbackAccount.Uid });
void Sql(string commandText)
{
    using var connection = new SqliteConnection($"Data Source={Path.Combine(scratch, config.Database.DatabaseName)}");
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = commandText;
    command.ExecuteNonQuery();
}
Sql($"CREATE TRIGGER prevent_test_delete BEFORE DELETE ON Mail WHEN OLD.Uid = {rollbackAccount.Uid} BEGIN SELECT RAISE(ABORT, 'intentional deletion failure'); END;");
var partial = (await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = new[] { rollbackAccount.Uid, survivor.Uid }, confirm = "DELETE" })).GetProperty("data");
Check("partial failure reports actual successes and rolls back failed account", partial.GetProperty("failed").GetArrayLength() == 1 && partial.GetProperty("deleted_uids").GetArrayLength() == 1 && AccountData.GetAccountByUid(rollbackAccount.Uid) != null && database.GetInstance<MailData>(rollbackAccount.Uid) != null && !rollbackAccount.IsDeleting);
Sql("DROP TRIGGER prevent_test_delete;");

var liveAccount = AccountHelper.CreateAccount("live-delete", "live-delete@example.test", Password, 0, null);
var connections = (SortedList<long, March7thHoneyConnection>)typeof(March7thHoneyListener)
    .GetField("Connections", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
var live = new DeletionProbeConnection(liveAccount);
connections.Add(987654, live);
var liveToken = liveAccount.GenerateDispatchToken();
var liveDeleted = await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = new[] { liveAccount.Uid }, confirm = "DELETE" });
Check("online session is stopped and token revoked before data is removed", live.SawAccountBeforeDeletion && !liveAccount.ValidateGameToken(liveToken) && liveDeleted.GetProperty("data").GetProperty("deleted_uids").GetArrayLength() == 1);
connections.Clear();
var stuckAccount = AccountHelper.CreateAccount("stuck-delete", "stuck-delete@example.test", Password, 0, null);
connections.Add(987655, new DeletionProbeConnection(stuckAccount) { RefuseStop = true });
var stuckDeleted = (await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = new[] { stuckAccount.Uid }, confirm = "DELETE" })).GetProperty("data");
Check("a session that cannot stop is reported as failure without deleting its data", stuckDeleted.GetProperty("failed").GetArrayLength() == 1 && AccountData.GetAccountByUid(stuckAccount.Uid) != null && !stuckAccount.IsDeleting);
connections.Clear();

var concurrentDeletes = Enumerable.Range(0, 6).Select(i => AccountHelper.CreateAccount($"flush-delete-{i}", $"flush-delete-{i}@example.test", Password, 0, null)).ToList();
var flushTask = Task.Run(() =>
{
    for (var i = 0; i < 8; i++)
    {
        foreach (var account in concurrentDeletes) DatabaseHelper.MarkDirty(account.Uid);
        database.SaveDatabase();
    }
});
var concurrentResult = await Send(admin, "/admin/api/accounts/bulk-delete", new { uids = concurrentDeletes.Select(a => a.Uid).ToArray(), confirm = "DELETE" });
await flushTask;
Check("database flush and batch deletion complete concurrently without resurrecting data", concurrentResult.GetProperty("data").GetProperty("deleted_uids").GetArrayLength() == concurrentDeletes.Count && concurrentDeletes.All(a => AccountData.GetAccountByUid(a.Uid) == null));

var expired = new IdentityBlockData { Uid = -12345, Kind = "device", Value = "expired-device", IdentityKey = AccountBanHelper.CreateDeviceIdentityKey("expired-device")!, CreatedAt = 1, ExpireAt = 2 };
DatabaseHelper.SaveInstance(expired);
IdentityBlockHelper.Initialize();
Check("expired persisted rule no longer blocks registration", Ok(await Register("expired-rule", "expired-device")));

database.SaveDatabase();
DatabaseHelper.UidInstanceMap.Clear();
database.Initialize(startSaveThread: false);
AuthRateLimiter.Initialize();
Check("block persists through database reload", IdentityBlockHelper.TryGetActiveBan(AccountBanHelper.BuildIdentityKeys(["victim-device"], null), out _));
Check("deleted account stays deleted after reload", DatabaseHelper.IsUidDeleted(victim.Uid) && AccountData.GetAccountByUid(victim.Uid) == null && database.GetInstance<PlayerData>(victim.Uid) == null);
Check("IP history persists through reload", AccountData.GetAccountByUid(alice.Uid)!.LastIp == "127.0.0.1");
await Unblock(persistentBlock);
Check("removing persistent block allows a fresh registration with a new UID", Ok(await Register("reentry-unblocked", "victim-device")) && AccountData.GetAccountByUserName("reentry-unblocked")!.Uid != victim.Uid);

auth.AntiAbuse.LoginRateLimitEnabled = true;
auth.AntiAbuse.LoginRateLimitMaxRequests = 1;
AuthRateLimiter.ClearAll(false);
await Login("wrong-password-one", "one");
Check("rotating username, device and forwarding header cannot evade per-IP login limit", !Ok(await Send(client, "/hkrpg_global/mdk/shield/api/login",
    new { account = "bob", password = Password, device_id = "changed" }, headers: new() { ["X-Forwarded-For"] = "203.0.113.91" })));
auth.AntiAbuse.LoginRateLimitEnabled = false;
AuthRateLimiter.ClearAll(false);

// A second real HTTP host trusts exactly loopback, as a local reverse-proxy deployment would.
config.HttpServer.TrustedProxies = ["127.0.0.1", "::1"];
await using (var proxyApp = WebProgram.BuildWebApp([], 0, "http://127.0.0.1:0"))
{
    await proxyApp.StartAsync();
    using var proxyClient = new HttpClient { BaseAddress = new Uri(proxyApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    var forwarded = await Send(proxyClient, "/hkrpg_global/mdk/shield/api/login", new { account = "bob", password = Password }, headers: new() { ["X-Forwarded-For"] = "192.0.2.10, 198.51.100.10" });
    Check("trusted proxy uses only the rightmost configured forwarding hop", Ok(forwarded) && AccountData.GetAccountByUserName("bob")!.LastIp == "198.51.100.10");
    await proxyApp.StopAsync();
}

config.ServerOption.ServerConfig.RunDispatch = true;
config.ServerOption.ServerConfig.ServerExchangeSecret = "";
using (var response = await client.PostAsync("/validate_account_token", new FormUrlEncodedContent(new Dictionary<string, string> { ["accountUid"] = alice.Uid.ToString(), ["token"] = "fake", ["clientIp"] = "192.0.2.1" })))
    Check("server exchange fails closed without a shared secret", response.StatusCode == HttpStatusCode.Unauthorized);
config.ServerOption.ServerConfig.ServerExchangeSecret = "regression-exchange-only";
var exchangeBlock = await Block("ip", "192.0.2.55");
using (var request = new HttpRequestMessage(HttpMethod.Post, "/validate_account_token"))
{
    request.Headers.Add("X-March7thHoney-Server-Secret", config.ServerOption.ServerConfig.ServerExchangeSecret);
    request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["accountUid"] = alice.Uid.ToString(), ["token"] = AccountData.GetAccountByUid(alice.Uid)!.GenerateDispatchToken(), ["clientIp"] = "192.0.2.55" });
    using var response = await client.SendAsync(request);
    Check("split server validates player IP supplied by authenticated game server", response.StatusCode == HttpStatusCode.Forbidden);
}
await Unblock(exchangeBlock);

// Website game-account provisioning (WebAccountRoutes).
var webApi = auth.WebAccountApi;
const string WebSecret = "regression-web-account-only";
var webHeaders = new Dictionary<string, string> { ["X-March7thHoney-Web-Secret"] = WebSecret };
async Task<(HttpStatusCode Status, JsonElement Body)> Web(HttpClient http, string path, object? body = null,
    HttpMethod? method = null, Dictionary<string, string>? headers = null)
{
    using var request = new HttpRequestMessage(method ?? (body == null ? HttpMethod.Get : HttpMethod.Post), path);
    if (body != null) request.Content = JsonContent.Create(body);
    foreach (var (key, value) in headers ?? webHeaders) request.Headers.TryAddWithoutValidation(key, value);
    using var response = await http.SendAsync(request);
    var text = await response.Content.ReadAsStringAsync();
    using var json = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
    return (response.StatusCode, json.RootElement.Clone());
}
string WebError((HttpStatusCode Status, JsonElement Body) r) =>
    r.Body.TryGetProperty("error", out var e) ? e.GetString() ?? "" : "";
Task<JsonElement> LoginAs(string account, string password) => Send(client, "/hkrpg_global/mdk/shield/api/login",
    new { account, password, device_id = "web-login-device", is_crypto = false });

webApi.Enabled = false;
webApi.Secret = WebSecret;
Check("web account API is hidden while disabled",
    (await Web(client, "/external/web-account/v1/status")).Status == HttpStatusCode.NotFound);
webApi.Enabled = true;
Check("web account API rejects a missing secret",
    (await Web(client, "/external/web-account/v1/status", headers: [])).Status == HttpStatusCode.Unauthorized);
Check("web account API rejects a wrong secret", (await Web(client, "/external/web-account/v1/status",
    headers: new() { ["X-March7thHoney-Web-Secret"] = "regression-web-account-onlX" })).Status == HttpStatusCode.Unauthorized);
Check("web account API answers status with the secret", (await Web(client, "/external/web-account/v1/status")).Status == HttpStatusCode.OK);

// Public registration closed: the website is the only way in, and its accounts must still work.
auth.EnableRegistration = false;
const string WebPassword = "Web-Account-Password-1";
var webCreated = await Web(client, "/external/web-account/v1/accounts",
    new { username = "WebPlayer", email = "web.player@example.test", password = WebPassword });
var webAccount = AccountData.GetAccountByUserName("WebPlayer");
Check("web account API creates a verified account while public registration is off",
    webCreated.Status == HttpStatusCode.OK && webAccount != null && webAccount.IsEmailVerified &&
    webCreated.Body.GetProperty("data").GetProperty("account").GetProperty("uid").GetInt32() == webAccount.Uid);
Check("web-created account can log in to the game", Ok(await LoginAs("WebPlayer", WebPassword)));
Check("web account usernames are unique regardless of case", WebError(await Web(client, "/external/web-account/v1/accounts",
    new { username = "webplayer", email = "other@example.test", password = WebPassword })) == "username_taken");
Check("web account emails stay unique", WebError(await Web(client, "/external/web-account/v1/accounts",
    new { username = "WebPlayer2", email = "WEB.PLAYER@example.test", password = WebPassword })) == "email_taken");
Check("web account usernames are restricted to login-safe characters", WebError(await Web(client,
    "/external/web-account/v1/accounts", new { username = "bad name", password = WebPassword })) == "invalid_username");
Check("web account passwords follow the server password policy", WebError(await Web(client,
    "/external/web-account/v1/accounts", new { username = "ShortPw", password = "x" })) == "invalid_password" &&
    AccountData.GetAccountByUserName("ShortPw") == null);
var placeholder = await Web(client, "/external/web-account/v1/accounts", new { username = "NoMail", password = WebPassword });
Check("web account without an address gets a unique placeholder", placeholder.Status == HttpStatusCode.OK &&
    AccountData.GetAccountByUserName("NoMail")?.Email == "nomail@web-account.invalid");
var webRace = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Web(client, "/external/web-account/v1/accounts",
    new { username = i % 2 == 0 ? "RaceWeb" : "raceweb", email = $"race-web-{i}@example.test", password = WebPassword })));
Check("concurrent web account creation for one username creates exactly one account",
    webRace.Count(r => r.Status == HttpStatusCode.OK) == 1);

var webToken = webAccount!.GenerateDispatchToken();
const string WebPassword2 = "Web-Account-Password-2";
var reset = await Web(client, $"/external/web-account/v1/accounts/{webAccount.Uid}/password",
    new { password = WebPassword2 }, HttpMethod.Put);
Check("web password reset replaces the password and signs clients out", reset.Status == HttpStatusCode.OK &&
    !Ok(await LoginAs("WebPlayer", WebPassword)) && !webAccount.ValidateGameToken(webToken) &&
    Ok(await LoginAs("WebPlayer", WebPassword2)));
Check("web password reset still enforces the password policy", WebError(await Web(client,
    $"/external/web-account/v1/accounts/{webAccount.Uid}/password", new { password = "x" }, HttpMethod.Put)) == "invalid_password");
var banned = await Web(client, $"/external/web-account/v1/accounts/{webAccount.Uid}/ban", new { reason = "web ban" });
Check("web ban blocks game login", banned.Status == HttpStatusCode.OK &&
    banned.Body.GetProperty("data").GetProperty("account").GetProperty("banned").GetBoolean() &&
    !Ok(await LoginAs("WebPlayer", WebPassword2)));
await Web(client, $"/external/web-account/v1/accounts/{webAccount.Uid}/ban", method: HttpMethod.Delete);
Check("web unban restores game login", Ok(await LoginAs("WebPlayer", WebPassword2)));
Check("web account lookup reports missing accounts", WebError(await Web(client, "/external/web-account/v1/accounts/999999")) == "not_found");

// Behind a local reverse proxy, a relayed public request is not "local" even with the right secret.
await using (var proxyApp = WebProgram.BuildWebApp([], 0, "http://127.0.0.1:0"))
{
    await proxyApp.StartAsync();
    using var proxyClient = new HttpClient { BaseAddress = new Uri(proxyApp.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
    var relayed = await Web(proxyClient, "/external/web-account/v1/status",
        headers: new() { ["X-March7thHoney-Web-Secret"] = WebSecret, ["X-Forwarded-For"] = "198.51.100.20" });
    Check("web account API refuses requests relayed from outside the allowed addresses", relayed.Status == HttpStatusCode.Unauthorized);
    await proxyApp.StopAsync();
}

auth.ExternalRegistrationUrl = "https://example.test/register";
using (var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(address) })
using (var response = await noRedirect.GetAsync("/login-platform/register.html"))
    Check("closed registration page points at the external sign-up site",
        response.StatusCode == HttpStatusCode.Redirect && response.Headers.Location?.ToString() == auth.ExternalRegistrationUrl);
auth.ExternalRegistrationUrl = "";
auth.EnableRegistration = true;

// Account bans must not propagate to other UIDs through a shared PC fingerprint or network.
await BanRegression.Run(client, admin, Password, Check);

// Reset and verification links must never point at the Host header of the request.
await EmailLinkRegression.Run(client, Password, Check);

Console.WriteLine($"Auth regression failures: {failures.Count}");
if (serve && failures.Count == 0)
{
    for (var i = 0; i < 30; i++) AccountHelper.CreateAccount($"fixture-{i:00}", $"fixture-{i:00}@example.test", Password, 0, null,
        identityKeys: AccountBanHelper.BuildIdentityKeys([$"fixture-device-{i}"], $"192.0.2.{100 + i}"));
    Console.WriteLine($"UI fixture: {address}/admin/index.html (key: local-auth-regression-only). Disposable database: {scratch}");
    await app.WaitForShutdownAsync();
}
await app.StopAsync();
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

sealed class DeletionProbeConnection : Connection
{
    public bool RefuseStop;
    public bool SawAccountBeforeDeletion;
    public DeletionProbeConnection(AccountData account) : base(null!, new IPEndPoint(IPAddress.Loopback, account.Uid))
    {
        Player = new PlayerInstance(new PlayerData { Uid = account.Uid }) { Uid = account.Uid };
        State = SessionStateEnum.WAITING_FOR_LOGIN;
    }
    public override void Start() { }
    public override void Stop()
    {
        SawAccountBeforeDeletion = AccountData.GetAccountByUid(Player!.Uid)?.IsDeleting == true;
        if (!RefuseStop) IsOnline = false;
    }
}
