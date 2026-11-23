// Merges the accounts and save data of other March7thHoney servers into one database.
//
//   dotnet AccountMigration.dll --target <db> --source <name>=<db>[:<uidOffset>] [...] --report <json> [--dry-run]
//   dotnet AccountMigration.dll --validate <db>
//   dotnet AccountMigration.dll --check-password <salt> <hash> <iterations> <password>
//
// Rules (see docs/operations/account-migration.md):
// - Banned accounts are skipped; AuthAttempt / IdentityBlock / DeletedAccount rows are not copied.
// - New uid = old uid + the source's offset; on a clash the account moves to a free uid from 30000 up.
// - A username already taken (case-insensitive) gets "_<source>" appended; a duplicate email is
//   replaced by uid<newUid>@web-account.invalid on the later account (both still log in by username).
// - Uids of other players inside save blobs (friends, chat, assist avatars, challenge owners, cake
//   likes) are rewritten with the same mapping; references to players that were not migrated are
//   dropped. The system profile and global chat uids are kept.
// - Game session tokens are cleared, so every player logs in again with the old password.
// - Everything is written in one transaction. Afterwards every MemoryPack column of every row is
//   deserialized with the server's own types, and the transaction is only committed if all of them load.

using System.Reflection;
using System.Text.Json;
using March7thHoney.Database;
using March7thHoney.Database.Challenge;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.Quests;
using March7thHoney.Database.Scene;
using March7thHoney.Database.TrainCakeCatch;
using March7thHoney.Util.Security;
using MemoryPack;
using Microsoft.Data.Sqlite;

if (args.Length >= 5 && args[0] == "--check-password")
{
    var ok = AuthSecurity.VerifyPassword(args[4], args[1], args[2], int.Parse(args[3]));
    Console.WriteLine(ok ? "password OK" : "password MISMATCH");
    return ok ? 0 : 1;
}

if (args.Length == 2 && args[0] == "--validate")
{
    using var con = Open(args[1], readOnly: true);
    var failures = Validate(con, null);
    return failures == 0 ? 0 : 1;
}

string? targetPath = null, reportPath = null;
var dryRun = false;
var sources = new List<(string Name, string Path, int Offset)>();
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--target": targetPath = args[++i]; break;
        case "--report": reportPath = args[++i]; break;
        case "--dry-run": dryRun = true; break;
        case "--source":
        {
            var spec = args[++i];
            var eq = spec.IndexOf('=');
            var name = spec[..eq];
            var rest = spec[(eq + 1)..];
            var colon = rest.LastIndexOf(':');
            var offset = 0;
            if (colon > 1 && int.TryParse(rest[(colon + 1)..], out var parsed)) { offset = parsed; rest = rest[..colon]; }
            sources.Add((name, rest, offset));
            break;
        }
        default: throw new ArgumentException($"Unknown argument {args[i]}");
    }
}
if (targetPath == null || reportPath == null || sources.Count == 0)
    throw new ArgumentException("--target, --report and at least one --source are required");

// Synthetic uids that are not players and must never be remapped (ConfigContainer defaults).
int[] systemUids = [5201314, 98];
string[] skipTables = ["AuthAttempt", "IdentityBlock", "DeletedAccount"];

using var target = Open(targetPath, readOnly: false);
using var tx = target.BeginTransaction();

var takenUids = new HashSet<int>(Query(target, tx, "select Uid from Account union select Uid from DeletedAccount", r => r.GetInt32(0)));
var takenNames = new HashSet<string>(Query(target, tx, "select Username from Account", r => r.GetString(0).Trim().ToLowerInvariant()));
var takenEmails = new HashSet<string>(Query(target, tx, "select coalesce(NormalizedEmail, lower(trim(Email)), '') from Account", r => r.GetString(0)).Where(e => e != ""));
var targetTables = Query(target, tx, "select name from sqlite_master where type='table'", r => r.GetString(0)).ToHashSet();
var nextFreeUid = 30000;
var entityTypes = EntityTypes();
var report = new List<Dictionary<string, object?>>();
var summary = new Dictionary<string, object>();

