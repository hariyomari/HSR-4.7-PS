using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using March7thHoney.Command;
using March7thHoney.Command.Command;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.Enums;
using March7thHoney.GameServer.Game.Friend;
using March7thHoney.GameServer.Game.Inventory;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Internationalization;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;
using March7thHoney.WebServer;
using March7thHoney.WebServer.Server;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;

// An isolated HTTP/command regression runner. Never load the operator's Config.json or database.
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var scratch = Path.Combine(Path.GetTempPath(), "m7h-web-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
Environment.CurrentDirectory = scratch;
var config = ConfigManager.Config;
config.Path.ConfigPath = Path.Combine(repository, "Config");
config.Path.LanguagePath = Path.Combine(repository, "Config/Languages");
config.Path.DatabasePath = Path.Combine(scratch, "Database");
config.Path.LogPath = Path.Combine(scratch, "Logs");
config.GameServer.UsePacketEncryption = false;
config.GameServer.GameServerName = "WebServer regression";
config.ServerOption.Language = "CHS";
config.HttpServer.UseSSL = false;
config.HttpServer.SendHotfix = false;
config.ServerOption.ServerConfig.RunGateway = true;
config.MuipServer.AdminKey = "local-web-regression-only";
// The console fixture needs a real account row, so the scratch database gets its tables created.
// No save thread: the run is throwaway and must not linger.
new DatabaseHelper().Initialize(startSaveThread: false);
I18NManager.LoadLanguage();
var manager = new CommandManager();
manager.RegisterCommands();
MuipManager.CommandHandler = manager.HandleCommandAsync;
MuipManager.CommandCatalogProvider = manager.BuildCatalog;

// A fixture player for the self-service console at /player: default role, so the page shows both the
// commands a normal account may run and the ones it may not.
const int fixtureUid = 10002;
const string fixturePassword = "fixture-console-only";
AccountHelper.CreateAccount("fixture", "fixture@example.invalid", fixturePassword, fixtureUid, null);

var connections = (SortedList<long, March7thHoneyConnection>)typeof(March7thHoneyListener)
    .GetField("Connections", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

var serve = args.Contains("--serve");
await using var app = WebProgram.BuildWebApp([], 0, serve ? "http://127.0.0.1:21000" : "http://127.0.0.1:0");
await app.StartAsync();
var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
if (serve)
{
    // Pretend the fixture player is in-game so the console can actually run commands.
    connections.Add(2, new TestConnection(fixtureUid));
    // Warps page: the repo history pool and item names, with stub item rows so every banner is eligible (the
    // fixture loads no game data).
    var warpHistory = ResourceManager.LoadBannerHistory();
    foreach (var id in warpHistory.Banners.SelectMany(b => b.RateUpItems5.Concat(b.RateUpItems4).Concat(b.PrizeItems)))
        GameData.ItemConfigData.TryAdd(id, new ItemConfigExcel { ID = id });
    GameData.BannerHistoryConfig = warpHistory;
    GameData.WarpItemNamesConfig =
        ResourceManager.LoadCustomFile<WarpItemNamesConfig>("Warp item names", "WarpItemNames") ?? new WarpItemNamesConfig();
    GameData.WarpStoryGatesConfig =
        ResourceManager.LoadCustomFile<WarpStoryGatesConfig>("Warp story gates", "WarpStoryGates") ?? new WarpStoryGatesConfig();
    // Story dialog: --with-resources reads Config/Resource.bin (never writes it) and registers the mission
    // finish-type/finish-action handlers as EntryPoint does, so a checkpoint replays the real story into this scratch
    // database exactly as the server would (without the handlers no SubMissionFinishCnt/EnterFloor cascade runs).
    if (args.Contains("--with-resources"))
    {
        config.Path.ResourcePath = Path.Combine(scratch, "no-raw-resource-json");
        if (!ResourceCache.LoadCache()) throw new InvalidOperationException("Config/Resource.bin could not be loaded");
        March7thHoney.GameServer.Server.ServerUtils.InitializeHandlers();
    }
    Console.WriteLine($"UI fixture: {address}/admin/index.html (key: local-web-regression-only)");
    Console.WriteLine($"UI fixture: {address}/player/index.html (uid: {fixtureUid}, password: {fixturePassword})");
    await app.WaitForShutdownAsync();
    return;
}

var failures = new List<string>();
void Check(string name, bool pass)
{
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures.Add(name);
}

using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) };
using (var response = await client.GetAsync($"/query_gateway?version=CNBETAWin{GameConstants.GAME_VERSION}"))
{
    response.EnsureSuccessStatusCode();
    var gateway = GateServer.Parser.ParseFrom(Convert.FromBase64String(await response.Content.ReadAsStringAsync()));
    Check("gateway advertises the local RogueTourn BuildRef prefix",
        gateway.RogueTournBuildRefStaticDataUrlPrefix == address.TrimEnd('/') + "/rpgsimulator/0/");
    Check("gateway advertises the RogueTourn BuildRef color header",
        gateway.RogueTournBuildRefApiReqColorHeaderKey == "x-rpc-rpglineup-tourn" &&
        gateway.RogueTournBuildRefApiReqColorHeaderValue == "tourn");
}
foreach (var path in new[]
         {
             "/rpgsimulator/0/rpg_lineup_list.json",
             "/event/rpgsimulator/game/tourn/lineup/cdn_content"
         })
{
    using var response = await client.GetAsync(path);
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Check($"RogueTourn BuildRef route {path}",
        json.RootElement.GetProperty("version").GetInt32() == 249 &&
        json.RootElement.GetProperty("lineups").GetArrayLength() == 0);
}
using (var response = await client.GetAsync("/rpgsimulator/0/not-a-build-ref.json"))
    Check("RogueTourn BuildRef prefix is not a wildcard", response.StatusCode == HttpStatusCode.NotFound);

