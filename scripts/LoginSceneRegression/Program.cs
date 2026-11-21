using System.Net;
using System.Buffers.Binary;
using System.Net.Sockets;
using System.Threading.Channels;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using March7thHoney.Command;
using March7thHoney.Command.Command;
using March7thHoney.Command.Command.Cmd;
using March7thHoney.Data;
using March7thHoney.Data.Config.Scene;
using March7thHoney.Data.Custom;
using March7thHoney.Data.Excel;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Generated;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.Enums;
using March7thHoney.Enums.Avatar;
using March7thHoney.Enums.Scene;
using March7thHoney.Enums.Task;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Command;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Game.ChessRogue;
using March7thHoney.GameServer.Game.Friend;
using March7thHoney.GameServer.Game.Rogue;
using March7thHoney.GameServer.Game.RogueMagic;
using March7thHoney.GameServer.Game.Scene;
using March7thHoney.GameServer.Game.Scene.Entity;
using March7thHoney.GameServer.Game.Tutorial;
using March7thHoney.GameServer.Server;
using March7thHoney.GameServer.Server.Packet;
using March7thHoney.GameServer.Server.Packet.Recv.Activity;
using March7thHoney.GameServer.Game.Pet;
using March7thHoney.GameServer.Server.Packet.Recv.Lineup;
using March7thHoney.GameServer.Server.Packet.Recv.Pet;
using March7thHoney.GameServer.Server.Packet.Recv.RelicPreset;
using March7thHoney.GameServer.Server.Packet.Send.Item;
using March7thHoney.GameServer.Server.Packet.Send.Activity;
using March7thHoney.GameServer.Server.Packet.Send.Avatar;
using March7thHoney.GameServer.Server.Packet.Send.Chat;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.GameServer.Server.Packet.Recv.Mission;
using March7thHoney.GameServer.Server.Packet.Recv.Rogue;
using March7thHoney.GameServer.Server.Packet.Send.Scene;
using March7thHoney.Internationalization;
using March7thHoney.Proto;
using March7thHoney.ProtoOrganizer;
using March7thHoney.Kcp;
using March7thHoney.Kcp.KcpSharp;
using Google.Protobuf;
using MemoryPack;
using Microsoft.Data.Sqlite;
using March7thHoney.Util;
using March7thHoney.WebServer;
using March7thHoney.WebServer.Handler;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RSA = System.Security.Cryptography.RSA;

// Isolated configuration, random test keys, temporary SQLite database, loopback-only HTTP.
// The checked-out Resource.bin is read, never regenerated or changed.
var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var scratch = Path.Combine(Path.GetTempPath(), "m7h-login-scene-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
Environment.CurrentDirectory = scratch;
var config = ConfigManager.Config;
config.Path.ConfigPath = scratch;
config.Path.LanguagePath = Path.Combine(repository, "Config/Languages");
config.Path.ResourcePath = Path.Combine(repository, "Resources");
config.Path.DatabasePath = scratch;
config.Path.LogPath = Path.Combine(scratch, "Logs");
config.Database.DatabaseType = "sqlite";
config.Database.DatabaseName = "regression.db";
config.HttpServer.UseSSL = false;
config.MuipServer.AdminKey = "local-login-scene-regression-only";
config.GameServer.UsePacketEncryption = false;
config.ServerOption.Language = "CHS";
config.ServerOption.WelcomeMail.Enabled = false;
config.ServerOption.EnableMission = false;
config.ServerOption.AutoCreateUser = true;
config.ServerOption.Auth.PasswordHashIterations = 10000;
config.ServerOption.Auth.SendVerificationEmailOnRegister = false;
config.ServerOption.Auth.AntiAbuse.RateLimitEnabled = false;
config.ServerOption.Auth.AntiAbuse.LoginRateLimitEnabled = false;
config.ServerOption.Auth.AntiAbuse.LimitRegistrationPerIdentity = false;
config.ServerOption.Auth.AntiAbuse.RequireRegistrationIdentity = false;
config.WebSecurity.EnableRsaLoginDecrypt = true;
var applies45ProdCompatibility = GameConstants.IsVersion("4.5.0");
I18NManager.LoadLanguage();
new DatabaseHelper().Initialize(startSaveThread: false);
AuthRateLimiter.Initialize();

var failures = new List<string>();
var checks = 0;
void Check(string name, bool pass)
{
    checks++;
    Console.WriteLine($"{(pass ? "PASS" : "FAIL")} {name}");
    if (!pass) failures.Add(name);
}
int AccountCount() => DatabaseHelper.GetAllInstance<AccountData>()!.Count;
string Encrypt(RSA key, string value, RSAEncryptionPadding? padding = null) =>
    Convert.ToBase64String(key.Encrypt(Encoding.UTF8.GetBytes(value), padding ?? RSAEncryptionPadding.Pkcs1));
void CheckRejected(string name, (string account, string password, bool decryptFailed) result) =>
    Check(name, result.decryptFailed && result.account == "" && result.password == "");

using var key = RSA.Create(2048);
using var otherKey = RSA.Create(2048);
using var legacyKey = RSA.Create(1024);
config.WebSecurity.RsaPrivateKeyPem = key.ExportRSAPrivateKeyPem();
const string Username = "rsa-regression";
const string Password = "Regression-Password-42";
foreach (var padding in new[] { RSAEncryptionPadding.Pkcs1, RSAEncryptionPadding.OaepSHA1, RSAEncryptionPadding.OaepSHA256 })
{
    var result = LoginCryptoHelper.TryDecryptCredentialsStrict(Encrypt(key, Username, padding), Encrypt(key, Password, padding), true);
    Check($"RSA {padding} decrypts both credentials", !result.decryptFailed && result.account == Username && result.password == Password);
}
CheckRejected("wrong 2048-bit key never returns ciphertext", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Encrypt(otherKey, Username), Encrypt(otherKey, Password), true));
CheckRejected("legacy 1024-bit key mismatch never returns ciphertext", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Encrypt(legacyKey, Username), Encrypt(legacyKey, Password), true));
CheckRejected("one failed credential rejects the whole login", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Encrypt(key, Username), Encrypt(otherKey, Password), true));
CheckRejected("explicit encrypted password cannot fall back to plaintext", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Username, Password, true, true));
CheckRejected("explicit malformed Base64 password is rejected", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Username, "not-base64!", true, true));
CheckRejected("encrypted non-UTF8 plaintext is rejected", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Username, Convert.ToBase64String(key.Encrypt(new byte[] { 0xff, 0xfe }, RSAEncryptionPadding.Pkcs1)), true, true));
config.WebSecurity.RsaPrivateKeyPem = "";
CheckRejected("missing private key rejects encrypted credentials", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Encrypt(key, Username), Encrypt(key, Password), true));
var plain = LoginCryptoHelper.TryDecryptCredentialsStrict("YWJjZA==", "cGFzc3dvcmQ=", true);
Check("short Base64-looking plaintext works without a private key", !plain.decryptFailed && plain.account == "YWJjZA==" && plain.password == "cGFzc3dvcmQ=");
config.WebSecurity.RsaPrivateKeyPem = "not-a-private-key";
CheckRejected("invalid PEM rejects encrypted login without throwing", LoginCryptoHelper.TryDecryptCredentialsStrict(
    Encrypt(key, Username), Encrypt(key, Password), true));
config.WebSecurity.RsaPrivateKeyPem = legacyKey.ExportRSAPrivateKeyPem();
var legacy = LoginCryptoHelper.TryDecryptCredentialsStrict(Encrypt(legacyKey, Username), Encrypt(legacyKey, Password), true);
Check("matching legacy 1024-bit key is still supported", !legacy.decryptFailed && legacy.account == Username && legacy.password == Password);
config.WebSecurity.RsaPrivateKeyPem = key.ExportRSAPrivateKeyPem();