foreach (var (name, path, offset) in sources)
{
    using var src = Open(path, readOnly: true);
    var players = Query(src, null, "select Uid from Player", r => r.GetInt32(0)).ToHashSet();
    var accounts = Query(src, null,
        "select Uid, Username, coalesce(Email,''), coalesce(NormalizedEmail,''), IsBanned, PasswordSalt, PasswordHash, PasswordIterations, CreatedAt, LastSeenAt, RegistrationIp from Account order by Uid",
        r => new SourceAccount(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), Long(r, 4) != 0,
            Text(r, 5), Text(r, 6), (int)Long(r, 7), Long(r, 8), Long(r, 9), Text(r, 10)));

    var map = new Dictionary<int, int>();
    var renames = new Dictionary<int, (string Username, string? Email)>();
    int banned = 0, renamed = 0, moved = 0, emailPlaceholders = 0;
    foreach (var a in accounts)
    {
        if (a.IsBanned) { banned++; continue; }

        var newUid = a.Uid + offset;
        if (newUid <= 0 || takenUids.Contains(newUid))
        {
            while (takenUids.Contains(nextFreeUid)) nextFreeUid++;
            newUid = nextFreeUid++;
            moved++;
        }
        takenUids.Add(newUid);
        map[a.Uid] = newUid;

        var username = a.Username.Trim();
        if (takenNames.Contains(username.ToLowerInvariant()))
        {
            var suffix = "_" + name;
            var candidate = Truncate(username, 32 - suffix.Length) + suffix;
            for (var n = 2; takenNames.Contains(candidate.ToLowerInvariant()); n++)
                candidate = Truncate(username, 32 - suffix.Length - n.ToString().Length) + suffix + n;
            username = candidate;
            renamed++;
        }
        takenNames.Add(username.ToLowerInvariant());

        var originalEmail = (a.NormalizedEmail != "" ? a.NormalizedEmail : a.Email).Trim().ToLowerInvariant();
        var realEmail = originalEmail != "" && !originalEmail.EndsWith(".invalid") ? originalEmail : null;
        string? newEmail = null;
        if (originalEmail != "" && takenEmails.Contains(originalEmail))
        {
            newEmail = $"uid{newUid}@web-account.invalid";
            emailPlaceholders++;
        }
        else if (originalEmail != "") takenEmails.Add(originalEmail);

        if (username != a.Username || newEmail != null) renames[a.Uid] = (username, newEmail);

        report.Add(new()
        {
            ["source"] = name, ["oldUid"] = a.Uid, ["uid"] = newUid, ["username"] = username,
            ["originalUsername"] = a.Username, ["email"] = realEmail, ["gameEmail"] = newEmail ?? originalEmail,
            ["passwordSalt"] = a.PasswordSalt, ["passwordHash"] = a.PasswordHash, ["passwordIterations"] = a.PasswordIterations,
            ["createdAt"] = a.CreatedAt, ["lastSeenAt"] = a.LastSeenAt, ["registrationIp"] = a.RegistrationIp,
            ["hasPlayer"] = players.Contains(a.Uid),
        });
    }

    int? MapUid(int uid) => systemUids.Contains(uid) ? uid : map.TryGetValue(uid, out var n) ? n : null;

    var copied = new Dictionary<string, int>();
    var tables = Query(src, null, "select name from sqlite_master where type='table' order by name", r => r.GetString(0));
    foreach (var table in tables.Where(t => !skipTables.Contains(t)))
    {
        if (!targetTables.Contains(table)) throw new InvalidOperationException($"target has no table {table}");
        var columns = Query(src, null, $"pragma table_info('{table}')", r => r.GetString(1));
        var targetColumns = Query(target, tx, $"pragma table_info('{table}')", r => r.GetString(1)).ToHashSet();
        var missing = columns.Where(c => !targetColumns.Contains(c)).ToList();
        if (missing.Count > 0) throw new InvalidOperationException($"target {table} lacks columns {string.Join(",", missing)}");

        using var insert = target.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = $"insert into \"{table}\" ({string.Join(",", columns.Select(c => $"\"{c}\""))}) values ({string.Join(",", columns.Select((_, i) => $"@p{i}"))})";
        var parameters = columns.Select((_, i) => insert.Parameters.Add(new SqliteParameter($"@p{i}", null))).ToArray();

        using var read = src.CreateCommand();
        read.CommandText = $"select {string.Join(",", columns.Select(c => $"\"{c}\""))} from \"{table}\"";
        using var reader = read.ExecuteReader();
        var count = 0;
        while (reader.Read())
        {
            var oldUid = reader.GetInt32(0);
            if (!map.TryGetValue(oldUid, out var newUid)) continue;
            for (var i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                if (column == "Uid") value = newUid;
                else if (value is byte[] blob)
                    value = RepairDuplicateKeys(entityTypes, table, column, RewriteBlob(table, column, blob, oldUid, newUid, MapUid), $"{name} uid {oldUid}");
                else if (table == "Account") value = RewriteAccount(column, value, renames.GetValueOrDefault(oldUid));
                parameters[i].Value = value ?? DBNull.Value;
            }
            insert.ExecuteNonQuery();
            count++;
        }
        copied[table] = count;
    }

    summary[name] = new
    {
        accounts = accounts.Count, skippedBanned = banned, migrated = map.Count, uidsMoved = moved,
        usernamesRenamed = renamed, emailsReplaced = emailPlaceholders, rows = copied,
    };
    Console.WriteLine($"{name}: {JsonSerializer.Serialize(summary[name])}");
}