using (var unauthorized = await client.GetAsync("/admin/api/maintenance"))
    Check("maintenance requires authentication", unauthorized.StatusCode == HttpStatusCode.Unauthorized);
using (var unauthorized = await client.PostAsJsonAsync("/admin/api/commands", new { command = "/help" }))
    Check("commands require authentication", unauthorized.StatusCode == HttpStatusCode.Unauthorized);
await AdminAuthChecks.RunAnonymous(address, app.Services.GetRequiredService<EndpointDataSource>(), Check);
string adminToken;
using (var response = await client.PostAsJsonAsync("/admin/api/login", new { admin_key = config.MuipServer.AdminKey }))
{
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    adminToken = json.RootElement.GetProperty("data").GetProperty("token").GetString() ?? "";
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
}
await AdminAuthChecks.RunWithToken(address, adminToken, Check);

async Task<string> Execute(string command, int? target = null)
{
    using var response = await client.PostAsJsonAsync("/admin/api/commands", new { command, target_uid = target });
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("data").GetProperty("output").GetString()!.Replace("\r\n", "\n");
}

Check("help without a target", (await Execute("/help")).Contains("/help"));
Check("mail usage without a target", await Execute("/mail") == I18NManager.Translate("Game.Command.Mail.Usage").TrimEnd());
Check("player command still requires a target", await Execute("/give 1") == I18NManager.Translate("Game.Command.Notice.PlayerNotFound").TrimEnd());
Check("unknown command reports command error", await Execute("/no-such-command") == I18NManager.Translate("Game.Command.Notice.CommandNotFound").TrimEnd());