await using (var app = WebProgram.BuildWebApp([], 0, "http://127.0.0.1:0"))
{
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    async Task<JsonElement> Login(string path, string account, string password, bool crypto = false)
    {
        using var response = await client.PostAsJsonAsync(path, new { account, password, is_crypto = crypto, device_id = "local-regression" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
    string[] routes = ["/hkrpg_global/mdk/shield/api/login",
        "/hkrpg_global/account/ma-passport/api/appLoginByPassword",
        "/hkrpg_cn/account/ma-passport/api/appLoginByPassword",
        "/account/ma-cn-passport/app/loginByPassword"];
    foreach (var path in routes)
    {
        var baseline = AccountCount();
        foreach (var badKey in new[] { otherKey, legacyKey })
        for (var retry = 0; retry < 2; retry++)
        {
            var response = await Login(path, Encrypt(badKey, Username), Encrypt(badKey, Password), true);
            Check($"{path}: wrong {badKey.KeySize}-bit key retry {retry + 1} rejected before registration",
                response.GetProperty("retcode").GetInt32() == -202 && AccountCount() == baseline);
        }
        var mixedFailure = await Login(path, "must-not-register", Encrypt(otherKey, Password), true);
        Check($"{path}: failed encrypted password cannot create a plaintext account",
            mixedFailure.GetProperty("retcode").GetInt32() == -202 && AccountCount() == baseline);
    }
    foreach (var path in routes)
    {
        for (var retry = 0; retry < 2; retry++)
        {
            var response = await Login(path, Encrypt(key, Username), Encrypt(key, Password), true);
            var uid = AccountData.GetAccountByUserName(Username)?.Uid;
            Check($"{path}: randomized valid ciphertext retry {retry + 1} resolves to one account",
                response.GetProperty("retcode").GetInt32() == 0 && uid != null && AccountCount() == 1);
        }
    }
    var stableUid = AccountData.GetAccountByUserName(Username)!.Uid;
    var mixed = await Login(routes[0], Username, Encrypt(key, Password), true);
    Check("Shield plaintext account plus encrypted password preserves UID",
        mixed.GetProperty("retcode").GetInt32() == 0 && AccountData.GetAccountByUserName(Username)!.Uid == stableUid);
    config.WebSecurity.RsaPrivateKeyPem = "";
    foreach (var path in routes)
    {
        var response = await Login(path, Encrypt(key, "missing-key"), Encrypt(key, Password), true);
        Check($"{path}: missing private key creates no account", response.GetProperty("retcode").GetInt32() == -202 && AccountCount() == 1);
        var plaintext = await Login(path, Username, Password);
        Check($"{path}: ordinary plaintext login remains compatible", plaintext.GetProperty("retcode").GetInt32() == 0);
    }
    config.WebSecurity.EnableRsaLoginDecrypt = false;
    var disabled = await Login(routes[0], Encrypt(key, Username), Encrypt(key, Password), true);
    Check("Shield is_crypto cannot bypass disabled decryption", disabled.GetProperty("retcode").GetInt32() == -202 && AccountCount() == 1);
    await app.StopAsync();
}

SavedValueLoadCondition Condition(OperationEnum operation, params SavedValueCondition[] clauses) => new()
{
    Operation = operation, Conditions = clauses.ToList()
};
var a = new SavedValueCondition { SavedValueName = "a", Value = 1, Operation = CompareTypeEnum.Equal };
var b = new SavedValueCondition { SavedValueName = "b", Value = 1, Operation = CompareTypeEnum.Equal };
foreach (var av in new[] { 0, 1 })
foreach (var bv in new[] { 0, 1 })
{
    var values = new Dictionary<string, int> { ["a"] = av, ["b"] = bv };
    Check($"FSV AND truth table {av}/{bv}", Condition(OperationEnum.And, a, b).IsTrue(values) == (av == 1 && bv == 1));
    Check($"FSV OR truth table {av}/{bv}", Condition(OperationEnum.Or, a, b).IsTrue(values) == (av == 1 || bv == 1));
    Check($"FSV NOT truth table {av}/{bv}", Condition(OperationEnum.Not, a, b).IsTrue(values) == !(av == 1 || bv == 1));
}
Check("empty FSV condition preserves caller default", Condition(OperationEnum.And).IsTrue([])
    && !Condition(OperationEnum.And).IsTrue([], false));
Check("unsupported nonempty FSV operator cannot pass", !Condition(OperationEnum.Unknow, a).IsTrue(new() { ["a"] = 1 }));
var omitted = Condition(OperationEnum.And, new SavedValueCondition { SavedValueName = "a", Value = 1 });
Check("omitted comparison in existing cache means equality", !omitted.IsTrue([]) && omitted.IsTrue(new() { ["a"] = 1 }));
foreach (var (comparison, expected) in new[] { (CompareTypeEnum.Equal, false), (CompareTypeEnum.NotEqual, true),
    (CompareTypeEnum.Greater, true), (CompareTypeEnum.GreaterEqual, true), (CompareTypeEnum.Less, false), (CompareTypeEnum.LessEqual, false) })
{
    Check($"FSV comparison {comparison}", Condition(OperationEnum.And,
        new SavedValueCondition { SavedValueName = "x", Value = 1, Operation = comparison }).IsTrue(new() { ["x"] = 2 }) == expected);
}

string? OptionValue(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? Path.GetFullPath(args[index + 1], repository) : null;
}
if (OptionValue("--legacy-cache") is { } legacyCachePath)
{
    File.Copy(legacyCachePath, Path.Combine(scratch, "Resource.bin"));
    var legacyLoaded = ResourceCache.LoadCache();
    Check("legacy cache validation follows the active protocol requirements", applies45ProdCompatibility
        ? !legacyLoaded && GameData.FloorInfoData["P10202_F10202001"].Groups[169].IsHoyoGroup == null
        : legacyLoaded);
}
// --resource-cache tests a freshly generated cache before it replaces the tracked Config/Resource.bin.
var cacheSource = OptionValue("--resource-cache") ?? Path.Combine(repository, "Config/Resource.bin");
File.Copy(cacheSource, Path.Combine(scratch, "Resource.bin"), overwrite: true);
var rawResourcePath = config.Path.ResourcePath;
config.Path.ResourcePath = Path.Combine(scratch, "no-raw-resource-json");
var loadedCache = ResourceCache.LoadCache();
Check("upgraded cache loads without the raw Resources tree", loadedCache);
if (!loadedCache) throw new InvalidOperationException("The checked-out 4.5 resource cache could not be loaded.");
config.Path.ResourcePath = rawResourcePath;

// With the raw RogueBuffGroup table present, the stale check must accept this cache and reject one whose classic
// groups collapsed onto id 0 (pre-4.5-key-mapping) or whose buff rows lost their classic subtype.
var validateCache = typeof(ResourceCache).GetMethod("ValidateLoadedCache", BindingFlags.Static | BindingFlags.NonPublic)!;
var rogueRawPath = Path.Combine(scratch, "rogue-raw-resource-json");
Directory.CreateDirectory(Path.Combine(rogueRawPath, config.Path.ExcelOutputDirs[0]));
File.WriteAllText(Path.Combine(rogueRawPath, config.Path.ExcelOutputDirs[0], "RogueBuffGroup.json"), "[]");
config.Path.ResourcePath = rogueRawPath;
var freshCacheValid = (bool)validateCache.Invoke(null, null)!;
GameData.RogueBuffGroupData[0] = new RogueBuffGroupExcel();
var collapsedCacheValid = (bool)validateCache.Invoke(null, null)!;
GameData.RogueBuffGroupData.Remove(0);
config.Path.ResourcePath = rawResourcePath;
Check("cache keeps classic Simulated Universe buff and group subtypes",
    GameData.RogueBuffData.Values.OfType<RogueBuffExcel>().Any() &&
    GameData.RogueBuffData.Values.OfType<RogueTournBuffExcel>().Any() &&
    GameData.RogueBuffGroupData.GetValueOrDefault(100005) is RogueBuffGroupExcel { IsLoaded: true } normalDraft &&
    normalDraft.BuffList.Count > 0 && normalDraft.BuffList.All(buff => buff is RogueBuffExcel));
Check("stale check accepts the fresh cache and rejects collapsed classic RogueBuffGroup rows",
    freshCacheValid && !collapsedCacheValid);
Check("cache satisfies the active version's Hoyo metadata requirement", applies45ProdCompatibility
    ? GameData.FloorInfoData["P10202_F10202001"].Groups[169].IsHoyoGroup == true
    : GameData.FloorInfoData["P10202_F10202001"].Groups[169].IsHoyoGroup is null or true);
var mapEntranceFiles = typeof(MapEntranceExcel).GetCustomAttribute<ResourceEntity>()!.FileName;
Check("map entrance loader includes the 4.5 LD table", mapEntranceFiles.Contains("MapEntranceLD.json"));

var cacheConfigPath = config.Path.ConfigPath;
config.Path.ConfigPath = Path.Combine(repository, "Config");
GameData.RogueMapGenData = ResourceManager.LoadCustomFile<Dictionary<int, List<int>>>("Rogue Map", "RogueMapGen")!;
ResourceManager.LoadRogueMagicRoomData();
ResourceManager.LoadChessRogueRoomData();
ResourceManager.LoadChessRogueDiceSurfaceEffectData();
ResourceManager.LoadRogueChestMapInfo();
config.Path.ConfigPath = cacheConfigPath;
var rogueMap = new RogueInstance { MapId = 904 };
rogueMap.InitRooms();
Check("new Rogue maps resolve every site through the room pools",
    rogueMap.StartSiteId != 0 && rogueMap.Rooms.Count == GameData.RogueMapData[904].Count &&
    rogueMap.Rooms.Values.All(room => GameData.RogueRoomData.ContainsKey(room.RoomId)));

var defaultChessBranch = ChessRogueResources.ResolveBranchId(0);
Check("ChessRogue omitted dice branch resolves to a complete default",
    defaultChessBranch != 0 &&
    ChessRogueResources.DefaultSurfaces(defaultChessBranch).Count == ChessRogueResources.DiceSlotCount);
var rogueMagicQuery = RogueMagicProtoBuilder.BuildQueryInfo();
Check("RogueMagic map query includes every area and required nested metadata",
    rogueMagicQuery.DANNIDIPFAL.Count == GameData.RogueMagicAreaData.Count &&
    rogueMagicQuery.DANNIDIPFAL.Count > 0 && rogueMagicQuery.StoryInfo != null &&
    rogueMagicQuery.IHJKLHGMKBI?.JCOBJAMPOAL != null);
var absentFlag = new GroupInfo();
Check("old-cache metadata remains distinguishable from false", MemoryPackSerializer.Deserialize<GroupInfo>(
    MemoryPackSerializer.Serialize(absentFlag))!.IsHoyoGroup == null);
absentFlag.Load();
Check("omitted raw resource Hoyo flag normalizes to false", absentFlag.IsHoyoGroup == false);
var commonFlagFloor = new FloorInfo { GroupInstanceCommonMap = new()
    { ["1"] = new GroupInstanceCommonInfo { IsHoyoGroup = false } } };
var commonFlagGroup = new GroupInfo { Id = 1, IsHoyoGroup = true };
commonFlagFloor.StampCommonMapFields(commonFlagGroup);
Check("explicit floor metadata false overrides group-file true", commonFlagGroup.IsHoyoGroup == false);

// Inspect numeric wire tags independently of the generated property names/descriptors.
Dictionary<int, ulong> UIntFields(byte[] bytes)
{
    var fields = new Dictionary<int, ulong>();
    using var input = new CodedInputStream(bytes);
    uint tag;
    while ((tag = input.ReadTag()) != 0)
        if (WireFormat.GetTagWireType(tag) == WireFormat.WireType.Varint)
            fields.Add(WireFormat.GetTagFieldNumber(tag), input.ReadUInt64());
        else
            input.SkipLastField();
    return fields;
}
// Exercise the operator JSON load and serialized response against video values from official 4.6 seq 133.
// Opening mission 100010102 triggers PerformanceVideo 100010100, whose graph plays video 8.
var cachedVideoKeys = GameData.VideoKeysConfig;
Directory.CreateDirectory(Path.Combine(scratch, "Custom"));
File.Copy(Path.Combine(repository, "Config/Custom/VideoKeysConfig.json"),
    Path.Combine(scratch, "Custom/VideoKeysConfig.json"));
try
{
    GameData.VideoKeysConfig = ResourceManager.LoadCustomFile<VideoKeysConfig>("VideoKeys", "VideoKeysConfig")
        ?? throw new InvalidOperationException("Video key configuration did not load.");
    var videoRsp = GetVideoVersionKeyScRsp.Parser.ParseFrom(
        new March7thHoney.GameServer.Server.Packet.Send.Player.PacketGetVideoVersionKeyScRsp().Data);
    Check("video response preserves both configured lists without mixing their entries",
        videoRsp.VideoKeyInfoList.Select(x => x.Id).SequenceEqual(GameData.VideoKeysConfig.VideoKeyInfoData.Select(x => (uint)x.Id)) &&
        videoRsp.ActivityVideoKeyInfoList.Select(x => x.Id).SequenceEqual(GameData.VideoKeysConfig.ActivityVideoKeyData.Select(x => (uint)x.Id)));
    var openingVideoFields = UIntFields(videoRsp.VideoKeyInfoList.Single(x => x.Id == 8).ToByteArray());
    Check("opening video 8 carries the captured decryption key at the active 4.7 wire tag",
        openingVideoFields.GetValueOrDefault(10) == 70220919 &&
        !openingVideoFields.ContainsKey(7) && !openingVideoFields.ContainsKey(9));
    var modernVideoFields = UIntFields(videoRsp.VideoKeyInfoList.Single(x => x.Id == 5600).ToByteArray());
    Check("modern story video preserves all three captured uint64 fields without swapping or truncation",
        modernVideoFields.GetValueOrDefault(10) == 455385195335115081UL &&
        modernVideoFields.GetValueOrDefault(9) == 453193354110313382UL &&
        modernVideoFields.GetValueOrDefault(7) == 10148726845613043778UL);
    var activityVideoFields = UIntFields(videoRsp.ActivityVideoKeyInfoList.Single(x => x.Id == 1806).ToByteArray());
    Check("activity video carries its captured key and third field independently of story videos",
        activityVideoFields.GetValueOrDefault(10) == 452999623148752523UL &&
        activityVideoFields.GetValueOrDefault(7) == 12506515817811705491UL);
}
finally
{
    GameData.VideoKeysConfig = cachedVideoKeys;
}
var successFields = UIntFields(new SceneMapInfo { FloorId = 10202001 }.ToByteArray());
var sceneMapFloorTag = applies45ProdCompatibility ? 14 : 11;
var sceneMapRetcodeTag = applies45ProdCompatibility ? 6 : 3;
Check("active SceneMapInfo writes floor_id to its verified wire field",
    successFields.GetValueOrDefault(sceneMapFloorTag) == 10202001 && !successFields.ContainsKey(sceneMapRetcodeTag));
var errorFields = UIntFields(new SceneMapInfo { FloorId = 10202001, Retcode = 2 }.ToByteArray());
Check("active SceneMapInfo writes an independent retcode field",
    errorFields.GetValueOrDefault(sceneMapFloorTag) == 10202001 &&
    errorFields.GetValueOrDefault(sceneMapRetcodeTag) == 2);

// Explicit field corrections must also survive a full same-seed regeneration, not just protoc.
TypeDef Definition(string name, string fields) => new()
{
    Name = name, Kind = DefKind.Message, Refs = [], Body = $"message {name} {{\n{fields}\n}}"
};
var rawMap = Definition("HEACNLOGGPH", "    uint32 JFFKLDOPFOJ = 14;\n    uint32 BPKNKCKFEIF = 6;");
var oldMap = Definition("SceneMapInfo", "    uint32 retcode = 14;\n    uint32 floor_id = 6;");
var translations = Translations.Load(Path.Combine(repository, "Proto/4.5v3/translations.txt"));
var migratedMap = SameSeedMigrator.Apply([rawMap], [rawMap], [oldMap], translations).Definitions.Single().Body;
Check("forced 4.5 field mappings override inferred older field meanings",
    migratedMap.Contains("floor_id = 14;") && migratedMap.Contains("retcode = 6;"));
var boundaryPacket = new PacketSyncServerSceneChangeNotify();
Check("active scene boundary uses the generated cmdid and an empty protobuf payload",
    boundaryPacket.CmdId == CmdIds.SyncServerSceneChangeNotify && boundaryPacket.Data.Length == 0 &&
    (applies45ProdCompatibility || CmdIds.SyncServerSceneChangeNotify == 1433));

var player = new PlayerInstance(60001);
await player.InitializeAsync();

var roguePlayer = new PlayerInstance(60002);
await roguePlayer.InitializeAsync();
var rogueAvatarIds = roguePlayer.AvatarManager!.Data.FormalAvatars
    .Select(avatar => avatar.BaseAvatarId).Distinct().Take(2).ToList();
var rogueStart = await roguePlayer.RogueManager!.StartRogue(193, 4, rogueAvatarIds, false);
Check("new Rogue area starts and enters its generated first room",
    rogueStart == Retcode.RetSucc && roguePlayer.RogueManager.RogueInstance?.MapId == 904 &&
    roguePlayer.SceneInstance?.EntryId != 0);

var rogueRoomScene = roguePlayer.SceneInstance!;
var rogueRoomExcel = roguePlayer.RogueManager.GetActiveRoomExcel(rogueRoomScene.EntryId)!;
Check("ordinary Rogue room loads only its configured main and content groups",
    rogueRoomScene.Groups.All(groupId => groupId == rogueRoomExcel.GroupID || rogueRoomExcel.GroupWithContent.ContainsKey(groupId)));
var rogueExitTargets = roguePlayer.RogueManager.GetRoomExitTargets();
List<EntityProp> RogueExitDoors(SceneInstance scene) => scene.GetEntitiesInGroup<EntityProp>(rogueRoomExcel.GroupID)
    .Where(prop => prop.Excel.PropType == PropTypeEnum.PROP_ROGUE_DOOR).ToList();
var rogueRoomHasMonsters = rogueRoomScene.Entities.Values.OfType<EntityMonster>().Any();
var rogueDoors = RogueExitDoors(rogueRoomScene);
Check("Rogue room exit portals carry a next-room destination",
    rogueExitTargets.Count > 0 && rogueDoors.Count > 0 &&
    rogueDoors.All(door => door.RogueDoorTarget is { } target && rogueExitTargets.Contains(target)));
Check("Rogue room exit portal serializes rogue_game_info {room_id, site}", rogueDoors.All(door =>
    door.ToProto().Prop.ExtraInfo?.RogueGameInfo is { } info && info.RoomId == door.RogueDoorTarget!.Value.RoomId &&
    info.MAJKFIMPOOF == door.RogueDoorTarget.Value.SiteId));
Check("Rogue combat room exit portal loads Closed until the room is cleared", !rogueRoomHasMonsters ||
    rogueDoors.All(door => door.State == PropStateEnum.Closed));
var seededDoor = rogueDoors[0];
roguePlayer.SetScenePropData(rogueRoomScene.FloorId, seededDoor.GroupId, seededDoor.InstId, PropStateEnum.Open);
var rebuiltRogueRoom = new SceneInstance(roguePlayer, rogueRoomScene.Excel, rogueRoomScene.FloorId, rogueRoomScene.EntryId);
var rebuiltDoor = await rebuiltRogueRoom.EntityLoader!.LoadProp(seededDoor.PropInfo, seededDoor.Group);
Check("Rogue room ignores a saved portal state and ships it Closed with its destination",
    rebuiltRogueRoom.EntityLoader is RogueEntityLoader && rebuiltDoor?.State == PropStateEnum.Closed &&
    rebuiltDoor.RogueDoorTarget == seededDoor.RogueDoorTarget);
roguePlayer.SceneData!.ScenePropData.Remove(rogueRoomScene.FloorId);

var chessPlayer = new PlayerInstance(60003);
await chessPlayer.InitializeAsync();
var chessAvatarIds = chessPlayer.AvatarManager!.Data.FormalAvatars
    .Select(avatar => avatar.BaseAvatarId).Distinct().Take(2).ToList();
var chessStart = await chessPlayer.ChessRogueManager!.StartRun(205, 6, 0, chessAvatarIds, []);
Check("ChessRogue start accepts the client's omitted dice branch",
    chessStart == Retcode.RetSucc && chessPlayer.ChessRogueManager.RunInstance?.BranchId == defaultChessBranch);

var magicPlayer = new PlayerInstance(60004);
await magicPlayer.InitializeAsync();
var magicAvatarIds = magicPlayer.AvatarManager!.Data.FormalAvatars
    .Select(avatar => avatar.BaseAvatarId).Distinct().Take(2).ToList();
var magicAreaId = GameData.RogueMagicAreaData.Keys.Min();
var magicStart = await magicPlayer.RogueMagicManager!.StartRun(magicAreaId, 1, [], magicAvatarIds);
Check("RogueMagic area data builds a playable run",
    magicStart.Ret == Retcode.RetSucc && magicStart.Run?.Layers.Count > 0);

await RogueTournRegression.Run(Check, Path.Combine(repository, "Config"));
await ChallengeRegression.Run(Check);

// Global chat: a second synthetic contact whose messages are relayed to every online player.
var serverOption = config.ServerOption;
var globalChatUid = serverOption.GetGlobalChatUid() ?? 0;
Check("global chat is enabled by default on its own uid", globalChatUid == 98 && globalChatUid != serverOption.ServerProfile.Uid);
var friendList = player.FriendManager!.ToProto();
var globalContact = friendList.FriendList.SingleOrDefault(friend => friend.PlayerInfo.Uid == globalChatUid);
Check("friend list shows the global chat contact next to the command bot", globalContact != null
    && friendList.FriendList.Any(friend => friend.PlayerInfo.Uid == serverOption.ServerProfile.Uid)
    && globalContact.IsMarked && globalContact.PlayerInfo.OnlineStatus == FriendOnlineStatus.Online
    && globalContact.PlayerInfo.Nickname == serverOption.GlobalChat.Name
    && globalContact.PlayerInfo.AssistSimpleInfoList.Count == serverOption.GlobalChat.AssistInfo.Count);
Check("player detail lookup resolves the global contact, not the first system entry",
    player.FriendManager.GetFriendPlayerData([globalChatUid]).FirstOrDefault(x => x.Uid == globalChatUid)?.Name
    == serverOption.GlobalChat.Name);
Check("friend requests to system contacts are rejected", await player.FriendManager.AddFriend(globalChatUid)
    == Retcode.RetFriendAlreadyIsFriend && !player.FriendManager.FriendData.SendApplyList.Contains(globalChatUid));
List<FriendChatData> GlobalHistory(PlayerInstance p) =>
    p.FriendManager!.FriendData.ChatHistory.GetValueOrDefault(globalChatUid)?.MessageList ?? [];
var chatClock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var relayRecipients = new[] { player, roguePlayer, chessPlayer };
var relayResult = await GlobalChatManager.RelayAsync(magicPlayer, "  hello everyone  ", relayRecipients, chatClock);
var expectedRelay = $"[{magicPlayer.Data.Name} / {magicPlayer.Uid}]: hello everyone";
Check("global chat relays to every recipient and the sender from the contact", relayResult == GlobalChatRelayResultEnum.Relayed
    && relayRecipients.Append(magicPlayer).All(p => GlobalHistory(p).LastOrDefault() is { } last
        && last.Message == expectedRelay && last.SendUid == globalChatUid && last.ReceiveUid == p.Uid));
Check("global chat never runs through the command bot conversation",
    !magicPlayer.FriendManager!.FriendData.ChatHistory.ContainsKey(serverOption.ServerProfile.Uid));
var historyBefore = GlobalHistory(player).Count;
Check("empty global chat text is ignored", await GlobalChatManager.RelayAsync(magicPlayer, "   ", relayRecipients, chatClock)
    == GlobalChatRelayResultEnum.Ignored && GlobalHistory(player).Count == historyBefore);
Check("too-long global chat text is rejected without relaying", await GlobalChatManager.RelayAsync(magicPlayer,
    new string('x', serverOption.GlobalChat.MaxLength + 1), relayRecipients, chatClock) == GlobalChatRelayResultEnum.TooLong
    && GlobalHistory(player).Count == historyBefore);
Check("global chat rate limit allows the configured burst", await GlobalChatManager.RelayAsync(magicPlayer, "two",
    relayRecipients, chatClock.AddSeconds(1)) == GlobalChatRelayResultEnum.Relayed
    && await GlobalChatManager.RelayAsync(magicPlayer, "three", relayRecipients, chatClock.AddSeconds(2))
    == GlobalChatRelayResultEnum.Relayed);
Check("global chat rate limit rejects the next message inside the window", await GlobalChatManager.RelayAsync(magicPlayer,
    "four", relayRecipients, chatClock.AddSeconds(59)) == GlobalChatRelayResultEnum.RateLimited
    && GlobalHistory(player).Last().Message.EndsWith(": three"));
Check("global chat rate limit is per sender", await GlobalChatManager.RelayAsync(chessPlayer, "other sender",
    relayRecipients, chatClock.AddSeconds(59)) == GlobalChatRelayResultEnum.Relayed);
Check("global chat rate limit window slides", await GlobalChatManager.RelayAsync(magicPlayer, "five", relayRecipients,
    chatClock.AddSeconds(60)) == GlobalChatRelayResultEnum.Relayed);
var rateLimitCount = serverOption.GlobalChat.RateLimitCount;
serverOption.GlobalChat.RateLimitCount = 0;
for (var i = 0; i < serverOption.GlobalChat.MaxHistory + 5; i++)
    await GlobalChatManager.RelayAsync(roguePlayer, $"flood {i}", relayRecipients, chatClock.AddMinutes(5));
serverOption.GlobalChat.RateLimitCount = rateLimitCount;
Check("global chat history is trimmed to MaxHistory per player", relayRecipients.All(p =>
    GlobalHistory(p).Count == serverOption.GlobalChat.MaxHistory
    && GlobalHistory(p).Last().Message.EndsWith($": flood {serverOption.GlobalChat.MaxHistory + 4}")));
var globalHistoryPacket = GetPrivateChatHistoryScRsp.Parser.ParseFrom(
    new PacketGetPrivateChatHistoryScRsp((uint)player.Uid, (uint)globalChatUid, player).Data);
Check("global chat history ends with the configured welcome as its oldest entry",
    globalHistoryPacket.ChatMessageList.Count == serverOption.GlobalChat.MaxHistory + 1
    && globalHistoryPacket.ChatMessageList.Last().MessageDatas.Single().ChatData.MessageText
    == serverOption.GlobalChat.WelcomeMessage);
var botHistoryPacket = GetPrivateChatHistoryScRsp.Parser.ParseFrom(new PacketGetPrivateChatHistoryScRsp(
    (uint)player.Uid, (uint)serverOption.ServerProfile.Uid, player).Data);
Check("command bot history gets no global chat welcome", botHistoryPacket.ChatMessageList.All(message =>
    message.MessageDatas.All(data => data.ChatData.MessageText != serverOption.GlobalChat.WelcomeMessage)));
serverOption.GlobalChat.Uid = serverOption.ServerProfile.Uid;
Check("global chat uid colliding with the command bot disables the contact", serverOption.GetGlobalChatUid() == null
    && player.FriendManager.GetFriendPlayerData().Count(x => x.Uid == serverOption.ServerProfile.Uid) == 1);
serverOption.GlobalChat.Uid = globalChatUid;
serverOption.GlobalChat.Enabled = false;
Check("disabled global chat removes the contact and relays nothing", serverOption.GetGlobalChatUid() == null
    && player.FriendManager.GetFriendPlayerData().All(x => x.Uid != globalChatUid)
    && await GlobalChatManager.RelayAsync(magicPlayer, "off", relayRecipients, chatClock.AddHours(1))
    == GlobalChatRelayResultEnum.Disabled);
serverOption.GlobalChat.Enabled = true;

// /giveall avatar: only typed values touch owned characters; new characters start at l1 / r0.
var givePlayer = new PlayerInstance(60005);
await givePlayer.InitializeAsync();
var giveTarget = new ProbeConnection { Player = givePlayer };
var giveSender = new ProbeSender();
var ownedHero = givePlayer.AvatarManager!.GetHero()!;
var ownedMarch = givePlayer.AvatarManager.GetFormalAvatar(1001)!;
ownedHero.Level = 70;
ownedHero.GetCurPathInfo().Rank = 2;
ownedMarch.Level = 50;
ownedMarch.GetCurPathInfo().Rank = 4;
var heroPathBefore = ownedHero.AvatarId;
var marchPathBefore = ownedMarch.AvatarId;
var maleHeroPathsBefore = ownedHero.PathInfos.Keys.Where(pathId => pathId % 2 == 1).ToHashSet();
var giveall = new CommandGiveall();
await giveall.GiveAllAvatar(new CommandArg("avatar", giveSender, giveTarget));
var newAvatar = givePlayer.AvatarManager.Data.FormalAvatars.FirstOrDefault(avatar => avatar.BaseAvatarId == 1002);
Check("bare /giveall avatar keeps owned levels and Eidolons", ownedHero.Level == 70 && ownedHero.GetCurPathInfo().Rank == 2
    && ownedMarch.Level == 50 && ownedMarch.GetCurPathInfo().Rank == 4);
Check("bare /giveall avatar keeps the current path of owned characters",
    ownedHero.AvatarId == heroPathBefore && ownedMarch.AvatarId == marchPathBefore);
Check("bare /giveall avatar creates missing characters at l1 / r0",
    newAvatar is { Level: 1 } && newAvatar.PathInfos.Values.All(path => path.Rank == 0));
Check("giveall unlocks every Path of multi-path characters", ownedMarch.PathInfos.ContainsKey(1224)
    && GameData.MultiplePathAvatarConfigData.Values.Where(x => x.BaseAvatarID == 8001 && x.AvatarID % 2 == 0)
        .All(x => ownedHero.PathInfos.ContainsKey(x.AvatarID)));
Check("giveall unlocks only the Trailblazer paths of the player's gender",
    ownedHero.PathInfos.Keys.Where(pathId => pathId % 2 == 1).ToHashSet().SetEquals(maleHeroPathsBefore));
await giveall.GiveAllAvatar(new CommandArg("avatar r6", giveSender, giveTarget));
Check("explicit giveall Eidolon applies to every owned path without touching levels",
    ownedMarch.PathInfos.Values.All(path => path.Rank == 6) && ownedHero.PathInfos.Values.All(path => path.Rank == 6)
    && ownedMarch.Level == 50 && ownedHero.Level == 70 && newAvatar?.Level == 1);
await giveall.GiveAllAvatar(new CommandArg("avatar l80", giveSender, giveTarget));
Check("explicit giveall level applies without touching Eidolons", ownedMarch.Level == 80 && ownedHero.Level == 80
    && newAvatar?.Level == 80 && ownedMarch.PathInfos.Values.All(path => path.Rank == 6)
    && ownedMarch.Promotion == GameData.GetMinPromotionForLevel(80));

// /language: per-player pin persisted on PlayerData, used by command replies and chat notices.
var languageCommand = new CommandLanguage();
var languageTarget = new ProbeConnection { Player = givePlayer };
var languageSender = new ProbeSender(givePlayer.Uid);
async Task<string> LanguageReply(string raw, ICommandSender who)
{
    var probeWho = who as ProbeSender;
    probeWho?.Messages.Clear();
    await languageCommand.Language(new CommandArg(raw, who, languageTarget));
    return probeWho?.Messages.LastOrDefault() ?? "";
}
givePlayer.Language = "CHS";
Check("console cannot pin a language", await LanguageReply("en", new ProbeSender())
    == I18NManager.Translate("Game.Command.Language.PlayerOnly") && givePlayer.Data.LanguagePin == null);
Check("language list shows only installed language files", (await LanguageReply("list", languageSender))
    == I18NManager.Translate("Game.Command.Language.Available", "CHS, CHT, EN"));
Check("language alias pins EN and answers in English", await LanguageReply("English", languageSender)
    == I18NManager.TranslateAsCertainLang("EN", "Game.Command.Language.Set", "EN")
    && givePlayer.Data.LanguagePin == "EN" && givePlayer.EffectiveLanguage == "EN"
    && new PlayerCommandSender(givePlayer).Language == "EN");
Check("language aliases are case-insensitive", await LanguageReply("ZH-Hant", languageSender)
    == I18NManager.TranslateAsCertainLang("CHT", "Game.Command.Language.Set", "CHT") && givePlayer.Data.LanguagePin == "CHT");
Check("language code without a file is rejected", (await LanguageReply("id", languageSender))
    == I18NManager.Translate("Game.Command.Language.NotFound", "id", "CHS, CHT, EN") && givePlayer.Data.LanguagePin == "CHT");
Check("language auto clears the pin and answers in the client language", await LanguageReply("auto", languageSender)
    == I18NManager.TranslateAsCertainLang("CHS", "Game.Command.Language.Cleared", "CHS")
    && givePlayer.Data.LanguagePin == null && givePlayer.EffectiveLanguage == "CHS");
givePlayer.Data.LanguagePin = "EN";
DatabaseHelper.MarkDirty(givePlayer.Uid);
DatabaseHelper.Instance!.SaveDatabase();
await using (var db = new SqliteConnection($"Data Source={Path.Combine(scratch, "regression.db")}"))
{
    await db.OpenAsync();
    await using var query = db.CreateCommand();
    query.CommandText = "select LanguagePin from Player where Uid = $uid";
    query.Parameters.AddWithValue("$uid", givePlayer.Uid);
    Check("language pin is persisted in the Player table", (await query.ExecuteScalarAsync()) as string == "EN");
}
await using (var oldDb = new SqliteConnection($"Data Source={Path.Combine(scratch, "old-player-table.db")}"))
{
    await oldDb.OpenAsync();
    await using (var create = oldDb.CreateCommand())
    {
        create.CommandText = "create table \"Player\" (\"Uid\" INTEGER PRIMARY KEY, \"Name\" TEXT); " +
                             "insert into \"Player\" (\"Uid\", \"Name\") values (70001, 'legacy')";
        await create.ExecuteNonQueryAsync();
    }
    var playerStore = EntityStores.Map[typeof(PlayerData)];
    playerStore.EnsureTable(oldDb);
    var legacyPlayer = playerStore.LoadAll(oldDb).OfType<PlayerData>().Single();
    Check("old Player tables gain a nullable LanguagePin column that loads as unpinned",
        legacyPlayer.Uid == 70001 && legacyPlayer.Name == "legacy" && legacyPlayer.LanguagePin == null);
}
givePlayer.Data.LanguagePin = null;

// Trailblazer paths: both genders stay stored, only the current gender is announced, and path changes are
// normalized to the player's gender and echo the stored id.
var heroPathConfigs = GameData.MultiplePathAvatarConfigData.Values.Where(x => x.BaseAvatarID == 8001).ToList();
Check("Trailblazer path configs pair odd=male / even=female", heroPathConfigs.Count > 0 && heroPathConfigs.All(x =>
    x.Gender == (x.AvatarID % 2 == 1 ? GenderTypeEnum.GENDER_MAN : GenderTypeEnum.GENDER_WOMAN)));
var hero = givePlayer.AvatarManager.GetHero()!;
List<uint> AnnouncedHeroPaths() => hero.ToAvatarPathDataProto().Select(path => path.AvatarId).ToList();
bool OnlyGender(IEnumerable<uint> ids, int parity) => ids.Where(id => id is > 8000 and < 9000).All(id => id % 2 == parity);
Check("female hero stores both genders but announces only female paths", givePlayer.Data.CurrentGender == Gender.Woman
    && hero.PathInfos.ContainsKey(8001) && hero.PathInfos.ContainsKey(8002) && OnlyGender(AnnouncedHeroPaths(), 0)
    && AnnouncedHeroPaths().Count == heroPathConfigs.Count(x => x.AvatarID % 2 == 0));
var avatarDataRsp = GetAvatarDataScRsp.Parser.ParseFrom(new PacketGetAvatarDataScRsp(givePlayer).Data);
Check("avatar data basic types and path list agree on the hero's gender",
    OnlyGender(avatarDataRsp.BasicTypeIdList, 0) && OnlyGender(avatarDataRsp.AvatarPathDataInfoList.Select(x => x.AvatarId), 0)
    && avatarDataRsp.BasicTypeIdList.Contains(8004u) && !avatarDataRsp.BasicTypeIdList.Contains(8003u));
var heroSync = PlayerSyncScNotify.Parser.ParseFrom(new PacketPlayerSyncScNotify(hero).Data);
Check("avatar sync announces only the hero's gender paths",
    OnlyGender(heroSync.AvatarSync.AvatarPathDataInfoList.Select(x => x.AvatarId), 0));
Check("female choosing a male path id stores the female variant", await givePlayer.ChangeAvatarPathType(8001,
    (MultiPathAvatarTypeEnum)8003) == 8004 && hero.AvatarId == 8004 && givePlayer.Data.CurBasicType == 8004);
Check("female choosing a female path id keeps it", await givePlayer.ChangeAvatarPathType(8001,
    (MultiPathAvatarTypeEnum)8006) == 8006 && hero.AvatarId == 8006);
Check("path change accepts any variant id as the base", await givePlayer.ChangeAvatarPathType(8004,
    (MultiPathAvatarTypeEnum)8002) == 8002 && hero.AvatarId == 8002);
Check("another character's path cannot be put on the hero", await givePlayer.ChangeAvatarPathType(8001,
    MultiPathAvatarTypeEnum.Mar_7thRogue) == 0 && hero.AvatarId == 8002);
Check("non-hero path change stores the requested path", await givePlayer.ChangeAvatarPathType(1001,
    MultiPathAvatarTypeEnum.Mar_7thRogue) == 1224 && ownedMarch.AvatarId == 1224);
giveSender.Messages.Clear();
await new CommandHero().ChangeType(new CommandArg("type 1001", giveSender, giveTarget));
Check("/hero type rejects a non-Trailblazer path", hero.AvatarId == 8002
    && giveSender.Messages.Single() == I18NManager.Translate("Game.Command.Hero.HeroTypeNotSpecified"));
await givePlayer.SetHeroGender(Gender.Man);
Check("gender switch keeps stored paths and announces only male paths", hero.AvatarId == 8001
    && hero.PathInfos.ContainsKey(8004) && OnlyGender(AnnouncedHeroPaths(), 1));
Check("male choosing a female path id stores the male variant", await givePlayer.ChangeAvatarPathType(8001,
    (MultiPathAvatarTypeEnum)8004) == 8003 && hero.AvatarId == 8003 && OnlyGender(AnnouncedHeroPaths(), 1));

var rogueTournLobby = new SceneInstance(player, GameData.MazePlaneData[10341], 10341001, 10341901);
var rogueTournLobbyGroup = rogueTournLobby.FloorInfo!.Groups[13];
var rogueTournEntrance = await rogueTournLobby.EntityLoader!.LoadProp(
    rogueTournLobbyGroup.PropList.Single(prop => prop.ID == 300001), rogueTournLobbyGroup);
var rogueTournEmblem = await rogueTournLobby.EntityLoader.LoadProp(
    rogueTournLobbyGroup.PropList.Single(prop => prop.ID == 300002), rogueTournLobbyGroup);
Check("RogueTourn lobby entrance and emblem default open",
    rogueTournEntrance?.State == PropStateEnum.Open && rogueTournEmblem?.State == PropStateEnum.Open);
// Anomaly Arbitration group 11 (4.7): its floor 30147001 is extracted from the 4.6.51 client and all four
// levels resolve to that floor with a monster in their maze group.
if (GameData.ChallengePeakGroupConfigData.ContainsKey(11))
{
    var group11Levels = new[] { 1101, 1102, 1103, 1104 }
        .Select(id => GameData.ChallengePeakConfigData.GetValueOrDefault(id))
        .ToList();
    Check("Anomaly Arbitration group 11 levels enter floor 30147001 with a placed monster",
        GameData.FloorInfoData.TryGetValue("P30147_F30147001", out var peakFloor11) &&
        group11Levels.All(level => level != null && level.GetMapEntranceId() == 3014701 &&
                                   peakFloor11.Groups.TryGetValue(level.GetMazeGroupId(), out var mazeGroup) &&
                                   mazeGroup.MonsterList.Count > 0 &&
                                   level.ChallengeMonsters.GetValueOrDefault(level.GetMazeGroupId())?.Count > 0) &&
        GameData.ChallengePeakConfigData[1104].BossExcel != null);
}
// 4.7 maps extracted from the 4.6.51 client (absent from the community dump): each floor is cached and a scene
// can be built at its entrance with its server-side groups.
foreach (var (planeId, floorId, entryId) in new[] { (20571, 20571001, 2057102), (20572, 20572001, 2057201),
             (30547, 30547001, 30547001), (40571, 40571001, 40571001), (40571, 40571002, 40571002),
             (40572, 40572001, 40572001), (40573, 40573001, 40573001) })
{
    if (!GameData.MazePlaneData.ContainsKey(planeId)) continue;
    var built = GameData.FloorInfoData.TryGetValue($"P{planeId}_F{floorId}", out var newFloor) &&
                newFloor.Groups.Count > 0 &&
                new SceneInstance(player, GameData.MazePlaneData[planeId], floorId, entryId).FloorInfo == newFloor;
    Check($"4.7 floor {floorId} is cached and builds a scene", built);
}
// Anomaly Arbitration group 10 floor: the RoomStateChange trigger prop's level graph is missing from the
// 4.7 client and blocks its scene loading; the floor's other props must still be created.
if (GameData.FloorInfoData.ContainsKey("P30146_F30146001"))
{
    var peakFloor = new SceneInstance(player, GameData.MazePlaneData[30146], 30146001, 3014601);
    var roomStateGroup = peakFloor.FloorInfo!.Groups[16];
    var defaultGroup = peakFloor.FloorInfo.Groups[1];
    Check("Anomaly Arbitration floor omits the client-missing RoomStateChange trigger prop",
        await peakFloor.EntityLoader!.LoadProp(roomStateGroup.PropList.Single(prop => prop.ID == 300001), roomStateGroup) == null &&
        await peakFloor.EntityLoader.LoadProp(defaultGroup.PropList.Single(prop => prop.ID == 300001), defaultGroup) != null);
}
var scene = new SceneInstance(player, GameData.MazePlaneData[10202], 10202001, 102020107);
var snapshot = scene.BuildClientFloorSavedData();
Check("Changle Tian restores floor-wide activity defaults without writing player saves",
    snapshot.GetValueOrDefault("LoadBianZhong") == 1 && snapshot.GetValueOrDefault("ChallengeStoryEntrance") == 1
    && snapshot.GetValueOrDefault("FSV_UmbrellaGroup_IsHidden") == 0
    && !player.SceneData!.FloorSavedData.ContainsKey(10202001));
var group170 = scene.FloorInfo!.Groups[170];
Check("production-cache Pure Fiction entrance is enabled with empty save", group170.SavedValueCondition.IsTrue(snapshot));
scene.EntityLoader!.LoadGroups.Add(170);
Check("production-cache Pure Fiction entrance props are actually created", (await scene.EntityLoader.LoadGroup(group170))
    ?.OfType<EntityProp>().Any(prop => prop.InstId == 300002 && prop.Excel.ID == 102226) == true);
player.SceneData!.FloorSavedData[10202001] = new() { ["ChallengeStoryEntrance"] = 0, ["LoadBianZhong"] = 0 };
Check("explicit saved zero still overrides floor-wide activity defaults",
    scene.BuildClientFloorSavedData().GetValueOrDefault("ChallengeStoryEntrance") == 0
    && scene.BuildClientFloorSavedData().GetValueOrDefault("LoadBianZhong") == 0
    && !group170.SavedValueCondition.IsTrue(scene.BuildClientFloorSavedData()));
player.SceneData.FloorSavedData.Remove(10202001);

var synthetic = new FloorInfo { FloorID = 99000001, StartGroupID = 901, DimensionList =
[
    new() { ID = 0, GroupIDList = [901], SavedValues = [new() { Name = "defaultOn", DefaultValue = 1 }, new() { Name = "zero", DefaultValue = 0 }] },
    new() { ID = 2, GroupIDList = [902], SavedValues = [new() { Name = "otherDimension", DefaultValue = 9 }] }
] };
var syntheticGroup = new GroupInfo
{
    Id = 901,
    LoadSide = GroupLoadSideEnum.Server,
    SavedValueCondition = Condition(OperationEnum.And,
        new SavedValueCondition { SavedValueName = "defaultOn", Value = 1, Operation = CompareTypeEnum.Equal }),
    PropList = [new PropInfo { ID = 300001, PropID = 100, LoadOnInitial = false }],
    NPCList = [new NpcInfo { ID = 400001, NPCID = 100, LoadOnInitial = false }]
};
synthetic.Groups[901] = syntheticGroup;
var syntheticScene = new SceneInstance(player, GameData.MazePlaneData[10202], synthetic.FloorID, 0) { FloorInfo = synthetic };
syntheticScene.EntityLoader = new SceneEntityLoader(syntheticScene) { LoadGroups = [901] };
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(player, syntheticScene);
var defaults = syntheticScene.BuildClientFloorSavedData();
Check("scene defaults come only from the active dimension", defaults.GetValueOrDefault("defaultOn") == 1
    && defaults.GetValueOrDefault("zero") == 0 && !defaults.ContainsKey("otherDimension"));
var syntheticEntities = await syntheticScene.EntityLoader.LoadGroup(syntheticGroup);
Check("entity loader evaluates nonzero resource defaults", syntheticEntities != null);
Check("ordinary LoadOnInitial=false server prop remains available", syntheticEntities?.OfType<EntityProp>()
    .Any(prop => prop.InstId == 300001) == true);
Check("ordinary LoadOnInitial=false server NPC remains available", syntheticEntities?.OfType<EntityNpc>()
    .Any(npc => npc.InstId == 400001) == true);
const int SyntheticProbeEntityId = 900001;
syntheticScene.Entities[SyntheticProbeEntityId] = new ProbeEntity { EntityId = SyntheticProbeEntityId, GroupId = 901 };
await syntheticScene.EntityLoader.SyncEntity();
Check("entity sync retains groups enabled by resource defaults", syntheticScene.Entities.ContainsKey(SyntheticProbeEntityId));
player.SceneData.FloorSavedData[synthetic.FloorID] = new() { ["defaultOn"] = 0 };
Check("persisted zero overrides a nonzero default", syntheticScene.BuildClientFloorSavedData()["defaultOn"] == 0);
await syntheticScene.EntityLoader.SyncEntity();
Check("entity sync unloads group when persisted switch becomes zero",
    !syntheticScene.Entities.ContainsKey(SyntheticProbeEntityId) && !syntheticScene.Groups.Contains(901)
    && syntheticScene.Entities.Values.All(entity => entity.GroupId != 901));
Check("entity loader respects persisted zero", await syntheticScene.EntityLoader.LoadGroup(syntheticGroup) == null);
var ordinaryAtmosphere = new GroupInfo { Id = 902, LoadSide = GroupLoadSideEnum.Server,
    Category = GroupCategoryEnum.Atmosphere, IsHoyoGroup = false };
synthetic.Groups[902] = ordinaryAtmosphere;
syntheticScene.EntityLoader.LoadGroups.Add(902);
Check("non-Hoyo atmosphere groups remain loadable", await syntheticScene.EntityLoader.LoadGroup(ordinaryAtmosphere) != null);
ordinaryAtmosphere.IsHoyoGroup = true;
await syntheticScene.EntityLoader.SyncEntity();
Check("sync retains ordinary empty Hoyo atmosphere groups", syntheticScene.Groups.Count(id => id == 902) == 1);
await syntheticScene.EntityLoader.UnloadGroup(902);
Check("Hoyo atmosphere alone does not block group activation", await syntheticScene.EntityLoader.LoadGroup(ordinaryAtmosphere) != null);
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(player, null);

var badEntry = await player.TryEnterScene(10001, 0, false);
Check("plane ID is not silently accepted as entrance ID", !badEntry.Success && !badEntry.IsOverMap);
var initial = await player.TryEnterScene(1000001, 0, false);
Check("valid scene entry reports actual success", initial.Success && player.Data.EntryId == 1000001);
var same = await player.TryEnterScene(1000001, 0, false, mapTp: true);
Check("same-scene teleport is success with IsOverMap=false", same.Success && !same.IsOverMap);
Check("legacy EnterScene API keeps its over-map return contract", !await player.EnterScene(1000001, 0, false));
var realEntry = await player.TryEnterScene(102020107, 0, false);
Check("Changle Tian scene can be built from production cache", realEntry.Success && realEntry.IsOverMap && player.Data.FloorId == 10202001);
Check("Changle Tian scene snapshot includes the enabled activity group", player.SceneInstance!.Groups.Contains(170)
    && player.SceneInstance.BuildClientFloorSavedData().GetValueOrDefault("ChallengeStoryEntrance") == 1);
var changle = player.SceneInstance!;
var excludedChangleProps = new[] { (144, 300003), (208, 300001), (208, 300002) };
if (applies45ProdCompatibility)
{
    foreach (var (groupId, instanceId) in excludedChangleProps)
        Check($"Changle Tian omits Android-incompatible prop {groupId}/{instanceId}", !changle.Entities.Values.OfType<EntityProp>()
            .Any(prop => prop.GroupId == groupId && prop.InstId == instanceId));
    Check("ordinary Changle Tian puzzle prop remains available", changle.Entities.Values.OfType<EntityProp>()
        .Any(prop => prop.GroupId == 144 && prop.InstId == 300001));
    Check("Changle Tian incompatible atmosphere group is not spawned", !changle.Groups.Contains(169)
        && changle.Entities.Values.All(entity => entity.GroupId != 169));
    await changle.EntityLoader!.SyncEntity();
    await changle.EntityLoader.SyncEntity();
    Check("stage handler stays loaded exactly once across repeated sync", changle.Groups.Count(id => id == 208) == 1
        && await changle.EntityLoader.LoadGroup(changle.FloorInfo!.Groups[208]) == null);
    Check("sync does not reintroduce incompatible Changle Tian content", !changle.Groups.Contains(169)
        && changle.Entities.Values.All(entity => entity.GroupId != 208 && entity.GroupId != 169
            && (entity.GroupId != 144 || entity is not EntityProp { InstId: 300003 })));
    foreach (var (groupId, instanceId) in excludedChangleProps.Append((169, 300001)))
    {
        var group = changle.FloorInfo!.Groups[groupId];
        Check($"direct creation cannot reintroduce incompatible prop {groupId}/{instanceId}",
            await changle.EntityLoader.LoadProp(group.PropList.Single(prop => prop.ID == instanceId), group) == null);
    }
    Check("forced group activation cannot reintroduce incompatible atmosphere",
        await changle.EntityLoader.LoadGroup(changle.FloorInfo!.Groups[169], forceLoad: true) == null);
    // Simulate stale group membership and an entity created before the compatibility rule applied.
    changle.Groups.Add(169);
    changle.Entities[SyntheticProbeEntityId] = new ProbeEntity { EntityId = SyntheticProbeEntityId, GroupId = 169 };
    await changle.EntityLoader.SyncEntity();
    Check("sync removes stale incompatible atmosphere membership and entities", !changle.Groups.Contains(169)
        && changle.Entities.Values.All(entity => entity.GroupId != 169));
    var stageGroup = changle.FloorInfo!.Groups[208];
    Check("explicit creation cannot reintroduce the PCOnly capture", await changle.EntityLoader.LoadProp(
        stageGroup.PropList.Single(prop => prop.ID == 300001), stageGroup) == null);
    await changle.EntityLoader.UnloadGroup(208);
    var forcedStage = await changle.EntityLoader.LoadGroup(stageGroup, forceLoad: true);
    Check("forced stage activation keeps an empty handler without incompatible captures", forcedStage is { Count: 0 }
        && changle.Groups.Count(id => id == 208) == 1);
    await changle.EntityLoader.UnloadGroup(144);
    var forcedPuzzle = await changle.EntityLoader.LoadGroup(changle.FloorInfo!.Groups[144], forceLoad: true);
    Check("forced puzzle activation retains ordinary prop but excludes incompatible prop", forcedPuzzle?.OfType<EntityProp>()
        .Any(prop => prop.InstId == 300001) == true && forcedPuzzle.All(entity => entity is not EntityProp { InstId: 300003 }));
}
else
{
    Check("4.6 keeps content outside the 4.5 Changle compatibility exclusion",
        excludedChangleProps.All(item => changle.Entities.Values.OfType<EntityProp>()
            .Any(prop => prop.GroupId == item.Item1 && prop.InstId == item.Item2)) &&
        changle.Groups.Contains(169) && changle.Entities.Values.Any(entity => entity.GroupId == 169));
    await changle.EntityLoader!.SyncEntity();
    await changle.EntityLoader.SyncEntity();
    Check("4.6 sync does not apply the 4.5 Changle compatibility exclusion",
        excludedChangleProps.All(item => changle.Entities.Values.OfType<EntityProp>()
            .Any(prop => prop.GroupId == item.Item1 && prop.InstId == item.Item2)) &&
        changle.Groups.Contains(169) && changle.Entities.Values.Any(entity => entity.GroupId == 169));
}
var elevatorScene = new SceneInstance(player, GameData.MazePlaneData[20002], 20002001, 2000201);
elevatorScene.EntityLoader!.LoadGroups.Add(8);
var elevatorGroup = elevatorScene.FloorInfo!.Groups[8];
Check("cache keeps ordinary server Hoyo metadata compatible with the active version",
    (applies45ProdCompatibility ? elevatorGroup.IsHoyoGroup == true : elevatorGroup.IsHoyoGroup is null or true)
    && elevatorGroup.Category != GroupCategoryEnum.Atmosphere);
Check("ordinary server Hoyo elevator is not excluded", (await elevatorScene.EntityLoader.LoadGroup(elevatorGroup, true))
    ?.OfType<EntityProp>().Any(prop => prop.Excel.PropType == PropTypeEnum.PROP_ELEVATOR) == true);
var loginScenePacket = new PacketGetCurSceneInfoScRsp(player);
var loginScene = GetCurSceneInfoScRsp.Parser.ParseFrom(loginScenePacket.Data).Scene;
Check("login restoration carries the 4.6 jump-energy data",
    loginScene.FiveDimGameData?.MaxJumpEnergy == 5);
bool ExcludesIncompatibleChangleContent(SceneInfo info) => info.EntityGroupList.All(group =>
    group.GroupId != 169 && (group.GroupId != 208 || group.EntityList.Count == 0)
    && (group.GroupId != 144 || group.EntityList.All(entity => entity.InstId != 300003)));
var teleportScene = EnterSceneByServerScNotify.Parser.ParseFrom(new PacketEnterSceneByServerScNotify(changle).Data).Scene;
Check("login restoration applies only the active version's Changle compatibility policy",
    ExcludesIncompatibleChangleContent(loginScene) == applies45ProdCompatibility);
Check("teleport snapshot applies only the active version's Changle compatibility policy",
    ExcludesIncompatibleChangleContent(teleportScene) == applies45ProdCompatibility);
var mapPacket = new PacketGetSceneMapInfoScRsp(new GetSceneMapInfoCsReq
    { SceneIdentifiers = { new SceneIdentifier { FloorId = 10202001 } } }, player);
var mapInfo = GetSceneMapInfoScRsp.Parser.ParseFrom(mapPacket.Data).SceneMapInfo.Single();
Check("real map response carries Changle Tian floor at the active wire tag",
    UIntFields(mapInfo.ToByteArray()).GetValueOrDefault(sceneMapFloorTag) == 10202001 && mapInfo.Retcode == 0);
Console.WriteLine($"Changle Tian fixture: {changle.Entities.Count} total entities, " +
                  $"{changle.Entities.Values.OfType<EntityProp>().Count()} props, groups [{string.Join(',', changle.Groups.Order())}].");

bool HasSceneProp(SceneInfo info, int groupId, int instanceId, int propId) => info.EntityGroupList
    .Any(group => group.GroupId == groupId && group.EntityList.Any(entity =>
        entity.InstId == instanceId && entity.Prop?.PropId == propId));

foreach (var (entryId, floorId, groupId, savedName, propId) in new[]
{
    (102020107, 10202001, 170, "ChallengeStoryEntrance", 102226),
    (1030402, 10304001, 145, "ChallengeEntrance", 103249)
})
{
    var entered = await player.TryEnterScene(entryId, 0, false);
    var entryScene = player.SceneInstance!;
    var instanceId = floorId == 10202001 ? 300002 : 300001;
    Check($"challenge entrance {floorId}/{groupId} loads with no persisted switch", entered.Success
        && !player.SceneData.FloorSavedData.GetValueOrDefault(floorId, []).ContainsKey(savedName)
        && HasSceneProp(entryScene.ToProto(), groupId, instanceId, propId));
    var floorWideDefaults = entryScene.BuildClientFloorSavedData();
    Check($"activity floor {floorId} restores all zero defaults, not only the challenge switch",
        entryScene.FloorInfo!.FloorSavedValue.Where(value => value.DefaultValue == 0 && !value.Name.Contains("_IsHidden"))
            .All(value => floorWideDefaults.GetValueOrDefault(value.Name) == 1));
    var entryPacket = EnterSceneByServerScNotify.Parser.ParseFrom(new PacketEnterSceneByServerScNotify(entryScene).Data);
    Check($"teleport snapshot includes challenge entrance {floorId}/{groupId} and enabled switch",
        entryPacket.Scene.FloorSavedData.GetValueOrDefault(savedName) == 1
        && HasSceneProp(entryPacket.Scene, groupId, instanceId, propId));
    await entryScene.EntityLoader!.SyncEntity();
    await entryScene.EntityLoader.SyncEntity();
    var restoredScene = GetCurSceneInfoScRsp.Parser.ParseFrom(new PacketGetCurSceneInfoScRsp(player).Data).Scene;
    Check($"login snapshot after sync retains challenge entrance {floorId}/{groupId}",
        restoredScene.FloorSavedData.GetValueOrDefault(savedName) == 1
        && HasSceneProp(restoredScene, groupId, instanceId, propId));
    if (floorId == 10202001)
        Check("all activities stay enabled while snapshots apply the active compatibility policy",
            ExcludesIncompatibleChangleContent(entryPacket.Scene) == applies45ProdCompatibility
            && ExcludesIncompatibleChangleContent(restoredScene) == applies45ProdCompatibility
            && restoredScene.FloorSavedData.GetValueOrDefault("LoadBianZhong") == 1);
    player.SceneData.FloorSavedData[floorId] = new() { [savedName] = 0 };
    await entryScene.EntityLoader.SyncEntity();
    Check($"explicit saved zero still unloads challenge entrance {floorId}/{groupId}",
        !entryScene.Groups.Contains(groupId) && !HasSceneProp(entryScene.ToProto(), groupId, instanceId, propId));
    player.SceneData.FloorSavedData.Remove(floorId);
    await entryScene.EntityLoader.SyncEntity();
    Check($"removing the saved override restores challenge entrance {floorId}/{groupId}",
        HasSceneProp(entryScene.ToProto(), groupId, instanceId, propId));
}

var starskiff = new SceneInstance(player, GameData.MazePlaneData[10201], 10201001, 1020101);
starskiff.EntityLoader!.LoadGroups.Add(280);
var starskiffGroup = starskiff.FloorInfo!.Groups[280];
var starskiffEntities = await starskiff.EntityLoader.LoadGroup(starskiffGroup);
if (applies45ProdCompatibility)
{
    Check("Starskiff Haven PCOnly capture is excluded while its empty handler loads",
        starskiffEntities is { Count: 0 } && starskiff.Groups.Contains(280));
    await starskiff.EntityLoader.SyncEntity();
    await starskiff.EntityLoader.SyncEntity();
    Check("PCOnly-only handler remains loaded exactly once after sync", starskiff.Groups.Count(id => id == 280) == 1
        && starskiff.Entities.Values.All(entity => entity.GroupId != 280));
    Check("explicit creation also excludes Starskiff Haven PCOnly capture", await starskiff.EntityLoader.LoadProp(
        starskiffGroup.PropList.Single(prop => prop.ID == 300001), starskiffGroup) == null);
    await starskiff.EntityLoader.UnloadGroup(280);
    Check("forced Starskiff Haven activation still excludes PCOnly", (await starskiff.EntityLoader.LoadGroup(
        starskiffGroup, forceLoad: true)) is { Count: 0 });
}
else
{
    Check("4.6 keeps Starskiff content outside the 4.5 compatibility exclusion",
        starskiffEntities?.OfType<EntityProp>().Any(prop => prop.InstId == 300001) == true);
    await starskiff.EntityLoader.SyncEntity();
    Check("4.6 sync retains Starskiff content outside the 4.5 compatibility exclusion",
        starskiff.Entities.Values.OfType<EntityProp>().Any(prop => prop.GroupId == 280 && prop.InstId == 300001));
    await starskiff.EntityLoader.UnloadGroup(280);
    Check("4.6 forced activation retains Starskiff content", (await starskiff.EntityLoader.LoadGroup(
        starskiffGroup, forceLoad: true))?.OfType<EntityProp>().Any(prop => prop.InstId == 300001) == true);
}

// Main-mission state on a content-package Maze floor: missions off keeps the lite-mode 1, missions on keeps the
// resource/official 0 (official 20001001 feed ships SceneStateByMainMission 0).
var missionFloorEntry = GameData.MapEntranceData.Values.Where(entry => entry.FloorID == 20001001).Min(entry => entry.ID);
var missionFloorEntered = await player.TryEnterScene(missionFloorEntry, 0, false);
var missionFloor = player.SceneInstance!;
Check("floor 20001001 declares a zero-default SceneStateByMainMission in the production cache", missionFloorEntered.Success
    && missionFloor.FloorInfo!.FloorSavedValue.Any(value => value.Name == "SceneStateByMainMission" && value.DefaultValue == 0));
Check("missions disabled: content-package floor still exposes SceneStateByMainMission as 1",
    missionFloor.BuildClientFloorSavedData().GetValueOrDefault("SceneStateByMainMission") == 1);
player.MissionManager!.Data.EnableMissionOverride = true;
var missionSnapshot = missionFloor.BuildClientFloorSavedData();
Check("missions enabled: SceneStateByMainMission is not forced to 1",
    missionSnapshot.GetValueOrDefault("SceneStateByMainMission") == 0
    && missionFloor.FloorInfo!.Groups.Values.Any(group => group.SavedValueCondition.Conditions
        .Any(condition => condition.SavedValueName == "SceneStateByMainMission") && !group.SavedValueCondition.IsTrue(missionSnapshot)));
player.SceneData!.FloorSavedData[20001001] = new() { ["SceneStateByMainMission"] = 1 };
Check("missions enabled: a story-written SceneStateByMainMission still wins",
    missionFloor.BuildClientFloorSavedData().GetValueOrDefault("SceneStateByMainMission") == 1);
player.SceneData.FloorSavedData.Remove(20001001);
player.MissionManager.Data.EnableMissionOverride = null;

var mapPlayer = new PlayerInstance(60011);
await mapPlayer.InitializeAsync();
mapPlayer.MissionManager!.Data.EnableMissionOverride = true;
mapPlayer.MissionManager.Data.SetMainMissionStatus(1000300, MissionPhaseEnum.Accept);
var mapScene = new SceneInstance(mapPlayer, GameData.MazePlaneData[10001], 10001001, 1000101);
// Official 4.6 entry snapshot for floor 10001001 ships exactly these client-group sub missions.
var officialClientSubIds = new HashSet<uint>
{
    200010818, 200011007, 200020103, 200020107, 200020113, 200020122, 200021100, 200021113,
    201150112, 202030303, 202030331, 202030332, 202030347, 202030349, 204370120, 300030102,
    300030103, 300030104, 300030202, 300030203, 300030204, 300070101, 300070102, 300070201,
    300070301, 300070401, 404000501, 404001401, 404001402, 404001803, 800000101, 800000102
};
var officeGate = mapScene.FloorInfo!.Groups[1];
var dim0 = mapScene.FloorInfo.DimensionList.Find(x => x.ID == 0);
Console.WriteLine($"MAP_DIAGNOSTIC gate1 inDim0={dim0?.GroupIDList.Contains(1)} " +
    $"system=[{string.Join(',', officeGate.SystemUnlockCondition?.Conditions ?? [])}] op={officeGate.SystemUnlockCondition?.Operation} " +
    $"saved=[{string.Join(',', officeGate.SavedValueCondition.Conditions.Select(c => $"{c.SavedValueName}={c.Value}"))}] " +
    $"battle=[{string.Join(',', officeGate.RelatedBattleId)}] name={officeGate.GroupName}");
mapPlayer.MissionManager.Data.SetSubMissionStatus(100040121, MissionPhaseEnum.Finish);
var gateLoaded = await mapScene.EntityLoader!.LoadGroup(officeGate);
Console.WriteLine($"MAP_DIAGNOSTIC gate1 directLoad={(gateLoaded != null ? gateLoaded.Count.ToString() : "null")} sceneGroups=[{string.Join(',', mapScene.Groups.Order())}]");
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(mapPlayer, mapScene);
GetSceneMapInfoScRsp MissionMap() => GetSceneMapInfoScRsp.Parser.ParseFrom(new PacketGetSceneMapInfoScRsp(
    new GetSceneMapInfoCsReq { SceneIdentifiers = { new SceneIdentifier { FloorId = 10001001 } } }, mapPlayer).Data);
Check("Asta proximity-trigger group loads while its prerequisite mission is still active",
    MissionMap().SceneMapInfo.Single().MazeGroupList.Any(group => group.GroupId == 5));
mapPlayer.MissionManager.Data.SetMainMissionStatus(1000300, MissionPhaseEnum.Finish);
mapPlayer.MissionManager.Data.SetMainMissionStatus(1000301, MissionPhaseEnum.Accept);
mapPlayer.MissionManager.Data.SetSubMissionStatus(100030103, MissionPhaseEnum.Accept);
var missionMap = MissionMap().SceneMapInfo.Single();
Check("Asta mission map includes client-side NPC and proximity-trigger groups without server entities",
    !mapScene.Groups.Contains(4) && !mapScene.Groups.Contains(5)
    && missionMap.MazeGroupList.Any(group => group.GroupId == 4)
    && missionMap.MazeGroupList.Any(group => group.GroupId == 5));
Check("current-floor map does not invent unloaded server Book groups",
    !mapScene.Groups.Contains(143) && !mapScene.Groups.Contains(165)
    && !missionMap.MazeGroupList.Any(group => group.GroupId is 143 or 165));
Check("Asta mission map sends the completed prerequisite and active main mission to client groups",
    missionMap.ClientGroupMissionInfo?.FinishedMainMissionIdList.Contains(1000300) == true
    && missionMap.ClientGroupMissionInfo.UnfinishedMainMissionIdList.Contains(1000301));
Check("scene mission status matches the official client-group sub set",
    missionMap.ClientGroupMissionInfo!.SubMissionStatusList.Select(x => x.Id).ToHashSet()
        .SetEquals(officialClientSubIds));
Check("the running story talk mission stays out of the official-style entry snapshot",
    !missionMap.ClientGroupMissionInfo.SubMissionStatusList.Any(x => x.Id == 100030103)
    && !mapScene.ToProto().SceneMissionInfo.SubMissionStatusList.Any(x => x.Id == 100030103));
Check("current-floor map and scene send the same client-group mission snapshot",
    missionMap.ClientGroupMissionInfo?.Equals(mapScene.ToProto().SceneMissionInfo) == true);
mapPlayer.MissionManager.Data.SetMainMissionStatus(1000302, MissionPhaseEnum.Finish);
Check("finishing the authored unload mission removes the proximity-trigger group from map info",
    !MissionMap().SceneMapInfo.Single().MazeGroupList.Any(group => group.GroupId == 5));
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(mapPlayer, null);
var noncurrentMissionMap = MissionMap().SceneMapInfo.Single();
Check("noncurrent floor still sends its client-group mission state",
    noncurrentMissionMap.ClientGroupMissionInfo?.FinishedMainMissionIdList.Contains(1000300) == true);
Check("noncurrent floor respects authored group unload conditions",
    !noncurrentMissionMap.MazeGroupList.Any(group => group.GroupId == 5));

var hertaEntry = GameData.MapEntranceData.Values.Where(entry => entry.FloorID == 80112001).MinBy(entry => entry.ID)!;
var hertaScene = new SceneInstance(mapPlayer, GameData.MazePlaneData[hertaEntry.PlaneID], 80112001, hertaEntry.ID);
mapPlayer.MissionManager.Data.SetMainMissionStatus(1000401, MissionPhaseEnum.Finish);
var hertaGroup = hertaScene.FloorInfo!.Groups[2];
Check("Herta group defers its authored mission unload until scene reload",
    hertaGroup.UnloadCondition.DelayToLevelReload && hertaGroup.UnloadCondition.IsTrue(mapPlayer.MissionManager.Data, false));
hertaScene.Groups.Add(2);
typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(mapPlayer, hertaScene);
var hertaMap = GetSceneMapInfoScRsp.Parser.ParseFrom(new PacketGetSceneMapInfoScRsp(
    new GetSceneMapInfoCsReq { SceneIdentifiers = { new SceneIdentifier { FloorId = 80112001 } } }, mapPlayer).Data);
Check("current-floor map retains a loaded group during its deferred unload",
    hertaMap.SceneMapInfo.Single().MazeGroupList.Any(group => group.GroupId == 2));

// Summon-delivered maze buffs (Cyrene 14151 -> 141502) reference the summon entity, keep the authored lifetime,
// and leave with the summon; the summon's lifetime is owned by the caster's timed maze buff 141501.
var fieldScene = missionFloor;
var fieldMonsters = fieldScene.Entities.Values.OfType<EntityMonster>().Where(monster => monster.IsAlive).Take(2).ToList();
var fieldCaster = fieldScene.AvatarInfo.Values.First();
Console.WriteLine($"Summon fixture: floor {fieldScene.FloorId}, {fieldMonsters.Count} monsters, caster entity {fieldCaster.EntityId}.");
Check("summon fixture has two live monsters", fieldMonsters.Count == 2);
Check("cache keeps SummonUnitData destroy flags", GameData.SummonUnitDataData[14151].RemoveMazeBuffOnDestroy
    && !GameData.SummonUnitDataData[14101].RemoveMazeBuffOnDestroy);
await fieldCaster.AddBuff(new SceneBuff(141501, 1, 1415, 30));
var cyreneField = fieldScene.SummonUnit.Values.SingleOrDefault(unit => unit.SummonUnitId == 14151);
Check("timed technique buff creates the field summon with official infinite lifetime",
    cyreneField is { LifeTimeMs: -1 } && cyreneField.AttachEntityId == fieldCaster.EntityId);
if (cyreneField != null && fieldMonsters.Count == 2)
{
    var fieldTargets = fieldMonsters.Select(monster => (uint)monster.EntityId).ToList();
    Check("field trigger accepts the monsters", await fieldScene.TriggerSummonUnit(cyreneField.EntityId,
        "HitNpcMonsterTrigger", fieldTargets) == Retcode.RetSucc);
    var delivered = fieldMonsters.Select(monster => monster.BuffList.SingleOrDefault(buff => buff.BuffId == 141502)).ToList();
    Check("summon-delivered buff references the summon entity with authored lifetime 500", delivered.All(buff =>
        buff is { SummonUnitEntityId: var summonId, Duration: 500 } && summonId == cyreneField.EntityId
        && buff.ToProto().BuffSummonEntityId == (uint)cyreneField.EntityId && buff.ToProto().LifeTime == 500));
    Check("each monster receives its own buff instance", !ReferenceEquals(delivered[0], delivered[1]));
    await fieldScene.TriggerSummonUnit(cyreneField.EntityId, "HitNpcMonsterTrigger", [fieldTargets[0]]);
    var refreshed = fieldMonsters[1].BuffList.SingleOrDefault(buff => buff.BuffId == 141502);
    Check("trigger exit refreshes the lifetime and keeps the summon reference",
        refreshed is { Duration: 1 } && refreshed.SummonUnitEntityId == cyreneField.EntityId
        && fieldMonsters[0].BuffList.Single(buff => buff.BuffId == 141502).Duration == 500);
    await fieldCaster.RemoveBuff(141501);
    Check("technique buff removal destroys the summon and the buffs it delivered",
        !fieldScene.SummonUnit.Values.Any(unit => unit.SummonUnitId == 14151)
        && fieldMonsters.All(monster => monster.BuffList.All(buff => buff.BuffId != 141502)));
}
await fieldCaster.AddBuff(new SceneBuff(141501, 1, 1415));
Check("permanent owning buff keeps the bounded 45s summon lifetime",
    fieldScene.SummonUnit.Values.SingleOrDefault(unit => unit.SummonUnitId == 14151)?.LifeTimeMs == 45000);
await fieldCaster.RemoveBuff(141501);
var keepBuffSummon = new EntitySummonUnit
{
    SummonUnitId = 14101, CreateAvatarEntityId = fieldCaster.EntityId, LifeTimeMs = -1,
    TriggerList = GameData.SummonUnitDataData[14101].ConfigInfo?.TriggerConfig.CustomTriggers ?? []
};
await fieldScene.AddSummonUnitEntity(keepBuffSummon);
if (fieldMonsters.Count == 2)
{
    await fieldScene.TriggerSummonUnit(keepBuffSummon.EntityId, keepBuffSummon.TriggerList[0].TriggerName,
        [(uint)fieldMonsters[0].EntityId]);
    var keptBuffIds = fieldMonsters[0].BuffList.Where(buff => buff.SummonUnitEntityId == keepBuffSummon.EntityId)
        .Select(buff => buff.BuffId).ToList();
    await fieldScene.RemoveSummonUnitById(14101);
    Check("summon without RemoveMazeBuffOnDestroy leaves its delivered buffs", keptBuffIds.Count > 0
        && keptBuffIds.All(id => fieldMonsters[0].BuffList.Any(buff => buff.BuffId == id)));
}

var previousScene = player.SceneInstance;
const int MissingEntry = 99000002;
GameData.MapEntranceData[MissingEntry] = new MapEntranceExcel { ID = MissingEntry, PlaneID = 10202, FloorID = 99000002 };
var noFloor = await player.TryEnterScene(MissingEntry, 0, false, storyLineId: 123456);
Check("missing floor fails without changing scene/story state", !noFloor.Success && ReferenceEquals(previousScene, player.SceneInstance)
    && player.StoryLineManager!.StoryLineData.CurStoryLineId == 0);
GameData.FloorInfoData["P10202_F99000002"] = new FloorInfo { FloorID = 99000002 };
Check("floor with no usable anchor fails", !(await player.TryEnterScene(MissingEntry, 0, false)).Success);
GameData.FloorInfoData.TryRemove("P10202_F99000002", out _);
GameData.MapEntranceData[MissingEntry].PlaneID = 99000002;
Check("missing plane fails", !(await player.TryEnterScene(MissingEntry, 0, false)).Success);
GameData.MapEntranceData.Remove(MissingEntry);
Check("unknown story line fails instead of false success", !(await player.TryEnterScene(1000001, 0, false, storyLineId: 123456)).Success);
Check("entry zero without story transition fails", !(await player.TryEnterScene(0, 0, false)).Success);
var loadingFloors = (HashSet<long>)typeof(PlayerInstance).GetField("_loadingFloors", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(player)!;
var floorKey = ((long)10202 << 32) | 10202001u;
loadingFloors.Add(floorKey);
Check("recursive scene-load guard is reported as failure", !(await player.TryEnterScene(102020107, 0, false)).Success);
loadingFloors.Remove(floorKey);

var sender = new ProbeSender();
var target = new ProbeConnection { Player = player };
var command = new CommandScene();
await command.ChangeScene(new CommandArg("10001", sender, target));
Check("invalid scene command reports failure, never entered", sender.Messages.Single() == I18NManager.Translate("Game.Command.Scene.SceneEnterFailed", "10001"));
sender.Messages.Clear();
await command.ChangeScene(new CommandArg("102020107", sender, target));
Check("same-scene command still reports successful entry", sender.Messages.Single() == I18NManager.Translate("Game.Command.Scene.SceneChanged", "102020107"));
sender.Messages.Clear();
await command.ReloadScene(new CommandArg("", sender, target));
Check("reload command still reports successful reload", sender.Messages.Single() == I18NManager.Translate("Game.Command.Scene.SceneReloaded"));
sender.Messages.Clear();
loadingFloors.Add(floorKey);
await command.ReloadScene(new CommandArg("", sender, target));
loadingFloors.Remove(floorKey);
Check("aborted reload reports failure", sender.Messages.Single() == I18NManager.Translate("Game.Command.Scene.SceneEnterFailed", "102020107"));
var failurePacket = new PacketEnterSceneScRsp(Retcode.RetFail);
Check("scene failure response encodes nonzero retcode", EnterSceneScRsp.Parser.ParseFrom(failurePacket.Data).Retcode != 0);

// Entry zero is a real protocol path for entering/leaving a story's saved scene.
var storyId = GameData.StoryLineFloorDataData.Keys.First(GameData.StoryLineData.ContainsKey);
player.StoryLineManager!.StoryLineData.RunningStoryLines[storyId] = new StoryLineInfo
{
    StoryLineId = storyId, SavedPlaneId = 10000, SavedFloorId = 10000000, SavedEntryId = 1000001,
    Lineup = player.LineupManager!.GetCurLineup()!.BaseAvatars!.ToList()
};
Check("invalid entrance cannot activate an otherwise valid story", !(await player.TryEnterScene(10001, 0, false, storyId)).Success
    && player.StoryLineManager.StoryLineData.CurStoryLineId == 0);
var storyEnter = await player.TryEnterScene(0, 0, false, storyId);
Check("entry-zero story transition preserves successful saved-scene teleport", storyEnter.Success && storyEnter.IsOverMap
    && player.StoryLineManager.StoryLineData.CurStoryLineId == storyId && player.Data.EntryId == 1000001);
var storyLeave = await player.TryEnterScene(0, 0, false);
Check("entry-zero story exit returns to original scene", storyLeave.Success && storyLeave.IsOverMap
    && player.StoryLineManager.StoryLineData.CurStoryLineId == 0 && player.Data.EntryId == 102020107);

// /spawn: runtime monster/prop injection into the real Changle Tian scene and the custom battle it defines.
var spawnCommand = new CommandSpawn();
var spawnScene = player.SceneInstance!;
var spawnOrigin = player.Data.Pos!;
var battleMonsterIds = GameData.MonsterConfigData.Keys.Order().Take(8).ToList();
Check("spawn parser reads two-letter options, clamps them and splits waves at separators and every 5 monsters",
    CommandSpawn.TryParse(["monster", "lv150", "hp900", "x0", "r2", "s7", "@60001",
            $"{battleMonsterIds[0]}", $"{battleMonsterIds[1]}", "/", .. battleMonsterIds.Skip(2).Select(id => $"{id}"), "bogus", "0"],
        out var spawnRequest)
    && spawnRequest.SpawnId == "monster" && spawnRequest.Level == CommandSpawn.MaxLevel && spawnRequest.Hp == 900
    && spawnRequest.Amount == 1 && spawnRequest.Radius == CommandSpawn.MinRadius && spawnRequest.StageId == 7
    && spawnRequest.Waves.Count == 3 && spawnRequest.Waves[0].SequenceEqual(battleMonsterIds.Take(2))
    && spawnRequest.Waves[1].SequenceEqual(battleMonsterIds.Skip(2).Take(5)) && spawnRequest.Waves[2].SequenceEqual(battleMonsterIds.Skip(7))
    && spawnRequest.InvalidBattleMonsters.SequenceEqual(["bogus", "0"]));
Check("spawn parser rejects an overflowing option value", !CommandSpawn.TryParse(["monster", "x99999999999"], out _));
Check("spawn 'monster' resolves the smallest NPC monster", CommandSpawn.Resolve("monster") is
    { Kind: CommandSpawn.SpawnTargetKind.Monster } baseTarget && baseTarget.Monster!.ID == GameData.NpcMonsterDataData.Keys.Min());
var mappedBattleMonster = GameData.MonsterConfigData.Values.FirstOrDefault(monster =>
    !GameData.NpcMonsterDataData.ContainsKey(monster.MonsterID) &&
    GameData.MonsterTemplateConfigData.TryGetValue(monster.MonsterTemplateID, out var template) &&
    template.NPCMonsterList.Any(GameData.NpcMonsterDataData.ContainsKey));
if (mappedBattleMonster != null)
    Check("spawn maps a battle MonsterID to its template's first spawnable NPC monster",
        CommandSpawn.Resolve(mappedBattleMonster.MonsterID.ToString()).Monster?.ID == GameData
            .MonsterTemplateConfigData[mappedBattleMonster.MonsterTemplateID].NPCMonsterList.First(GameData.NpcMonsterDataData.ContainsKey));
var battleOnlyMonster = GameData.MonsterConfigData.Values.FirstOrDefault(monster =>
    !GameData.NpcMonsterDataData.ContainsKey(monster.MonsterID) && !GameData.MazePropData.ContainsKey(monster.MonsterID) &&
    !(GameData.MonsterTemplateConfigData.TryGetValue(monster.MonsterTemplateID, out var template) &&
      template.NPCMonsterList.Any(GameData.NpcMonsterDataData.ContainsKey)));
if (battleOnlyMonster != null)
    Check("spawn reports a battle-only MonsterID instead of a bare invalid ID",
        CommandSpawn.Resolve(battleOnlyMonster.MonsterID.ToString()).Kind == CommandSpawn.SpawnTargetKind.BattleMonsterNoScene);
Check("spawn maze prop and unknown IDs resolve distinctly", CommandSpawn.Resolve("102226").Kind == CommandSpawn.SpawnTargetKind.Prop
    && CommandSpawn.Resolve("abc").Kind == CommandSpawn.SpawnTargetKind.Invalid);

// Changle Tian is a town floor with no monster groups at all: nothing to borrow, nothing spawned.
var townEntityCount = spawnScene.Entities.Count;
sender.Messages.Clear();
await spawnCommand.Spawn(new CommandArg("monster", sender, target));
Check("spawn on a floor without monster configs reports it and spawns nothing",
    spawnScene.Entities.Count == townEntityCount
    && sender.Messages.SequenceEqual([I18NManager.Translate("Game.Command.Spawn.NoMonsterTemplate")]));

// Move to the first ordinary maze floor whose loaded dimension has a usable server monster group.
bool HasSpawnableMonsterGroup(MapEntranceExcel entrance)
{
    if (!GameData.MazePlaneData.TryGetValue(entrance.PlaneID, out var plane) || plane.PlaneType != PlaneTypeEnum.Maze)
        return false;
    GameData.GetFloorInfo(entrance.PlaneID, entrance.FloorID, out var floor);
    var dimension = floor?.DimensionList.Find(item => item.ID == 0);
    return floor != null && dimension != null && floor.Groups.Values.Any(group =>
        group.LoadSide == GroupLoadSideEnum.Server && dimension.GroupIDList.Contains(group.Id)
        && group.MonsterList.Any(monster => monster.FarmElementID == 0 && !monster.IsDelete
            && March7thHoney.GameServer.Game.Battle.BattleInstance.TryResolveStageFromEvent(
                monster.EventID, player.Data.WorldLevel, out _)));
}
var mazeEntry = GameData.MapEntranceData.Values.OrderBy(entrance => entrance.ID).First(HasSpawnableMonsterGroup);
Check($"spawn fixture enters maze entry {mazeEntry.ID} (floor {mazeEntry.FloorID})",
    (await player.TryEnterScene(mazeEntry.ID, 0, false)).Success && player.SceneInstance?.FloorId == mazeEntry.FloorID);
spawnScene = player.SceneInstance!;
spawnOrigin = player.Data.Pos!;

List<T> NewEntities<T>(HashSet<int> before) where T : BaseGameEntity =>
    spawnScene.Entities.Values.OfType<T>().Where(entity => !before.Contains(entity.EntityId)).ToList();
bool UsesMonsterTemplate(EntityMonster monster) => spawnScene.Groups.Contains(monster.GroupId)
    && spawnScene.FloorInfo!.Groups[monster.GroupId].MonsterList.Any(template => template.ID == monster.InstId
        && template.FarmElementID == 0 && template.EventID == monster.EventId);
bool NearOrigin(Position pos) => Math.Abs(pos.X - spawnOrigin.X) <= 5000 && pos.Y == spawnOrigin.Y
    && Math.Abs(pos.Z - spawnOrigin.Z) <= 5000;

var spawnStage = GameData.StageConfigData.Values.First(stage => stage.MonsterList.Count > 0);
var baseNpcMonsterId = GameData.NpcMonsterDataData.Keys.Min();
var entitiesBefore = spawnScene.Entities.Keys.ToHashSet();
sender.Messages.Clear();
await spawnCommand.Spawn(new CommandArg(
    $"monster s{spawnStage.StageID} lv50 hp12345 x2 r1 {battleMonsterIds[0]} {battleMonsterIds[1]} / {battleMonsterIds[2]} bogus",
    sender, target));
var spawnedMonsters = NewEntities<EntityMonster>(entitiesBefore);
Check("spawn adds the requested monsters to a loaded group with the borrowed template identity",
    spawnedMonsters.Count == 2 && spawnedMonsters.All(monster => monster.IsGmSpawned && monster.MonsterData.ID == baseNpcMonsterId
        && UsesMonsterTemplate(monster) && NearOrigin(monster.Position)
        && monster.Rotation is { X: 0, Y: 0, Z: 0 }));
Check("spawn stores stage, level, HP and waves on each spawned monster", spawnedMonsters.All(monster =>
    monster.CustomStageId == spawnStage.StageID && monster.CustomLevel == 50 && monster.CustomBattleHp == 12345
    && monster.CustomBattleWaves is { Count: 2 } waves && waves[0].SequenceEqual(battleMonsterIds.Take(2))
    && waves[1].SequenceEqual([battleMonsterIds[2]])));
Check("spawn reports skipped wave IDs and the spawn result", sender.Messages.Contains(
        I18NManager.Translate("Game.Command.Spawn.InvalidBattleMonster", "bogus"))
    && sender.Messages.Contains(I18NManager.Translate("Game.Command.Spawn.SpawnedMonsters", "2", baseNpcMonsterId.ToString())));
Console.WriteLine($"/spawn monster template: group {spawnedMonsters.FirstOrDefault()?.GroupId}, inst {spawnedMonsters.FirstOrDefault()?.InstId}.");

var castAvatar = spawnScene.AvatarInfo.Values.First();
var gmBattle = await player.BattleManager!.StartBattle(castAvatar, [spawnedMonsters[0]], false);
var gmBattleProto = gmBattle!.ToProto();
Check("custom waves replace the stage waves with sequential wave IDs on the pinned stage",
    gmBattle.StageId == spawnStage.StageID && gmBattle.IsGmSpawnedEncounter && gmBattleProto.MonsterWaveList.Count == 2
    && gmBattleProto.MonsterWaveList.Select(wave => wave.BattleWaveId).SequenceEqual([1u, 2u])
    && gmBattleProto.MonsterWaveList.All(wave => wave.BattleStageId == spawnStage.StageID)
    && gmBattleProto.MonsterWaveList[0].MonsterList.Select(monster => (int)monster.MonsterId).SequenceEqual(battleMonsterIds.Take(2))
    && gmBattleProto.MonsterWaveList[1].MonsterList.Select(monster => (int)monster.MonsterId).SequenceEqual([battleMonsterIds[2]]));
Check("custom battle carries the HP override and level on every wave",
    gmBattleProto.MonsterWaveList.All(wave => wave.MonsterParam.Level == 50
        && wave.MonsterList.All(monster => monster.MaxHp == 12345 && monster.CurHp == 12345)));
player.BattleInstance = null;

entitiesBefore = spawnScene.Entities.Keys.ToHashSet();
await spawnCommand.Spawn(new CommandArg("monster hp777 lv0", sender, target));
var hpOnlyMonster = NewEntities<EntityMonster>(entitiesBefore).Single();
var hpOnlyBattle = await player.BattleManager.StartBattle(castAvatar, [hpOnlyMonster], false);
var hpOnlyProto = hpOnlyBattle!.ToProto();
var expectedStageWaves = hpOnlyBattle.Stages.SelectMany(stage => stage.ToProto()).ToList();
Check("HP override without custom waves keeps the stage waves and level",
    hpOnlyBattle.CustomWaves == null && hpOnlyBattle.CustomLevel == 0 && hpOnlyProto.MonsterWaveList.Count == expectedStageWaves.Count
    && hpOnlyProto.MonsterWaveList.Zip(expectedStageWaves).All(pair => pair.First.MonsterList.Select(monster => monster.MonsterId)
        .SequenceEqual(pair.Second.MonsterList.Select(monster => monster.MonsterId)) && pair.First.MonsterParam.Level == 0)
    && hpOnlyProto.MonsterWaveList.SelectMany(wave => wave.MonsterList).All(monster => monster.MaxHp == 777 && monster.CurHp == 777));
player.BattleInstance = null;
var sceneMonster = spawnScene.Entities.Values.OfType<EntityMonster>().FirstOrDefault(monster => !monster.IsGmSpawned && monster.IsAlive);
if (sceneMonster != null)
{
    var mixedBattle = await player.BattleManager.StartBattle(castAvatar, [sceneMonster, hpOnlyMonster], false);
    Check("a fight that includes a real scene monster still counts for missions", mixedBattle is { IsGmSpawnedEncounter: false });
    player.BattleInstance = null;
}

entitiesBefore = spawnScene.Entities.Keys.ToHashSet();
await spawnCommand.Spawn(new CommandArg("102226 x3", sender, target));
var spawnedProps = NewEntities<EntityProp>(entitiesBefore);
Check("spawn adds open props borrowing a plain prop's group and instance", spawnedProps.Count == 3 && spawnedProps.All(prop =>
    prop.IsGmSpawned && prop.State == PropStateEnum.Open && prop.Excel.ID == 102226 && NearOrigin(prop.Position)
    && spawnScene.Groups.Contains(prop.GroupId) && spawnScene.FloorInfo!.Groups[prop.GroupId].PropList.Any(template =>
        template.ID == prop.InstId && template.FarmElementID == 0 && template.AnchorID == 0)
    && prop.PropInfo is { EventID: 0, ChestID: 0, CocoonID: 0, InteractLinks.Count: 0 }));
var spawnedProp = spawnedProps[0];
var savedTemplateState = player.GetScenePropData(spawnScene.FloorId, spawnedProp.GroupId, spawnedProp.InstId)?.State;
await spawnedProp.SetState(PropStateEnum.Closed);
Check("spawned prop state change is not persisted onto the template prop", spawnedProp.State == PropStateEnum.Closed
    && player.GetScenePropData(spawnScene.FloorId, spawnedProp.GroupId, spawnedProp.InstId)?.State == savedTemplateState);

var entityCount = spawnScene.Entities.Count;
sender.Messages.Clear();
await spawnCommand.Spawn(new CommandArg("abc", sender, target));
await spawnCommand.Spawn(new CommandArg($"monster s{GameData.StageConfigData.Keys.Max() + 1}", sender, target));
await spawnCommand.Spawn(new CommandArg($"monster x{SceneInstance.GmSpawnMaxEntities}", sender, target));
await spawnCommand.Spawn(new CommandArg("", sender, target));
await spawnCommand.Spawn(new CommandArg("monster", sender, new ProbeConnection { Player = roguePlayer }));
Check("spawn rejects invalid ID, unknown stage, entity cap, missing ID and special-mode scenes without spawning",
    spawnScene.Entities.Count == entityCount && sender.Messages.SequenceEqual([
        I18NManager.Translate("Game.Command.Spawn.InvalidId", "abc"),
        I18NManager.Translate("Game.Command.Spawn.InvalidStageId", (GameData.StageConfigData.Keys.Max() + 1).ToString()),
        I18NManager.Translate("Game.Command.Spawn.MaxEntities", SceneInstance.GmSpawnMaxEntities.ToString()),
        I18NManager.Translate("Game.Command.Spawn.Usage"),
        I18NManager.Translate("Game.Command.Spawn.UnsupportedScene")
    ]));
sender.Messages.Clear();
// Tutorials popped from the maze main page hold free-play input, so a merely running target cannot close them.
var tutorialPlayer = new PlayerInstance(60006);
await tutorialPlayer.InitializeAsync();
var tutorialMissions = tutorialPlayer.MissionManager!.Data;
tutorialMissions.EnableMissionOverride = true;
bool TutorialClosesWith(int tutorialId, int mainId, MissionPhaseEnum status)
{
    tutorialMissions.SetMainMissionStatus(mainId, status);
    return TutorialHelper.CanClose(tutorialPlayer, tutorialId);
}
Check("free-play tutorial 3006 is not closable while its target main 1000511 only runs",
    GameData.TutorialDataData[3006].TriggerParams.Any(trigger => trigger.TriggerType == "OnMazeMainPage") &&
    !TutorialClosesWith(3006, 1000511, MissionPhaseEnum.Accept) && TutorialClosesWith(3006, 1000511, MissionPhaseEnum.Finish));
Check("battle tutorial 1001 stays closable on a running target main 1000101",
    TutorialClosesWith(1001, 1000101, MissionPhaseEnum.Accept) && TutorialClosesWith(1001, 1000101, MissionPhaseEnum.Finish) &&
    !TutorialClosesWith(1001, 1000101, MissionPhaseEnum.None));
TutorialDataExcel SyntheticTutorial(int id, bool freePlay, string finishType, string finishParam) => new()
{
    TutorialID = id,
    TriggerParams = freePlay ? [new TutorialTriggerParam { TriggerType = "OnMazeMainPage", TriggerParam = "0" }] : [],
    FinishTriggerParams = [new TutorialTriggerParam { TriggerType = finishType, TriggerParam = finishParam }]
};
GameData.TutorialDataData[99000001] = SyntheticTutorial(99000001, false, "TutorialFinish", "99000002");
GameData.TutorialDataData[99000002] = SyntheticTutorial(99000002, true, "FinishMainMission", "1000511");
GameData.TutorialDataData[99000003] = SyntheticTutorial(99000003, true, "TutorialFinish", "99000004");
GameData.TutorialDataData[99000004] = SyntheticTutorial(99000004, false, "FinishMainMission", "1000511");
Check("TutorialFinish recursion takes strictness from the target tutorial's own triggers",
    !TutorialClosesWith(99000001, 1000511, MissionPhaseEnum.Accept) && TutorialClosesWith(99000003, 1000511, MissionPhaseEnum.Accept));
foreach (var id in new[] { 99000001, 99000002, 99000003, 99000004 }) GameData.TutorialDataData.Remove(id);

// BeginParam is a second gate after TakeParam: 1010502 is taken after 1010403 but begins at Trailblaze Level 13.
var gatedPlayer = new PlayerInstance(60007);
await gatedPlayer.InitializeAsync();
var gatedMissions = gatedPlayer.MissionManager!.Data;
gatedMissions.EnableMissionOverride = true;
const int LevelGatedMain = 1010502;
var levelGatedExcel = GameData.MainMissionData[LevelGatedMain];
gatedPlayer.Data.Level = 11;
gatedPlayer.Data.Exp = 0;
gatedMissions.SetMainMissionStatus(1010403, MissionPhaseEnum.Finish);
await gatedPlayer.MissionManager.AcceptMainMissionByCondition(false);
// The chain is reached and only a PlayerLevel gate is unmet, so the main is Prepared: listed locked, no sub running.
Check("BeginParam PlayerLevel 13 keeps 1010502 Prepared, not accepted, at level 11 although its take gate is met",
    levelGatedExcel.IsEqual(gatedMissions) && !levelGatedExcel.IsBeginSatisfied(gatedMissions) &&
    gatedMissions.GetMainMissionStatus(LevelGatedMain) == MissionPhaseEnum.Prepared &&
    levelGatedExcel.SubMissionIds.All(id => gatedMissions.GetSubMissionStatus(id) == MissionPhaseEnum.None));
var expToLevel13 = GameData.PlayerLevelConfigData[13].PlayerExp - GameData.PlayerLevelConfigData[11].PlayerExp;
await gatedPlayer.InventoryManager!.AddItem(22, expToLevel13);
Check("reaching level 13 through an exp grant accepts the level-gated main",
    gatedPlayer.Data.Level == 13 && gatedMissions.GetMainMissionStatus(LevelGatedMain) == MissionPhaseEnum.Accept);
Check("an empty or Auto BeginParam does not gate a main", GameData.MainMissionData.Values
    .Where(main => main.BeginParam.All(param => param.Type == MissionTakeTypeEnum.Auto))
    .All(main => main.IsBeginSatisfied(gatedMissions)));

// Rogue Index arms in the Simulated Universe lobby (80112001 group 3, MazeProp 1005 Prop_Rogue_Gallery).
async Task<List<PropStateEnum?>> RogueIndexArmStates(PlayerInstance owner)
{
    var lobby = new SceneInstance(owner, GameData.MazePlaneData[80112], 80112001, RogueManager.RogueEntranceId);
    var armGroup = lobby.FloorInfo!.Groups[3];
    var states = new List<PropStateEnum?>();
    foreach (var armId in new[] { 300003, 300008 })
        states.Add((await lobby.EntityLoader!.LoadProp(armGroup.PropList.Single(prop => prop.ID == armId), armGroup))?.State);
    return states;
}
Check("Rogue Index arms default open with missions disabled",
    (await RogueIndexArmStates(player)).All(state => state == PropStateEnum.Open));
tutorialMissions.SetMainMissionStatus(4030002, MissionPhaseEnum.Accept);
Check("Rogue Index arms stay closed before the final roaming lobby talk",
    (await RogueIndexArmStates(tutorialPlayer)).All(state => state == PropStateEnum.Closed));
tutorialMissions.SetMainMissionStatus(4030002, MissionPhaseEnum.Finish);
Check("Rogue Index arms open once main 4030002 is finished",
    (await RogueIndexArmStates(tutorialPlayer)).All(state => state == PropStateEnum.Open));

// Exercise the real send path with two in-memory KCP peers: no sockets or production server.
var outbound = new LoopbackKcpTransport();
var inbound = new LoopbackKcpTransport();
var endpoint = new IPEndPoint(IPAddress.Loopback, 60002);
var options = new KcpConversationOptions { NoDelay = true, UpdateInterval = 10, DisableCongestionControl = true,
    SendWindow = 128, ReceiveWindow = 128, SendQueueSize = 256, ReceiveQueueSize = 256 };
using (var serverPeer = new KcpConversation(endpoint, outbound, options))
using (var clientPeer = new KcpConversation(endpoint, inbound, options))
{
    outbound.Peer = clientPeer;
    inbound.Peer = serverPeer;
    outbound.Start();
    inbound.Start();
    player.Connection = new ProbeConnection(serverPeer) { Player = player };
    async Task<List<ushort>> SentPackets(Func<Task> action)
    {
        await action();
        await player.SendPacket(new BasePacket(ushort.MaxValue)); // deterministic end-of-batch marker
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[March7thHoneyConnection.MaxPacketBytes];
        var ids = new List<ushort>();
        while (true)
        {
            var received = await clientPeer.ReceiveAsync(buffer, timeout.Token);
            if (received.TransportClosed || received.BytesReceived < 16)
                throw new InvalidDataException("KCP scene test did not receive a complete game packet.");
            var id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4));
            if (id == ushort.MaxValue) return ids;
            ids.Add(id);
        }
    }
    var teleportPackets = await SentPackets(async () =>
    {
        await player.TryEnterScene(1000001, 0, true);
        await player.SendPacket(new PacketEnterSceneScRsp());
    });
    Check("teleport sends the active scene boundary immediately before scene notify and before response",
        teleportPackets.TakeLast(3).SequenceEqual(new ushort[]
            { CmdIds.SyncServerSceneChangeNotify, CmdIds.EnterSceneByServerScNotify, CmdIds.EnterSceneScRsp }));
    var movePackets = await SentPackets(async () => { await player.TryEnterScene(1000001, 0, true, mapTp: true); });
    Check("same-scene map move does not send a scene-change boundary", movePackets.Contains(CmdIds.SceneEntityMoveScNotify)
        && !movePackets.Contains(CmdIds.SyncServerSceneChangeNotify) && !movePackets.Contains(CmdIds.EnterSceneByServerScNotify));
    var invalidPackets = await SentPackets(async () => { await player.TryEnterScene(10001, 0, true); });
    Check("failed scene entry sends no boundary or scene snapshot", !invalidPackets.Contains(CmdIds.SyncServerSceneChangeNotify)
        && !invalidPackets.Contains(CmdIds.EnterSceneByServerScNotify));
    var quietPackets = await SentPackets(async () => { await player.TryEnterScene(102020107, 0, false); });
    Check("quiet scene build sends no boundary or scene snapshot", !quietPackets.Contains(CmdIds.SyncServerSceneChangeNotify)
        && !quietPackets.Contains(CmdIds.EnterSceneByServerScNotify));
    var restorePackets = await SentPackets(async () => { await player.SendPacket(new PacketGetCurSceneInfoScRsp(player)); });
    Check("login snapshot is not mistaken for a scene transition", restorePackets.SequenceEqual(new ushort[] { CmdIds.GetCurSceneInfoScRsp }));
    // A virtual lineup that only hosted a trial avatar is torn down, announced, and replaced by the own lineup.
    var ownLineupIndex = player.LineupManager!.Data.GetCurLineupIndex();
    player.LineupManager.SetExtraLineup(ExtraLineupType.LineupHeliobus, []);
    player.LineupManager.GetCurLineup()!.BaseAvatars!.Add(new March7thHoney.Database.Lineup.LineupAvatarInfo
        { BaseAvatarId = 1001, SpecialAvatarId = 9_999_001 });
    var trialTeardownPackets = await SentPackets(async () =>
    {
        await player.LineupManager.RemoveTrialAvatarFromCurTeam(9_999_001);
    });
    Check("removing the last trial avatar destroys the virtual lineup and restores the own lineup",
        player.LineupManager.Data.CurExtraLineup == -1
        && !player.LineupManager.Data.Lineups.ContainsKey((int)ExtraLineupType.LineupHeliobus + 10)
        && player.LineupManager.Data.GetCurLineupIndex() == ownLineupIndex);
    Check("trial lineup teardown notifies destroy before syncing the restored lineup",
        trialTeardownPackets.IndexOf((ushort)CmdIds.ExtraLineupDestroyNotify) is >= 0 and var destroyAt
        && trialTeardownPackets.LastIndexOf((ushort)CmdIds.SyncLineupNotify) > destroyAt);
    player.Connection = null;

    // Rogue flows run through the real handlers on the rogue player's connection.
    var rogueConnection = new ProbeConnection(serverPeer) { Player = roguePlayer };
    roguePlayer.Connection = rogueConnection;
    async Task<List<(ushort Id, byte[] Body)>> SentFrames(Func<Task> action)
    {
        await action();
        await roguePlayer.SendPacket(new BasePacket(ushort.MaxValue));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[March7thHoneyConnection.MaxPacketBytes];
        var frames = new List<(ushort, byte[])>();
        while (true)
        {
            var received = await clientPeer.ReceiveAsync(buffer, timeout.Token);
            if (received.TransportClosed || received.BytesReceived < 16)
                throw new InvalidDataException("KCP rogue test did not receive a complete game packet.");
            var id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4));
            if (id == ushort.MaxValue) return frames;
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8));
            frames.Add((id, buffer.AsSpan(12, length).ToArray()));
        }
    }

    ServerUtils.InitializeHandlers();
    await MainFixRegression.Run(scratch, serverPeer, Check, SentFrames);
    await SharedConfigRegression.Run(Check);
    // 4.7 activity data queries sent at login (obfuscated names, so the generic empty reply skips them): both
    // must be answered or the client keeps its loading spinner up.
    var activityQueryReplies = (await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Activity
            .HandlerCKMHMAJPJEP().OnHandle(rogueConnection, [], [])))
        .Concat(await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Activity
            .HandlerMGCJKEEOPEC().OnHandle(rogueConnection, [], [])))
        .ToList();
    Check("4.7 login activity queries 10795/10215 are answered with 10788/10208 retcode 0",
        activityQueryReplies.Any(frame => frame.Id == CmdIds.PAMDEKPLLEL && PAMDEKPLLEL.Parser.ParseFrom(frame.Body).Retcode == 0) &&
        activityQueryReplies.Any(frame => frame.Id == CmdIds.LPLCFFGANFC && LPLCFFGANFC.Parser.ParseFrom(frame.Body).Retcode == 0));
    // Voracity Invasion: the activity tab builds its world activity from 10668; without it the client loads a nil prefab.
    var voracityReplies = (await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Activity.VoracityInvasion
            .HandlerVoracityInvasionGetDataCsReq().OnHandle(rogueConnection, [], [])))
        .Concat(await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Activity.VoracityInvasion
            .HandlerGetActivityVoracityInvasionDataCsReq().OnHandle(rogueConnection, [], [])))
        .ToList();
    Check("Voracity Invasion queries 10275/10675 are answered with 10268/10668 and a current update time",
        voracityReplies.Any(frame => frame.Id == CmdIds.GetVoracityInvasionDataScRsp &&
                                     GetVoracityInvasionDataScRsp.Parser.ParseFrom(frame.Body).Retcode == 0) &&
        voracityReplies.Any(frame => frame.Id == CmdIds.GetActivityVoracityInvasionDataScRsp &&
                                     GetActivityVoracityInvasionDataScRsp.Parser.ParseFrom(frame.Body) is { Retcode: 0 } progress &&
                                     Math.Abs(progress.LBEGAHHDKBE - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) < 60));
    // Player return (循行归程) is not implemented server-side; its modules must stay off the schedule, otherwise the
    // client falls back to the 3.6 return page whose bundle the 4.7 client no longer ships (black screen).
    // 1001901 = PlayerReturnPanel, 1018301/1018401 = PlayerReturnConfig 29/30 (ExcelOutput/PlayerReturnConfig.json).
    int[] playerReturnModules = [1001901, 1018301, 1018401];
    using var scheduleJson = System.Text.Json.JsonDocument.Parse(
        File.ReadAllText(Path.Combine(repository, "Config/Custom/ActivityConfig.json")));
    var scheduledModules = scheduleJson.RootElement.GetProperty("scheduleData").EnumerateArray()
        .Select(row => row.GetProperty("activityId").GetInt32()).ToHashSet();
    Check("activity schedule carries no player-return module",
        scheduledModules.Count > 0 && playerReturnModules.All(id => !scheduledModules.Contains(id)));
    // GridFight season 2. The map console (Prop_Common_GridFightConsole_01) raises OpenNewUI only when
    // ByCompareMainMissionState(GridFight_Tutorial_Main_Mission_Id) holds; its omitted MainMissionState is the class
    // default Finish, so the finished tutorial main mission must reach the client as finished (hidden, the console
    // falls back to the season-1 page). The overclock unlock quest (GridFight_OC_UnlockQuest) completes when a
    // standard run settles, and every season-2 run syncs its partner (the client game flow dereferences the first partner effect on entry).
    var gridFightConstants = GameData.GridFightConstValueData;
    if (gridFightConstants.TryGetValue("GridFight_Season_ID", out var gridFightSeason) && gridFightSeason.AsUInt32() >= 2)
    {
        var tutorialMission = gridFightConstants["GridFight_Tutorial_Main_Mission_Id"].AsUInt32();
        var missionReplies = (await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Mission
                .HandlerGetMissionStatusCsReq().OnHandle(rogueConnection, [],
                    new GetMissionStatusCsReq { MainMissionIdList = { tutorialMission } }.ToByteArray())))
            .Concat(await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Mission
                .HandlerGetMainMissionCustomValueCsReq().OnHandle(rogueConnection, [],
                    new GetMainMissionCustomValueCsReq { MainMissionIdList = { tutorialMission } }.ToByteArray())))
            .ToList();
        var missionData = GetMissionDataScRsp.Parser.ParseFrom(new March7thHoney.GameServer.Server.Packet.Send.Mission
            .PacketGetMissionDataScRsp(roguePlayer).Data);
        Check("season 2 reports the finished GridFight tutorial main mission as finished to the client",
            roguePlayer.MissionManager!.GetMainMissionStatus((int)tutorialMission) == MissionPhaseEnum.Finish &&
            (!roguePlayer.MissionManager.Data.FinishedMainMissionIds.Contains((int)tutorialMission) ||
             missionData.FinishedMainMissionIdList.Contains(tutorialMission)) &&
            missionReplies.Any(frame => frame.Id == CmdIds.GetMissionStatusScRsp &&
                                        GetMissionStatusScRsp.Parser.ParseFrom(frame.Body) is var status &&
                                        status.FinishedMainMissionIdList.Contains(tutorialMission) &&
                                        !status.UnfinishedMainMissionIdList.Contains(tutorialMission)) &&
            missionReplies.Any(frame => frame.Id == CmdIds.GetMainMissionCustomValueScRsp &&
                                        GetMainMissionCustomValueScRsp.Parser.ParseFrom(frame.Body).MainMissionList
                                            .Single().Status == MissionStatus.MissionFinish));

        var unlockQuest = (int)gridFightConstants["GridFight_OC_UnlockQuest"].AsUInt32();
        var questBefore = roguePlayer.QuestManager!.GetQuestStatus(unlockQuest);
        var season = gridFightSeason.AsUInt32();
        var division = GameData.GridFightDivisionInfoData.Values.Where(row => row.SeasonID == season).Min(row => row.ID);
        var startReplies = await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.GridFight
            .HandlerGridFightStartGamePlayCsReq().OnHandle(rogueConnection, [],
                new GridFightStartGamePlayCsReq { Season = season, DivisionId = division }.ToByteArray()));
        var started = startReplies.Where(frame => frame.Id == CmdIds.GridFightStartGamePlayScRsp)
            .Select(frame => GridFightStartGamePlayScRsp.Parser.ParseFrom(frame.Body)).SingleOrDefault();
        var partnerEffects = started?.CurrentInfo?.GameInfoList
            .Where(info => info.OGKMAKNOPOD != null)
            .SelectMany(info => info.OGKMAKNOPOD.BBEEGMDAMIB)
            .ToList() ?? [];
        Check("season 2 run starts with one active partner effect from that partner's effect list",
            started is { Retcode: 0 } && partnerEffects.Count == 1 && partnerEffects[0].MOCLKEPPIGA &&
            GameData.GridFightPetPalData.TryGetValue(partnerEffects[0].NDAHDHKGNBG, out var partner) &&
            partner.EffIDList.Contains(partnerEffects[0].OKGGFNJJLNG));
        var settleReplies = await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.GridFight
            .HandlerGridFightQuitSettleCsReq().OnHandle(rogueConnection, [], []));
        var settled = settleReplies.Where(frame => frame.Id == CmdIds.GridFightSettleNotify)
            .Select(frame => GridFightSettleNotify.Parser.ParseFrom(frame.Body)).SingleOrDefault();
        Check("season 2 settle carries the run's partner effect",
            settled?.FinishInfo?.BBEEGMDAMIB.SequenceEqual(partnerEffects) == true);
        Check("a settled standard run completes the overclock unlock quest",
            questBefore == QuestStatus.QuestDoing &&
            roguePlayer.QuestManager.GetQuestStatus(unlockQuest) is QuestStatus.QuestFinish or QuestStatus.QuestClose);
    }
    // The user's login snapshot carried six 1415 rows: one developed avatar plus five empty duplicates.
    // Skin changes must persist and both incremental and full snapshots must select the same owner.
    var skinPlayer = new PlayerInstance(60021);
    await skinPlayer.InitializeAsync();
    await skinPlayer.AddAvatar(1415, false, false);
    var skinAvatar = skinPlayer.AvatarManager!.GetFormalAvatar(1415)!;
    skinAvatar.Level = 21;
    var skinPath = skinAvatar.GetCurPathInfo();
    var skinCone = (await skinPlayer.InventoryManager!.AddItem(23052, 1, false, sync: false, returnRaw: true))!;
    var skinRelicConfig = GameData.RelicConfigData.Values.First(relic => (int)relic.Type == 1 &&
        GameData.ItemConfigData.ContainsKey(relic.ID) && GameData.RelicMainAffixData.ContainsKey(relic.MainAffixGroup) &&
        GameData.RelicSubAffixData.ContainsKey(relic.SubAffixGroup));
    var createdSkinRelic = (await skinPlayer.InventoryManager.AddItem(skinRelicConfig.ID, 1, false, sync: false, returnRaw: true))!;
    var skinRelic = skinPlayer.InventoryManager.Data.RelicItems.Single(item => item.UniqueId == createdSkinRelic.UniqueId);
    await skinPlayer.InventoryManager.EquipAvatar(1415, skinCone.UniqueId);
    await skinPlayer.InventoryManager.EquipRelic(1415, skinRelic.UniqueId, 1);
    skinPlayer.PlayerUnlockData!.Skins[1415] = [1141501];
    var skinConnection = new ProbeConnection(serverPeer) { Player = skinPlayer };
    skinPlayer.Connection = skinConnection;
    DatabaseHelper.MarkDirty(skinPlayer.Uid);
    DatabaseHelper.Instance!.SaveDatabase();
    async Task<List<(ushort Id, byte[] Body)>> DressSkin(int avatarId = 1415, int skinId = 1141501) => await SentFrames(() =>
        new March7thHoney.GameServer.Server.Packet.Recv.Avatar.HandlerDressAvatarSkinCsReq().OnHandle(skinConnection, [],
            new DressAvatarSkinCsReq { AvatarId = (uint)avatarId, SkinId = (uint)skinId }.ToByteArray()));
    var dressedFrames = await DressSkin();
    Check("skin dress sync keeps the original avatar level, cone and relic ownership",
        skinPath.Skin == 1141501 && skinAvatar.Level == 21 && skinPath.EquipId == skinCone.UniqueId && skinPath.Relic[1] == skinRelic.UniqueId &&
        dressedFrames.Any(frame => frame.Id == CmdIds.PlayerSyncScNotify &&
            PlayerSyncScNotify.Parser.ParseFrom(frame.Body).AvatarSync.AvatarPathDataInfoList.Any(path =>
                path.AvatarId == 1415 && path.DressedSkinId == 1141501 && path.PathEquipmentId == skinCone.UniqueId &&
                path.EquipRelicList.Any(relic => relic.RelicUniqueId == skinRelic.UniqueId))));
    DatabaseHelper.Instance.SaveDatabase();
    await using (var skinDb = new SqliteConnection($"Data Source={Path.Combine(scratch, "regression.db")}"))
    {
        await skinDb.OpenAsync();
        var savedSkinAvatar = ((March7thHoney.Database.Avatar.AvatarData)EntityStores.Map[typeof(March7thHoney.Database.Avatar.AvatarData)]
            .LoadAll(skinDb).Single(data => data.Uid == skinPlayer.Uid)).FormalAvatars.Single(avatar => avatar.BaseAvatarId == 1415);
        Check("skin dress alone is flushed to SQLite without another player mutation",
            savedSkinAvatar.GetCurPathInfo().Skin == 1141501 && savedSkinAvatar.Level == 21 &&
            savedSkinAvatar.GetCurPathInfo().EquipId == skinCone.UniqueId && savedSkinAvatar.GetCurPathInfo().Relic[1] == skinRelic.UniqueId);
    }
    await skinPlayer.AddAvatar(1415, false, false);
    Check("adding an owned avatar does not create an empty duplicate or reset its loadout",
        skinPlayer.AvatarManager.Data.FormalAvatars.Count(avatar => avatar.BaseAvatarId == 1415) == 1 &&
        ReferenceEquals(skinPlayer.AvatarManager.GetFormalAvatar(1415), skinAvatar) && skinPath.Skin == 1141501);
    // Keep malformed old records intact; the server already uses the first row for lookups.
    skinPlayer.AvatarManager.Data.FormalAvatars.Add(new(1415, 1415, true) { Level = 1 });
    var skinSnapshot = GetAvatarDataScRsp.Parser.ParseFrom(new PacketGetAvatarDataScRsp(skinPlayer).Data);
    Check("legacy duplicates cannot overwrite dressed avatar and equipment in the login snapshot",
        skinSnapshot.AvatarList.Count(avatar => avatar.BaseAvatarId == 1415) == 1 &&
        skinSnapshot.AvatarList.Single(avatar => avatar.BaseAvatarId == 1415).Level == 21 &&
        skinSnapshot.AvatarPathDataInfoList.Count(path => path.AvatarId == 1415) == 1 &&
        skinSnapshot.AvatarPathDataInfoList.Single(path => path.AvatarId == 1415).DressedSkinId == 1141501 &&
        skinSnapshot.AvatarPathDataInfoList.Single(path => path.AvatarId == 1415).PathEquipmentId == skinCone.UniqueId);
    await skinPlayer.TryEnterScene(101010102, 0, false);
    var skinAfterMap = GetAvatarDataScRsp.Parser.ParseFrom(new PacketGetAvatarDataScRsp(skinPlayer).Data);
    Check("map entry preserves selected skin and loadout in the authoritative avatar snapshot",
        skinAfterMap.AvatarPathDataInfoList.Where(path => path.AvatarId == 1415)
            .All(path => path.DressedSkinId == 1141501 && path.PathEquipmentId == skinCone.UniqueId &&
                path.EquipRelicList.Any(relic => relic.RelicUniqueId == skinRelic.UniqueId)));
    Check("skin switch preserves real inventory ownership and battle equipment serialization",
        skinCone.EquipAvatar == 1415 && skinRelic.EquipAvatar == 1415 &&
        skinAvatar.ToBattleProto(new(skinPlayer.Data, skinPlayer.InventoryManager!.Data, new())).EquipmentList
            .Any(equipment => equipment.Id == skinCone.ItemId) &&
        skinAvatar.ToBattleProto(new(skinPlayer.Data, skinPlayer.InventoryManager.Data, new())).RelicList
            .Any(relic => relic.Id == skinRelic.ItemId));
    DatabaseHelper.MarkDirty(skinPlayer.Uid);
    DatabaseHelper.Instance.SaveDatabase();
    await using (var skinDb = new SqliteConnection($"Data Source={Path.Combine(scratch, "regression.db")}"))
    {
        await skinDb.OpenAsync();
        var loadedAvatarData = (March7thHoney.Database.Avatar.AvatarData)EntityStores.Map[typeof(March7thHoney.Database.Avatar.AvatarData)]
            .LoadAll(skinDb).Single(data => data.Uid == skinPlayer.Uid);
        var cachedData = DatabaseHelper.UidInstanceMap[skinPlayer.Uid];
        lock (cachedData)
        {
            cachedData.Remove(skinPlayer.AvatarManager.Data);
            cachedData.Add(loadedAvatarData);
        }
        skinPlayer.Connection = null;
        skinPlayer = new PlayerInstance(skinPlayer.Data);
        await skinPlayer.InitializeAsync();
        skinConnection = new ProbeConnection(serverPeer) { Player = skinPlayer };
        skinPlayer.Connection = skinConnection;
        await SentFrames(async () => await skinPlayer.OnLogin());
        skinAvatar = skinPlayer.AvatarManager!.GetFormalAvatar(1415)!;
        skinPath = skinAvatar.GetCurPathInfo();
        var relogSnapshot = GetAvatarDataScRsp.Parser.ParseFrom(new PacketGetAvatarDataScRsp(skinPlayer).Data);
        Check("SQLite reload and player login restore skin and loadout without deleting legacy duplicates",
            loadedAvatarData.FormalAvatars.Count(avatar => avatar.BaseAvatarId == 1415) == 2 &&
            relogSnapshot.AvatarList.Count(avatar => avatar.BaseAvatarId == 1415) == 1 && skinAvatar.Level == 21 &&
            skinPath.Skin == 1141501 && skinPath.EquipId == skinCone.UniqueId && skinPath.Relic[1] == skinRelic.UniqueId);
    }
    // 奇缘星魂 apply in 货币战争 too: GridFight builds its battle avatar from the owned avatar, and it used to drop the
    // activated ExRank there.
    AvatarExRankMaterials.ApplyDocument(Newtonsoft.Json.Linq.JObject.Parse(
        File.ReadAllText(Path.Combine(repository, "Config", "Custom", "AvatarExRankMaterials.json"))));
    var exRankPlayer = new PlayerInstance(60031);
    await exRankPlayer.InitializeAsync();
    await exRankPlayer.AddAvatar(1308, false, false);
    exRankPlayer.AvatarManager!.Data.ExRankIds[1308] = [130801];
    var exRankAvatar = exRankPlayer.AvatarManager.GetFormalAvatar(1308)!;
    var exRankCollection = new March7thHoney.Database.Avatar.PlayerDataCollection(exRankPlayer.Data, exRankPlayer.InventoryManager!.Data, new());
    Check("an activated ExRank reaches GridFight battle avatars as well as ordinary ones",
        exRankAvatar.ToBattleProto(exRankCollection, 1308, AvatarType.AvatarGridFightType).IGDEHGIKFJA.Contains(130801u) &&
        exRankAvatar.ToBattleProto(exRankCollection, 1308, AvatarType.AvatarFormalType).IGDEHGIKFJA.Contains(130801u));
    var repeatSkinFrames = await DressSkin();
    Check("repeated dress request is ACK-only and preserves equipped items",
        repeatSkinFrames.Select(frame => frame.Id).SequenceEqual(new[] { (ushort)CmdIds.DressAvatarSkinScRsp }) &&
        DressAvatarSkinScRsp.Parser.ParseFrom(repeatSkinFrames[0].Body).PLDKENJLKME == 0 && skinPath.EquipId == skinCone.UniqueId);
    async Task RejectSkin(string name, int avatarId, int skinId, Retcode expected)
    {
        var frames = await DressSkin(avatarId, skinId);
        Check(name, frames.Count == 1 && frames[0].Id == CmdIds.DressAvatarSkinScRsp &&
            DressAvatarSkinScRsp.Parser.ParseFrom(frames[0].Body).PLDKENJLKME == (uint)expected &&
            skinPath.Skin == 1141501 && skinPath.EquipId == skinCone.UniqueId && skinPath.Relic[1] == skinRelic.UniqueId);
    }
    await RejectSkin("absent avatar skin request returns an error without state mutation", 999999, 1141501, Retcode.RetAvatarNotExist);
    await RejectSkin("unknown skin request returns an error without state mutation", 1415, 999999, Retcode.RetAvatarSkinItemNotExist);
    await RejectSkin("skin belonging to another avatar cannot change its equipment or appearance", 1001, 1141501, Retcode.RetAvatarSkinNotMatchAvatar);
    skinPlayer.PlayerUnlockData!.Skins.Remove(1415);
    await RejectSkin("unowned skin is refused before applying the change", 1415, 1141501, Retcode.RetAvatarSkinItemNotExist);
    DatabaseHelper.MarkDirty(skinPlayer.Uid);
    DatabaseHelper.Instance.SaveDatabase();
    var takeOffFrames = await SentFrames(() =>
        new March7thHoney.GameServer.Server.Packet.Recv.Avatar.HandlerTakeOffAvatarSkinCsReq().OnHandle(skinConnection, [],
            new TakeOffAvatarSkinCsReq { AvatarId = 1415 }.ToByteArray()));
    Check("default outfit can be restored without an unlocked-skins dictionary entry",
        skinPath.Skin == 0 && skinPath.EquipId == skinCone.UniqueId && skinPath.Relic[1] == skinRelic.UniqueId &&
        TakeOffAvatarSkinScRsp.Parser.ParseFrom(takeOffFrames.Single(frame => frame.Id == CmdIds.TakeOffAvatarSkinScRsp).Body).PLDKENJLKME == 0);
    DatabaseHelper.Instance.SaveDatabase();
    await using (var skinDb = new SqliteConnection($"Data Source={Path.Combine(scratch, "regression.db")}"))
    {
        await skinDb.OpenAsync();
        var savedData = (March7thHoney.Database.Avatar.AvatarData)EntityStores.Map[typeof(March7thHoney.Database.Avatar.AvatarData)]
            .LoadAll(skinDb).Single(data => data.Uid == skinPlayer.Uid);
        Check("take-off alone persists the default outfit with the same cone and relic",
            savedData.FormalAvatars.First(avatar => avatar.BaseAvatarId == 1415).GetCurPathInfo().Skin == 0 &&
            savedData.FormalAvatars.First(avatar => avatar.BaseAvatarId == 1415).GetCurPathInfo().EquipId == skinCone.UniqueId);
    }
    var pathOwner = skinPlayer.AvatarManager!.GetHero()!;
    var extraPathId = GameData.MultiplePathAvatarConfigData.Values.First(path => path.BaseAvatarID == 8001 &&
        path.AvatarID != 8001 && !pathOwner.PathInfos.ContainsKey(path.AvatarID) && GameData.AvatarConfigData.ContainsKey(path.AvatarID)).AvatarID;
    var currentPathId = pathOwner.AvatarId;
    await skinPlayer.AddAvatar(extraPathId, false, false);
    await skinPlayer.AddAvatar(extraPathId, false, false);
    Check("repeated alternate path unlock keeps a single owner and its current path",
        skinPlayer.AvatarManager.Data.FormalAvatars.Count(avatar => avatar.BaseAvatarId == 8001) == 1 &&
        pathOwner.AvatarId == currentPathId && pathOwner.PathInfos.ContainsKey(extraPathId));
    skinPlayer.Connection = null;

    // Official 4.6 seq 20633-20654: leaving Qlipoth Fort finishes the EnterFloor gate
    // before reporting the dialogue's successors. A delayed Doing snapshot sends the client back inside.
    var fortPlayer = new PlayerInstance(60018);
    await fortPlayer.InitializeAsync();
    fortPlayer.MissionManager!.Data.EnableMissionOverride = true;
    await fortPlayer.TryEnterScene(101010102, 0, false);
    fortPlayer.MissionManager.Data.SetMainMissionStatus(1010201, MissionPhaseEnum.Accept);
    fortPlayer.MissionManager.Data.SetSubMissionStatus(101020101, MissionPhaseEnum.Finish);
    fortPlayer.MissionManager.Data.SetSubMissionStatus(101020102, MissionPhaseEnum.Accept);
    var fortConnection = new ProbeConnection(serverPeer) { Player = fortPlayer };
    fortPlayer.Connection = fortConnection;
    async Task<List<(ushort Id, byte[] Body)>> FortTalk(int subId) => await SentFrames(() =>
        new HandlerFinishTalkMissionCsReq().OnHandle(fortConnection, [],
            new FinishTalkMissionCsReq { TalkStr = "Mission_" + subId }.ToByteArray()));
    List<Mission> FortMissionUpdates(List<(ushort Id, byte[] Body)> frames, int subId) => frames
        .Where(frame => frame.Id == CmdIds.PlayerSyncScNotify)
        .SelectMany(frame => PlayerSyncScNotify.Parser.ParseFrom(frame.Body).MissionSync?.MissionList ?? [])
        .Where(mission => mission.Id == subId).ToList();
    bool NoMissionRollback(List<Mission> updates) => updates.Count > 0 &&
        updates.Last().Status == MissionStatus.MissionFinish &&
        updates.SkipWhile(mission => mission.Status != MissionStatus.MissionFinish)
            .All(mission => mission.Status == MissionStatus.MissionFinish && mission.Progress == 1);
    var fortEnterFrames = await FortTalk(101020102);
    Check("story dialogue enters Qlipoth Fort and completes its EnterFloor gate",
        fortPlayer.Data.EntryId == 1010102 && fortPlayer.Data.FloorId == 10101002 &&
        fortPlayer.MissionManager.GetSubMissionStatus(101020103) == MissionPhaseEnum.Finish &&
        fortEnterFrames.Any(frame => frame.Id == CmdIds.EnterSceneByServerScNotify));
    Check("nested scene entry never overwrites finished gate 101020103 with Doing",
        NoMissionRollback(FortMissionUpdates(fortEnterFrames, 101020103)));
    var fortExitFrames = await FortTalk(101020104);
    Check("story dialogue leaves Qlipoth Fort for the Administrative District",
        fortPlayer.Data.EntryId == 101010102 && fortPlayer.Data.FloorId == 10101001 &&
        fortPlayer.MissionManager.GetSubMissionStatus(101020105) == MissionPhaseEnum.Finish &&
        fortPlayer.MissionManager.GetSubMissionStatus(101020106) == MissionPhaseEnum.Accept);
    Check("nested scene exit reports finished gate 101020105 without any later Doing rollback",
        NoMissionRollback(FortMissionUpdates(fortExitFrames, 101020105)));
    var fortRepeatFrames = await FortTalk(101020104);
    Check("repeated exit dialogue sends only ACK and keeps the completed gate and outside scene",
        fortRepeatFrames.Select(frame => frame.Id).SequenceEqual(new[] { (ushort)CmdIds.FinishTalkMissionScRsp }) &&
        fortPlayer.Data.EntryId == 101010102 &&
        fortPlayer.MissionManager.GetSubMissionStatus(101020105) == MissionPhaseEnum.Finish);
    await FortTalk(101020114);
    Check("client completion of the exit performance keeps the outside scene and finishes its hidden gate",
        fortPlayer.Data.EntryId == 101010102 &&
        fortPlayer.MissionManager.GetSubMissionStatus(101020114) == MissionPhaseEnum.Finish);
    fortPlayer.Connection = null;

    // Official 4.6 seq 36813/36822/36827 pair quick-up request EKLIDIDKFHC with PMEMIDKDAFG.
    // 4.7 keeps those identities, but moves commands 376/357 to 355/341 and reshuffles field numbers.
    Check("quick avatar-up binds the active version's request and response commands and scalar fields",
        CmdIds.KELLLHMHFHL == 355 && CmdIds.MILFFHPMLMN == 341 &&
        EKLIDIDKFHC.IGOOJKMPMDCFieldNumber == 7 && EKLIDIDKFHC.KLMIBJIKIBBFieldNumber == 3 &&
        EKLIDIDKFHC.BaseAvatarIdFieldNumber == 11 && PMEMIDKDAFG.ReturnItemListFieldNumber == 6 && PMEMIDKDAFG.RetcodeFieldNumber == 10);
    March7thHoney.GameServer.Server.Packet.HandlerManager.Init();
    var quickHandler = March7thHoney.GameServer.Server.Packet.HandlerManager.GetHandler(CmdIds.KELLLHMHFHL);
    Check("quick-up request dispatches through the generated opcode registry",
        quickHandler is March7thHoney.GameServer.Server.Packet.Recv.Avatar.HandlerAvatarQuickUpCsReq);
    var quickPlayer = new PlayerInstance(60017);
    await quickPlayer.InitializeAsync();
    await quickPlayer.InventoryManager!.AddItem(1202, 1, false, sync: false);
    var quickAvatar = quickPlayer.AvatarManager!.GetFormalAvatar(1202)!;
    var quickConnection = new ProbeConnection(serverPeer) { Player = quickPlayer };
    quickPlayer.Connection = quickConnection;
    async Task ResetQuickAvatar(int level = 20, int promotion = 0, int exp = 0)
    {
        quickAvatar.Level = level;
        quickAvatar.Promotion = promotion;
        quickAvatar.Exp = exp;
        quickPlayer.Data.Level = 70;
        quickPlayer.Data.WorldLevel = 6;
        quickPlayer.Data.Scoin = 1000000;
        quickPlayer.InventoryManager!.Data.MaterialItems.Clear();
        foreach (var id in new[] { 211, 212, 213 }) await quickPlayer.InventoryManager.PutItem(id, 100);
        foreach (var id in GameData.AvatarPromotionConfigData.Values.Where(row => row.AvatarID == quickAvatar.AvatarId)
                     .SelectMany(row => row.PromotionCostList).Where(cost => cost.ItemID != 2).Select(cost => cost.ItemID).Distinct())
            await quickPlayer.InventoryManager.PutItem(id, 100);
    }
    EKLIDIDKFHC QuickRequest(uint level = 30, uint promotion = 1)
    {
        var request = new EKLIDIDKFHC { BaseAvatarId = 1202, IGOOJKMPMDC = level, KLMIBJIKIBB = promotion,
            BACAOLDADMC = new ItemCostData(), OAINNDAHIMG = new ItemCostData() };
        request.BACAOLDADMC.ItemList.Add(new ItemCost { PileItem = new PileItem { ItemId = 213, ItemNum = 9 } });
        var promotionCosts = GameData.AvatarPromotionConfigData.Values.Where(row => row.AvatarID == quickAvatar.AvatarId &&
                row.Promotion >= quickAvatar.Promotion && row.Promotion < promotion)
            .SelectMany(row => row.PromotionCostList).GroupBy(cost => cost.ItemID);
        foreach (var group in promotionCosts)
            request.OAINNDAHIMG.ItemList.Add(new ItemCost { PileItem = new PileItem
                { ItemId = (uint)group.Key, ItemNum = (uint)group.Sum(cost => cost.ItemNum) } });
        return request;
    }
    string QuickState() => $"{quickAvatar.Level}/{quickAvatar.Promotion}/{quickAvatar.Exp}/{quickPlayer.Data.Scoin}:" +
        string.Join(",", quickPlayer.InventoryManager!.Data.MaterialItems.OrderBy(item => item.ItemId).Select(item => $"{item.ItemId}={item.Count}"));
    async Task<(PMEMIDKDAFG Response, List<(ushort Id, byte[] Body)> Frames)> SendQuick(EKLIDIDKFHC request)
    {
        var frames = await SentFrames(() => quickHandler!.OnHandle(quickConnection, [], request.ToByteArray()));
        return (PMEMIDKDAFG.Parser.ParseFrom(frames.Single(frame => frame.Id == CmdIds.MILFFHPMLMN).Body), frames);
    }
    async Task RejectQuick(string name, EKLIDIDKFHC request, Retcode expected)
    {
        var before = QuickState();
        var result = await SendQuick(request);
        Check(name, result.Response.Retcode == (uint)expected && before == QuickState() &&
            result.Frames.All(frame => frame.Id != CmdIds.PlayerSyncScNotify));
    }
    await ResetQuickAvatar();
    var capturedPromotionRequest = QuickRequest();
    var promotionMoney = GameData.AvatarPromotionConfigData[quickAvatar.AvatarId * 10].PromotionCostList.Where(cost => cost.ItemID == 2).Sum(cost => cost.ItemNum);
    var promotionMaterials = capturedPromotionRequest.OAINNDAHIMG.ItemList.Where(cost => cost.PileItem.ItemId != 2).Select(cost => cost.PileItem).ToList();
    var quickSuccess = await SendQuick(capturedPromotionRequest);
    Check("quick upgrade with promotion consumes both book and promotion costs and reaches level 30 / promotion 1",
        quickSuccess.Response.Retcode == 0 && quickAvatar.Level == 30 && quickAvatar.Promotion == 1 && quickAvatar.Exp == 0 &&
        quickPlayer.Data.Scoin == 1000000 - 18000 - promotionMoney && quickPlayer.InventoryManager!.GetItem(213)?.Count == 91 &&
        promotionMaterials.All(cost => quickPlayer.InventoryManager.GetItem((int)cost.ItemId)?.Count == 100 - cost.ItemNum));
    Check("quick-up refunds the captured two low-tier avatar books instead of equipment EXP materials",
        quickSuccess.Response.ReturnItemList.Count == 1 && quickSuccess.Response.ReturnItemList[0].ItemId == 211 &&
        quickSuccess.Response.ReturnItemList[0].ItemNum == 2 && quickPlayer.InventoryManager!.GetItem(211)?.Count == 102);
    var quickSync = quickSuccess.Frames.Where(frame => frame.Id == CmdIds.PlayerSyncScNotify)
        .Select(frame => PlayerSyncScNotify.Parser.ParseFrom(frame.Body)).ToList();
    Check("quick-up synchronizes the final avatar, credit and material balances before its own response",
        quickSuccess.Frames.FindIndex(frame => frame.Id == CmdIds.PlayerSyncScNotify) < quickSuccess.Frames.FindIndex(frame => frame.Id == CmdIds.MILFFHPMLMN) &&
        quickSync.Any(sync => sync.AvatarSync?.AvatarList.Any(avatar => avatar.BaseAvatarId == 1202 && avatar.Level == 30 && avatar.Promotion == 1) == true &&
            sync.BasicInfo?.Scoin == quickPlayer.Data.Scoin && sync.MaterialList.Any(item => item.Tid == 211 && item.Num == 102)));
    await RejectQuick("replaying a successful quick upgrade cannot consume materials again", capturedPromotionRequest, Retcode.RetAvatarExpReachPromotionLimit);
    await ResetQuickAvatar(promotion: 1);
    var capturedBooksRequest = QuickRequest();
    capturedBooksRequest.BACAOLDADMC.ItemList.Clear();
    foreach (var (id, count) in new[] { (211, 8), (212, 6), (213, 7) })
        capturedBooksRequest.BACAOLDADMC.ItemList.Add(new ItemCost { PileItem = new PileItem { ItemId = (uint)id, ItemNum = (uint)count } });
    var booksSuccess = await SendQuick(capturedBooksRequest);
    Check("already promoted quick upgrade only consumes the captured EXP-book mix and fee",
        booksSuccess.Response.Retcode == 0 && quickAvatar.Level == 30 && quickAvatar.Promotion == 1 &&
        booksSuccess.Response.ReturnItemList.Count == 0 && quickPlayer.Data.Scoin == 982200 &&
        quickPlayer.InventoryManager!.GetItem(211)?.Count == 92 && quickPlayer.InventoryManager.GetItem(212)?.Count == 94 &&
        quickPlayer.InventoryManager.GetItem(213)?.Count == 93);
    await ResetQuickAvatar(exp: 1000);
    var partialExpSuccess = await SendQuick(QuickRequest());
    Check("quick upgrade accounts for existing avatar EXP when refunding surplus books",
        partialExpSuccess.Response.Retcode == 0 && partialExpSuccess.Response.ReturnItemList.Single().ItemNum == 3 && quickAvatar.Exp == 0);
    await ResetQuickAvatar();
    quickPlayer.Data.Scoin = 0;
    await RejectQuick("quick upgrade with insufficient credits changes no avatar or material", QuickRequest(), Retcode.RetScoinNotEnough);
    await ResetQuickAvatar();
    quickPlayer.InventoryManager!.GetItem(213)!.Count = 8;
    await RejectQuick("quick upgrade with insufficient books changes no avatar or material", QuickRequest(), Retcode.RetItemNotEnough);
    await ResetQuickAvatar();
    var requiredMaterial = QuickRequest().OAINNDAHIMG.ItemList.First(cost => cost.PileItem.ItemId != 2).PileItem.ItemId;
    quickPlayer.InventoryManager!.GetItem((int)requiredMaterial)!.Count = 0;
    await RejectQuick("missing promotion materials cannot cause partial quick-up consumption", QuickRequest(), Retcode.RetItemNotEnough);
    await ResetQuickAvatar();
    var forgedCosts = QuickRequest();
    forgedCosts.OAINNDAHIMG.ItemList.Clear();
    await RejectQuick("quick upgrade cannot omit required promotion costs", forgedCosts, Retcode.RetItemNotInCostList);
    var duplicateBooks = QuickRequest();
    duplicateBooks.BACAOLDADMC.ItemList[0].PileItem.ItemNum = 60;
    duplicateBooks.BACAOLDADMC.ItemList.Add(duplicateBooks.BACAOLDADMC.ItemList[0].Clone());
    await RejectQuick("duplicate EXP-book rows are aggregated before balance validation", duplicateBooks, Retcode.RetItemNotEnough);
    var overflowBooks = QuickRequest();
    overflowBooks.BACAOLDADMC.ItemList[0].PileItem.ItemNum = uint.MaxValue;
    await RejectQuick("quick upgrade rejects an overflowing item quantity", overflowBooks, Retcode.RetReqParaInvalid);
    var equipmentAsBook = QuickRequest();
    equipmentAsBook.BACAOLDADMC.ItemList[0] = new ItemCost { EquipmentUniqueId = 1 };
    await RejectQuick("quick upgrade rejects equipment as an EXP-book cost", equipmentAsBook, Retcode.RetReqParaInvalid);
    var wrongBook = QuickRequest();
    wrongBook.BACAOLDADMC.ItemList[0].PileItem.ItemId = (uint)GameData.EquipmentExpItemConfigData.Keys.First();
    await RejectQuick("quick upgrade rejects light-cone EXP materials", wrongBook, Retcode.RetAvatarExpItemNotExist);
    var insufficientExp = QuickRequest();
    insufficientExp.BACAOLDADMC.ItemList[0].PileItem.ItemNum = 1;
    await RejectQuick("quick upgrade rejects a target not covered by supplied EXP", insufficientExp, Retcode.RetAvatarResExpNotEnough);
    await RejectQuick("quick upgrade respects the selected promotion's level cap", QuickRequest(40), Retcode.RetAvatarExpReachPromotionLimit);
    quickPlayer.Data.WorldLevel = 0;
    await RejectQuick("quick upgrade respects world-level requirements across multiple promotions", QuickRequest(40, 2), Retcode.RetAvatarPromotionNotMeet);
    quickPlayer.Data.WorldLevel = 6;
    quickPlayer.Data.Level = 1;
    await RejectQuick("quick upgrade respects Trailblaze-level promotion requirements", QuickRequest(), Retcode.RetAvatarPromotionNotMeet);
    await ResetQuickAvatar();
    var missingAvatar = QuickRequest(); missingAvatar.BaseAvatarId = uint.MaxValue;
    await RejectQuick("quick-up for an absent avatar receives an error response", missingAvatar, Retcode.RetAvatarNotExist);
    await RejectQuick("quick upgrade rejects a lower target level", QuickRequest(19), Retcode.RetReqParaInvalid);
    var parallelReq = QuickRequest();
    var parallelFrames = await SentFrames(async () => await Task.WhenAll(
        quickHandler!.OnHandle(quickConnection, [], parallelReq.ToByteArray()),
        quickHandler.OnHandle(quickConnection, [], parallelReq.ToByteArray())));
    var parallelResponses = parallelFrames.Where(frame => frame.Id == CmdIds.MILFFHPMLMN).Select(frame => PMEMIDKDAFG.Parser.ParseFrom(frame.Body)).ToList();
    Check("parallel identical quick-up requests produce one upgrade and one material charge",
        parallelResponses.Count == 2 && parallelResponses.Count(response => response.Retcode == 0) == 1 &&
        quickAvatar.Level == 30 && quickPlayer.Data.Scoin == 1000000 - 18000 - promotionMoney && quickPlayer.InventoryManager!.GetItem(213)?.Count == 91);
    quickPlayer.Connection = null;

    var quickEquipmentHandler = March7thHoney.GameServer.Server.Packet.HandlerManager.GetHandler(CmdIds.EquipmentQuickUpCsReq);
    Check("equipment quick-up has a registered handler instead of an empty automatic success response",
        quickEquipmentHandler != null);
    if (quickEquipmentHandler != null)
    {
        Check("equipment quick-up binds 4.7 commands and fields from the stable EOBJKAMNNPI identity",
            CmdIds.EquipmentQuickUpCsReq == 513 && CmdIds.EquipmentQuickUpScRsp == 517 &&
            EquipmentQuickUpCsReq.IGOOJKMPMDCFieldNumber == 10 && EquipmentQuickUpCsReq.KLMIBJIKIBBFieldNumber == 1 &&
            EquipmentQuickUpCsReq.BOBOKJBAMNBFieldNumber == 6 && EquipmentQuickUpCsReq.IDGEEENIMMKFieldNumber == 8);
        var equipmentPlayer = new PlayerInstance(60019);
        await equipmentPlayer.InitializeAsync();
        var equipmentInventory = equipmentPlayer.InventoryManager!;
        var equipmentConnection = new ProbeConnection(serverPeer) { Player = equipmentPlayer };
        March7thHoney.Database.Inventory.ItemData upgradedEquipment = new();
        async Task ResetQuickEquipment(int level = 1, int promotion = 0, int exp = 0, int itemId = 23052)
        {
            equipmentPlayer.Connection = null;
            equipmentPlayer.Data.WorldLevel = 6;
            equipmentPlayer.Data.Scoin = 1000000;
            equipmentInventory.Data.EquipmentItems.Clear();
            equipmentInventory.Data.MaterialItems.Clear();
            upgradedEquipment = await equipmentInventory.PutItem(itemId, 1, rank: 1, level: level,
                promotion: promotion, exp: exp, uniqueId: 107);
            // Upgrading an equipped and protected target is valid; consuming such fodder is not.
            upgradedEquipment.Locked = true;
            upgradedEquipment.EquipAvatar = 1003;
            foreach (var id in new[] { 221, 222, 223 }) await equipmentInventory.PutItem(id, 100);
            foreach (var id in GameData.EquipmentPromotionConfigData.Values.Where(row => row.EquipmentID == upgradedEquipment.ItemId)
                         .SelectMany(row => row.PromotionCostList).Where(cost => cost.ItemID != 2).Select(cost => cost.ItemID).Distinct())
                await equipmentInventory.PutItem(id, 100);
            equipmentPlayer.Connection = equipmentConnection;
        }
        EquipmentQuickUpCsReq EquipmentQuickRequest(uint level = 20, uint promotion = 0, uint count = 5)
        {
            var request = new EquipmentQuickUpCsReq { EquipmentUniqueId = 107, IGOOJKMPMDC = level,
                KLMIBJIKIBB = promotion, BOBOKJBAMNB = new ItemCostData(), IDGEEENIMMK = new ItemCostData() };
            if (count > 0) request.BOBOKJBAMNB.ItemList.Add(new ItemCost { PileItem = new PileItem { ItemId = 223, ItemNum = count } });
            foreach (var group in GameData.EquipmentPromotionConfigData.Values.Where(row => row.EquipmentID == upgradedEquipment.ItemId &&
                         row.Promotion >= upgradedEquipment.Promotion && row.Promotion < promotion)
                         .SelectMany(row => row.PromotionCostList).GroupBy(cost => cost.ItemID))
                request.IDGEEENIMMK.ItemList.Add(new ItemCost { PileItem = new PileItem
                    { ItemId = (uint)group.Key, ItemNum = (uint)group.Sum(cost => cost.ItemNum) } });
            return request;
        }
        string EquipmentQuickState() => $"{equipmentPlayer.Data.Scoin}:" +
            string.Join(",", equipmentInventory.Data.EquipmentItems.OrderBy(item => item.UniqueId)
                .Select(item => $"{item.UniqueId}={item.Level}/{item.Promotion}/{item.Exp}/{item.Locked}/{item.EquipAvatar}")) + ":" +
            string.Join(",", equipmentInventory.Data.MaterialItems.OrderBy(item => item.ItemId).Select(item => $"{item.ItemId}={item.Count}"));
        async Task<(EquipmentQuickUpScRsp Response, List<(ushort Id, byte[] Body)> Frames)> SendEquipmentQuick(EquipmentQuickUpCsReq request)
        {
            var frames = await SentFrames(() => quickEquipmentHandler.OnHandle(equipmentConnection, [], request.ToByteArray()));
            return (EquipmentQuickUpScRsp.Parser.ParseFrom(frames.Single(frame => frame.Id == CmdIds.EquipmentQuickUpScRsp).Body), frames);
        }
        async Task RejectEquipmentQuick(string name, EquipmentQuickUpCsReq request, Retcode expected)
        {
            var before = EquipmentQuickState();
            var result = await SendEquipmentQuick(request);
            Check(name, result.Response.Retcode == (uint)expected && before == EquipmentQuickState());
        }
        await ResetQuickEquipment();
        // The user's 4.7 log sends five 223 materials, target level 20 and promotion 0.
        var equipmentRequest = EquipmentQuickRequest();
        var requiredEquipmentExp = Enumerable.Range(1, 19).Sum(level => GameData.GetEquipmentExpRequired(
            GameData.EquipmentConfigData[upgradedEquipment.ItemId].ExpType, level));
        var equipmentExpProvided = GameData.EquipmentExpItemConfigData[223].ExpProvide * 5;
        var equipmentResult = await SendEquipmentQuick(equipmentRequest);
        Check("captured equipment quick-up reaches level 20 and charges EXP materials and credit once",
            equipmentResult.Response.Retcode == 0 && upgradedEquipment.Level == 20 && upgradedEquipment.Promotion == 0 &&
            upgradedEquipment.Exp == 0 && upgradedEquipment.Locked && upgradedEquipment.EquipAvatar == 1003 &&
            equipmentPlayer.Data.Scoin == 1000000 - equipmentExpProvided / 2 && equipmentInventory.GetItem(223)?.Count == 95 + equipmentResult.Response.ReturnItemList.Where(item => item.ItemId == 223).Sum(item => item.ItemNum));
        var refundedEquipmentExp = equipmentResult.Response.ReturnItemList.Sum(item =>
            GameData.EquipmentExpItemConfigData[(int)item.ItemId].ExpProvide * (int)item.ItemNum);
        Check("equipment quick-up refunds surplus as equipment EXP materials with matching inventory balances",
            equipmentResult.Response.ReturnItemList.Count > 0 && refundedEquipmentExp <= equipmentExpProvided - requiredEquipmentExp &&
            equipmentExpProvided - requiredEquipmentExp - refundedEquipmentExp < GameData.EquipmentExpItemConfigData[221].ExpProvide &&
            equipmentResult.Response.ReturnItemList.All(item => equipmentInventory.GetItem((int)item.ItemId)?.Count ==
                (item.ItemId == 223 ? 95 : 100) + item.ItemNum));
        var equipmentSync = equipmentResult.Frames.Where(frame => frame.Id == CmdIds.PlayerSyncScNotify)
            .Select(frame => PlayerSyncScNotify.Parser.ParseFrom(frame.Body)).ToList();
        Check("equipment quick-up synchronizes the upgraded target and balances before its response",
            equipmentResult.Frames.FindIndex(frame => frame.Id == CmdIds.PlayerSyncScNotify) < equipmentResult.Frames.FindIndex(frame => frame.Id == CmdIds.EquipmentQuickUpScRsp) &&
            equipmentSync.Any(sync => sync.EquipmentList.Any(item => item.UniqueId == 107 && item.Level == 20) &&
                sync.BasicInfo?.Scoin == equipmentPlayer.Data.Scoin));
        await RejectEquipmentQuick("replayed equipment quick-up cannot charge or refund again", equipmentRequest, Retcode.RetEquipmentLevelReachMax);
        await ResetQuickEquipment(level: 20, itemId: 21027);
        // Official 4.6 seq 36859: 18 medium materials plus promotion costs produce 30 / 1.
        var equipmentPromoteRequest = EquipmentQuickRequest(30, 1, 0);
        equipmentPromoteRequest.BOBOKJBAMNB.ItemList.Add(new ItemCost { PileItem = new PileItem { ItemId = 222, ItemNum = 18 } });
        var equipmentPromotionCredit = equipmentPromoteRequest.IDGEEENIMMK.ItemList.Where(cost => cost.PileItem.ItemId == 2).Sum(cost => cost.PileItem.ItemNum);
        var promotedEquipmentResult = await SendEquipmentQuick(equipmentPromoteRequest);
        Check("equipment quick-up performs the captured 20 to 30 promotion and combined 22000 credit cost",
            promotedEquipmentResult.Response.Retcode == 0 && upgradedEquipment.Level == 30 && upgradedEquipment.Promotion == 1 &&
            equipmentPlayer.Data.Scoin == 1000000 - 18000 - equipmentPromotionCredit && equipmentInventory.GetItem(222)?.Count == 82 &&
            equipmentPromoteRequest.IDGEEENIMMK.ItemList.Where(cost => cost.PileItem.ItemId != 2)
                .All(cost => equipmentInventory.GetItem((int)cost.PileItem.ItemId)?.Count == 100 - cost.PileItem.ItemNum));
        await ResetQuickEquipment();
        var fodder = await equipmentInventory.PutItem(20000, 1, rank: 1, level: 1, uniqueId: 101);
        // The user's mixed selection: eight small materials, five unlocked 3-star cones and ten medium materials.
        var mixedEquipmentRequest = EquipmentQuickRequest(count: 0);
        mixedEquipmentRequest.BOBOKJBAMNB.ItemList.Add(new ItemCost { PileItem = new PileItem { ItemId = 221, ItemNum = 8 } });
        for (var id = 101; id <= 105; id++)
        {
            if (id != 101) await equipmentInventory.PutItem(20000, 1, rank: 1, level: 1, uniqueId: id);
            mixedEquipmentRequest.BOBOKJBAMNB.ItemList.Add(new ItemCost { EquipmentUniqueId = (uint)id });
        }
        mixedEquipmentRequest.BOBOKJBAMNB.ItemList.Add(new ItemCost { PileItem = new PileItem { ItemId = 222, ItemNum = 10 } });
        fodder.Locked = true;
        await RejectEquipmentQuick("quick-up cannot consume protected equipment", mixedEquipmentRequest, Retcode.RetEquipmentLocked);
        fodder.Locked = false; fodder.EquipAvatar = 1001;
        await RejectEquipmentQuick("quick-up cannot consume equipped fodder", mixedEquipmentRequest, Retcode.RetEquipmentAlreadyDressed);
        fodder.EquipAvatar = 0;
        var mixedResult = await SendEquipmentQuick(mixedEquipmentRequest);
        Check("quick-up accepts mixed materials and equipment fodder and synchronizes the removed unique id",
            mixedResult.Response.Retcode == 0 && upgradedEquipment.Level == 20 && equipmentPlayer.Data.Scoin == 986750 &&
            equipmentInventory.Data.EquipmentItems.Count == 1 && equipmentInventory.GetItem(221)?.Count == 92 && equipmentInventory.GetItem(222)?.Count == 90 &&
            mixedResult.Frames.Where(frame => frame.Id == CmdIds.PlayerSyncScNotify)
                .Any(frame => PlayerSyncScNotify.Parser.ParseFrom(frame.Body).DelEquipmentList.ToHashSet()
                    .SetEquals(new uint[] { 101, 102, 103, 104, 105 })));
        await ResetQuickEquipment();
        var selfEquipment = EquipmentQuickRequest(); selfEquipment.BOBOKJBAMNB.ItemList.Add(new ItemCost { EquipmentUniqueId = 107 });
        await RejectEquipmentQuick("quick-up cannot consume its target", selfEquipment, Retcode.RetEquipmentConsumeSelf);
        var missingEquipment = EquipmentQuickRequest(); missingEquipment.EquipmentUniqueId = uint.MaxValue;
        await RejectEquipmentQuick("quick-up rejects absent targets without charging", missingEquipment, Retcode.RetEquipmentNotExist);
        var missingFodder = EquipmentQuickRequest(); missingFodder.BOBOKJBAMNB.ItemList.Add(new ItemCost { EquipmentUniqueId = 9999 });
        await RejectEquipmentQuick("quick-up rejects stale fodder without partial payment", missingFodder, Retcode.RetEquipmentNotExist);
        await equipmentInventory.PutItem(20000, 1, rank: 1, level: 1, uniqueId: 101);
        var duplicateFodder = EquipmentQuickRequest();
        duplicateFodder.BOBOKJBAMNB.ItemList.Add(new ItemCost { EquipmentUniqueId = 101 });
        duplicateFodder.BOBOKJBAMNB.ItemList.Add(new ItemCost { EquipmentUniqueId = 101 });
        await RejectEquipmentQuick("quick-up rejects repeated fodder IDs without inflating EXP", duplicateFodder, Retcode.RetReqParaInvalid);
        await RejectEquipmentQuick("equipment quick-up rejects a target beyond its promotion cap", EquipmentQuickRequest(30), Retcode.RetEquipmentLevelReachMax);
        await RejectEquipmentQuick("equipment quick-up rejects insufficient EXP before payment", EquipmentQuickRequest(count: 1), Retcode.RetItemNotEnough);
        equipmentPlayer.Data.Scoin = 1;
        await RejectEquipmentQuick("equipment quick-up rejects insufficient credit before payment", EquipmentQuickRequest(), Retcode.RetScoinNotEnough);
        await ResetQuickEquipment();
        equipmentInventory.GetItem(223)!.Count = 4;
        await RejectEquipmentQuick("equipment quick-up rejects insufficient material balance before payment", EquipmentQuickRequest(), Retcode.RetItemNotEnough);
        await ResetQuickEquipment();
        var invalidCost = EquipmentQuickRequest(); invalidCost.BOBOKJBAMNB.ItemList[0].PileItem.ItemNum = 0;
        await RejectEquipmentQuick("equipment quick-up rejects zero cost quantities", invalidCost, Retcode.RetReqParaInvalid);
        var foreignCost = EquipmentQuickRequest(); foreignCost.BOBOKJBAMNB.ItemList[0].PileItem.ItemId = 213;
        await RejectEquipmentQuick("equipment quick-up rejects avatar books as equipment EXP", foreignCost, Retcode.RetEquipmentItemNotInCostList);
        var overflowCost = EquipmentQuickRequest(); overflowCost.BOBOKJBAMNB.ItemList[0].PileItem.ItemNum = uint.MaxValue;
        await RejectEquipmentQuick("equipment quick-up rejects overflowing quantities", overflowCost, Retcode.RetReqParaInvalid);
        var badPromotion = EquipmentQuickRequest(30, 1); badPromotion.IDGEEENIMMK.ItemList.Clear();
        await RejectEquipmentQuick("equipment quick-up verifies promotion costs instead of trusting the client", badPromotion, Retcode.RetEquipmentItemNotInCostList);
        equipmentPlayer.Data.WorldLevel = 0;
        await RejectEquipmentQuick("equipment quick-up respects higher promotion world-level requirements", EquipmentQuickRequest(40, 2, 20), Retcode.RetEquipmentLevelNotMeet);
        await ResetQuickEquipment(level: 20, exp: 17, itemId: 21027);
        var promotionOnlyEquipment = await SendEquipmentQuick(EquipmentQuickRequest(20, 1, 0));
        Check("promotion-only equipment quick-up preserves existing EXP without charging EXP material credit",
            promotionOnlyEquipment.Response.Retcode == 0 && upgradedEquipment.Level == 20 && upgradedEquipment.Promotion == 1 &&
            upgradedEquipment.Exp == 17 && equipmentPlayer.Data.Scoin == 1000000 - equipmentPromotionCredit);
        await ResetQuickEquipment();
        var concurrentEquipment = EquipmentQuickRequest();
        var concurrentEquipmentFrames = await SentFrames(async () => await Task.WhenAll(
            quickEquipmentHandler.OnHandle(equipmentConnection, [], concurrentEquipment.ToByteArray()),
            quickEquipmentHandler.OnHandle(equipmentConnection, [], concurrentEquipment.ToByteArray())));
        var concurrentEquipmentResponses = concurrentEquipmentFrames.Where(frame => frame.Id == CmdIds.EquipmentQuickUpScRsp)
            .Select(frame => EquipmentQuickUpScRsp.Parser.ParseFrom(frame.Body)).ToList();
        Check("concurrent identical equipment quick-up requests apply and charge only once",
            concurrentEquipmentResponses.Count == 2 && concurrentEquipmentResponses.Count(response => response.Retcode == 0) == 1 &&
            upgradedEquipment.Level == 20 && equipmentInventory.GetItem(223)?.Count == 95 + concurrentEquipmentResponses
                .SelectMany(response => response.ReturnItemList).Where(item => item.ItemId == 223).Sum(item => item.ItemNum) &&
            equipmentPlayer.Data.Scoin == 1000000 - equipmentExpProvided / 2);
        equipmentPlayer.Connection = null;
    }
    // Official 4.6 seq 9469-9471: the ramp monster's projectile reports its own source id,
    // a cast sequence number, and no hit targets. The response is only an ACK, not a battle.
    var ambushPlayer = new PlayerInstance(60015);
    await ambushPlayer.InitializeAsync();
    var ambushScene = new SceneInstance(ambushPlayer, GameData.MazePlaneData[20002], 20002001, 2000201);
    typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(ambushPlayer, ambushScene);
    await ambushScene.LoadEntitiesAsync();
    await ambushScene.SyncLineup(true);
    await ambushScene.EntityLoader!.LoadGroup(5, false);
    var rampMonster = ambushScene.GetEntitiesInGroup<EntityMonster>(5).Single(x => x.InstId == 200007);
    var ambushAvatar = ambushScene.AvatarInfo.Values.First();
    var ambushConnection = new ProbeConnection(serverPeer) { Player = ambushPlayer };
    ambushPlayer.Connection = ambushConnection;
    async Task<(SceneCastSkillScRsp Response, List<(ushort Id, byte[] Body)> Frames)> CastAmbush(SceneCastSkillCsReq request)
    {
        var frames = await SentFrames(async () => await new March7thHoney.GameServer.Server.Packet.Recv.Scene.HandlerSceneCastSkillCsReq()
            .OnHandle(ambushConnection, [], request.ToByteArray()));
        return (SceneCastSkillScRsp.Parser.ParseFrom(frames.Single(x => x.Id == CmdIds.SceneCastSkillScRsp).Body), frames);
    }
    var missedAttack = new SceneCastSkillCsReq
    {
        AttackedByEntityId = (uint)rampMonster.EntityId,
        CastEntityId = 98,
        TargetMotion = new MotionInfo { Pos = new Vector { X = -71464, Y = 10273, Z = -122029 } }
    };
    missedAttack.SkillExtraTags.Add(SkillExtraTag.SceneCastSkillProjectileHit);
    var miss = await CastAmbush(missedAttack);
    Check("ramp monster projectile without a hit target only ACKs and leaves the scene outside battle",
        miss.Response.Retcode == 0 && miss.Response.CastEntityId == 98 && miss.Response.BattleInfo == null &&
        miss.Response.MonsterBattleInfo.Count == 0 && ambushPlayer.BattleInstance == null &&
        miss.Frames.Select(x => x.Id).SequenceEqual(new ushort[] { CmdIds.SceneCastSkillScRsp }));
    ambushPlayer.BattleInstance = null;
    var invalidTargetAttack = missedAttack.Clone();
    invalidTargetAttack.CastEntityId = (uint)ambushAvatar.EntityId; // sequence number can collide with an entity id
    invalidTargetAttack.AssistMonsterEntityIdList.Add(uint.MaxValue);
    var invalidTarget = await CastAmbush(invalidTargetAttack);
    Check("unknown projectile targets and a cast-id collision cannot reinterpret a monster as an avatar attack",
        invalidTarget.Response.BattleInfo == null && invalidTarget.Response.MonsterBattleInfo.Count == 0 &&
        ambushPlayer.BattleInstance == null);
    ambushPlayer.BattleInstance = null;
    var missingSourceAttack = missedAttack.Clone();
    missingSourceAttack.AttackedByEntityId = uint.MaxValue;
    var missingSource = await CastAmbush(missingSourceAttack);
    Check("an unknown attack source is rejected without substituting the leader avatar",
        missingSource.Response.Retcode == (uint)Retcode.RetSceneEntityNotExist && ambushPlayer.BattleInstance == null);
    var avatarAttack = new SceneCastSkillCsReq { AttackedByEntityId = (uint)ambushAvatar.EntityId, CastEntityId = 99 };
    avatarAttack.AssistMonsterEntityIdList.Add((uint)rampMonster.EntityId);
    avatarAttack.SkillExtraTags.Add(SkillExtraTag.SceneCastSkillProjectileHit);
    var avatarHit = await CastAmbush(avatarAttack);
    Check("a real avatar attack still starts the ramp monster's configured battle",
        avatarHit.Response.BattleInfo?.StageId == 200020080 && ambushPlayer.BattleInstance != null &&
        avatarHit.Response.MonsterBattleInfo.Any(x => x.TargetMonsterEntityId == rampMonster.EntityId));
    ambushPlayer.BattleInstance = null;
    // Official seq 30233-30239: a monster hit reports the avatar in assistMonsterEntityIdList,
    // and the response identifies the attacking monster, including when HIT and LIFETIME_FINISH coexist.
    var landedAttack = missedAttack.Clone();
    landedAttack.CastEntityId = 100;
    landedAttack.AssistMonsterEntityIdList.Add((uint)ambushAvatar.EntityId);
    landedAttack.SkillExtraTags.Add(SkillExtraTag.SceneCastSkillProjectileLifetimeFinish);
    var landed = await CastAmbush(landedAttack);
    Check("a monster hit on an avatar starts an ambush and identifies the monster rather than the avatar",
        landed.Response.BattleInfo != null && ambushPlayer.BattleInstance?.Buffs.Any(x => x.BuffID == GameConstants.AMBUSH_BUFF_ID) == true &&
        landed.Response.MonsterBattleInfo.Count == 1 &&
        landed.Response.MonsterBattleInfo[0].TargetMonsterEntityId == rampMonster.EntityId);
    var firstAmbush = ambushPlayer.BattleInstance;
    var repeatedHit = await CastAmbush(landedAttack);
    Check("a repeated monster hit reuses the active battle",
        firstAmbush != null && ReferenceEquals(firstAmbush, ambushPlayer.BattleInstance) &&
        repeatedHit.Response.BattleInfo?.BattleId == landed.Response.BattleInfo?.BattleId);
    ambushPlayer.BattleInstance = null;
    var monsterAbilityId = rampMonster.MonsterData.ID;
    GameData.AdventureAbilityConfigListData.Remove(monsterAbilityId, out var monsterAbility);
    try
    {
        var abilitylessHit = await CastAmbush(landedAttack);
        Check("a monster with no exported ability still ambushes only a reported avatar target",
            abilitylessHit.Response.BattleInfo != null && abilitylessHit.Response.MonsterBattleInfo.Count == 1 &&
            abilitylessHit.Response.MonsterBattleInfo[0].TargetMonsterEntityId == rampMonster.EntityId);
    }
    finally
    {
        if (monsterAbility != null) GameData.AdventureAbilityConfigListData[monsterAbilityId] = monsterAbility;
        ambushPlayer.BattleInstance = null;
        ambushPlayer.Connection = null;
    }
    // Official seq 14891-14971: Herta's talk unlocks tutorial main 4030001, then area 100
    // starts directly with story avatars. It must never auto-complete on acceptance.
    var tutorialFlowPlayer = new PlayerInstance(60013);
    await tutorialFlowPlayer.InitializeAsync();
    tutorialFlowPlayer.MissionManager!.Data.EnableMissionOverride = true;
    tutorialFlowPlayer.Data.PlaneId = 80112;
    tutorialFlowPlayer.Data.FloorId = 80112001;
    tutorialFlowPlayer.Data.EntryId = 8011201;
    var tutorialLobby = new SceneInstance(tutorialFlowPlayer, GameData.MazePlaneData[80112], 80112001, 8011201);
    typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(tutorialFlowPlayer, tutorialLobby);
    await tutorialLobby.LoadEntitiesAsync();
    _ = new PacketGetCurSceneInfoScRsp(tutorialFlowPlayer);
    var tutorialConnection = new ProbeConnection(serverPeer) { Player = tutorialFlowPlayer };
    tutorialFlowPlayer.Connection = tutorialConnection;
    var tutorialAcceptFrames = await SentFrames(async () =>
        await tutorialFlowPlayer.MissionManager.AcceptMainMission(4030001));
    Check("accepting Rogue tutorial arms only its entry gates and grants no completion reward",
        tutorialFlowPlayer.MissionManager.GetMainMissionStatus(4030001) == MissionPhaseEnum.Accept
        && tutorialFlowPlayer.MissionManager.Data.RunningSubMissionIds.Where(id => id is >= 403000100 and <= 403000199)
            .ToHashSet().SetEquals(new[] { 403000101, 403000102 })
        && tutorialAcceptFrames.All(frame => frame.Id != CmdIds.MissionRewardScNotify));
    await SentFrames(async () => await tutorialLobby.EntityLoader!.SyncEntity());
    var tutorialConsole = tutorialLobby.Entities.Values.OfType<EntityProp>()
        .SingleOrDefault(prop => prop.GroupId == 14 && prop.InstId == 300002);
    Check("tutorial entrance is registered as an enabled server prop instead of an empty group",
        tutorialConsole is { EntityId: > 0, State: PropStateEnum.Open } && tutorialConsole.Excel.ID == 1003
        && !tutorialLobby.Groups.Contains(15));
    var tutorialGroup = tutorialAcceptFrames.Where(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify)
        .SelectMany(frame => SceneGroupRefreshScNotify.Parser.ParseFrom(frame.Body).GroupRefreshList)
        .SingleOrDefault(group => group.GroupId == 14);
    Check("tutorial entrance notification carries the complete group including its Open console",
        tutorialGroup is { RefreshType: SceneGroupRefreshType.Loaded, GroupInfo: not null }
        && tutorialGroup.RefreshEntity.Count == 0
        && tutorialGroup.GroupInfo.EntityList.Any(entity => entity.InstId == 300002
            && entity.EntityId != 0 && entity.Prop is { PropId: 1003, PropState: 1 }));
    async Task<List<(ushort Id, byte[] Body)>> TutorialTalk(int subId) => await SentFrames(() =>
        new HandlerFinishTalkMissionCsReq().OnHandle(tutorialConnection, [],
            new FinishTalkMissionCsReq { TalkStr = "Mission_" + subId }.ToByteArray()));
    await TutorialTalk(403000101);
    var tutorialRequest = new StartRogueCsReq { AreaId = 100,
        UnfinishedStoryLineIdList = { 4000004, 4000001, 4000002 } };
    var tutorialStartFrames = await SentFrames(() => new HandlerStartRogueCsReq()
        .OnHandle(tutorialConnection, [], tutorialRequest.ToByteArray()));
    var tutorialStartRsp = tutorialStartFrames.Where(frame => frame.Id == CmdIds.StartRogueScRsp)
        .Select(frame => StartRogueScRsp.Parser.ParseFrom(frame.Body)).Single();
    Check("tutorial start handler enters map 1 room 100 with the story trial roster",
        tutorialStartRsp.Retcode == 0 && tutorialStartRsp.Scene.FloorId == 80001001
        && tutorialFlowPlayer.RogueManager!.RogueInstance is { AreaId: 100, MapId: 1, Trial: true }
        && tutorialStartRsp.Lineup.AvatarList.Count == 3
        && tutorialStartRsp.Lineup.AvatarList.All(avatar => avatar.AvatarType == AvatarType.AvatarTrialType)
        && tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000102) == MissionPhaseEnum.Finish);
    var tutorialRun = tutorialFlowPlayer.RogueManager!.RogueInstance!;
    // Official 4.6 seq 14971 contains only these six room groups. Groups 2/7 put Closed barriers
    // at exactly the same positions as group 10, so checking only group 10's state misses a collision.
    var capturedTutorialGroups = new HashSet<int> { 3, 4, 5, 10, 11, 12 };
    Check("first tutorial start response contains exactly the captured room groups without overlapping templates",
        tutorialStartRsp.Scene.EntityGroupList.Where(group => group.GroupId != 0)
            .Select(group => (int)group.GroupId).ToHashSet().SetEquals(capturedTutorialGroups));
    List<SceneEntityInfo> TutorialBarriersAt(int x, int z) => tutorialFlowPlayer.SceneInstance!.ToProto()
        .EntityGroupList.SelectMany(group => group.EntityList)
        .Where(entity => entity.Prop?.PropId == 100006 && entity.Motion?.Pos is { } pos && pos.X == x && pos.Z == z)
        .ToList();
    Check("first tutorial has one physical barrier at the first encounter exit",
        TutorialBarriersAt(-23855, 216150).Count == 1);
    var tutorialGroupProbeFrames = await SentFrames(async () =>
    {
        Check("forced group activation cannot load another Rogue room template",
            await tutorialFlowPlayer.SceneInstance!.EntityLoader!.LoadGroup(2) == null &&
            await tutorialFlowPlayer.SceneInstance.EntityLoader.LoadGroup(tutorialFlowPlayer.SceneInstance.FloorInfo!.Groups[7], true) == null);
        var foreignGroup = tutorialFlowPlayer.SceneInstance!.FloorInfo!.Groups[2];
        Check("direct prop creation cannot add another room's overlapping barrier",
            await tutorialFlowPlayer.SceneInstance.EntityLoader!.LoadProp(
                foreignGroup.PropList.Single(prop => prop.ID == 300001), foreignGroup, true) == null);
        await tutorialFlowPlayer.SceneInstance.EntityLoader!.SyncEntity();
    });
    Check("Rogue scene reconciliation sends no activation for excluded room templates",
        tutorialFlowPlayer.SceneInstance!.Groups.All(capturedTutorialGroups.Contains) &&
        tutorialGroupProbeFrames.Where(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify)
            .Select(frame => SceneGroupRefreshScNotify.Parser.ParseFrom(frame.Body))
            .SelectMany(notify => notify.GroupRefreshList).All(group => capturedTutorialGroups.Contains((int)group.GroupId)));
    await TutorialTalk(403000103);
    Check("reported intermediate tutorial talk is armed and completed only inside its run",
        tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000103) == MissionPhaseEnum.Finish);
    var tutorialTrial = tutorialFlowPlayer.AvatarManager!.GetTrialAvatar(4000004)!;
    tutorialTrial.SetCurHp(7500, true);
    tutorialTrial.SetCurSp(3210, true);
    tutorialFlowPlayer.LineupManager!.GetCurLineup()!.Mp = 2;
    var ownTutorialRoster = tutorialFlowPlayer.LineupManager.GetLineup(tutorialFlowPlayer.LineupManager.Data.CurLineup)!
        .BaseAvatars!.Select(slot => slot.BaseAvatarId).ToArray();
    var tutorialRelogin = new PlayerInstance(tutorialFlowPlayer.Data);
    await tutorialRelogin.InitializeAsync();
    await tutorialRelogin.OnLogin();
    var restoredTutorialLineup = tutorialRelogin.LineupManager!.GetCurLineup()!;
    Check("relogin inside an unfinished tutorial restores the saved story trial roster before scene loading",
        restoredTutorialLineup.LineupType == (int)ExtraLineupType.LineupRogue &&
        restoredTutorialLineup.BaseAvatars!.Select(slot => slot.SpecialAvatarId).SequenceEqual(new[] { 4000004, 4000001, 4000002 }) &&
        tutorialRelogin.SceneInstance!.AvatarInfo.Values.All(avatar => avatar.AvatarType == AvatarType.AvatarTrialType));
    Check("tutorial lineup restoration preserves saved HP, energy and technique points",
        restoredTutorialLineup.Mp == 2 && tutorialRelogin.AvatarManager!.GetTrialAvatar(4000004) is
            { ExtraLineupHp: 7500, ExtraLineupSp: 3210 });
    Check("restoring tutorial trials leaves the player's ordinary squad unchanged",
        tutorialRelogin.LineupManager.GetLineup(tutorialRelogin.LineupManager.Data.CurLineup)!
            .BaseAvatars!.Select(slot => slot.BaseAvatarId).SequenceEqual(ownTutorialRoster));
    // Saves written by the old login path have already selected the ordinary squad; repair that too.
    tutorialRelogin.LineupManager.Data.CurExtraLineup = -1;
    var previouslyResetTutorial = new PlayerInstance(tutorialRelogin.Data);
    await previouslyResetTutorial.InitializeAsync();
    await previouslyResetTutorial.OnLogin();
    Check("a tutorial save already reset to the ordinary squad regains its story trials on login",
        previouslyResetTutorial.LineupManager!.GetCurLineup()!.BaseAvatars!
            .Select(slot => slot.SpecialAvatarId).SequenceEqual(new[] { 4000004, 4000001, 4000002 }));
    var tutorialRoomEntry = previouslyResetTutorial.Data.EntryId;
    await previouslyResetTutorial.LineupManager!.SetExtraLineup(ExtraLineupType.LineupNone, false);
    previouslyResetTutorial.Data.EntryId = RogueManager.RogueEntranceId;
    Check("a saved Rogue run outside its current room cannot replace the lobby's ordinary squad",
        !await previouslyResetTutorial.RogueManager!.RestoreLineupAtLogin() &&
        previouslyResetTutorial.LineupManager.Data.CurExtraLineup == -1);
    previouslyResetTutorial.Data.EntryId = tutorialRoomEntry;
    previouslyResetTutorial.RogueManager!.RogueInstance!.IsWin = true;
    Check("a won Rogue run without a pending tutorial settlement cannot reactivate its extra lineup",
        !await previouslyResetTutorial.RogueManager.RestoreLineupAtLogin() &&
        previouslyResetTutorial.LineupManager.Data.CurExtraLineup == -1);
    previouslyResetTutorial.RogueManager.RogueInstance.IsWin = false;
    previouslyResetTutorial.LineupManager.Data.Lineups.Remove((int)ExtraLineupType.LineupRogue + 10);
    var missingRogueLineup = new PlayerInstance(previouslyResetTutorial.Data);
    await missingRogueLineup.InitializeAsync();
    await missingRogueLineup.OnLogin();
    Check("a missing Rogue lineup is rebuilt from the saved trial ids without healing or refilling energy",
        missingRogueLineup.LineupManager!.GetCurLineup()!.BaseAvatars!
            .Select(slot => slot.SpecialAvatarId).SequenceEqual(new[] { 4000004, 4000001, 4000002 }) &&
        missingRogueLineup.LineupManager.GetCurLineup()!.Mp == 3 &&
        missingRogueLineup.AvatarManager!.GetTrialAvatar(4000004) is { ExtraLineupHp: 7500, ExtraLineupSp: 3210 });
    tutorialFlowPlayer.Connection = null;
    tutorialFlowPlayer = missingRogueLineup;
    tutorialConnection = new ProbeConnection(serverPeer) { Player = tutorialFlowPlayer };
    tutorialFlowPlayer.Connection = tutorialConnection;
    tutorialRun = tutorialFlowPlayer.RogueManager!.RogueInstance!;
    var formalRoguePlayer = new PlayerInstance(60016);
    await formalRoguePlayer.InitializeAsync();
    var formalRogueStart = await formalRoguePlayer.RogueManager!.StartRogue(110, 1, [8001, 1001], false);
    formalRoguePlayer.LineupManager!.GetCurLineup()!.Mp = 1;
    formalRoguePlayer.AvatarManager!.GetFormalAvatar(1001)!.SetCurHp(6500, true);
    formalRoguePlayer.AvatarManager.GetFormalAvatar(1001)!.SetCurSp(2100, true);
    var formalRogueRelogin = new PlayerInstance(formalRoguePlayer.Data);
    await formalRogueRelogin.InitializeAsync();
    await formalRogueRelogin.OnLogin();
    Check("ordinary Rogue resumes its selected formal roster and saved state without substituting tutorial trials",
        formalRogueStart == Retcode.RetSucc && formalRogueRelogin.LineupManager!.GetCurLineup()!.BaseAvatars!
            .Select(slot => (slot.BaseAvatarId, slot.SpecialAvatarId)).SequenceEqual(new[] { (8001, 0), (1001, 0) }) &&
        formalRogueRelogin.LineupManager.GetCurLineup()!.Mp == 1 &&
        formalRogueRelogin.AvatarManager!.GetFormalAvatar(1001) is { ExtraLineupHp: 6500, ExtraLineupSp: 2100 });
    Check("first tutorial starts with all three linked barriers closed",
        tutorialFlowPlayer.SceneInstance!.GetEntitiesInGroup<EntityProp>(10)
            .Count(prop => prop.InstId is 300005 or 300006 or 300007 && prop.State == PropStateEnum.Closed) == 3);
    foreach (var (groupId, subId, talks) in new[]
    {
        (3, 403000104, new[] { 403000105, 403000106 }),
        (4, 403000107, new[] { 403000108, 403000109 }),
        (5, 403000110, new[] { 403000111, 403000112 }),
        (10, 403000113, Array.Empty<int>())
    })
    {
        var monster = tutorialFlowPlayer.SceneInstance!.Entities.Values.OfType<EntityMonster>()
            .Single(entity => entity.GroupId == groupId && entity.InstId == 200001);
        var caster = tutorialFlowPlayer.SceneInstance.AvatarInfo.Values.First();
        var tutorialCast = new SceneCastSkillCsReq
        {
            AttackedByEntityId = (uint)caster.EntityId,
            CastEntityId = (uint)(242 + groupId),
            AssistMonsterEntityIdList = { (uint)monster.EntityId },
            SkillExtraTags = { SkillExtraTag.SceneCastSkillProjectileHit, SkillExtraTag.SceneCastSkillProjectileLifetimeFinish }
        };
        if (groupId == 5)
        {
            // The actual client joins groups 5/11/12 in one fight; all have inst 200001. Those later
            // kills must not satisfy the newly accepted group-10 boss mission.
            var joinedMonsters = tutorialFlowPlayer.SceneInstance.Entities.Values.OfType<EntityMonster>()
                .Where(entity => entity.GroupId is 5 or 11 or 12).Select(entity => (uint)entity.EntityId);
            tutorialCast.AssistMonsterEntityInfo.Add(new AssistMonsterEntityInfo { EntityIdList = { joinedMonsters } });
        }
        var tutorialBattleFrames = await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Scene.HandlerSceneCastSkillCsReq()
            .OnHandle(tutorialConnection, [], tutorialCast.ToByteArray()));
        var tutorialBattleRsp = SceneCastSkillScRsp.Parser.ParseFrom(
            tutorialBattleFrames.Single(frame => frame.Id == CmdIds.SceneCastSkillScRsp).Body);
        Check($"tutorial group {groupId} actual cast response enters combat with the three story avatars",
            tutorialBattleRsp.Retcode == 0 && tutorialBattleRsp.BattleInfo?.BattleAvatarList.Count == 3 &&
            tutorialBattleRsp.BattleInfo.BattleAvatarList.All(avatar => avatar.AvatarType == AvatarType.AvatarTrialType));
        if (groupId == 3)
        {
            // Official 4.6 seq 15037 has RogueMode 1, an empty action-event message, and no trial light cones.
            Check("first tutorial battle carries the captured classic Rogue mode instead of ordinary-world metadata",
                tutorialBattleRsp.BattleInfo?.StageId == 80010011 &&
                tutorialBattleRsp.BattleInfo.BattleRogueMagicInfo?.OOPCPMANCBO?.RogueMode == 1);
            Check("tutorial battle includes the captured empty action-event message",
                tutorialBattleRsp.BattleInfo?.BattleActionEventId != null);
            Check("unequipped story trials do not serialize invalid equipment id zero",
                tutorialBattleRsp.BattleInfo!.BattleAvatarList.All(avatar => avatar.EquipmentList.Count == 0));
            var equippedTrial = tutorialFlowPlayer.AvatarManager!.GetTrialAvatar(4000004)!;
            var trialPath = equippedTrial.GetCurPathInfo();
            var previousEquipment = trialPath.EquipData;
            try
            {
                trialPath.EquipData = new March7thHoney.Database.Inventory.ItemData { ItemId = 20001, Level = 1, Rank = 1 };
                var equippedBattleAvatar = equippedTrial.ToBattleProto(new March7thHoney.Database.Avatar.PlayerDataCollection(tutorialFlowPlayer.Data,
                    tutorialFlowPlayer.InventoryManager!.Data, tutorialFlowPlayer.LineupManager!.GetCurLineup()!), AvatarType.AvatarTrialType);
                Check("a valid trial light cone remains in the battle response",
                    equippedBattleAvatar.EquipmentList.Count == 1 && equippedBattleAvatar.EquipmentList[0].Id == 20001);
            }
            finally
            {
                trialPath.EquipData = previousEquipment;
            }
        }
        var tutorialVictoryFrames = await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Battle.HandlerPVEBattleResultCsReq()
            .OnHandle(tutorialConnection, [], new PVEBattleResultCsReq
                { EndStatus = BattleEndStatus.BattleEndWin, Stt = new() }.ToByteArray()));
        Check($"tutorial group {groupId} actual battle settlement receives a successful response",
            PVEBattleResultScRsp.Parser.ParseFrom(tutorialVictoryFrames
                .Single(frame => frame.Id == CmdIds.PVEBattleResultScRsp).Body).Retcode == 0);
        Check($"tutorial battle in group {groupId} completes its kill gate {subId}",
            tutorialFlowPlayer.MissionManager!.GetSubMissionStatus(subId) == MissionPhaseEnum.Finish);
        var expectedMarker = groupId == 10 ? 300002 : 300001;
        var openedTutorialProps = tutorialVictoryFrames.Where(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify)
            .Select(frame => SceneGroupRefreshScNotify.Parser.ParseFrom(frame.Body))
            .SelectMany(notify => notify.GroupRefreshList)
            .Where(group => group.RefreshType == SceneGroupRefreshType.Afibfmafncc)
            .SelectMany(group => group.RefreshEntity).Select(refresh => refresh.AddEntity)
            .Where(entity => entity?.Prop?.PropState == (uint)PropStateEnum.Open).ToList();
        Check($"tutorial group {groupId} victory sends its cleared marker Open as an entity delta",
            openedTutorialProps.Any(entity => entity.GroupId == groupId && entity.InstId == expectedMarker));
        Check($"tutorial group {groupId} only wins the run after the final encounter",
            tutorialRun.IsWin == (groupId == 10));
        if (groupId == 5)
        {
            Check("joined tutorial encounter does not complete or open the living boss's kill gate",
                tutorialFlowPlayer.MissionManager!.GetSubMissionStatus(403000113) == MissionPhaseEnum.Accept &&
                tutorialFlowPlayer.SceneInstance!.GetEntitiesInGroup<EntityMonster>(10).Any(entity => entity.IsAlive) &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(10)
                    .Single(prop => prop.InstId == 300002).State == PropStateEnum.Closed);
            var defeatedMonsterGroup = monster.GroupId;
            var defeatedMonsterScene = monster.Scene;
            try
            {
                // Same group/instance on a different floor must not satisfy a global mission event.
                monster.GroupId = 10;
                monster.Scene = new SceneInstance(tutorialFlowPlayer, defeatedMonsterScene.Excel, 80001002, 8000102);
                await new March7thHoney.GameServer.Game.Mission.FinishType.Handler.MissionHandlerKillMonster()
                    .HandleMissionFinishType(tutorialFlowPlayer,
                        tutorialFlowPlayer.MissionManager!.GetSubMissionInfo(403000113)!, monster);
                Check("a defeated monster on another floor cannot finish the tutorial boss gate",
                    tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000113) == MissionPhaseEnum.Accept);
            }
            finally
            {
                monster.GroupId = defeatedMonsterGroup;
                monster.Scene = defeatedMonsterScene;
            }
        }
        var exploreWinIndex = tutorialVictoryFrames.FindIndex(frame => frame.Id == CmdIds.SyncRogueExploreWinScNotify);
        if (groupId != 10)
            Check($"tutorial group {groupId} has no final victory notification", exploreWinIndex < 0);
        else
        {
            var finishRoomIndex = tutorialVictoryFrames.FindIndex(frame => frame.Id == CmdIds.SyncRogueMapRoomScNotify &&
                SyncRogueMapRoomScNotify.Parser.ParseFrom(frame.Body).HNEANMNJHDB?.CAGDNHCOCKM == RogueRoomStatus.Finish);
            var battleResultIndex = tutorialVictoryFrames.FindIndex(frame => frame.Id == CmdIds.PVEBattleResultScRsp);
            Check("tutorial boss sends room Finish then exploration victory before the battle response",
                finishRoomIndex >= 0 && exploreWinIndex > finishRoomIndex && battleResultIndex > exploreWinIndex &&
                SyncRogueExploreWinScNotify.Parser.ParseFrom(tutorialVictoryFrames[exploreWinIndex].Body).ILOFGCAFHAH);
            Check("tutorial boss completion has no extra blessing draft",
                tutorialRun.BuffSelect?.HasSelection != true &&
                tutorialVictoryFrames.All(frame => frame.Id != CmdIds.SyncRogueCommonPendingActionScNotify));
        }
        if (groupId != 10)
        {
            var barrierId = 300002 + groupId;
            var barrierRefreshIndex = tutorialVictoryFrames.FindIndex(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify &&
                SceneGroupRefreshScNotify.Parser.ParseFrom(frame.Body).GroupRefreshList
                    .SelectMany(group => group.RefreshEntity).Any(refresh => refresh.AddEntity is { GroupId: 10 } entity &&
                        entity.InstId == barrierId && entity.Prop?.PropState == (uint)PropStateEnum.Open));
            var draftIndex = tutorialVictoryFrames.FindIndex(frame => frame.Id == CmdIds.SyncRogueCommonPendingActionScNotify);
            Check($"tutorial group {groupId} opens the linked barrier before offering blessings",
                barrierRefreshIndex >= 0 && draftIndex > barrierRefreshIndex);
            var barrierPosition = groupId switch { 3 => (-23855, 216150), 4 => (-23860, 186930), _ => (-11750, 157340) };
            var barriersAtExit = TutorialBarriersAt(barrierPosition.Item1, barrierPosition.Item2);
            Check($"tutorial group {groupId} exit snapshot has one Open barrier and no Closed overlap",
                barriersAtExit.Count == 1 && barriersAtExit.All(entity => entity.Prop.PropState == (uint)PropStateEnum.Open));
            Check($"tutorial group {groupId} leaves later and other barrier props at their initial state",
                tutorialFlowPlayer.SceneInstance!.GetEntitiesInGroup<EntityProp>(10)
                    .Where(prop => prop.InstId is 300005 or 300006 or 300007 or 300010 or 300011)
                    .All(prop => prop.State == (prop.InstId <= barrierId ? PropStateEnum.Open : PropStateEnum.Closed)));
        }
        if (tutorialRun.BuffSelect?.HasSelection == true)
        {
            var tutorialPickedBuff = tutorialRun.BuffSelect.SelectBuffs[0].MazeBuffID;
            var tutorialPickFrames = await SentFrames(() => new HandlerHandleRogueCommonPendingActionCsReq()
                .OnHandle(tutorialConnection, [], new OFEDNNJIJFL
                {
                    MPLGEEGOAMC = (uint)tutorialRun.ActionSeq,
                    DLKIKBIDJDB = new BPLNKHGAILK { BAOFIGFFMLN = (uint)tutorialPickedBuff }
                }.ToByteArray()));
            Check($"tutorial group {groupId} blessing pick resolves through the actual pending-action handler",
                tutorialRun.BuffSelect?.HasSelection != true && tutorialRun.Buffs.ContainsKey(tutorialPickedBuff) &&
                tutorialPickFrames.Any(frame => frame.Id == CmdIds.HandleRogueCommonPendingActionScRsp));
        }
        foreach (var talk in talks) await TutorialTalk(talk);
        if (groupId == 3)
        {
            // Simulate the old premature IsWin flag without changing tasks, roster or blessings.
            tutorialRun.IsWin = true;
            tutorialFlowPlayer.RogueManager!.RogueData.RunBlob =
                Convert.ToBase64String(MemoryPackSerializer.Serialize(tutorialRun));
            var clearedTutorialRelogin = new PlayerInstance(tutorialFlowPlayer.Data);
            await clearedTutorialRelogin.InitializeAsync();
            await clearedTutorialRelogin.OnLogin();
            Check("relogin repairs the old premature tutorial victory without losing selected blessings",
                clearedTutorialRelogin.RogueManager!.RogueInstance is { IsWin: false } resumed &&
                resumed.Buffs.Count == tutorialRun.Buffs.Count && resumed.Coin == tutorialRun.Coin);
            Check("relogin keeps the cleared tutorial monster absent and its marker and barrier open",
                !clearedTutorialRelogin.SceneInstance!.GetEntitiesInGroup<EntityMonster>(3).Any() &&
                clearedTutorialRelogin.SceneInstance.GetEntitiesInGroup<EntityProp>(3)
                    .Single(prop => prop.InstId == 300001).State == PropStateEnum.Open &&
                clearedTutorialRelogin.SceneInstance.GetEntitiesInGroup<EntityProp>(10)
                    .Single(prop => prop.InstId == 300005).State == PropStateEnum.Open &&
                clearedTutorialRelogin.SceneInstance.GetEntitiesInGroup<EntityProp>(10)
                    .Where(prop => prop.InstId is 300006 or 300007).All(prop => prop.State == PropStateEnum.Closed));
            Check("tutorial relogin snapshot never restores overlapping room templates",
                clearedTutorialRelogin.SceneInstance!.ToProto().EntityGroupList.Where(group => group.GroupId != 0)
                    .Select(group => (int)group.GroupId).ToHashSet().SetEquals(capturedTutorialGroups));
            if (clearedTutorialRelogin.RogueManager!.RogueInstance is { } resumedTutorialRun)
            {
                tutorialFlowPlayer = clearedTutorialRelogin;
                tutorialConnection = new ProbeConnection(serverPeer) { Player = tutorialFlowPlayer };
                tutorialFlowPlayer.Connection = tutorialConnection;
                tutorialRun = resumedTutorialRun;
            }
            else tutorialRun.IsWin = false;
        }
    }
    Check("tutorial victory waits for settlement rather than completing the main automatically",
        tutorialRun.IsWin && tutorialFlowPlayer.MissionManager!.GetSubMissionStatus(403000114) == MissionPhaseEnum.Accept
        && tutorialFlowPlayer.MissionManager.GetMainMissionStatus(4030001) == MissionPhaseEnum.Accept);
    await SentFrames(async () => await tutorialFlowPlayer.MissionManager!.HandleFinishType(
        MissionFinishTypeEnum.RoguePassAreaProgress,
        new RogueInstance { AreaId = 100, Status = RogueStatus.Finish, IsWin = false }));
    Check("a lost or abandoned run cannot pass the tutorial's area completion gate",
        tutorialFlowPlayer.MissionManager!.GetSubMissionStatus(403000114) == MissionPhaseEnum.Accept);
    // The old build saved a winning tutorial but never notified the client to quit/settle it.
    tutorialRun.GetCurrentRoom()!.Status = RogueRoomStatus.Play;
    tutorialFlowPlayer.RogueManager!.RogueData.RunBlob = Convert.ToBase64String(MemoryPackSerializer.Serialize(tutorialRun));
    var pendingTutorialRelogin = new PlayerInstance(tutorialFlowPlayer.Data);
    await pendingTutorialRelogin.InitializeAsync();
    await pendingTutorialRelogin.OnLogin();
    var pendingTutorialRun = pendingTutorialRelogin.RogueManager!.RogueInstance;
    Check("a winning tutorial awaiting settlement survives relogin with its room marked Finish",
        pendingTutorialRun is { IsWin: true } && pendingTutorialRun.GetCurrentRoom()?.Status == RogueRoomStatus.Finish &&
        pendingTutorialRun.Buffs.Count == tutorialRun.Buffs.Count &&
        pendingTutorialRelogin.MissionManager!.GetSubMissionStatus(403000114) == MissionPhaseEnum.Accept);
    Check("relogin into a completed tutorial room does not respawn any encounter",
        !pendingTutorialRelogin.SceneInstance!.Entities.Values.OfType<EntityMonster>().Any());
    Check("the pending tutorial settlement retains its trial squad until leaving for the lobby",
        pendingTutorialRelogin.LineupManager!.GetCurLineup()!.BaseAvatars!
            .Select(slot => slot.SpecialAvatarId).SequenceEqual(new[] { 4000004, 4000001, 4000002 }));
    if (pendingTutorialRun != null)
    {
        tutorialFlowPlayer = pendingTutorialRelogin;
        tutorialConnection = new ProbeConnection(serverPeer) { Player = tutorialFlowPlayer };
        tutorialFlowPlayer.Connection = tutorialConnection;
        tutorialRun = pendingTutorialRun;
        var resumeVictoryFrames = await SentFrames(() => new HandlerGetRogueInfoCsReq().OnHandle(tutorialConnection, [], []));
        Check("querying a recovered tutorial replays the missing final victory without settling or rewarding it",
            resumeVictoryFrames.Count(frame => frame.Id == CmdIds.SyncRogueExploreWinScNotify) == 1 &&
            resumeVictoryFrames.Any(frame => frame.Id == CmdIds.SyncRogueMapRoomScNotify) &&
            resumeVictoryFrames.All(frame => frame.Id != CmdIds.SyncRogueFinishScNotify && frame.Id != CmdIds.MissionRewardScNotify));
        var repeatVictoryFrames = await SentFrames(() => new HandlerGetRogueInfoCsReq().OnHandle(tutorialConnection, [], []));
        Check("repeated Rogue info queries do not replay the tutorial completion notification",
            repeatVictoryFrames.All(frame => frame.Id != CmdIds.SyncRogueExploreWinScNotify));
    }
    var tutorialQuitFrames = await SentFrames(() => new HandlerQuitRogueCsReq().OnHandle(tutorialConnection, [], []));
    Check("tutorial quit preserves victory for the subsequent settlement",
        tutorialRun is { IsWin: true, Status: RogueStatus.Finish } &&
        tutorialQuitFrames.Any(frame => frame.Id == CmdIds.QuitRogueScRsp));
    var tutorialLeaveFrames = await SentFrames(() => new HandlerLeaveRogueCsReq().OnHandle(tutorialConnection, [], []));
    Check("settled tutorial victory returns to the lobby and advances to Herta's final talk",
        tutorialFlowPlayer.Data.FloorId == 80112001
        && tutorialFlowPlayer.MissionManager!.GetSubMissionStatus(403000114) == MissionPhaseEnum.Finish
        && tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000116) == MissionPhaseEnum.Accept
        && tutorialLeaveFrames.Any(frame => frame.Id == CmdIds.LeaveRogueScRsp));
    var tutorialFinalTalk = await TutorialTalk(403000116);
    Check("Herta's final tutorial talk completes main 4030001 and grants its reward once",
        tutorialFlowPlayer.MissionManager!.GetMainMissionStatus(4030001) == MissionPhaseEnum.Finish
        && tutorialFinalTalk.Count(frame => frame.Id == CmdIds.MissionRewardScNotify) == 1);
    var duplicateTutorialTalk = await TutorialTalk(403000116);
    Check("repeating the completed tutorial talk grants no reward",
        duplicateTutorialTalk.All(frame => frame.Id != CmdIds.MissionRewardScNotify));
    await SentFrames(async () => await tutorialFlowPlayer.MissionManager.AcceptMainMission(4030002));
    Check("second Rogue tutorial remains active rather than auto-finishing on acceptance",
        tutorialFlowPlayer.MissionManager.GetMainMissionStatus(4030002) == MissionPhaseEnum.Accept);
    var secondTutorialFrames = await SentFrames(() => new HandlerStartRogueCsReq()
        .OnHandle(tutorialConnection, [], new StartRogueCsReq { AreaId = 100,
            UnfinishedStoryLineIdList = { 4000004, 4000001, 4000002 } }.ToByteArray()));
    var secondTutorialRsp = secondTutorialFrames.Where(frame => frame.Id == CmdIds.StartRogueScRsp)
        .Select(frame => StartRogueScRsp.Parser.ParseFrom(frame.Body)).Single();
    Check("second tutorial starts area 100 on map 2 instead of the formal area selection flow",
        secondTutorialRsp.Retcode == 0 && secondTutorialRsp.Scene.FloorId == 80001002
        && tutorialFlowPlayer.RogueManager!.RogueInstance is { AreaId: 100, MapId: 2, Trial: true }
        && tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000202) == MissionPhaseEnum.Finish);
    tutorialRun = tutorialFlowPlayer.RogueManager!.RogueInstance!;
    await TutorialTalk(403000203);
    async Task ResolveRoamingDrafts()
    {
        for (var i = 0; i < 5 && tutorialRun.BuffSelect?.HasSelection == true; i++)
        {
            var picked = tutorialRun.BuffSelect.SelectBuffs[0].MazeBuffID;
            var frames = await SentFrames(() => new HandlerHandleRogueCommonPendingActionCsReq()
                .OnHandle(tutorialConnection, [], new OFEDNNJIJFL
                {
                    MPLGEEGOAMC = (uint)tutorialRun.ActionSeq,
                    DLKIKBIDJDB = new BPLNKHGAILK { BAOFIGFFMLN = (uint)picked }
                }.ToByteArray()));
            Check("roaming blessing response resolves the selected draft",
                frames.Any(frame => frame.Id == CmdIds.HandleRogueCommonPendingActionScRsp) && tutorialRun.Buffs.ContainsKey(picked));
        }
        Check("roaming pending drafts finish without an orphan selection", tutorialRun.BuffSelect?.HasSelection != true);
    }
    async Task RelogRoamingTutorial()
    {
        var relog = new PlayerInstance(tutorialFlowPlayer.Data);
        await relog.InitializeAsync();
        await relog.OnLogin();
        Check("roaming run and selected blessings survive relogin",
            relog.RogueManager!.RogueInstance is { } restored && restored.MapId == 2 &&
            restored.CurrentSiteId == tutorialRun.CurrentSiteId && restored.Buffs.Count == tutorialRun.Buffs.Count);
        tutorialFlowPlayer = relog;
        tutorialConnection = new ProbeConnection(serverPeer) { Player = relog };
        relog.Connection = tutorialConnection;
        tutorialRun = relog.RogueManager!.RogueInstance!;
    }
    int? roamingClientAttackEntityId = null;
    foreach (var (groupId, monsterId, missionId, stageId, barrierGroup, barrierId) in new[]
    {
        (3, 200002, 403000204, 80020011, 19, 300004),
        (4, 200001, 403000209, 80020021, 19, 0),
        (19, 200005, 403000210, 80020031, 19, 0),
        (6, 200001, 403000212, 80020041, 18, 300008),
        (7, 200001, 403000214, 80020051, 18, 300009),
        (18, 200005, 403000217, 80020061, 18, 0),
        (29, 200001, 403000219, 80021011, 29, 0)
    })
    {
        var roamingMonster = tutorialFlowPlayer.SceneInstance!.GetEntitiesInGroup<EntityMonster>(groupId)
            .Single(monster => monster.InstId == monsterId);
        var castFrames = await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Scene.HandlerSceneCastSkillCsReq()
            .OnHandle(tutorialConnection, [], new SceneCastSkillCsReq
            {
                AttackedByEntityId = (uint)(roamingClientAttackEntityId ?? tutorialFlowPlayer.SceneInstance.AvatarInfo.Values.First().EntityId),
                CastEntityId = (uint)(900 + groupId),
                AssistMonsterEntityIdList = { (uint)roamingMonster.EntityId },
                SkillExtraTags = { SkillExtraTag.SceneCastSkillProjectileHit, SkillExtraTag.SceneCastSkillProjectileLifetimeFinish }
            }.ToByteArray()));
        var battleRsp = SceneCastSkillScRsp.Parser.ParseFrom(castFrames.Single(frame => frame.Id == CmdIds.SceneCastSkillScRsp).Body);
        if (roamingClientAttackEntityId != null)
        {
            Check($"roaming group {groupId} starts a battle using the client avatar ID retained across room changes",
                battleRsp.BattleInfo != null && battleRsp.BattleInfo.StageId == stageId);
            // Let a failing regression continue through the later encounters to report all failures.
            if (battleRsp.BattleInfo == null)
            {
                var retry = await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Scene.HandlerSceneCastSkillCsReq()
                    .OnHandle(tutorialConnection, [], new SceneCastSkillCsReq
                    {
                        AttackedByEntityId = (uint)tutorialFlowPlayer.SceneInstance.LeaderEntityId,
                        CastEntityId = (uint)(1900 + groupId),
                        AssistMonsterEntityIdList = { (uint)roamingMonster.EntityId }
                    }.ToByteArray()));
                battleRsp = SceneCastSkillScRsp.Parser.ParseFrom(retry.Single(frame => frame.Id == CmdIds.SceneCastSkillScRsp).Body);
            }
            roamingClientAttackEntityId = null;
        }
        Check($"roaming group {groupId} battle slots and the team-leader buff index stay within the trial roster",
            battleRsp.BattleInfo != null && battleRsp.BattleInfo.BattleAvatarList.Select(avatar => avatar.Index)
                .SequenceEqual(Enumerable.Range(0, battleRsp.BattleInfo.BattleAvatarList.Count).Select(index => (uint)index)) &&
            battleRsp.BattleInfo.BuffList.Where(buff => buff.Id == 1000111)
                .All(buff => buff.OwnerIndex < battleRsp.BattleInfo.BattleAvatarList.Count) &&
            battleRsp.BattleInfo.BuffList.All(buff => buff.TargetIndexList.All(index => index < battleRsp.BattleInfo.BattleAvatarList.Count)));
        Check($"roaming group {groupId} enters its captured stage with the current story trials",
            battleRsp.Retcode == 0 && battleRsp.BattleInfo != null && battleRsp.BattleInfo.StageId == stageId &&
            battleRsp.BattleInfo.BattleAvatarList.Count == (groupId is 3 or 4 ? 3 : 4) &&
            battleRsp.BattleInfo.BattleAvatarList.All(avatar => avatar.AvatarType == AvatarType.AvatarTrialType));
        var winFrames = await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Battle.HandlerPVEBattleResultCsReq()
            .OnHandle(tutorialConnection, [], new PVEBattleResultCsReq
                { EndStatus = BattleEndStatus.BattleEndWin, Stt = new() }.ToByteArray()));
        Check($"roaming group {groupId} completes only its matching kill mission",
            tutorialFlowPlayer.MissionManager!.GetSubMissionStatus(missionId) == MissionPhaseEnum.Finish && tutorialRun.IsWin == (groupId == 29));
        bool RefreshedOpen(int group, int instance) => winFrames.Where(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify)
            .Select(frame => SceneGroupRefreshScNotify.Parser.ParseFrom(frame.Body)).SelectMany(notify => notify.GroupRefreshList)
            .SelectMany(delta => delta.RefreshEntity).Any(refresh => refresh.AddEntity is { } entity &&
                entity.GroupId == group && entity.InstId == instance && entity.Prop?.PropState == (uint)PropStateEnum.Open);
        Check($"roaming group {groupId} sends its cleared marker Open",
            RefreshedOpen(groupId, groupId == 29 ? 300002 : 300001));
        if (barrierId != 0)
        {
            var barrier = tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(barrierGroup).Single(prop => prop.InstId == barrierId);
            var overlapping = tutorialFlowPlayer.SceneInstance.ToProto().EntityGroupList.SelectMany(group => group.EntityList)
                .Where(entity => entity.Prop?.PropId == 100006 && entity.Motion.Pos.X == barrier.Position.X &&
                    entity.Motion.Pos.Z == barrier.Position.Z).ToList();
            Check($"roaming group {groupId} opens one linked barrier without a Closed overlap",
                RefreshedOpen(barrierGroup, barrierId) && overlapping.Count == 1 &&
                overlapping.All(entity => entity.Prop.PropState == (uint)PropStateEnum.Open));
        }
        if (groupId == 4)
            Check("roaming group-4 victory keeps the pick-machine wall Closed until Herta is chosen",
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(19).Single(prop => prop.InstId == 300005).State == PropStateEnum.Closed &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(4).Single(prop => prop.InstId == 300002).State == PropStateEnum.Locked);
        if (groupId is 19 or 18)
            Check("the final normal encounter finishes the room but keeps the exit Closed until its draft resolves",
                tutorialRun.GetCurrentRoom()!.Status == RogueRoomStatus.Finish && !RefreshedOpen(groupId, 300002) &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(groupId).Single(prop => prop.InstId == 300002).State == PropStateEnum.Closed);
        if (groupId != 29)
        {
            Check($"roaming group {groupId} does not send final exploration victory",
                winFrames.All(frame => frame.Id != CmdIds.SyncRogueExploreWinScNotify));
            await ResolveRoamingDrafts();
        }
        if (groupId is 3 or 4 or 6 or 7)
        {
            var roomGroup = groupId is 3 or 4 ? 19 : 18;
            Check($"roaming group {groupId} does not prematurely open the room exit",
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(roomGroup).Single(prop => prop.InstId == 300002).State == PropStateEnum.Closed);
            var siteBefore = tutorialRun.CurrentSiteId;
            var earlyEnter = await SentFrames(() => new HandlerEnterRogueMapRoomCsReq().OnHandle(tutorialConnection, [],
                new EnterRogueMapRoomCsReq { MAJKFIMPOOF = (uint)(siteBefore + 1) }.ToByteArray()));
            Check("an unfinished tutorial room rejects early room-entry requests without changing the run",
                tutorialRun.CurrentSiteId == siteBefore && EnterRogueMapRoomScRsp.Parser.ParseFrom(
                    earlyEnter.Single(frame => frame.Id == CmdIds.EnterRogueMapRoomScRsp).Body).Retcode != 0);
        }
        if (groupId == 3)
        {
            await TutorialTalk(403000205);
            await TutorialTalk(403000215);
            await RelogRoamingTutorial();
            Check("relogin restores the cleared first roaming encounter and wall without respawning the monster",
                !tutorialFlowPlayer.SceneInstance!.GetEntitiesInGroup<EntityMonster>(3).Any() &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(19).Single(prop => prop.InstId == 300004).State == PropStateEnum.Open &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(19).Single(prop => prop.InstId == 300005).State == PropStateEnum.Closed);
        }
        if (groupId == 4)
        {
            await TutorialTalk(403000206);
            var pickMachine = tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(4).Single(prop => prop.InstId == 300002);
            Check("the resolved second blessing enables the trial picker and accepts its prop-state mission",
                pickMachine.State == PropStateEnum.Closed && tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000207) == MissionPhaseEnum.Accept);
            var invalidPick = await SentFrames(() => new HandlerPickRogueAvatarCsReq().OnHandle(tutorialConnection, [],
                new PickRogueAvatarCsReq { PropEntityId = (uint)pickMachine.EntityId, DFPPCAJDBFK = { 4000001 } }.ToByteArray()));
            Check("the tutorial picker rejects another trial without opening the wall or changing the saved roster",
                PickRogueAvatarScRsp.Parser.ParseFrom(invalidPick.Single(frame => frame.Id == CmdIds.PickRogueAvatarScRsp).Body).Retcode != 0 &&
                tutorialRun.BaseAvatarIds.Count == 3 && pickMachine.State == PropStateEnum.Closed);
            // Official seq 28667 replaces the lineup before the pick answer (seq 28671).
            var replace = new ReplaceLineupCsReq { ExtraLineupType = ExtraLineupType.LineupRogue };
            foreach (var (id, slot) in new[] { (4000004, 0), (4000001, 1), (4000002, 2), (4000005, 3) })
                replace.LineupSlotList.Add(new LineupSlotData { Id = (uint)id, Slot = (uint)slot });
            await SentFrames(() => new March7thHoney.GameServer.Server.Packet.Recv.Lineup.HandlerReplaceLineupCsReq()
                .OnHandle(tutorialConnection, [], replace.ToByteArray()));
            var pickReq = new PickRogueAvatarCsReq { PropEntityId = (uint)pickMachine.EntityId, DFPPCAJDBFK = { 4000005 } };
            var roamingAvatarPickFrames = await SentFrames(() => new HandlerPickRogueAvatarCsReq().OnHandle(tutorialConnection, [], pickReq.ToByteArray()));
            Check("choosing Herta opens the pick machine and its wall, advances the mission and saves her trial id",
                tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000207) == MissionPhaseEnum.Finish &&
                tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000210) == MissionPhaseEnum.Accept &&
                pickMachine.State == PropStateEnum.Open && tutorialRun.BaseAvatarIds.SequenceEqual(new[] { 4000004, 4000001, 4000002, 4000005 }) &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(19).Single(prop => prop.InstId == 300005).State == PropStateEnum.Open &&
                PickRogueAvatarScRsp.Parser.ParseFrom(roamingAvatarPickFrames.Single(frame => frame.Id == CmdIds.PickRogueAvatarScRsp).Body).Retcode == 0);
            var repeatPick = await SentFrames(() => new HandlerPickRogueAvatarCsReq().OnHandle(tutorialConnection, [], pickReq.ToByteArray()));
            Check("repeating the valid trial pick does not re-open props or duplicate the saved avatar",
                repeatPick.All(frame => frame.Id != CmdIds.SceneGroupRefreshScNotify) && tutorialRun.BaseAvatarIds.Count == 4);
            await RelogRoamingTutorial();
            Check("relogin retains Herta and the picker wall while leaving the final monster alive",
                tutorialFlowPlayer.LineupManager!.GetCurLineup()!.BaseAvatars!.Count == 4 &&
                tutorialFlowPlayer.SceneInstance!.GetEntitiesInGroup<EntityProp>(19).Single(prop => prop.InstId == 300005).State == PropStateEnum.Open &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(4).Single(prop => prop.InstId == 300002).State == PropStateEnum.Open &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityMonster>(19).Count == 1);
        }
        if (groupId == 6)
        {
            await TutorialTalk(403000213);
            await TutorialTalk(403000216);
        }
        if (groupId is 19 or 18)
        {
            var finishMessages = winFrames.Where(frame => frame.Id == CmdIds.SyncRogueMapRoomScNotify)
                .Select(frame => SyncRogueMapRoomScNotify.Parser.ParseFrom(frame.Body)).ToList();
            Check($"roaming room {tutorialRun.GetCurrentRoom()!.RoomId} finishes and unlocks its successor after the final encounter",
                finishMessages.Any(notify => notify.HNEANMNJHDB.CAGDNHCOCKM == RogueRoomStatus.Finish) &&
                finishMessages.Any(notify => notify.HNEANMNJHDB.CAGDNHCOCKM == RogueRoomStatus.Unlock) &&
                tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(groupId).Single(prop => prop.InstId == 300002).State == PropStateEnum.Open);
            if (groupId == 19)
            {
                await RelogRoamingTutorial();
                Check("a cleared roaming room reloads with its portal open and no defeated monsters",
                    !tutorialFlowPlayer.SceneInstance!.Entities.Values.OfType<EntityMonster>().Any() &&
                    tutorialFlowPlayer.SceneInstance.GetEntitiesInGroup<EntityProp>(19).Single(prop => prop.InstId == 300002).State == PropStateEnum.Open);
            }
            var precedingScene = tutorialFlowPlayer.SceneInstance!;
            var precedingLeaderId = precedingScene.LeaderEntityId;
            var precedingAvatarIds = precedingScene.AvatarInfo.Values.ToDictionary(avatar => avatar.AvatarInfo.BaseAvatarId, avatar => avatar.EntityId);
            var enterFrames = await SentFrames(() => new HandlerEnterRogueMapRoomCsReq().OnHandle(tutorialConnection, [],
                new EnterRogueMapRoomCsReq { MAJKFIMPOOF = (uint)(tutorialRun.CurrentSiteId + 1) }.ToByteArray()));
            var entered = EnterRogueMapRoomScRsp.Parser.ParseFrom(enterFrames.Single(frame => frame.Id == CmdIds.EnterRogueMapRoomScRsp).Body);
            Check("same-floor Rogue room changes retain every client avatar ID and never reuse old IDs for room props",
                entered.Scene.LeaderEntityId == precedingLeaderId &&
                tutorialFlowPlayer.SceneInstance!.AvatarInfo.Values.All(avatar => precedingAvatarIds.GetValueOrDefault(avatar.AvatarInfo.BaseAvatarId) == avatar.EntityId) &&
                tutorialFlowPlayer.SceneInstance.Entities.Values.Where(entity => entity is not AvatarSceneInfo)
                    .All(entity => entity.EntityId > precedingScene.LastEntityId));
            roamingClientAttackEntityId = precedingLeaderId;
            Check("roaming room transition updates the room mission and only loads its own room groups",
                entered.Retcode == 0 && tutorialFlowPlayer.MissionManager.GetSubMissionStatus(groupId == 19 ? 403000211 : 403000218) == MissionPhaseEnum.Finish &&
                entered.Scene.EntityGroupList.Where(group => group.GroupId != 0).Select(group => (int)group.GroupId).ToHashSet()
                    .SetEquals(groupId == 19 ? new[] { 6, 7, 18 } : new[] { 29 }));
        }
        if (groupId == 29)
        {
            Check("roaming boss opens the final door and sends exploration victory before the battle response without a draft",
                RefreshedOpen(29, 300004) && tutorialRun.GetCurrentRoom()!.Status == RogueRoomStatus.Finish &&
                tutorialRun.BuffSelect?.HasSelection != true &&
                winFrames.FindIndex(frame => frame.Id == CmdIds.SyncRogueExploreWinScNotify) >= 0 &&
                winFrames.FindIndex(frame => frame.Id == CmdIds.SyncRogueExploreWinScNotify) < winFrames.FindIndex(frame => frame.Id == CmdIds.PVEBattleResultScRsp));
        }
    }
    await RelogRoamingTutorial();
    var roamingResumeFrames = await SentFrames(() => new HandlerGetRogueInfoCsReq().OnHandle(tutorialConnection, [], []));
    Check("an unsettled roaming victory survives relogin and replays the client completion notification",
        tutorialRun.IsWin && !tutorialFlowPlayer.SceneInstance!.Entities.Values.OfType<EntityMonster>().Any() &&
        roamingResumeFrames.Count(frame => frame.Id == CmdIds.SyncRogueExploreWinScNotify) == 1);
    await SentFrames(() => new HandlerQuitRogueCsReq().OnHandle(tutorialConnection, [], []));
    await SentFrames(() => new HandlerLeaveRogueCsReq().OnHandle(tutorialConnection, [], []));
    Check("roaming settlement completes its pass-area gate and accepts the final lobby talk",
        tutorialFlowPlayer.Data.FloorId == 80112001 && tutorialFlowPlayer.MissionManager!.GetSubMissionStatus(403000220) == MissionPhaseEnum.Finish &&
        tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000221) == MissionPhaseEnum.Accept);
    List<EntityProp> RoamingGalleryArms(PlayerInstance owner) => owner.SceneInstance!.GetEntitiesInGroup<EntityProp>(3)
        .Where(prop => prop.Excel.ID == 1005 && prop.InstId is 300003 or 300008).ToList();
    Check("both gallery arms remain Closed before the final Herta dialogue",
        RoamingGalleryArms(tutorialFlowPlayer) is { Count: 2 } galleryBefore && galleryBefore.All(prop => prop.State == PropStateEnum.Closed));
    // Official seq 29407-29412 opens both real arms (3/300003 and 3/300008), not the quest's dummy UI target.
    var galleryTalkFrames = await TutorialTalk(403000221);
    Check("the use-handbook objective opens both gallery arms while adventure 4030002 is still running",
        tutorialFlowPlayer.MissionManager.GetMainMissionStatus(4030002) == MissionPhaseEnum.Accept &&
        tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000222) == MissionPhaseEnum.Accept &&
        RoamingGalleryArms(tutorialFlowPlayer) is { Count: 2 } galleryOpened && galleryOpened.All(prop => prop.State == PropStateEnum.Open));
    var galleryUpdates = galleryTalkFrames.Where(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify)
        .SelectMany(frame => SceneGroupRefreshScNotify.Parser.ParseFrom(frame.Body).GroupRefreshList)
        .Where(group => group.GroupId == 3).SelectMany(group => group.RefreshEntity)
        .Where(refresh => refresh.AddEntity is { Prop.PropId: 1005, Prop.PropState: 1 }).Select(refresh => refresh.AddEntity).ToList();
    var galleryObjectiveAt = galleryTalkFrames.FindIndex(frame => frame.Id == CmdIds.PlayerSyncScNotify &&
        PlayerSyncScNotify.Parser.ParseFrom(frame.Body).MissionSync?.MissionList.Any(mission => mission.Id == 403000222) == true);
    Check("gallery Open deltas for both arms precede the use-handbook mission notification",
        galleryUpdates.Select(entity => entity.InstId).ToHashSet().SetEquals(new uint[] { 300003, 300008 }) && galleryObjectiveAt >= 0 &&
        galleryTalkFrames.Take(galleryObjectiveAt).Where(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify)
            .SelectMany(frame => SceneGroupRefreshScNotify.Parser.ParseFrom(frame.Body).GroupRefreshList)
            .Where(group => group.GroupId == 3).SelectMany(group => group.RefreshEntity)
            .Count(refresh => refresh.AddEntity is { Prop.PropId: 1005, Prop.PropState: 1 }) == 2);
    var galleryRepeatTalk = await TutorialTalk(403000221);
    Check("replaying the Herta unlock talk does not toggle or re-notify the gallery arms",
        galleryRepeatTalk.Select(frame => frame.Id).SequenceEqual(new[] { (ushort)CmdIds.FinishTalkMissionScRsp }) &&
        RoamingGalleryArms(tutorialFlowPlayer).All(prop => prop.State == PropStateEnum.Open));
    foreach (var armId in new[] { 300003, 300008 })
        tutorialFlowPlayer.SetScenePropData(80112001, 3, armId, PropStateEnum.Closed);
    var galleryRelogPlayer = new PlayerInstance(tutorialFlowPlayer.Data);
    await galleryRelogPlayer.InitializeAsync();
    await galleryRelogPlayer.OnLogin();
    Check("relogin at the use-handbook objective overrides both obsolete saved Closed states",
        galleryRelogPlayer.MissionManager!.GetMainMissionStatus(4030002) == MissionPhaseEnum.Accept &&
        galleryRelogPlayer.MissionManager.GetSubMissionStatus(403000222) == MissionPhaseEnum.Accept &&
        RoamingGalleryArms(galleryRelogPlayer) is { Count: 2 } galleryRestored && galleryRestored.All(prop => prop.State == PropStateEnum.Open));
    tutorialFlowPlayer.Connection = null;
    tutorialFlowPlayer = galleryRelogPlayer;
    tutorialConnection = new ProbeConnection(serverPeer) { Player = tutorialFlowPlayer };
    tutorialFlowPlayer.Connection = tutorialConnection;
    var handbookFrames = await SentFrames(() => new HandlerGetRogueHandbookDataCsReq().OnHandle(tutorialConnection, [], []));
    var handbookReply = GetRogueHandbookDataScRsp.Parser.ParseFrom(handbookFrames.Single(frame => frame.Id == CmdIds.GetRogueHandbookDataScRsp).Body);
    Check("the live handbook request returns data without auto-finishing the task or paying its reward",
        handbookReply.Retcode == 0 && handbookReply.HAMKPLCBEBG?.BuffList.Count > 0 &&
        tutorialFlowPlayer.MissionManager.GetSubMissionStatus(403000222) == MissionPhaseEnum.Accept &&
        handbookFrames.All(frame => frame.Id != CmdIds.MissionRewardScNotify));
    var roamingFinalTalk = await TutorialTalk(403000222);
    var roamingDuplicateTalk = await TutorialTalk(403000222);
    Check("roaming final lobby talk completes adventure 4030002 and rewards exactly once",
        tutorialFlowPlayer.MissionManager!.GetMainMissionStatus(4030002) == MissionPhaseEnum.Finish &&
        roamingFinalTalk.Count(frame => frame.Id == CmdIds.MissionRewardScNotify) == 1 &&
        roamingDuplicateTalk.All(frame => frame.Id != CmdIds.MissionRewardScNotify));
    tutorialFlowPlayer.Connection = null;

    var skippedTutorialPlayer = new PlayerInstance(60014);
    await skippedTutorialPlayer.InitializeAsync();
    skippedTutorialPlayer.MissionManager!.Data.EnableMissionOverride = true;
    skippedTutorialPlayer.MissionManager.Data.SetSubMissionStatus(100040129, MissionPhaseEnum.Accept);
    foreach (var id in new[] { 4030001, 4030002 })
    {
        skippedTutorialPlayer.MissionManager.Data.SetMainMissionStatus(id, MissionPhaseEnum.Finish);
        foreach (var subId in GameData.MainMissionData[id].SubMissionIds)
            skippedTutorialPlayer.MissionManager.Data.SetSubMissionStatus(subId, MissionPhaseEnum.Finish);
    }
    var tutorialRepair = await skippedTutorialPlayer.MissionManager.RepairSkippedRogueTutorials();
    Check("stalled story save resumes the skipped tutorial while retaining paid reward markers",
        tutorialRepair && skippedTutorialPlayer.MissionManager.GetMainMissionStatus(4030001) == MissionPhaseEnum.Accept
        && skippedTutorialPlayer.MissionManager.GetMainMissionStatus(4030002) == MissionPhaseEnum.None
        && skippedTutorialPlayer.MissionManager.GetSubMissionStatus(100040129) == MissionPhaseEnum.Accept
        && skippedTutorialPlayer.MissionManager.Data.ReplayedRogueTutorialRewardIds.ToHashSet()
            .SetEquals(new[] { 4030001, 4030002 })
        && !await skippedTutorialPlayer.MissionManager.RepairSkippedRogueTutorials());
    skippedTutorialPlayer.Connection = new ProbeConnection(serverPeer) { Player = skippedTutorialPlayer };
    var replayRewards = await SentFrames(async () =>
    {
        await skippedTutorialPlayer.MissionManager.HandleMissionReward(4030001);
        await skippedTutorialPlayer.MissionManager.HandleSubMissionReward(403000116);
    });
    Check("replayed auto-skipped tutorial grants no second main or sub reward", replayRewards.Count == 0);
    skippedTutorialPlayer.Connection = null;
    skippedTutorialPlayer.MissionManager.Data.SetMainMissionStatus(4030001, MissionPhaseEnum.Finish);
    skippedTutorialPlayer.RogueManager!.RogueData.FirstPassAreas.Add(100);
    Check("a save with a recorded tutorial run is never rewound", !await skippedTutorialPlayer.MissionManager.RepairSkippedRogueTutorials());

    await using (var legacyMissionDb = new SqliteConnection($"Data Source={Path.Combine(scratch, "old-mission-table.db")}"))
    {
        await legacyMissionDb.OpenAsync();
        await using (var create = legacyMissionDb.CreateCommand())
        {
            create.CommandText = "create table Mission (Uid INTEGER PRIMARY KEY); insert into Mission (Uid) values (70002)";
            await create.ExecuteNonQueryAsync();
        }
        var missionStore = EntityStores.Map[typeof(MissionData)];
        missionStore.EnsureTable(legacyMissionDb);
        var legacyMission = missionStore.LoadAll(legacyMissionDb).OfType<MissionData>().Single();
        Check("legacy mission rows gain the tutorial replay column with an empty default",
            legacyMission.Uid == 70002 && legacyMission.ReplayedRogueTutorialRewardIds.Count == 0);
        legacyMission.ReplayedRogueTutorialRewardIds.AddRange([4030001, 4030002]);
        missionStore.Update(legacyMissionDb, legacyMission);
        var replayedMission = missionStore.LoadAll(legacyMissionDb).OfType<MissionData>().Single();
        Check("tutorial replay reward markers survive a database round trip",
            replayedMission.ReplayedRogueTutorialRewardIds.SequenceEqual(new[] { 4030001, 4030002 }));
    }

    var rogueInst = roguePlayer.RogueManager.RogueInstance!;
    // Any level-1 blessing rows: the draft mechanics, not the pool contents, are under test here.
    var draftPool = GameData.RogueBuffData.Values.Where(buff => buff.MazeBuffLevel == 1).Take(5).ToList();
    rogueInst.BuffSelect = RogueBuffSelectMenu.Create(rogueInst, draftPool);
    var pickedBuff = rogueInst.BuffSelect.SelectBuffs[0].MazeBuffID;
    Check("Rogue exit stays shut while a blessing draft is pending",
        !await roguePlayer.RogueManager.OpenRoomExit() &&
        (!rogueRoomHasMonsters || rogueDoors.All(door => door.State == PropStateEnum.Closed)));
    var pickRequest = new OFEDNNJIJFL { MPLGEEGOAMC = 3, DLKIKBIDJDB = new BPLNKHGAILK { BAOFIGFFMLN = (uint)pickedBuff } };
    var pickFrames = await SentFrames(() => new HandlerHandleRogueCommonPendingActionCsReq()
        .OnHandle(rogueConnection, [], pickRequest.ToByteArray()));
    var doorRefreshIndex = pickFrames.FindIndex(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify);
    var pickRspIndex = pickFrames.FindIndex(frame => frame.Id == CmdIds.HandleRogueCommonPendingActionScRsp);
    List<SceneEntityInfo> openedDoors = doorRefreshIndex < 0 ? [] : SceneGroupRefreshScNotify.Parser.ParseFrom(pickFrames[doorRefreshIndex].Body)
        .GroupRefreshList.SelectMany(group => group.RefreshEntity).Select(refresh => refresh.AddEntity)
        .Where(entity => entity?.Prop != null).ToList();
    Check("blessing pick refreshes the exit portal Open with its destination before the response",
        doorRefreshIndex >= 0 && doorRefreshIndex < pickRspIndex && openedDoors.Count == rogueDoors.Count &&
        openedDoors.All(entity => entity.Prop.PropState == (uint)PropStateEnum.Open &&
                                  entity.Prop.ExtraInfo?.RogueGameInfo?.RoomId > 0) &&
        rogueDoors.All(door => door.State == PropStateEnum.Open));
    Check("opened Rogue exit portal is not persisted to the floor prop data",
        rogueDoors.All(door => roguePlayer.GetScenePropData(rogueRoomScene.FloorId, door.GroupId, door.InstId) == null));
    Check("an already open Rogue exit is not refreshed again", !await roguePlayer.RogueManager.OpenRoomExit());

    // A won battle with no blessing to draft clears the room at once.
    foreach (var door in rogueDoors) door.State = PropStateEnum.Closed;
    var roomMonsters = rogueRoomScene.Entities.Values.OfType<EntityMonster>().Take(1).ToList();
    var rogueBattle = new BattleInstance(roguePlayer, roguePlayer.LineupManager!.GetCurLineup()!, roomMonsters);
    var rogueBattleEnd = typeof(RogueManager).GetMethod("OnBattleEnd", BindingFlags.Instance | BindingFlags.NonPublic)!;
    var battleFrames = await SentFrames(async () => await (ValueTask)rogueBattleEnd.Invoke(roguePlayer.RogueManager,
        [rogueBattle, new PVEBattleResultCsReq { EndStatus = BattleEndStatus.BattleEndWin }])!);
    var draftedAfterBattle = rogueInst.BuffSelect?.HasSelection == true;
    Check("Rogue battle win opens the exit unless it drafts a blessing", roomMonsters.Count > 0 && (draftedAfterBattle
        ? rogueDoors.All(door => door.State == PropStateEnum.Closed)
        : rogueDoors.All(door => door.State == PropStateEnum.Open) &&
          battleFrames.Any(frame => frame.Id == CmdIds.SceneGroupRefreshScNotify)));

    // The draft pool comes from the real RogueBuffGroup rows (100005 / 100003 / room content + 8000), so the
    // offered blessings must be classic RogueBuff rows that some classic group resolves to.
    var classicGroupBuffIds = GameData.RogueBuffGroupData.Values.OfType<RogueBuffGroupExcel>()
        .SelectMany(group => group.BuffList).Select(buff => buff.GetId()).ToHashSet();
    var draftedBuffs = rogueInst.BuffSelect?.SelectBuffs ?? [];
    var draftNotifies = battleFrames.Where(frame => frame.Id == CmdIds.SyncRogueCommonPendingActionScNotify)
        .Select(frame => SyncRogueCommonPendingActionScNotify.Parser.ParseFrom(frame.Body)).ToList();
    Check("classic Simulated Universe room win drafts classic blessings from the real RogueBuffGroup data",
        draftedAfterBattle && draftedBuffs.Count == 3 &&
        draftedBuffs.All(buff => buff is RogueBuffExcel && classicGroupBuffIds.Contains(buff.GetId()) &&
                                 GameData.RogueBuffData.GetValueOrDefault(buff.GetId()) is RogueBuffExcel) &&
        draftNotifies.Count == 2 && draftNotifies.All(notify =>
            notify.Action?.Detail?.PCHGEGFBKLF?.LEOFNINDJDB.Select(offer => (int)offer.BuffId)
                .SequenceEqual(draftedBuffs.Select(buff => buff.MazeBuffID)) == true));
    var draftedPick = draftedBuffs.FirstOrDefault();
    Check("drafted classic blessing can be picked into the run",
        draftedPick != null && roguePlayer.RogueManager.SelectBuff(draftedPick.MazeBuffID) == Retcode.RetSucc &&
        rogueInst.Buffs.TryGetValue(draftedPick.MazeBuffID, out var ownedPick) &&
        ownedPick.Level == draftedPick.MazeBuffLevel);
    rogueInst.BuffSelect = null;

    // A won run is dropped on relog while login restores its room scene: LeaveRogue has no run to settle.
    typeof(RogueManager).GetProperty(nameof(RogueManager.RogueInstance))!.SetValue(roguePlayer.RogueManager, null);
    async Task<(List<(ushort Id, byte[] Body)> Frames, LeaveRogueScRsp? Rsp)> LeaveRogue()
    {
        var frames = await SentFrames(() => new HandlerLeaveRogueCsReq().OnHandle(rogueConnection, [], []));
        var rsp = frames.Where(frame => frame.Id == CmdIds.LeaveRogueScRsp)
            .Select(frame => LeaveRogueScRsp.Parser.ParseFrom(frame.Body)).SingleOrDefault();
        return (frames, rsp);
    }
    var orphanLeave = await LeaveRogue();
    Check("LeaveRogue with no live run returns an orphaned Rogue room to the lobby",
        orphanLeave.Rsp is { Retcode: 0 } && orphanLeave.Rsp.Scene?.EntryId == RogueManager.RogueEntranceId &&
        orphanLeave.Rsp.Lineup != null && roguePlayer.SceneInstance?.EntryId == RogueManager.RogueEntranceId &&
        roguePlayer.LineupManager.Data.CurExtraLineup == -1 &&
        orphanLeave.Frames.FindIndex(frame => frame.Id == CmdIds.SyncServerSceneChangeNotify) is var boundary and >= 0 &&
        boundary < orphanLeave.Frames.FindIndex(frame => frame.Id == CmdIds.LeaveRogueScRsp) &&
        orphanLeave.Frames.All(frame => frame.Id != CmdIds.SyncRogueFinishScNotify));
    var lobbyLeave = await LeaveRogue();
    Check("LeaveRogue outside a Rogue room with no run still reports status mismatch",
        lobbyLeave.Rsp?.Retcode == (uint)Retcode.RetRogueStatusNotMatch);

    // A degenerate talk report ("0", no sub id) is ACK-only even when the tracked main has exactly one running
    // Talk finish gate on the current floor (the official capture ACKs it and the client finishes the gate later).
    var talkPlayer = new PlayerInstance(60008);
    await talkPlayer.InitializeAsync();
    talkPlayer.MissionManager!.Data.EnableMissionOverride = true;
    const int TalkGateMain = 1010202;
    const int TalkGateSub = 101020206;
    var talkGate = GameData.SubMissionInfoData[TalkGateSub].SubMissionInfo!;
    var talkEntrance = GameData.MapEntranceData.Values.First(entrance => entrance.FloorID == talkGate.LevelFloorID);
    var talkScene = new SceneInstance(talkPlayer, GameData.MazePlaneData[talkEntrance.PlaneID], talkEntrance.FloorID,
        talkEntrance.ID);
    typeof(PlayerInstance).GetProperty(nameof(PlayerInstance.SceneInstance))!.SetValue(talkPlayer, talkScene);
    talkPlayer.MissionManager.Data.SetMainMissionStatus(TalkGateMain, MissionPhaseEnum.Accept);
    talkPlayer.MissionManager.Data.SetSubMissionStatus(TalkGateSub, MissionPhaseEnum.Accept);
    talkPlayer.MissionManager.Data.TrackingMainMissionId = TalkGateMain;
    talkPlayer.Connection = new ProbeConnection(serverPeer) { Player = talkPlayer };
    var talkFrames = await SentFrames(() => new HandlerFinishTalkMissionCsReq()
        .OnHandle(talkPlayer.Connection, [], new FinishTalkMissionCsReq { TalkStr = "0" }.ToByteArray()));
    Check("degenerate FinishTalkMission is ACK-only and leaves the talk finish gate running",
        talkGate.FinishType == MissionFinishTypeEnum.Talk &&
        talkPlayer.MissionManager.GetSubMissionStatus(TalkGateSub) == MissionPhaseEnum.Accept &&
        talkPlayer.MissionManager.GetMainMissionStatus(TalkGateMain) == MissionPhaseEnum.Accept &&
        talkFrames.Select(frame => frame.Id).SequenceEqual(new[] { (ushort)CmdIds.FinishTalkMissionScRsp }));
    await SentFrames(() => new HandlerFinishTalkMissionCsReq()
        .OnHandle(talkPlayer.Connection, [], new FinishTalkMissionCsReq { TalkStr = talkGate.ParamStr1 }.ToByteArray()));
    Check("the client's own talk string still finishes the gate",
        talkPlayer.MissionManager.GetSubMissionStatus(TalkGateSub) == MissionPhaseEnum.Finish);
    talkPlayer.Connection = null;

    // Classic Simulated Universe command pairs (4.7 Proto/4.7/Raw/packetIds.txt): enhance 1823/1828, pick 1848/1875.
    int OpcodeOf(Type handler) => handler.GetCustomAttribute<Opcode>()!.CmdId;
    Check("classic Simulated Universe enhance and pick handlers listen on the 18xx command ids",
        OpcodeOf(typeof(HandlerEnhanceRogueBuffCsReq)) == 1823 && CmdIds.RogueEnhanceRogueBuffScRsp == 1828 &&
        OpcodeOf(typeof(HandlerPickRogueAvatarCsReq)) == 1848 && CmdIds.PickRogueAvatarScRsp == 1875);
    var enhanceFrames = await SentFrames(() => new HandlerEnhanceRogueBuffCsReq().OnHandle(rogueConnection, [], []));
    Check("classic Simulated Universe enhance answers on 1828", enhanceFrames.Select(frame => frame.Id)
        .SequenceEqual(new[] { (ushort)CmdIds.RogueEnhanceRogueBuffScRsp }));
    var pickAvatarRequest = new PickRogueAvatarCsReq { PropEntityId = 4194339, DFPPCAJDBFK = { 4000005 }, NOEPBNMJFGL = { 1001 } };
    var pickAvatarFrames = await SentFrames(() => new HandlerPickRogueAvatarCsReq()
        .OnHandle(rogueConnection, [], pickAvatarRequest.ToByteArray()));
    var pickAvatarRsp = pickAvatarFrames.Where(frame => frame.Id == CmdIds.PickRogueAvatarScRsp)
        .Select(frame => PickRogueAvatarScRsp.Parser.ParseFrom(frame.Body)).SingleOrDefault();
    Check("PickRogueAvatar echoes special ids (req #1) in rsp #15 and base ids (req #7) in rsp #7",
        PickRogueAvatarCsReq.DFPPCAJDBFKFieldNumber == 1 && PickRogueAvatarCsReq.NOEPBNMJFGLFieldNumber == 7 &&
        PickRogueAvatarScRsp.BFEDFBEKOBIFieldNumber == 15 && PickRogueAvatarScRsp.NOEPBNMJFGLFieldNumber == 7 &&
        pickAvatarRsp is { Retcode: 0 } && pickAvatarRsp.BFEDFBEKOBI.SequenceEqual(new uint[] { 4000005 }) &&
        pickAvatarRsp.NOEPBNMJFGL.SequenceEqual(new uint[] { 1001 }));
    roguePlayer.Connection = null;
    await outbound.StopAsync();
    await inbound.StopAsync();
}