var validationFailures = Validate(target, tx);
if (validationFailures > 0 || dryRun)
{
    tx.Rollback();
    Console.WriteLine(validationFailures > 0 ? $"ROLLED BACK: {validationFailures} rows failed to load" : "dry run: rolled back");
}
else
{
    tx.Commit();
    Console.WriteLine("committed");
}

File.WriteAllText(reportPath, JsonSerializer.Serialize(new { summary, accounts = report }));
return validationFailures == 0 ? 0 : 1;

// ---------------------------------------------------------------------------

static SqliteConnection Open(string path, bool readOnly)
{
    var con = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWrite, Pooling = false,
    }.ToString());
    con.Open();
    return con;
}

static List<T> Query<T>(SqliteConnection con, SqliteTransaction? tx, string sql, Func<SqliteDataReader, T> row)
{
    using var cmd = con.CreateCommand();
    cmd.Transaction = tx;
    cmd.CommandText = sql;
    using var reader = cmd.ExecuteReader();
    var list = new List<T>();
    while (reader.Read()) list.Add(row(reader));
    return list;
}

static string Truncate(string s, int max) => s.Length <= max ? s : s[..Math.Max(1, max)];

static long Long(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : r.GetInt64(i);

static string? Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

static object? RewriteAccount(string column, object? value, (string Username, string? Email) rename) => column switch
{
    "Username" when rename.Username != null => rename.Username,
    "Email" or "NormalizedEmail" when rename.Email != null => rename.Email,
    // Sessions and one-time links belong to the old server.
    "ComboToken" or "ComboTokenHash" or "DispatchToken" or "DispatchTokenHash"
        or "EmailVerificationTokenHash" or "PasswordResetTokenHash" => null,
    "ComboTokenExpireAt" or "DispatchTokenExpireAt" or "EmailVerificationTokenExpireAt" or "PasswordResetTokenExpireAt" => 0L,
    _ => value,
};

static byte[] RewriteBlob(string table, string column, byte[] blob, int oldUid, int newUid, Func<int, int?> mapUid)
{
    List<int> MapList(List<int>? list) =>
        (list ?? []).Select(mapUid).Where(u => u.HasValue).Select(u => u!.Value).Distinct().ToList();

    void MapLineup(List<LineupAvatarInfo>? avatars)
    {
        if (avatars == null) return;
        foreach (var a in avatars.Where(a => a.AssistUid != 0)) a.AssistUid = mapUid(a.AssistUid) ?? 0;
    }

    int Owner(int uid) => uid == oldUid ? newUid : mapUid(uid) ?? newUid;

    switch (table, column)
    {
        case ("Friend", "FriendDetailList"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, FriendDetailData>>(blob) ?? [];
            var result = new Dictionary<int, FriendDetailData>();
            foreach (var (uid, detail) in d)
                if (mapUid(uid) is { } n) result[n] = detail;
            return MemoryPackSerializer.Serialize(result);
        }
        case ("Friend", "FriendList" or "BlackList" or "SendApplyList" or "ReceiveApplyList"):
            return MemoryPackSerializer.Serialize(MapList(MemoryPackSerializer.Deserialize<List<int>>(blob)));
        case ("Friend", "ChatHistory"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, FriendChatHistory>>(blob) ?? [];
            var result = new Dictionary<int, FriendChatHistory>();
            foreach (var (uid, history) in d)
            {
                if (mapUid(uid) is not { } n) continue;
                foreach (var m in history.MessageList)
                {
                    m.SendUid = m.SendUid == oldUid ? newUid : mapUid(m.SendUid) ?? 0;
                    m.ReceiveUid = m.ReceiveUid == oldUid ? newUid : mapUid(m.ReceiveUid) ?? 0;
                }
                result[n] = history;
            }
            return MemoryPackSerializer.Serialize(result);
        }
        case ("Lineup", "Lineups"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, LineupInfo>>(blob) ?? [];
            foreach (var info in d.Values) MapLineup(info.BaseAvatars);
            return MemoryPackSerializer.Serialize(d);
        }
        case ("RaidData", "RaidRecordDatas"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, Dictionary<int, RaidRecord>>>(blob) ?? [];
            foreach (var record in d.Values.SelectMany(x => x.Values)) MapLineup(record.Lineup);
            return MemoryPackSerializer.Serialize(d);
        }
        case ("RaidData", "RaidRecordData"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, RaidRecord>>(blob) ?? [];
            foreach (var record in d.Values) MapLineup(record.Lineup);
            return MemoryPackSerializer.Serialize(d);
        }
        case ("StoryLineData", "RunningStoryLines"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, StoryLineInfo>>(blob) ?? [];
            foreach (var info in d.Values) MapLineup(info.Lineup);
            return MemoryPackSerializer.Serialize(d);
        }
        case ("Challenge", "History"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, ChallengeHistoryData>>(blob) ?? [];
            foreach (var h in d.Values) h.OwnerId = Owner(h.OwnerId);
            return MemoryPackSerializer.Serialize(d);
        }
        case ("Challenge", "TakenRewards"):
        {
            var d = MemoryPackSerializer.Deserialize<Dictionary<int, ChallengeGroupReward>>(blob) ?? [];
            foreach (var r in d.Values) r.OwnerUid = Owner(r.OwnerUid);
            return MemoryPackSerializer.Serialize(d);
        }
        case ("TrainCakeCatch", "DiyLikeIds"):
            return MemoryPackSerializer.Serialize(MapList(MemoryPackSerializer.Deserialize<List<int>>(blob)));
        default:
            return blob;
    }
}

static Dictionary<string, Type> EntityTypes() => typeof(BaseDatabaseDataHelper).Assembly.GetTypes()
    .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(BaseDatabaseDataHelper)))
    .ToDictionary(t => t.GetCustomAttribute<DbTableAttribute>()?.Name ?? t.Name);