// Install a probe into the actual dispatcher so target and async behavior can be observed without gameplay writes.
var probeInfo = new CommandInfoAttribute("regression-probe", "probe", "probe");
var probe = new ProbeCommand();
var generated = (Dictionary<string, GeneratedCommand>)typeof(CommandManager)
    .GetField("_generated", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
manager.Commands[probeInfo.Name] = probe;
manager.CommandInfo[probeInfo.Name] = probeInfo;
generated[probeInfo.Name] = new GeneratedCommand(probeInfo, probe, [],
    new GeneratedCommandMethod("Probe", [], (_, arg) => probe.Run(arg)));

var terminalTarget = new TestConnection(10001);
var webTarget = new TestConnection(fixtureUid);
connections.Add(1, terminalTarget);
connections.Add(2, webTarget);
manager.Target = terminalTarget;

// createOrder grants paid products: an unproven caller must leave the online player's supply pass untouched.
async Task<int?> CreateOrder(string query, string? comboHeader = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, "/mdk/luckycat/luckycat/createOrder" + query);
    if (comboHeader != null) request.Headers.TryAddWithoutValidation("x-rpc-combo_token", comboHeader);
    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode) return null;
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return json.RootElement.GetProperty("retcode").GetInt32();
}
var shopPlayer = webTarget.Player!;
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.InventoryManager))!
    .SetValue(shopPlayer, new InventoryManager(shopPlayer));
const string shopProduct = "regression-supply-pass";
GameData.RechargeConfigData[shopProduct] = new RechargeConfigExcel
    { ProductID = shopProduct, GiftType = (int)ProductGiftType.ProductGiftMonthCard };
config.ServerOption.EnableMonthCard = true;
var otherToken = AccountHelper.CreateAccount("shop-other", "shop-other@example.invalid", "shop-other-only", 10001, null)
    .GenerateComboToken();
var fixtureToken = AccountData.GetAccountByUid(fixtureUid)!.GenerateComboToken();
async Task<bool> OrderRefused(string query, string? comboHeader = null)
{
    var before = shopPlayer.Data.MonthCardExpireTime;
    return await CreateOrder(query, comboHeader) == -1 && shopPlayer.Data.MonthCardExpireTime == before;
}
var product = $"&product_id={shopProduct}";
Check("createOrder rejects a uid without a token", await OrderRefused($"?uid={fixtureUid}{product}"));
connections.Remove(1);
Check("createOrder does not fall back to the only online player", await OrderRefused($"?x=1{product}"));
connections.Add(1, terminalTarget);
Check("createOrder rejects another account's token",
    await OrderRefused($"?uid={fixtureUid}&combo_token={otherToken}{product}"));
Check("createOrder rejects a forged combo token header",
    await OrderRefused($"?x=1{product}", $"ai=11;ci=1;oi={fixtureUid};ct=forged;bi=hkrpg_global"));
Check("createOrder rejects a header token for a different uid",
    await OrderRefused($"?x=1{product}", $"ai=11;ci=1;oi={fixtureUid};ct={otherToken};bi=hkrpg_global"));
var beforeGrant = shopPlayer.Data.MonthCardExpireTime;
await CreateOrder($"?x=1{product}", $"ai=11;ci=1;oi={fixtureUid};ct={fixtureToken};bi=hkrpg_global");
Check("createOrder credits the player whose combo token is presented",
    shopPlayer.Data.MonthCardExpireTime > beforeGrant);
GameData.RechargeConfigData.Remove(shopProduct);

var luaRoot = Path.Combine(scratch, "Lua");
Directory.CreateDirectory(Path.Combine(luaRoot, "nested"));
Directory.CreateDirectory(Path.Combine(luaRoot, "other"));
await File.WriteAllTextAsync(Path.Combine(luaRoot, "alpha.lua"), "return true");
await File.WriteAllTextAsync(Path.Combine(luaRoot, "nested", "beta.lua"), "return true");
await File.WriteAllTextAsync(Path.Combine(luaRoot, "nested", "shared.lua"), "return true");
await File.WriteAllTextAsync(Path.Combine(luaRoot, "other", "shared.lua"), "return true");
var windyList = await Execute("/windy list");
Check("windy lists Lua without a target", windyList.Contains("1. alpha.lua") &&
                                               windyList.Contains("nested/beta.lua") &&
                                               !windyList.Contains(scratch, StringComparison.OrdinalIgnoreCase));