// Role Trial (TrialActivity): start, battle stage choice, clear, reward claim, leave and relogin rescue, driven
// through the real request handlers and captured over the in-memory KCP pair.
Directory.CreateDirectory(Path.Combine(scratch, "Custom"));
File.Copy(Path.Combine(repository, "Config/Custom/ActivityConfig.json"), Path.Combine(scratch, "Custom/ActivityConfig.json"));
var schedule = ResourceManager.LoadCustomFile<ActivityConfig>("Activity", "ActivityConfig");
// The Role Trial modules of the captured 4.6 official schedule (ActivityModuleDemo -> AvatarDemoConfig TrialActivity).
// Modules the 4.7 client moved to another activity (2000603 -> 20016, 2002303 -> 20024, 2002502 -> 20027) keep
// being scheduled under that activity.
int[] trialModules = [2000603, 2001101, 2001102, 2001103, 2001104, 2001105, 2001106, 2001107, 2001108, 2001109,
    2002303, 2002502, 2006301, 2006302, 2008201, 2008401, 2008601, 2008701, 2009301];
Check("activity schedule lists every Role Trial module", schedule != null &&
    trialModules.All(id => schedule.ScheduleData.Any(row => row.ActivityId == id && row.PanelId is 20011 or 20016 or 20024 or 20027 or 20064 or 20087 or 20091 or 20092 or 20093)));