// Some old rows hold a dictionary (possibly nested deep inside) written with the same key twice; the
// server can't load those. Only for such a row: swap in dictionary formatters that keep the last value
// per key (what the indexer would have done), read it, put the standard formatters back, write it again.
static byte[] RepairDuplicateKeys(Dictionary<string, Type> entityTypes, string table, string column, byte[] blob, string where)
{
    if (!entityTypes.TryGetValue(table, out var entity)) return blob;
    var type = entity.GetProperty(column)?.PropertyType;
    if (type == null || type == typeof(byte[]) || type == typeof(string) || type.IsPrimitive || type.IsEnum) return blob;
    try
    {
        MemoryPackSerializer.Deserialize(type, blob);
        return blob;
    }
    catch (ArgumentException)
    {
        var dictionaries = new HashSet<Type>();
        CollectDictionaryTypes(type, dictionaries, []);
        foreach (var d in dictionaries) RegisterFormatter(d, typeof(LenientDictionaryFormatter<,>));
        object? value;
        try { value = MemoryPackSerializer.Deserialize(type, blob); }
        finally { foreach (var d in dictionaries) RegisterFormatter(d, typeof(MemoryPack.Formatters.DictionaryFormatter<,>)); }
        Console.WriteLine($"  repaired duplicate dictionary keys in {table}.{column} for {where}");
        return MemoryPackSerializer.Serialize(type, value);
    }
}