Check("windy rejects an invalid list number", (await Execute("/windy 99", fixtureUid)) ==
                                                I18NManager.Translate("Game.Command.Windy.InvalidChoice", "4"));
var ambiguousWindy = await Execute("/windy shared", fixtureUid);
Check("windy reports ambiguous file names", ambiguousWindy.Contains("nested/shared.lua") &&
                                                   ambiguousWindy.Contains("other/shared.lua"));
webTarget.ClientLua.RecordSentScript("nested/beta.lua");
var loadedWindy = await Execute("/windy loaded", fixtureUid);
Check("windy lists Lua sent for the selected connection", loadedWindy.Contains("nested/beta.lua") &&
                                                                  !loadedWindy.Contains("alpha.lua"));
Check("windy marks loaded Lua in the file list", (await Execute("/windy list", fixtureUid))
    .Contains("nested/beta.lua" + I18NManager.Translate("Game.Command.Windy.LoadedMarker")));
Check("windy loaded requires a target", await Execute("/windy loaded") ==
                                             I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));

Check("web console does not inherit terminal target", await Execute("/regression-probe") == "target=0;explicit=False");
// Web commands run on a per-request session that never enters the MUIP table or its admission limits.
Check("admin page commands do not register MUIP sessions", probe.SessionsDuringRun == 0);
Check("target field is explicit", await Execute("/regression-probe", 10002) == "target=10002;explicit=True");
Check("leading @uid selects only this command", await Execute("@10002 /regression-probe") == "target=10002;explicit=True");
Check("trailing @uid selects only this command", await Execute("/regression-probe @10002") == "target=10002;explicit=True");
await Execute("@10002");
Check("web target selection does not change terminal target", manager.Target == terminalTarget);
Check("web target selection does not leak to next request", await Execute("/regression-probe") == "target=0;explicit=False");
Check("invalid target is rejected", (await Execute("/help", 99999)).Contains("99999"));
Check("admin console cannot pin a language", await Execute("/language en", fixtureUid)
    == I18NManager.Translate("Game.Command.Language.PlayerOnly"));

// A real terminal retains its target behavior, observed through the probe's captured argument.
manager.HandleCommand("/regression-probe", new ConsoleCommandSender(new Logger("Regression")));
Check("terminal retains its selected target", probe.LastTarget == terminalTarget && probe.LastExplicit);

// MUIP status reads the live listener. The fixture players never ran login, so their raid and story-line
// managers are missing; that must read as plain exploring, not as an active mode.
const int offlineUid = 10004;
foreach (var uid in new[] { fixtureUid, offlineUid })
{
    var database = DatabaseHelper.Instance;
    if (database == null) break;
    database.GetInstanceOrCreateNew<PlayerData>(uid);
    database.GetInstanceOrCreateNew<AvatarData>(uid);
    database.GetInstanceOrCreateNew<LineupData>(uid);
    database.GetInstanceOrCreateNew<MissionData>(uid);
}
var infoSession = new MuipSession { SessionId = "regression-info", IsAdmin = true, IsAuthorized = true, ExpireTimeStamp = DateTime.Now.AddMinutes(1).ToUnixSec() };
MuipManager.Sessions[infoSession.SessionId] = infoSession;
var serverInfo = MuipManager.GetInformation(infoSession.SessionId);
Check("MUIP server info lists the online players", serverInfo is { Code: 0, Data: { } serverData } &&
    serverData.OnlinePlayers.Select(player => player.Uid).Order().SequenceEqual([10001, fixtureUid]));
var onlineInfo = MuipManager.GetPlayerInformation(infoSession.SessionId, fixtureUid);
Check("MUIP player info reports an idle online player as exploring", onlineInfo is
    { Code: 0, Data: { PlayerStatus: PlayerStatusEnum.Explore, PlayerSubStatus: PlayerSubStatusEnum.None } });