// Banners.json is parsed row by row and a bad row is only skipped with a warning, so check that every captured 4.6
// pool survives loading and only references avatars and light cones present in the resource cache.
File.Copy(Path.Combine(repository, "Config/Custom/Banners.json"), Path.Combine(scratch, "Custom/Banners.json"));
var banners = ResourceManager.LoadBanners();
int[] bannerIds = [1001, 2139, 2140, 2141, 3139, 3140, 3141, 5001, 5002, 5003, 5004, 6001, 6002, 6003, 6004];
Check("banner config loads every captured 4.6 pool with known rate-up items",
    banners.Banners.Select(b => b.GachaId).Order().SequenceEqual(bannerIds) &&
    banners.Banners.All(b => b.RateUpItems5.Concat(b.RateUpItems4).Concat(b.PrizeItems)
        .All(id => GameData.AvatarConfigData.ContainsKey(id) || GameData.EquipmentConfigData.ContainsKey(id))));
await WarpGachaRegression.Run(Check, repository, scratch);

var fakeRaw = Path.Combine(scratch, "fake-raw");
Directory.CreateDirectory(Path.Combine(fakeRaw, "ExcelOutputGameCore"));
File.WriteAllText(Path.Combine(fakeRaw, "ExcelOutputGameCore/AvatarDemoConfig.json"), "[]");
var savedResourcePath = config.Path.ResourcePath;
config.Path.ResourcePath = fakeRaw;
Check("cache with AvatarDemoConfig StageType passes validation", (bool)validateCache.Invoke(null, null)!);
var stageTypes = GameData.AvatarDemoConfigData.ToDictionary(pair => pair.Key, pair => pair.Value.StageType);
foreach (var row in GameData.AvatarDemoConfigData.Values) row.StageType = "";
Check("stale cache without AvatarDemoConfig StageType is rejected when the raw table exists",
    !(bool)validateCache.Invoke(null, null)!);