static void RegisterFormatter(Type dictionaryType, Type formatterDefinition)
{
    var formatter = Activator.CreateInstance(formatterDefinition.MakeGenericType(dictionaryType.GetGenericArguments()))!;
    typeof(MemoryPackFormatterProvider).GetMethods()
        .First(m => m.Name == "Register" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
        .MakeGenericMethod(dictionaryType).Invoke(null, [formatter]);
}

// Every closed Dictionary<,> reachable from a type through generic arguments and serialized members.
static void CollectDictionaryTypes(Type type, HashSet<Type> found, HashSet<Type> seen)
{
    if (!seen.Add(type) || type.IsPrimitive || type.IsEnum || type == typeof(string)) return;
    if (type.IsGenericType)
    {
        if (type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) found.Add(type);
        foreach (var arg in type.GetGenericArguments()) CollectDictionaryTypes(arg, found, seen);
    }
    if (type.IsArray) CollectDictionaryTypes(type.GetElementType()!, found, seen);
    if (type.Namespace?.StartsWith("March7thHoney") != true) return;
    foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) CollectDictionaryTypes(p.PropertyType, found, seen);
    foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) CollectDictionaryTypes(f.FieldType, found, seen);
}

// Deserializes every MemoryPack column of every row with the server's own entity types, the way the
// generated stores do on startup. Returns the number of rows that failed.
static int Validate(SqliteConnection con, SqliteTransaction? tx)
{
    var entityTypes = EntityTypes();

    var failures = 0;
    foreach (var table in Query(con, tx, "select name from sqlite_master where type='table' order by name", r => r.GetString(0)))
    {
        if (!entityTypes.TryGetValue(table, out var type)) { Console.WriteLine($"  {table}: no entity type, skipped"); continue; }
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<DbIgnoreAttribute>() == null)
            .ToDictionary(p => p.Name);
        using var cmd = con.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"select * from \"{table}\"";
        using var reader = cmd.ExecuteReader();
        int rows = 0, bad = 0;
        string? firstError = null;
        while (reader.Read())
        {
            rows++;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i) || reader.GetValue(i) is not byte[] blob) continue;
                if (!props.TryGetValue(reader.GetName(i), out var prop) || prop.PropertyType == typeof(byte[])) continue;
                if (typeof(Google.Protobuf.IMessage).IsAssignableFrom(prop.PropertyType)) continue;
                try
                {
                    MemoryPackSerializer.Deserialize(prop.PropertyType, blob);
                }
                catch (Exception ex)
                {
                    bad++;
                    firstError ??= $"uid {reader.GetInt32(0)} {reader.GetName(i)}: {ex.GetType().Name} {ex.Message}";
                    break;
                }
            }
        }
        failures += bad;
        if (bad > 0) Console.WriteLine($"  {table}: {bad}/{rows} rows FAILED, e.g. {firstError}");
    }
    Console.WriteLine(failures == 0 ? "validation: every row loads" : $"validation: {failures} rows failed");
    return failures;
}

// Reads the standard MemoryPack dictionary layout but lets a repeated key overwrite the earlier one.
sealed class LenientDictionaryFormatter<TKey, TValue> : MemoryPackFormatter<Dictionary<TKey, TValue?>> where TKey : notnull
{
    public override void Serialize<TBufferWriter>(ref MemoryPackWriter<TBufferWriter> writer, scoped ref Dictionary<TKey, TValue?>? value) =>
        throw new NotSupportedException("only used for reading");

    public override void Deserialize(ref MemoryPackReader reader, scoped ref Dictionary<TKey, TValue?>? value)
    {
        if (!reader.TryReadCollectionHeader(out var length))
        {
            value = null;
            return;
        }
        var keyFormatter = reader.GetFormatter<TKey>();
        var valueFormatter = reader.GetFormatter<TValue>();
        value = new Dictionary<TKey, TValue?>(length);
        for (var i = 0; i < length; i++)
        {
            MemoryPack.Formatters.KeyValuePairFormatter.Deserialize(keyFormatter, valueFormatter, ref reader, out var key, out var item);
            value[key!] = item;
        }
    }
}

record SourceAccount(int Uid, string Username, string Email, string NormalizedEmail, bool IsBanned, string? PasswordSalt,
    string? PasswordHash, int PasswordIterations, long CreatedAt, long LastSeenAt, string? RegistrationIp);
