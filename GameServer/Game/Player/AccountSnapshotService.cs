using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using March7thHoney.Database;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Generated;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.GameServer.Server;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Player;

/// <summary>A clone, snapshot or restore request that cannot be carried out; the message is safe to show.</summary>
public sealed class AccountSnapshotException(string message, bool notFound = false) : Exception(message)
{
    public bool NotFound { get; } = notFound;
}

public sealed class AccountSnapshotMeta
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("created_at")] public long CreatedAt { get; set; }
    [JsonPropertyName("source_uid")] public int SourceUid { get; set; }
    [JsonPropertyName("game_version")] public string GameVersion { get; set; } = "";
    [JsonPropertyName("player_level")] public int PlayerLevel { get; set; }
    [JsonPropertyName("frontier_main_mission")] public int FrontierMainMission { get; set; }
    [JsonPropertyName("floor_id")] public int FloorId { get; set; }
    [JsonPropertyName("table_count")] public int TableCount { get; set; }
}

internal sealed class AccountSnapshotFile
{
    [JsonPropertyName("format")] public int Format { get; set; } = 1;
    [JsonPropertyName("meta")] public AccountSnapshotMeta Meta { get; set; } = new();

    // Table name -> column name -> column text (BLOB columns base64, see EntityStore.Export).
    [JsonPropertyName("tables")]
    public Dictionary<string, Dictionary<string, string?>> Tables { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(AccountSnapshotFile))]
internal partial class AccountSnapshotJsonContext : JsonSerializerContext;

/// <summary>
///     Copies a uid's whole gameplay save to another uid, or to and from named snapshot files under
///     <c>Snapshots/</c> next to the database (default <c>Config/Database/Snapshots</c>): they are player save data
///     and share its location and handling. Accounts (credentials, role, tokens, bans) are never copied. Every copy
///     goes through the generated persistence round trip, so the target never shares an object with the source.
/// </summary>
public static partial class AccountSnapshotService
{
    private static readonly Logger Logger = new("Snapshot");
    private static readonly object FileLock = new();

    // Friendships and support-borrower rewards name other players' uids; a copied save starts without them.
    private static readonly HashSet<Type> SocialTables = [typeof(FriendData), typeof(AssistRewardData)];

    public static string SnapshotDirectory => Path.Combine(ConfigManager.Config.Path.DatabasePath, "Snapshots");

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex SnapshotIdPattern();

    public static bool IsValidSnapshotId(string? id) => id != null && SnapshotIdPattern().IsMatch(id);

    /// <summary>Replaces <paramref name="targetUid" />'s save with a deep copy of <paramref name="sourceUid" />'s.</summary>
    /// <returns>Whether the target was online (and was kicked).</returns>
    public static async Task<bool> CloneGameplayAsync(int sourceUid, int targetUid, string? nickname = null)
    {
        if (DatabaseHelper.Instance == null) throw new AccountSnapshotException("Database is not initialized");
        if (sourceUid == targetUid) throw new AccountSnapshotException("Source and target UID are the same");

        if (Listener.GetActiveConnection(sourceUid) != null) DatabaseHelper.Instance.SaveUidData(sourceUid);
        var rows = DatabaseHelper.CloneUidGameplayData(sourceUid)
            .Where(row => !SocialTables.Contains(row.GetType()))
            .ToList();
        if (!rows.OfType<PlayerData>().Any())
            throw new AccountSnapshotException($"UID {sourceUid} has no gameplay data", true);

        return await ReplaceAsync(targetUid, rows, nickname);
    }