config.Path.ResourcePath = Path.Combine(scratch, "no-raw-resource-json");
Check("cache-only deployments keep a cache without StageType", (bool)validateCache.Invoke(null, null)!);
foreach (var (id, type) in stageTypes) GameData.AvatarDemoConfigData[id].StageType = type;
config.Path.ResourcePath = savedResourcePath;
Check("trial stage 315130 carries StageType and SPList", GameData.AvatarDemoConfigData[315130].IsTrialActivity
    && GameData.AvatarDemoConfigData[315130].SPList.SequenceEqual([6500, 6500, 7500, 7000])
    && !GameData.AvatarDemoConfigData[313079].IsTrialActivity);
if (!GameData.AvatarDemoConfigData[315130].IsTrialActivity)
    throw new InvalidOperationException("The resource cache predates AvatarDemoConfig StageType/SPList. Regenerate " +
                                        "Config/Resource.bin or pass --resource-cache <regenerated cache>.");

var trialPlayer = new PlayerInstance(60010);
await trialPlayer.InitializeAsync();
const int TrialReturnEntry = 1000001;
await trialPlayer.TryEnterScene(TrialReturnEntry, 0, false);
var trialReturnPos = trialPlayer.Data.Pos!;
var trialData = trialPlayer.ActivityManager!.Data.TrialActivityData;
var trialOutbound = new LoopbackKcpTransport();
var trialInbound = new LoopbackKcpTransport();
using (var trialServerPeer = new KcpConversation(endpoint, trialOutbound, options))
using (var trialClientPeer = new KcpConversation(endpoint, trialInbound, options))
{
    trialOutbound.Peer = trialClientPeer;
    trialInbound.Peer = trialServerPeer;
    trialOutbound.Start();
    trialInbound.Start();
    var trialConnection = new ProbeConnection(trialServerPeer) { Player = trialPlayer };
    trialPlayer.Connection = trialConnection;
    async Task<List<(ushort Id, byte[] Body)>> Capture(Func<Task> action)
    {
        await action();
        await trialPlayer.SendPacket(new BasePacket(ushort.MaxValue));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[March7thHoneyConnection.MaxPacketBytes];
        var packets = new List<(ushort, byte[])>();
        while (true)
        {
            var received = await trialClientPeer.ReceiveAsync(buffer, timeout.Token);
            if (received.TransportClosed || received.BytesReceived < 16)
                throw new InvalidDataException("KCP trial test did not receive a complete game packet.");
            var id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8));
            if (id == ushort.MaxValue) return packets;
            packets.Add((id, buffer.AsSpan(12, length).ToArray()));
        }
    }
    Task Send<TReq>(Handler handler, TReq req) where TReq : IMessage =>
        handler.OnHandle(trialConnection, [], req.ToByteArray());
    Task Start(int stageId) => Send(new HandlerStartTrialActivityCsReq(), new StartTrialActivityCsReq { StageId = (uint)stageId });
    StartTrialActivityScRsp StartRsp(List<(ushort Id, byte[] Body)> packets) =>
        StartTrialActivityScRsp.Parser.ParseFrom(packets.Last(p => p.Id == CmdIds.StartTrialActivityScRsp).Body);
    async Task<List<(ushort Id, byte[] Body)>> WinTrialBattle()
    {
        var monsters = trialPlayer.SceneInstance!.Entities.Values.OfType<EntityMonster>().Where(m => m.IsAlive).ToList();
        trialPlayer.BattleInstance = new BattleInstance(trialPlayer, trialPlayer.LineupManager!.GetCurLineup()!, monsters);
        return await Capture(async () => await trialPlayer.BattleManager!.EndBattle(
            new PVEBattleResultCsReq { EndStatus = BattleEndStatus.BattleEndWin, Stt = new() }));
    }

    foreach (var invalidStage in new[] { 313079, 1 })
    {
        var rejected = await Capture(() => Start(invalidStage));
        var rsp = StartRsp(rejected);
        Check($"trial start rejects non-trial stage {invalidStage} without side effects",
            rsp.Retcode == (uint)Retcode.RetReqParaInvalid && rsp.StageId == invalidStage && trialData.CurTrialStageId == 0
            && trialPlayer.ActivityManager.TrialActivityInstance == null
            && !rejected.Any(p => p.Id == CmdIds.ExtraLineupDestroyNotify || p.Id == CmdIds.EnterSceneByServerScNotify));
    }

    var started = await Capture(() => Start(315130));
    var startRsp = StartRsp(started);
    var trialLineup = trialPlayer.LineupManager!.GetCurLineup()!;
    Check("trial 315130 starts and records the world return point", startRsp.Retcode == 0 && startRsp.StageId == 315130
        && trialData.CurTrialStageId == 315130 && trialData.ReturnEntryId == TrialReturnEntry
        && trialPlayer.SceneInstance?.Excel.PlaneType == PlaneTypeEnum.TrialActivity);
    Check("trial lineup uses the stage's special trial avatars", trialLineup.IsExtraLineup()
        && trialLineup.BaseAvatars!.Select(a => a.SpecialAvatarId).SequenceEqual([2001381, 2001382, 2001383, 2001384]));
    var enterTrial = EnterSceneByServerScNotify.Parser.ParseFrom(started.Last(p => p.Id == CmdIds.EnterSceneByServerScNotify).Body);
    Check("trial team starts with SPList 6500/6500/7500/7000",
        enterTrial.Lineup.AvatarList.OrderBy(a => a.Slot).Select(a => a.SpBar.CurSp).SequenceEqual([6500u, 6500u, 7500u, 7000u]));
    Check("trial scene stages the stage's own monster event", trialPlayer.SceneInstance!.Entities.Values.OfType<EntityMonster>()
        .Any(m => m.GroupId == GameData.AvatarDemoConfigData[315130].MazeGroupID1 && m.EventId == 315130));
    Check("trial scene resolves world 999 to the return point's world", enterTrial.Scene.WorldId != 999
        && enterTrial.Scene.WorldId == (uint)trialPlayer.SceneInstance.ResolveWorldId());
    Check("start sends lineup destroy, scene change and response in order", started.Select(p => p.Id)
        .Where(id => id is CmdIds.ExtraLineupDestroyNotify or CmdIds.SyncServerSceneChangeNotify
            or CmdIds.EnterSceneByServerScNotify or CmdIds.StartTrialActivityScRsp)
        .SequenceEqual(new ushort[] { CmdIds.ExtraLineupDestroyNotify, CmdIds.SyncServerSceneChangeNotify,
            CmdIds.EnterSceneByServerScNotify, CmdIds.StartTrialActivityScRsp }));

    var runningInstance = trialPlayer.ActivityManager.TrialActivityInstance;
    var rejectedWhileRunning = StartRsp(await Capture(() => Start(313079)));
    Check("rejected start keeps the running trial", rejectedWhileRunning.Retcode == (uint)Retcode.RetReqParaInvalid
        && trialData.CurTrialStageId == 315130 && ReferenceEquals(runningInstance, trialPlayer.ActivityManager.TrialActivityInstance)
        && trialPlayer.LineupManager.GetCurLineup()!.IsExtraLineup());
    var dataRsp = GetTrialActivityDataScRsp.Parser.ParseFrom(new PacketGetTrialActivityDataScRsp(trialPlayer).Data);
    Check("trial data reports the running stage", dataRsp.ActivityStageId == 315130);

    var brokenStage = GameData.AvatarDemoConfigData[315130];
    var brokenEntrance = brokenStage.MapEntranceID;
    brokenStage.MapEntranceID = 99000003;
    var failedStart = StartRsp(await Capture(() => Start(315130)));
    brokenStage.MapEntranceID = brokenEntrance;
    Check("failed scene entry rolls back to the running trial", failedStart.Retcode == (uint)Retcode.RetFail
        && trialData.CurTrialStageId == 315130 && trialData.ReturnEntryId == TrialReturnEntry
        && trialPlayer.LineupManager.GetCurLineup()!.BaseAvatars!.Select(a => a.SpecialAvatarId).First() == 2001381);

    Check("switching trials keeps the original return point", StartRsp(await Capture(() => Start(315120))).Retcode == 0
        && trialData.CurTrialStageId == 315120 && trialData.ReturnEntryId == TrialReturnEntry);
    var trialMonsters = trialPlayer.SceneInstance!.Entities.Values.OfType<EntityMonster>().Where(m => m.IsAlive).ToList();
    BattleInstance? trialBattle = null;
    try { trialBattle = new BattleInstance(trialPlayer, trialPlayer.LineupManager.GetCurLineup()!, trialMonsters); }
    catch (Exception ex) { Console.WriteLine(ex); }
    Check("trial 315120 battle resolves stage 315121 from the monster event", trialBattle is { StageId: 315121, IsTrialActivityBattle: true }
        && trialBattle.EntityMonsters.Count > 0);

    var lost = trialPlayer.BattleInstance = new BattleInstance(trialPlayer, trialPlayer.LineupManager.GetCurLineup()!, trialMonsters);
    var lostPackets = await Capture(async () => await trialPlayer.BattleManager!.EndBattle(
        new PVEBattleResultCsReq { EndStatus = BattleEndStatus.BattleEndLose, Stt = new() }));
    Check("lost trial battle does not clear the trial", trialData.CurTrialStageId == 315120 && trialData.Activities.Count == 0
        && !lostPackets.Any(p => p.Id == CmdIds.CurTrialActivityScNotify));

    var hcoinBeforeClear = trialPlayer.Data.Hcoin;
    var finished = await WinTrialBattle();
    var finishOrder = finished.Select(p => p.Id).Where(id => id is CmdIds.TrialActivityDataChangeScNotify
        or CmdIds.SyncServerSceneChangeNotify or CmdIds.EnterSceneByServerScNotify or CmdIds.CurTrialActivityScNotify
        or CmdIds.PVEBattleResultScRsp).ToList();
    Check("trial clear sends data change, return scene, finish notify, then battle result", finishOrder.SequenceEqual(new ushort[] {
        CmdIds.TrialActivityDataChangeScNotify, CmdIds.SyncServerSceneChangeNotify, CmdIds.EnterSceneByServerScNotify,
        CmdIds.CurTrialActivityScNotify, CmdIds.PVEBattleResultScRsp }));
    var change = TrialActivityDataChangeScNotify.Parser.ParseFrom(finished.First(p => p.Id == CmdIds.TrialActivityDataChangeScNotify).Body);
    Check("data change notify carries the cleared stage and featured avatar", change.TrialActivityInfo?.StageId == 315120
        && !change.TrialActivityInfo.TakenReward && change.OBOPMFBGBAE.Any(a => a.AvatarId == 1512));
    var finishNotify = CurTrialActivityScNotify.Parser.ParseFrom(finished.First(p => p.Id == CmdIds.CurTrialActivityScNotify).Body);
    Check("finish notify reports the stage as finished", finishNotify.ActivityStageId == 315120
        && finishNotify.Status == TrialActivityStatus.Finish);
    Check("trial clear returns the player to the saved world point without the trial lineup",
        trialData.CurTrialStageId == 0 && trialPlayer.ActivityManager.TrialActivityInstance == null
        && trialPlayer.Data.EntryId == TrialReturnEntry && trialPlayer.Data.Pos!.X == trialReturnPos.X
        && trialPlayer.Data.Pos.Z == trialReturnPos.Z && !trialPlayer.LineupManager.GetCurLineup()!.IsExtraLineup()
        && trialPlayer.LineupManager.GetExtraLineup(ExtraLineupType.LineupStageTrial) == null
        && trialPlayer.Data.Hcoin == hcoinBeforeClear);
    await Capture(() => Start(315120));
    await WinTrialBattle();
    Check("clearing a stage again keeps one record", trialData.Activities.Count(r => r.StageId == 315120) == 1);
    var afterClear = GetTrialActivityDataScRsp.Parser.ParseFrom(new PacketGetTrialActivityDataScRsp(trialPlayer).Data);
    Check("trial data lists the record and the featured avatar", afterClear.ActivityStageId == 0
        && afterClear.TrialActivityInfoList.Any(i => i.StageId == 315120)
        && afterClear.OBOPMFBGBAE.Count(a => a.AvatarId == 1512) == 1);

    Check("stage without a StageConfig row resolves its battle from the monster event",
        StartRsp(await Capture(() => Start(380060))).Retcode == 0 && new BattleInstance(trialPlayer,
            trialPlayer.LineupManager.GetCurLineup()!, trialPlayer.SceneInstance!.Entities.Values.OfType<EntityMonster>()
                .Where(m => m.IsAlive).ToList()) is { StageId: 380050, IsTrialActivityBattle: true });
    var left = await Capture(() => Send(new HandlerLeaveTrialActivityCsReq(), new LeaveTrialActivityCsReq { StageId = 380060 }));
    Check("leaving echoes the stage and returns without recording a clear",
        LeaveTrialActivityScRsp.Parser.ParseFrom(left.Last(p => p.Id == CmdIds.LeaveTrialActivityScRsp).Body).StageId == 380060
        && trialData.CurTrialStageId == 0 && trialPlayer.Data.EntryId == TrialReturnEntry
        && trialData.Activities.All(r => r.StageId != 380060));

    TakeTrialActivityRewardScRsp Claim(List<(ushort Id, byte[] Body)> packets) =>
        TakeTrialActivityRewardScRsp.Parser.ParseFrom(packets.Last(p => p.Id == CmdIds.TakeTrialActivityRewardScRsp).Body);
    Task<List<(ushort Id, byte[] Body)>> TakeReward(int stageId) => Capture(() =>
        Send(new HandlerTakeTrialActivityRewardCsReq(), new TakeTrialActivityRewardCsReq { StageId = (uint)stageId }));
    var rewardHcoin = GameData.RewardDataData[GameData.AvatarDemoConfigData[315120].RewardID].Hcoin;
    var hcoinBeforeClaim = trialPlayer.Data.Hcoin;
    var firstClaimPackets = await TakeReward(315120);
    var firstClaim = Claim(firstClaimPackets);
    Check("first claim grants the reward with Hcoin as item 1", firstClaim.Retcode == 0 && firstClaim.StageId == 315120
        && rewardHcoin > 0 && firstClaim.Reward.ItemList_.First().ItemId == 1
        && firstClaim.Reward.ItemList_.First().Num == (uint)rewardHcoin && firstClaim.Reward.ItemList_.Count > 1
        && trialPlayer.Data.Hcoin == hcoinBeforeClaim + rewardHcoin
        && trialData.Activities.Single(r => r.StageId == 315120).TakenReward
        && !firstClaimPackets.Any(p => p.Id == CmdIds.ScenePlaneEventScNotify));
    var secondClaim = Claim(await TakeReward(315120));
    Check("second claim is refused without granting", secondClaim.Retcode == (uint)Retcode.RetFail
        && secondClaim.StageId == 315120 && trialPlayer.Data.Hcoin == hcoinBeforeClaim + rewardHcoin);
    var unclearedClaim = Claim(await TakeReward(315130));
    Check("uncleared trial reward is refused", unclearedClaim.Retcode == (uint)Retcode.RetFail && unclearedClaim.StageId == 315130);
    var displayClaim = Claim(await TakeReward(313079));
    Check("non-trial reward claim is invalid", displayClaim.Retcode == (uint)Retcode.RetReqParaInvalid && displayClaim.StageId == 313079);

    await Capture(() => Start(315130));
    trialPlayer.Connection = null;
    await trialOutbound.StopAsync();
    await trialInbound.StopAsync();
}
Check("trial is running on the trial plane before relogin", trialData.CurTrialStageId == 315130
    && GameData.MazePlaneData[trialPlayer.Data.PlaneId].PlaneType == PlaneTypeEnum.TrialActivity);