var offlineInfo = MuipManager.GetPlayerInformation(infoSession.SessionId, offlineUid);
Check("MUIP player info reports an offline player", offlineInfo is { Code: 0, Data.PlayerStatus: PlayerStatusEnum.Offline });
MuipManager.Sessions.TryRemove(infoSession.SessionId, out _);

// --- player self-service console (/player) ------------------------------------------------------------
// A separate client: the admin bearer on `client` must never stand in for a console session.
using var consoleClient = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) };

async Task<JsonDocument> ConsolePost(string path, object body, string? token = null)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using var response = await consoleClient.SendAsync(request);
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
}

using (var denied = await ConsolePost("/player/catalog", new { }))
    Check("console catalog requires credentials", denied.RootElement.GetProperty("retcode").GetInt32() != 0);
using (var denied = await ConsolePost("/player/login", new { username = fixtureUid, password = "wrong" }))
    Check("console login rejects a wrong password", denied.RootElement.GetProperty("retcode").GetInt32() != 0);

string consoleToken;
using (var login = await ConsolePost("/player/login", new { username = fixtureUid, password = fixturePassword }))
{
    Check("console login succeeds", login.RootElement.GetProperty("retcode").GetInt32() == 0);
    var data = login.RootElement.GetProperty("data");
    consoleToken = data.GetProperty("token").GetString()!;
    Check("console login reports the player online", data.GetProperty("player").GetProperty("online").GetBoolean());
}

using (var catalog = await ConsolePost("/player/catalog", new { }, consoleToken))
{
    var commands = catalog.RootElement.GetProperty("data").GetProperty("commands").EnumerateArray().ToList();
    bool Allowed(string name) => commands.Single(c => c.GetProperty("name").GetString() == name).GetProperty("allowed").GetBoolean();
    Check("console catalog lists commands", commands.Count > 0);
    Check("console catalog allows a User-role command", Allowed("give"));
    Check("console catalog denies an Administrator command", !Allowed("account"));
    Check("console catalog allows the public language command", Allowed("language"));
}

using (var connect = await ConsolePost("/player/exec", new { command = "" }, consoleToken))
    Check("console connect reports the session", connect.RootElement.GetProperty("output").GetString()!.Contains($"UID {fixtureUid}"));
using (var help = await ConsolePost("/player/exec", new { command = "/help" }, consoleToken))
    Check("console runs an allowed command", help.RootElement.GetProperty("output").GetString()!.Contains("/help"));
probe.SessionsDuringRun = -1;
using (var probeRun = await ConsolePost("/player/exec", new { command = "/regression-probe" }, consoleToken))
    Check("player console commands do not register MUIP sessions",
        probeRun.RootElement.GetProperty("retcode").GetInt32() == 0 && probe.SessionsDuringRun == 0);
using (var denied = await ConsolePost("/player/exec", new { command = "/account roles" }, consoleToken))
    Check("console refuses a command the role lacks",
        denied.RootElement.GetProperty("output").GetString()!.Contains(I18NManager.Translate("Game.Command.Notice.NoPermission")));
// The launcher still posts uid + password on every call; that path must keep working.
using (var legacy = await ConsolePost("/player/exec", new { username = fixtureUid.ToString(), password = fixturePassword, command = "/help" }))
    Check("console keeps the launcher uid+password path", legacy.RootElement.GetProperty("output").GetString()!.Contains("/help"));
using (var pin = await ConsolePost("/player/exec", new { command = "/lang english" }, consoleToken))
    Check("player console pins its own language and answers in it",
        pin.RootElement.GetProperty("output").GetString()!.Contains(
            I18NManager.TranslateAsCertainLang("EN", "Game.Command.Language.Set", "EN"))
        && webTarget.Player!.Data.LanguagePin == "EN");
webTarget.Player!.Data.LanguagePin = null;
using (var forged = await ConsolePost("/player/exec", new { command = "/help" }, "not-a-real-token"))
    Check("console rejects a forged token", forged.RootElement.GetProperty("retcode").GetInt32() != 0);