    public static List<AccountSnapshotMeta> List()
    {
        var dir = new DirectoryInfo(SnapshotDirectory);
        if (!dir.Exists) return [];

        var result = new List<AccountSnapshotMeta>();
        foreach (var file in dir.GetFiles("*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file.Name);
            if (!IsValidSnapshotId(id)) continue;
            try
            {
                var meta = ReadFile(id).Meta;
                meta.Id = id;
                result.Add(meta);
            }
            catch (AccountSnapshotException e)
            {
                Logger.Warn($"Skipping snapshot {file.Name}: {e.Message}");
            }
        }

        return result.OrderByDescending(meta => meta.CreatedAt).ThenBy(meta => meta.Id).ToList();
    }

    /// <summary>Writes <paramref name="uid" />'s current save as a new snapshot named <paramref name="name" />.</summary>
    public static AccountSnapshotMeta Save(int uid, string name, string? description)
    {
        if (DatabaseHelper.Instance == null) throw new AccountSnapshotException("Database is not initialized");
        name = name.Trim();
        if (name.Length is 0 or > 64) throw new AccountSnapshotException("Snapshot name must be 1-64 characters");
        description = (description ?? "").Trim();
        if (description.Length > 512) throw new AccountSnapshotException("Snapshot description is too long");

        if (Listener.GetActiveConnection(uid) != null) DatabaseHelper.Instance.SaveUidData(uid);
        var rows = DatabaseHelper.CloneUidGameplayData(uid)
            .Where(row => !SocialTables.Contains(row.GetType()))
            .ToList();
        var player = rows.OfType<PlayerData>().FirstOrDefault()
                     ?? throw new AccountSnapshotException($"UID {uid} has no gameplay data", true);

        var file = new AccountSnapshotFile
        {
            Meta = new AccountSnapshotMeta
            {
                Name = name,
                Description = description,
                CreatedAt = Extensions.GetUnixSec(),
                SourceUid = uid,
                GameVersion = GameConstants.GAME_VERSION,
                PlayerLevel = player.Level,
                FrontierMainMission = rows.OfType<MissionData>().FirstOrDefault()?.TrackingMainMissionId ?? 0,
                FloorId = player.FloorId,
                TableCount = rows.Count
            }
        };
        foreach (var row in rows)
        {
            var store = EntityStores.Map[row.GetType()];
            file.Tables[store.Table] = store.Export(row);
        }

        lock (FileLock)
        {
            Directory.CreateDirectory(SnapshotDirectory);
            var baseId = Slug(name);
            var id = baseId;
            for (var n = 2; File.Exists(PathOf(id)); n++) id = $"{baseId}-{n}";
            file.Meta.Id = id;

            var path = PathOf(id);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(file, AccountSnapshotJsonContext.Default.AccountSnapshotFile));
            File.Move(temp, path);
        }

        return file.Meta;
    }

    public static bool Delete(string id)
    {
        RequireValidId(id);
        lock (FileLock)
        {
            var path = PathOf(id);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
    }

    public static AccountSnapshotMeta Get(string id)
    {
        RequireValidId(id);
        var meta = ReadFile(id).Meta;
        meta.Id = id;
        return meta;
    }

    /// <summary>Replaces <paramref name="uid" />'s save with snapshot <paramref name="id" />.</summary>
    /// <returns>Whether the uid was online (and was kicked).</returns>
    public static async Task<bool> RestoreAsync(string id, int uid, string? nickname = null)
    {
        if (DatabaseHelper.Instance == null) throw new AccountSnapshotException("Database is not initialized");
        RequireValidId(id);
        var file = ReadFile(id);

        var stores = EntityStores.Map.Values.ToDictionary(store => store.Table, StringComparer.Ordinal);
        var rows = new List<BaseDatabaseDataHelper>();
        foreach (var (table, columns) in file.Tables)
        {
            if (!stores.TryGetValue(table, out var store) || !DatabaseHelper.IsGameplayType(store.Type))
            {
                Logger.Warn($"Snapshot {id}: ignoring unknown table {table}");
                continue;
            }

            if (SocialTables.Contains(store.Type)) continue;
            try
            {
                rows.Add(store.Import(columns));
            }
            catch (Exception e)
            {
                throw new AccountSnapshotException($"Snapshot {id}: table {table} cannot be read ({e.GetType().Name})");
            }
        }

        if (!rows.OfType<PlayerData>().Any()) throw new AccountSnapshotException($"Snapshot {id} has no player data");
        return await ReplaceAsync(uid, rows, nickname);
    }

    private static async Task<bool> ReplaceAsync(int uid, List<BaseDatabaseDataHelper> rows, string? nickname)
    {
        var connection = Listener.GetActiveConnection(uid);
        if (connection != null)
        {
            await ConnectionDisconnectHelper.KickByGmAsync(connection);
            if (connection.IsOnline) throw new AccountSnapshotException("Session could not be stopped; retry");
        }

        nickname = nickname?.Trim();
        if (!string.IsNullOrEmpty(nickname))
            foreach (var player in rows.OfType<PlayerData>())
                player.Name = nickname;

        DatabaseHelper.ReplaceUidGameplayData(uid, rows);
        return connection != null;
    }

    private static AccountSnapshotFile ReadFile(string id)
    {
        var path = PathOf(id);
        if (!File.Exists(path)) throw new AccountSnapshotException($"Snapshot {id} not found", true);
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path),
                       AccountSnapshotJsonContext.Default.AccountSnapshotFile)
                   ?? throw new AccountSnapshotException($"Snapshot {id} is empty");
        }
        catch (JsonException)
        {
            throw new AccountSnapshotException($"Snapshot {id} is not a valid snapshot file");
        }
    }

    private static void RequireValidId(string id)
    {
        if (!IsValidSnapshotId(id)) throw new AccountSnapshotException("Invalid snapshot id");
    }

    private static string PathOf(string id) => Path.Combine(SnapshotDirectory, id + ".json");

    private static string Slug(string name)
    {
        var chars = name.ToLowerInvariant()
            .Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' ? c : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal);
        if (slug.Length > 48) slug = slug[..48].TrimEnd('-');
        return slug.Length == 0 ? "snapshot" : slug;
    }
}