var relogin = new PlayerInstance(trialPlayer.Data);
await relogin.InitializeAsync();
await relogin.OnLogin();
Check("relogin mid-trial abandons the run and returns to the return point", trialData.CurTrialStageId == 0
    && relogin.ActivityManager!.TrialActivityInstance == null && relogin.Data.EntryId == TrialReturnEntry
    && relogin.SceneInstance?.Excel.PlaneType != PlaneTypeEnum.TrialActivity
    && relogin.LineupManager!.GetExtraLineup(ExtraLineupType.LineupStageTrial) == null
    && !relogin.LineupManager.GetCurLineup()!.IsExtraLineup()
    && GetTrialActivityDataScRsp.Parser.ParseFrom(new PacketGetTrialActivityDataScRsp(relogin).Data).ActivityStageId == 0);

// Raid runs (ported from yuukisr_go 0a77679/e776655/24d080e): a run whose raid is missing from this version's
// tables must never trap the player, and ResetLocationOnLogin abandons the run behind the saved location.
var raidEntry = GameData.MapEntranceData.Values.OrderBy(e => e.ID).First(e =>
{
    if (!GameData.MazePlaneData.TryGetValue(e.PlaneID, out var plane) || plane.PlaneType != PlaneTypeEnum.Raid)
        return false;
    GameData.GetFloorInfo(e.PlaneID, e.FloorID, out var floor);
    return (floor?.GetAnchorInfo(e.StartGroupID, e.StartAnchorID) ??
            floor?.GetAnchorInfo(floor.StartGroupID, floor.StartAnchorID)) != null;
}).ID;
const int MissingRaidId = 99999;
var knownRaid = GameData.RaidConfigData.Values.OrderBy(r => r.RaidID).First(r => r.HardLevel == 0);