// /json export and import stay inside Config/Json for a User-role player.
async Task<string> ConsoleOutput(string command)
{
    using var result = await ConsolePost("/player/exec", new { command }, consoleToken);
    return result.RootElement.GetProperty("output").GetString() ?? "";
}
var jsonDir = Path.Combine(scratch, "Config", "Json");
var escapeName = "m7h-json-escape-" + Guid.NewGuid().ToString("N");
var relativeEscape = Path.GetFullPath(Path.Combine(jsonDir, "../..", escapeName + ".json"));
var absoluteEscape = Path.Combine(scratch, escapeName);
Check("json export rejects a relative traversal name",
    (await ConsoleOutput($"/json export ../../{escapeName}")).Contains(
        I18NManager.Translate("Game.Command.Json.InvalidFileName", $"../../{escapeName}")) &&
    !File.Exists(relativeEscape));
Check("json export rejects an absolute name",
    (await ConsoleOutput($"/json export {absoluteEscape} true")).Contains(
        I18NManager.Translate("Game.Command.Json.InvalidFileName", absoluteEscape)) &&
    !File.Exists(absoluteEscape + ".json"));
Check("json export accepts a plain name",
    (await ConsoleOutput("/json export regression-plain")).Contains(
        I18NManager.Translate("Game.Command.Json.ExportSuccess", "regression-plain.json")) &&
    File.Exists(Path.Combine(jsonDir, "regression-plain.json")));
var originalName = webTarget.Player!.Data.Name;
webTarget.Player!.Data.Name = $"../../{escapeName}";
await ConsoleOutput("/json export");
webTarget.Player!.Data.Name = originalName;
Check("json export keeps a default name with separators inside Config/Json",
    Directory.GetFiles(Path.GetDirectoryName(jsonDir)!, "*", SearchOption.AllDirectories)
        .Concat(Directory.GetFiles(scratch, "*.json", SearchOption.TopDirectoryOnly))
        .Where(f => f.Contains(escapeName))
        .All(f => Path.GetDirectoryName(f) == jsonDir) &&
    !File.Exists(relativeEscape));
var outsideJson = Path.Combine(scratch, "outside.json");
await File.WriteAllTextAsync(outsideJson, "{}");
Check("json import refuses a path outside Config/Json",
    (await ConsoleOutput($"/json {outsideJson}")) ==
    I18NManager.Translate("Game.Command.Json.PathOutsideJsonDirectory", outsideJson));

using (var page = await consoleClient.GetAsync("/player/index.html"))
{
    var body = await page.Content.ReadAsStringAsync();
    Check("console page renders", page.IsSuccessStatusCode && body.Contains("M7_CONSOLE_I18N"));
}

manager.Target = null;
connections.Clear();
Check("async command output is awaited", await Execute("/regression-probe delayed") == "before\nafter");
Check("async command failures reach the response", (await Execute("/regression-probe throws")).Contains(I18NManager.Translate("Game.Command.Notice.InternalError")));
var concurrentOutputs = await Task.WhenAll(Enumerable.Range(1, 12).Select(async id =>
    (Id: id, Output: await Execute($"/regression-probe delayed {id}"))));
Check("concurrent commands retain independent output", concurrentOutputs.All(result =>
    result.Output == $"before:{result.Id}\nafter:{result.Id}"));