async Task<PlayerInstance> RaidPlayer(int uid, int raidId)
{
    var raider = new PlayerInstance(uid);
    await raider.InitializeAsync();
    await raider.TryEnterScene(TrialReturnEntry, 0, false);
    var raidData = raider.RaidManager!.Data;
    raidData.RaidRecordDatas[raidId] = new Dictionary<int, March7thHoney.Database.Scene.RaidRecord>
    {
        [0] = new()
        {
            RaidId = raidId, WorldLevel = 0, Status = RaidStatus.Doing, OldEntryId = TrialReturnEntry,
            OldPos = raider.Data.Pos!, OldRot = raider.Data.Rot!
        }
    };
    raidData.CurRaidId = raidId;
    raidData.CurRaidWorldLevel = 0;
    raider.TryPlaceAtEntrance(raidEntry);
    return raider;
}

var orphanRaider = await RaidPlayer(60511, MissingRaidId);
var orphanRelogin = new PlayerInstance(orphanRaider.Data);
await orphanRelogin.InitializeAsync();
await orphanRelogin.OnLogin();
Check("relogin inside a raid missing from the tables abandons it and returns to the entry point",
    orphanRelogin.RaidManager!.Data.CurRaidId == 0 && !orphanRelogin.RaidManager.Data.RaidRecordDatas.ContainsKey(MissingRaidId)
    && orphanRelogin.Data.EntryId == TrialReturnEntry && orphanRelogin.SceneInstance?.Excel.PlaneType != PlaneTypeEnum.Raid);