var muipSession = new MuipSession { SessionId = "regression-muip", IsAdmin = true, IsAuthorized = true, ExpireTimeStamp = DateTime.Now.AddMinutes(1).ToUnixSec() };
MuipManager.Sessions[muipSession.SessionId] = muipSession;
foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
{
    using var response = method == HttpMethod.Get
        ? await client.GetAsync($"/muip/exec_cmd?SessionId={muipSession.SessionId}&Command=%2Fregression-probe%20delayed&TargetUid=0")
        : await client.PostAsJsonAsync("/muip/exec_cmd", new { sessionId = muipSession.SessionId, command = "/regression-probe delayed", targetUid = 0 });
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var output = Encoding.UTF8.GetString(Convert.FromBase64String(json.RootElement.GetProperty("data").GetProperty("message").GetString()!));
    Check($"MUIP {method} awaits output", output.Replace("\r\n", "\n").TrimEnd() == "before\nafter");
}
MuipManager.CommandHandler = null;
Check("missing command handler reports failure", (await MuipManager.ExecuteCommandAsync(muipSession.SessionId, "/help", 0)).Code != 0);
MuipManager.CommandHandler = manager.HandleCommandAsync;
var secondHandlerRefused = false;
try
{
    MuipManager.CommandHandler += manager.HandleCommandAsync;
}
catch (ArgumentException)
{
    secondHandlerRefused = true;
}
Check("MUIP refuses a second command handler", secondHandlerRefused && MuipManager.CommandHandler is { HasSingleTarget: true });
MuipManager.Sessions.TryRemove(muipSession.SessionId, out _);

var playerSession = new MuipSession { SessionId = "regression-player", IsAdmin = false, IsAuthorized = true, ExpireTimeStamp = DateTime.Now.AddMinutes(1).ToUnixSec() };
MuipManager.Sessions[playerSession.SessionId] = playerSession;
var playerResult = MuipManager.ExecuteCommand(playerSession.SessionId, "/help", 0);
Check("non-admin zero UID cannot become console", Encoding.UTF8.GetString(Convert.FromBase64String(playerResult.Data!.Message!)).Contains(I18NManager.Translate("Game.Command.Notice.TargetNotFound", "0")));
MuipManager.Sessions.TryRemove(playerSession.SessionId, out _);

for (var i = 0; i < 3; i++)
{
    var timer = Stopwatch.StartNew();
    using var response = await client.GetAsync("/admin/api/maintenance");
    response.EnsureSuccessStatusCode();
    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    var data = json.RootElement.GetProperty("data");
    var total = data.GetProperty("machine_total_mb").GetDouble();
    var available = data.GetProperty("machine_available_mb").GetDouble();
    Console.WriteLine($"MEASURE maintenance request {i + 1}: {timer.Elapsed.TotalMilliseconds:F1} ms; memory {available:F0}/{total:F0} MB available");
    Check($"maintenance sample {i + 1} has valid memory", total > 0 && available >= 0 && available <= total);
}
await WarpChecks.Run(client, address, Check);
await ProgressionRewardSettingsChecks.Run(client, Check);
await SecuritySessionChecks.Run(client, fixtureUid, fixturePassword, Check);
await SnapshotChecks.Run(client, Check);
await StoryChecks.Run(client, address, Check);
await EditorChecks.Run(client, Check);
Check("temporary command sessions are removed", MuipManager.Sessions.Count == 0);
await app.StopAsync();
// Private chat: only mutual friends may message each other, blocks are honoured, and history stays bounded.
FriendManager ChatFixture(int uid) =>
    new(new PlayerInstance(new PlayerData { Uid = uid, Name = $"Chat {uid}" }) { Uid = uid });
var alice = ChatFixture(20001);
var bob = ChatFixture(20002);
var mallory = ChatFixture(20003);
alice.FriendData.FriendDetailList[bob.Player.Uid] = new FriendDetailData();
bob.FriendData.FriendDetailList[alice.Player.Uid] = new FriendDetailData();
// Mallory still lists Alice as a friend, but Alice has blocked her.
mallory.FriendData.FriendDetailList[alice.Player.Uid] = new FriendDetailData();
alice.FriendData.BlackList.Add(mallory.Player.Uid);
bool NoChat(FriendManager a, FriendManager b) =>
    !a.FriendData.ChatHistory.ContainsKey(b.Player.Uid) && !b.FriendData.ChatHistory.ContainsKey(a.Player.Uid);