var orphanLeaver = await RaidPlayer(60512, MissingRaidId);
await orphanLeaver.RaidManager!.LeaveRaid(true);
Check("leaving a raid missing from the tables returns to the entry point and drops the record",
    orphanLeaver.RaidManager.Data.CurRaidId == 0 && !orphanLeaver.RaidManager.Data.RaidRecordDatas.ContainsKey(MissingRaidId)
    && orphanLeaver.Data.EntryId == TrialReturnEntry);

var recordlessLeaver = await RaidPlayer(60513, knownRaid.RaidID);
recordlessLeaver.RaidManager!.Data.RaidRecordDatas.Clear();
await recordlessLeaver.RaidManager.LeaveRaid(true);
Check("leaving a raid whose record is gone releases the player to the default spawn",
    recordlessLeaver.RaidManager.Data.CurRaidId == 0 && recordlessLeaver.Data.EntryId == PlayerInstance.DefaultSpawnEntryId);

var keptRaider = await RaidPlayer(60514, knownRaid.RaidID);
var keptRelogin = new PlayerInstance(keptRaider.Data);
await keptRelogin.InitializeAsync();
await keptRelogin.OnLogin();
Check("relogin inside a known raid still resumes it by default",
    keptRelogin.RaidManager!.Data.CurRaidId == knownRaid.RaidID && keptRelogin.Data.EntryId == raidEntry);

var resetRaider = await RaidPlayer(60515, knownRaid.RaidID);
config.ServerOption.ResetLocationOnLogin = true;
var resetRelogin = new PlayerInstance(resetRaider.Data);
await resetRelogin.InitializeAsync();
await resetRelogin.OnLogin();
config.ServerOption.ResetLocationOnLogin = false;
Check("ResetLocationOnLogin abandons the raid run and logs in at the default spawn",
    resetRelogin.RaidManager!.Data.CurRaidId == 0 && !resetRelogin.RaidManager.Data.RaidRecordDatas.ContainsKey(knownRaid.RaidID)
    && resetRelogin.Data.EntryId == PlayerInstance.DefaultSpawnEntryId
    && resetRelogin.SceneInstance?.EntryId == PlayerInstance.DefaultSpawnEntryId
    && !resetRelogin.LineupManager!.GetCurLineup()!.IsExtraLineup());
Check("reset raid info notify keeps an empty item_list",
    RaidInfoNotify.Parser.ParseFrom(new March7thHoney.GameServer.Server.Packet.Send.Raid.PacketRaidInfoNotify().Data).ItemList != null);

// CommonRogueQuery: the query type is the oneof arm's field number in the active protocol (4.6 moved the
// function-unlock arm BBEKBJPHEJK from 15 to 1, 4.7 to 10 and dropped ABPOOCOOKCF), so the unlock set must follow
// the arm, not a literal.
var commonQuery = RogueManager.BuildCommonQueryEntries([
    (uint)CCKJAEDDDCL.BBEKBJPHEJKFieldNumber, (uint)CCKJAEDDDCL.MECOEFEJANEFieldNumber,
    (uint)CCKJAEDDDCL.BMGOLFNGHGHFieldNumber]).ToList();
Check("CommonRogueQuery answers the function-unlock arm by its active field number",
    commonQuery.Count == 3 && commonQuery[0].BBEKBJPHEJK?.LFCGPOKMJLC.Count == Enum.GetValues<MGJDNGHDIKG>().Length
    && commonQuery.Skip(1).All(entry => entry.KKNBOACNCONCase == CCKJAEDDDCL.KKNBOACNCONOneofCase.None));

// Data responses whose sub-messages the 4.6 client dereferences ship them present and empty.
T Present<T>(int cmdId, T rsp) where T : IMessage<T>, new() =>
    new MessageParser<T>(() => new T()).ParseFrom(
        new March7thHoney.GameServer.Server.Packet.Send.MiscModule.PacketPresentEmptyScRsp(cmdId, rsp).Data);
var socialPlay = Present(CmdIds.SocialPlayGetDataScRsp, new SocialPlayGetDataScRsp());
Check("SocialPlayGetDataScRsp ships its room gate data present",
    socialPlay.ELDIONMAAHO != null && socialPlay.NDDJFLPBHBL != null && socialPlay.ELDIONMAAHO.KABGCGENKLK != null);
Check("PetMarbleGetDataScRsp ships its data present",
    Present(CmdIds.PetMarbleGetDataScRsp, new PetMarbleGetDataScRsp()).BJBJLBIOPKA != null);
Check("PetMarbleGetCycleTrialPetsScRsp ships its trial pet data present",
    Present(CmdIds.PetMarbleGetCycleTrialPetsScRsp, new PetMarbleGetCycleTrialPetsScRsp()).ACCGIILHFKL != null);
if (HandlerManager.handlers.Count == 0) HandlerManager.Init();
Check("presence handlers are registered for the three requests", new[]
    {
        CmdIds.SocialPlayGetDataCsReq, CmdIds.PetMarbleGetDataCsReq, CmdIds.PetMarbleGetCycleTrialPetsCsReq
    }.All(id => HandlerManager.GetHandler(id) != null));
// ---- 4.6 pets, relic presets, lineup slots, Trailblazer current path, video keys (yuukisr_go c4f5979/24e7083/6bb4fd1/a532fa0/6a76330) ----
var blindBoxPetItems = GameData.ItemConfigData.Values.Where(item => PetManager.IsBlindBoxPetItem(item.ID))
    .Select(item => item.ID).OrderBy(id => id).ToList();
var gen1PetItems = GameData.PetData.Values.Select(pet => pet.PetItemID).Where(GameData.ItemConfigData.ContainsKey).ToList();
Check("cache has 4.6 blind box pet items and keeps gen-1 summon pets out of them", blindBoxPetItems.Count >= 2
    && blindBoxPetItems.All(id => id >= 251501) && gen1PetItems.Count > 0 && gen1PetItems.All(id => !PetManager.IsBlindBoxPetItem(id)));

var petPlayer = new PlayerInstance(60020);
await petPlayer.InitializeAsync();
var pets = petPlayer.GetComponent<PetManager>();
var petOutbound = new LoopbackKcpTransport();
var petInbound = new LoopbackKcpTransport();
using (var petServerPeer = new KcpConversation(endpoint, petOutbound, options))
using (var petClientPeer = new KcpConversation(endpoint, petInbound, options))
{
    petOutbound.Peer = petClientPeer;
    petInbound.Peer = petServerPeer;
    petOutbound.Start();
    petInbound.Start();
    var petConnection = new ProbeConnection(petServerPeer) { Player = petPlayer };
    petPlayer.Connection = petConnection;
    async Task<List<(ushort Id, byte[] Body)>> CapturePet(Func<Task> action)
    {
        await action();
        await petPlayer.SendPacket(new BasePacket(ushort.MaxValue));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[March7thHoneyConnection.MaxPacketBytes];
        var packets = new List<(ushort, byte[])>();
        while (true)
        {
            var received = await petClientPeer.ReceiveAsync(buffer, timeout.Token);
            if (received.TransportClosed || received.BytesReceived < 16)
                throw new InvalidDataException("KCP pet test did not receive a complete game packet.");
            var id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(8));
            if (id == ushort.MaxValue) return packets;
            packets.Add((id, buffer.AsSpan(12, length).ToArray()));
        }
    }
    Task<List<(ushort Id, byte[] Body)>> Request<TReq>(Handler handler, TReq req) where TReq : IMessage =>
        CapturePet(() => handler.OnHandle(petConnection, [], req.ToByteArray()));
    T Last<T>(List<(ushort Id, byte[] Body)> packets, int cmd, MessageParser<T> parser) where T : IMessage<T> =>
        parser.ParseFrom(packets.Last(p => p.Id == cmd).Body);

    var freshPets = Last(await Request(new HandlerGetPetDataCsReq(), new GetPetDataCsReq()), CmdIds.GetPetDataScRsp,
        GetPetDataScRsp.Parser);
    Check("fresh account pet data carries empty-but-present cur_pet and lineup", freshPets.MPJNPINHCJL.Count == 0
        && freshPets.PLKBLNMOOPH is { LLGIIPALELD: EOGNOEJHAGN.Nlcdgipgfdj } && freshPets.LCFKGCJCHBO is { POJGKJENENA.Count: 0 }
        && freshPets.UnlockedPetId.Count == 0);

    var petA = blindBoxPetItems[0];
    var petB = blindBoxPetItems[1];
    var granted = await CapturePet(async () =>
    {
        await petPlayer.InventoryManager!.AddItem(petA, 1, false);
        await petPlayer.InventoryManager.AddItem(petA, 1, false);
        await petPlayer.InventoryManager.AddItems([new() { ItemId = petB, Count = 1 }, new() { ItemId = gen1PetItems[0], Count = 1 }], false);
    });
    var addedPets = granted.Where(p => p.Id == CmdIds.PetDataChangeScNotify)
        .SelectMany(p => PetDataChangeScNotify.Parser.ParseFrom(p.Body).DGNGMJMPFGK)
        .Where(change => change.MMCGBGDJIPNCase == CBHJJCBDJHA.MMCGBGDJIPNOneofCase.AHBNBNFABBO)
        .Select(change => change.AHBNBNFABBO).ToList();
    Check("each obtained blind box pet copy is pushed as an add_pet instance, gen-1 pets are not",
        addedPets.Select(p => (int)p.PetId).OrderBy(x => x).SequenceEqual(new[] { petA, petA, petB }.OrderBy(x => x))
        && addedPets.Select(p => p.EBAAFDHEPLO).Distinct().Count() == 3);
    var petMaterialIds = blindBoxPetItems.Concat(gen1PetItems).Select(id => (uint)id).ToHashSet();
    Check("pet items never ride the PlayerSync material list", granted.Where(p => p.Id == CmdIds.PlayerSyncScNotify)
        .All(p => PlayerSyncScNotify.Parser.ParseFrom(p.Body).MaterialList.All(m => !petMaterialIds.Contains(m.Tid))));
    var bag = GetBagScRsp.Parser.ParseFrom(new PacketGetBagScRsp(petPlayer).Data);
    Check("pet items are kept server-side but left out of GetBagScRsp", bag.MaterialList.All(m => !petMaterialIds.Contains(m.Tid))
        && petPlayer.InventoryManager.GetItem(petA)?.Count == 2 && petPlayer.InventoryManager.GetItem(gen1PetItems[0])?.Count == 1);

    var petData = Last(await Request(new HandlerGetPetDataCsReq(), new GetPetDataCsReq()), CmdIds.GetPetDataScRsp,
        GetPetDataScRsp.Parser);
    var instanceIds = petData.MPJNPINHCJL.Select(p => p.EBAAFDHEPLO).ToList();
    Check("GetPetData lists one instance per blind box copy with stable ids and summon ref 3",
        petData.MPJNPINHCJL.Count == 3 && instanceIds.SequenceEqual(addedPets.Select(p => p.EBAAFDHEPLO))
        && petData.MPJNPINHCJL.All(p => p.PetId >= 251501) && petData.PLKBLNMOOPH.LLGIIPALELD == EOGNOEJHAGN.Lcfkgcjchbo);

    var lineupReq = new SetBlindBoxPetLineupCsReq();
    for (var i = 0; i < 3; i++) lineupReq.POJGKJENENA.Add(new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[i], Slot = (uint)(i + 1) });
    var setLineup = await Request(new HandlerSetBlindBoxPetLineupCsReq(), lineupReq);
    var lineupRsp = Last(setLineup, CmdIds.SetBlindBoxPetLineupScRsp, SetBlindBoxPetLineupScRsp.Parser);
    var lineupNotify = Last(setLineup, CmdIds.PetDataChangeScNotify, PetDataChangeScNotify.Parser);
    Check("blind box lineup is stored and announced before the response", lineupRsp.Retcode == 0
        && lineupRsp.LCFKGCJCHBO.POJGKJENENA.Select(s => s.EBAAFDHEPLO).SequenceEqual(instanceIds)
        && lineupNotify.DGNGMJMPFGK.Single().DFNCMMIMKHG.POJGKJENENA.Count == 3
        && setLineup.FindIndex(p => p.Id == CmdIds.PetDataChangeScNotify) < setLineup.FindIndex(p => p.Id == CmdIds.SetBlindBoxPetLineupScRsp)
        && pets.Data.Lineup.Select(s => s.UniqueId).SequenceEqual(instanceIds));

    var badRequests = new List<SetBlindBoxPetLineupCsReq>
    {
        new() { POJGKJENENA = { new EPFJLNKPEPK { EBAAFDHEPLO = 12345, Slot = 1 } } },
        new() { POJGKJENENA = { new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[0], Slot = 1 }, new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[0], Slot = 2 } } },
        new() { POJGKJENENA = { new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[0], Slot = 1 }, new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[1], Slot = 1 } } },
        new() { POJGKJENENA = { new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[0], Slot = 1 }, new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[1], Slot = 2 },
            new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[2], Slot = 3 }, new EPFJLNKPEPK { EBAAFDHEPLO = instanceIds[2] + 1, Slot = 4 } } }
    };
    var badResults = new List<bool>();
    foreach (var bad in badRequests)
    {
        var rejected = await Request(new HandlerSetBlindBoxPetLineupCsReq(), bad);
        badResults.Add(Last(rejected, CmdIds.SetBlindBoxPetLineupScRsp, SetBlindBoxPetLineupScRsp.Parser).Retcode == (uint)Retcode.RetReqParaInvalid
            && rejected.All(p => p.Id != CmdIds.PetDataChangeScNotify));
    }
    Check("unowned, duplicate-instance, duplicate-slot and over-wide lineups are rejected without changes",
        badResults.All(x => x) && pets.Data.Lineup.Select(s => s.UniqueId).SequenceEqual(instanceIds));

    var summon = Last(await Request(new HandlerSummonPetCsReq(), new SummonPetCsReq { LLGIIPALELD = EOGNOEJHAGN.Lcfkgcjchbo }),
        CmdIds.SummonPetScRsp, SummonPetScRsp.Parser);
    var recall = Last(await Request(new HandlerRecallPetCsReq(), new RecallPetCsReq()), CmdIds.RecallPetScRsp, RecallPetScRsp.Parser);
    var afterRecall = Last(await Request(new HandlerGetPetDataCsReq(), new GetPetDataCsReq()), CmdIds.GetPetDataScRsp,
        GetPetDataScRsp.Parser);
    Check("summon echoes cur_pet, recall answers an empty cur_pet and is remembered",
        summon.PLKBLNMOOPH.LLGIIPALELD == EOGNOEJHAGN.Lcfkgcjchbo && recall.PLKBLNMOOPH != null
        && recall.PLKBLNMOOPH.LLGIIPALELD == EOGNOEJHAGN.Nlcdgipgfdj && afterRecall.PLKBLNMOOPH.LLGIIPALELD == EOGNOEJHAGN.Nlcdgipgfdj);

    var pool = Last(await Request(new HandlerBlindBoxGetDataCsReq(), new BlindBoxGetDataCsReq()), CmdIds.BlindBoxGetDataScRsp,
        BlindBoxGetDataScRsp.Parser);
    Check("blind box data answers the live pool 1001", pool.CEGOIBGJDBO.Count == 1 && pool.CEGOIBGJDBO[0].PoolId == 1001
        && pool.CEGOIBGJDBO[0].EndTime > Extensions.GetUnixSec());

    await petPlayer.InventoryManager.RemoveItem(petB, 1);
    Check("removing a pet copy drops its instance and prunes it from the lineup", pets.Data.Instances.Count == 2
        && pets.Data.Instances.All(i => i.PetId == petA) && pets.Data.Lineup.Select(s => s.UniqueId).SequenceEqual(instanceIds.Take(2)));

    // relic presets
    var relicId = GameData.RelicConfigData.Keys.First(id => GameData.ItemConfigData.ContainsKey(id));
    var relic = await petPlayer.InventoryManager.AddItem(relicId, 1, false);
    var presetAdd = Last(await Request(new HandlerRelicPresetAddPlanCsReq(), new RelicPresetAddPlanCsReq
    {
        AvatarId = 1001, Name = "preset", RelicIds = { (uint)relic!.UniqueId }
    }), CmdIds.RelicPresetAddPlanScRsp, RelicPresetAddPlanScRsp.Parser);
    var foreignRelic = Last(await Request(new HandlerRelicPresetAddPlanCsReq(), new RelicPresetAddPlanCsReq
    {
        AvatarId = 1001, Name = "foreign", RelicIds = { 987654 }
    }), CmdIds.RelicPresetAddPlanScRsp, RelicPresetAddPlanScRsp.Parser);
    var unownedAvatar = Last(await Request(new HandlerRelicPresetAddPlanCsReq(), new RelicPresetAddPlanCsReq
    {
        AvatarId = 1999, Name = "nobody", RelicIds = { (uint)relic.UniqueId }
    }), CmdIds.RelicPresetAddPlanScRsp, RelicPresetAddPlanScRsp.Parser);
    var emptyUpdate = Last(await Request(new HandlerRelicPresetUpdatePlanCsReq(), new RelicPresetUpdatePlanCsReq
    {
        UniqueId = presetAdd.RelicPlan.UniqueId
    }), CmdIds.RelicPresetUpdatePlanScRsp, RelicPresetUpdatePlanScRsp.Parser);
    var renamed = Last(await Request(new HandlerRelicPresetUpdatePlanCsReq(), new RelicPresetUpdatePlanCsReq
    {
        UniqueId = presetAdd.RelicPlan.UniqueId, Name = "renamed"
    }), CmdIds.RelicPresetUpdatePlanScRsp, RelicPresetUpdatePlanScRsp.Parser);
    Check("relic presets accept owned relics and reject unknown relics, unowned avatars and empty updates",
        presetAdd.Retcode == 0 && presetAdd.RelicPlan.RelicIds.SequenceEqual([(uint)relic.UniqueId])
        && foreignRelic.Retcode == (uint)Retcode.RetReqParaInvalid && unownedAvatar.Retcode == (uint)Retcode.RetReqParaInvalid
        && emptyUpdate.Retcode == (uint)Retcode.RetFail && renamed.Retcode == 0 && renamed.Name == "renamed"
        && petPlayer.InventoryManager.Data.RelicPresetPlans.Count == 1
        && petPlayer.InventoryManager.Data.RelicPresetPlans.Values.Single().Name == "renamed");

    // lineup favourite
    var favourite = Last(await Request(new HandlerMarkPresetLineupCsReq(), new MarkPresetLineupCsReq { Index = 2, IsFavourite = true }),
        CmdIds.SetTeamFavourite, SetTeamFavourite.Parser);
    var badFavourite = Last(await Request(new HandlerMarkPresetLineupCsReq(), new MarkPresetLineupCsReq { Index = 11, IsFavourite = true }),
        CmdIds.SetTeamFavourite, SetTeamFavourite.Parser);
    Check("MarkPresetLineup answers cmd 773 and stores the favourite flag", favourite.Retcode == 0 && favourite.Index == 2
        && favourite.IsFavourite && petPlayer.LineupManager!.GetLineup(2)!.ToProto().IsFavourite
        && badFavourite.Retcode == (uint)Retcode.RetLineupNotExist);

    petPlayer.Connection = null;
    await petOutbound.StopAsync();
    await petInbound.StopAsync();
}
var petRelogin = new PlayerInstance(petPlayer.Data);
await petRelogin.InitializeAsync();
var reloginPets = petRelogin.GetComponent<PetManager>().ToGetPetDataProto();
Check("pet instances and lineup survive a relogin with the same ids",
    reloginPets.MPJNPINHCJL.Select(p => p.EBAAFDHEPLO).SequenceEqual(pets.Data.Instances.Select(i => i.UniqueId))
    && reloginPets.LCFKGCJCHBO.POJGKJENENA.Count == 2 && reloginPets.LCFKGCJCHBO.CGBJOAJEMKN == HENPKNCKGGI.Oalpdgkgpbj);
DatabaseHelper.MarkDirty(petPlayer.Uid);
DatabaseHelper.Instance!.SaveDatabase();
await using (var petDb = new SqliteConnection($"Data Source={Path.Combine(scratch, "regression.db")}"))
{
    await petDb.OpenAsync();
    var storedPets = EntityStores.Map[typeof(March7thHoney.Database.Pet.PlayerPetData)].LoadAll(petDb)
        .OfType<March7thHoney.Database.Pet.PlayerPetData>().Single(x => x.Uid == petPlayer.Uid);
    var storedLineups = EntityStores.Map[typeof(March7thHoney.Database.Lineup.LineupData)].LoadAll(petDb)
        .OfType<March7thHoney.Database.Lineup.LineupData>().Single(x => x.Uid == petPlayer.Uid);
    Check("pet instances, lineup and the lineup favourite flag round-trip through the database",
        storedPets.Instances.Select(i => i.UniqueId).SequenceEqual(pets.Data.Instances.Select(i => i.UniqueId))
        && storedPets.Lineup.Count == 2 && storedPets.NextSerial == pets.Data.NextSerial && storedPets.CurPetRefType == 0
        && storedLineups.Lineups[2].IsFavourite && !storedLineups.Lineups[0].IsFavourite);
}

// lineup slots: a legacy save with gaps, an extra lineup inserted first and a current index on a missing slot
var lineupData = petRelogin.LineupManager!.Data;
var keptLineup = lineupData.Lineups[0];
var extraLineup = new March7thHoney.Database.Lineup.LineupInfo { Name = "", LineupType = (int)ExtraLineupType.LineupChallenge, BaseAvatars = [] };
var fifthLineup = new March7thHoney.Database.Lineup.LineupInfo { Name = "five", BaseAvatars = [new() { BaseAvatarId = 1001 }] };
lineupData.Lineups.Clear();
lineupData.Lineups[11] = extraLineup;
lineupData.Lineups[5] = fifthLineup;
lineupData.Lineups[0] = new March7thHoney.Database.Lineup.LineupInfo { Name = "", BaseAvatars = [] };
lineupData.CurLineup = 3;
lineupData.CurExtraLineup = -1;
var lineupRelogin = new PlayerInstance(petRelogin.Data);
await lineupRelogin.InitializeAsync();
var allLineups = GetAllLineupDataScRsp.Parser.ParseFrom(
    new March7thHoney.GameServer.Server.Packet.Send.Lineup.PacketGetAllLineupDataScRsp(lineupRelogin).Data);
var normalLineups = allLineups.LineupList.Take(9).ToList();
Check("legacy lineups are padded to nine slots whose wire index matches the slot",
    Enumerable.Range(0, 9).All(lineupData.Lineups.ContainsKey) && normalLineups.All(l => l.ExtraLineupType == ExtraLineupType.LineupNone)
    && normalLineups.Take(9).Select(l => l.Index).SequenceEqual(Enumerable.Range(0, 9).Select(i => (uint)i))
    && normalLineups[5].Name == "five");
Check("a current index on an empty slot falls back to the first non-empty lineup",
    lineupData.CurLineup == 5 && lineupRelogin.LineupManager!.GetCurLineup() == fifthLineup);
lineupData.Lineups[0] = keptLineup;
lineupData.CurLineup = 0;

var lineupBytes = MemoryPackSerializer.Serialize(new March7thHoney.Database.Lineup.LineupInfo { Name = "old", Mp = 3, BaseAvatars = [] });
// Trailing members: IsFavourite (bool), IsVirtual (bool), PlaneId (int).
March7thHoney.Database.Lineup.LineupInfo? LegacyLineup(int droppedMembers, int droppedBytes)
{
    var legacy = new byte[lineupBytes.Length - droppedBytes];
    legacy[0] = (byte)(lineupBytes[0] - droppedMembers);
    Array.Copy(lineupBytes, 1, legacy, 1, legacy.Length - 1);
    return MemoryPackSerializer.Deserialize<March7thHoney.Database.Lineup.LineupInfo>(legacy);
}
Check("lineups saved before the favourite flag still deserialize", lineupBytes[0] == 8
    && LegacyLineup(3, 6) is { Name: "old", Mp: 3, IsFavourite: false, IsVirtual: false, PlaneId: 0 });
Check("lineups saved before the virtual-lineup fields still deserialize",
    LegacyLineup(2, 5) is { Name: "old", Mp: 3, IsFavourite: false, IsVirtual: false, PlaneId: 0 });

// Trailblazer current path coherence at load
var pathHero = lineupRelogin.AvatarManager!.GetHero()!;
lineupRelogin.Data.CurrentGender = Gender.Woman;
pathHero.AvatarId = 8001;
var genderFixed = lineupRelogin.AvatarManager.NormalizeHeroCurPath();
Check("a male current path on a female save is retargeted to the female twin",
    genderFixed && pathHero.AvatarId == 8002 && lineupRelogin.Data.CurBasicType == 8002 && pathHero.PathInfos.ContainsKey(8002)
    && !lineupRelogin.AvatarManager.NormalizeHeroCurPath());
pathHero.AvatarId = 1001;
lineupRelogin.AvatarManager.NormalizeHeroCurPath();
var heroAvatarData = GetAvatarDataScRsp.Parser.ParseFrom(new PacketGetAvatarDataScRsp(lineupRelogin).Data);
Check("a non-path current id falls back to an owned path of the player's gender that the wire announces",
    pathHero.AvatarId == 8002 && heroAvatarData.AvatarList.Single(a => a.BaseAvatarId == 8001).CurMultiPathAvatarType == 8002
    && heroAvatarData.AvatarPathDataInfoList.Any(p => p.AvatarId == 8002));

// video keys
var savedVideoKeys = GameData.VideoKeysConfig;
GameData.VideoKeysConfig = Newtonsoft.Json.JsonConvert.DeserializeObject<VideoKeysConfig>(
    File.ReadAllText(Path.Combine(repository, "Config/Custom/VideoKeysConfig.json")))!;
var videoKeys = GetVideoVersionKeyScRsp.Parser.ParseFrom(
    new March7thHoney.GameServer.Server.Packet.Send.Player.PacketGetVideoVersionKeyScRsp().Data);
var video5600 = videoKeys.VideoKeyInfoList.Single(v => v.Id == 5600);
Check("video keys put video_key on HHCAKADKHAN and ps_field_3 on BOGECOHIINH", video5600.HHCAKADKHAN == 455385195335115081
    && video5600.IBDIMHPOPOJ == 453193354110313382 && video5600.BOGECOHIINH == 10148726845613043778
    && videoKeys.ActivityVideoKeyInfoList.Count > 0 && videoKeys.ActivityVideoKeyInfoList.All(v => v.HHCAKADKHAN != 0));
GameData.VideoKeysConfig = savedVideoKeys;

Console.WriteLine($"Login/scene regression: {checks} checks, {failures.Count} failures. Temporary artifacts: {scratch}");
Environment.ExitCode = failures.Count == 0 ? 0 : 1;

sealed class ProbeEntity : BaseGameEntity
{
    public override int EntityId { get; set; }
    public override int GroupId { get; set; }
    public override SceneEntityInfo ToProto() => new() { EntityId = (uint)EntityId, GroupId = (uint)GroupId };
}

sealed class ProbeSender(int uid = 0) : ICommandSender
{
    public List<string> Messages { get; } = [];
    public ValueTask SendMsg(string msg) { Messages.Add(msg); return ValueTask.CompletedTask; }
    public bool HasPermission(string permission) => true;
    public int GetSender() => uid;
    public bool IsConsole => uid == 0;
}

sealed class ProbeConnection(KcpConversation? conversation = null) : Connection(conversation!, new IPEndPoint(IPAddress.Loopback, 60001))
{
    public override void Start() { }
}

sealed class LoopbackKcpTransport : IKcpTransport
{
    private readonly Channel<UdpReceiveResult> packets = Channel.CreateUnbounded<UdpReceiveResult>();
    private Task? pump;
    public KcpConversation? Peer { get; set; }
    public void Start() => pump = Task.Run(async () =>
    {
        await foreach (var packet in packets.Reader.ReadAllAsync())
            await Peer!.InputPakcetAsync(packet);
    });
    public async Task StopAsync()
    {
        packets.Writer.TryComplete();
        if (pump != null) await pump.WaitAsync(TimeSpan.FromSeconds(5));
    }
    public ValueTask SendPacketAsync(Memory<byte> packet, IPEndPoint remoteEndpoint, CancellationToken cancellationToken)
    {
        // Like UDP, a send completes when queued, not when the remote peer has processed it.
        // Awaiting Input inside the sender's flush can deadlock two peers exchanging acknowledgments.
        packets.Writer.TryWrite(new UdpReceiveResult(packet.ToArray(), remoteEndpoint));
        return ValueTask.CompletedTask;
    }
}