Check("private message to a non-friend is rejected and not stored",
    await mallory.SendMessage(bob.Player.Uid, "hi") == Retcode.RetFriendIsNotFriend && NoChat(mallory, bob));
Check("private message from a blocked sender is rejected and not stored",
    await mallory.SendMessage(alice.Player.Uid, "hi") == Retcode.RetFriendInTargetBlacklist && NoChat(mallory, alice));
Check("private message to a blocked player is rejected",
    await alice.SendMessage(mallory.Player.Uid, "hi") == Retcode.RetFriendInBlacklist && NoChat(alice, mallory));
Check("private message to oneself is rejected",
    await bob.SendMessage(bob.Player.Uid, "hi") == Retcode.RetFriendIsNotFriend
    && !bob.FriendData.ChatHistory.ContainsKey(bob.Player.Uid));
Check("private message to an offline friend is stored for both sides",
    await bob.SendMessage(alice.Player.Uid, "hello") == Retcode.RetSucc
    && alice.FriendData.ChatHistory[bob.Player.Uid].MessageList.Single().Message == "hello"
    && bob.FriendData.ChatHistory[alice.Player.Uid].MessageList.Single().Message == "hello");
var tooManyTargets = Enumerable.Range(30001, FriendManager.MaxMessageTargets).Select(uid => (uint)uid)
    .Append((uint)alice.Player.Uid).ToList();
Check("a request with too many targets is rejected without delivering",
    await bob.SendMessage(tooManyTargets, "spam") == Retcode.RetFriendTargetNumberLimit
    && alice.FriendData.ChatHistory[bob.Player.Uid].MessageList.Count == 1);
Check("a mixed request delivers to friends and reports the rejection",
    await bob.SendMessage([(uint)mallory.Player.Uid, (uint)alice.Player.Uid], "mixed") == Retcode.RetFriendIsNotFriend
    && alice.FriendData.ChatHistory[bob.Player.Uid].MessageList.Last().Message == "mixed" && NoChat(bob, mallory));
for (var i = 0; i < FriendManager.MaxConversationHistory + 5; i++)
    await bob.SendMessage(alice.Player.Uid, $"flood {i}");
Check("private chat history is trimmed per conversation on both sides", new[]
{
    alice.FriendData.ChatHistory[bob.Player.Uid].MessageList, bob.FriendData.ChatHistory[alice.Player.Uid].MessageList
}.All(list => list.Count == FriendManager.MaxConversationHistory
              && list.Last().Message == $"flood {FriendManager.MaxConversationHistory + 4}"));

Console.WriteLine($"Regression failures: {failures.Count}");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

sealed class ProbeCommand : ICommand
{
    public Connection? LastTarget;
    public bool LastExplicit;
    public int SessionsDuringRun = -1;

    public object? Run(CommandArg arg)
    {
        LastTarget = arg.Target;
        LastExplicit = arg.IsTargetExplicit;
        SessionsDuringRun = MuipManager.Sessions.Count;
        if (arg.Args.Contains("delayed")) return Delayed(arg);
        if (arg.Args.Contains("throws")) return Throws();
        return arg.SendMsg($"target={arg.Target?.Player?.Uid ?? 0};explicit={arg.IsTargetExplicit}");
    }

    private static async ValueTask Delayed(CommandArg arg)
    {
        var suffix = arg.Args.Count > 1 ? $":{arg.Args[1]}" : "";
        await arg.SendMsg("before" + suffix);
        await Task.Delay(75);
        await arg.SendMsg("after" + suffix);
    }

    private static async Task Throws()
    {
        await Task.Delay(75);
        throw new InvalidOperationException("Intentional regression probe failure");
    }
}

sealed class TestConnection : Connection
{
    public TestConnection(int uid) : base(null!, new IPEndPoint(IPAddress.Loopback, uid))
    {
        Player = new PlayerInstance(new PlayerData { Uid = uid, Name = $"Fixture {uid}" }) { Uid = uid };
        State = SessionStateEnum.ACTIVE;
    }

    public override void Start() { }
}
