using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Generated;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Lineup;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.Proto;
using March7thHoney.Util;
using Newtonsoft.Json;

namespace March7thHoney.GameServer.Game.Player;

public sealed record ProfileEdit(
    string Name, string Signature, int HeadIcon, int Level, int Exp, int WorldLevel, int Scoin, int Hcoin, int Mcoin,
    int Stamina, double StaminaReserve, long MonthCardExpireTime);

/// <summary>Promotion -1 picks the lowest ascension the level allows. MaxTraces raises every trace to its cap.</summary>
public sealed record AvatarEdit(int Level, int Promotion, int Rank, bool MaxTraces);

public sealed record LightConeEdit(int Level, int Promotion, int Rank);

public sealed record ItemGrant(int ItemId, int Count, int Level, int Rank, int Promotion);

public sealed record LineupSlotEdit(int BaseAvatarId, int SpecialAvatarId);

public sealed record MissionAction(string Action, int MissionId, int Count, bool Strict);

public sealed record RawTableInfo(string Table, string Json, bool Editable, string Version);

/// <summary>
///     The admin player editor's changes, each applied to a save loaded by <see cref="OfflinePlayerSession.EditAsync" />.
///     They change the save the way the game would (the managers add avatars and items; missions run their hooks),
///     and refuse values the client cannot show.
/// </summary>
public static class PlayerDataEditor
{
    public const int MaxGrantCopies = 100;

    private static readonly JsonSerializerSettings RawJson = new()
    {
        Formatting = Formatting.Indented,
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    public static int MaxPlayerLevel => GameData.PlayerLevelConfigData.Count > 0 ? GameData.PlayerLevelConfigData.Keys.Max() : 70;
    public static int MaxWorldLevel => GameConstants.UpgradeWorldLevel.Count;

    /// <summary>A short content hash: an edit names the version it was made on, so a save changed since is refused.</summary>
    public static string Version(byte[] content) => Convert.ToHexString(SHA256.HashData(content))[..16].ToLowerInvariant();

    public static void RequireVersion(string? expected, string current)
    {
        if (!string.IsNullOrEmpty(expected) && expected != current)
            throw new PlayerEditException("The save changed since it was loaded; reload and apply the change again",
                PlayerEditErrorEnum.Conflict);
    }

    #region Profile

    public static async ValueTask SetProfile(PlayerInstance player, ProfileEdit edit)
    {
        var name = edit.Name.Trim();
        if (name.Length is 0 or > 32) Invalid("Nickname must be 1-32 characters");
        if (edit.Signature.Length > 120) Invalid("Signature must be at most 120 characters");
        if (edit.Level < 1 || edit.Level > MaxPlayerLevel) Invalid($"Trailblaze level must be 1-{MaxPlayerLevel}");
        if (edit.WorldLevel < 0 || edit.WorldLevel > MaxWorldLevel) Invalid($"Equilibrium level must be 0-{MaxWorldLevel}");
        if (edit.Exp < 0 || edit.Scoin < 0 || edit.Hcoin < 0 || edit.Mcoin < 0 || edit.MonthCardExpireTime < 0)
            Invalid("Experience, currencies and the month card cannot be negative");
        if (edit.Stamina < 0 || edit.Stamina > GameConstants.MAX_STAMINA * 8)
            Invalid($"Trailblaze Power must be 0-{GameConstants.MAX_STAMINA * 8}");
        if (edit.StaminaReserve < 0 || edit.StaminaReserve > GameConstants.MAX_STAMINA_RESERVE)
            Invalid($"Reserved Trailblaze Power must be 0-{GameConstants.MAX_STAMINA_RESERVE}");
        if (edit.HeadIcon <= 0) Invalid("Profile picture id must be positive");

        var data = player.Data;
        var levelledUp = edit.Level > data.Level;
        data.Name = name;
        data.Signature = edit.Signature;
        data.HeadIcon = edit.HeadIcon;
        data.Level = edit.Level;
        data.Exp = edit.Exp;
        data.WorldLevel = edit.WorldLevel;
        data.Scoin = edit.Scoin;
        data.Hcoin = edit.Hcoin;
        data.Mcoin = edit.Mcoin;
        data.Stamina = edit.Stamina;
        data.StaminaReserve = edit.StaminaReserve;
        data.MonthCardExpireTime = edit.MonthCardExpireTime;
        // Missions gated on the new level open as they would on a level-up in play.
        if (levelledUp) await player.OnLevelUp();
    }

    public static void SetPosition(PlayerInstance player, int entryId)
    {
        if (!player.TryPlaceAtEntrance(entryId))
            Invalid($"Map entrance {entryId} does not exist or has no usable spawn point");
    }

    #endregion

    #region Characters

    public static async ValueTask AddAvatar(PlayerInstance player, int avatarId)
    {
        var avatars = player.AvatarManager!;
        if (!GameData.AvatarConfigData.ContainsKey(avatarId)) Invalid($"Character {avatarId} does not exist");
        if (avatars.GetFormalAvatar(avatarId)?.PathInfos.ContainsKey(avatarId) == true)
            Invalid($"Character {avatarId} is already owned");

        await avatars.AddAvatar(avatarId, false, false);
        if (avatars.GetFormalAvatar(avatarId)?.PathInfos.ContainsKey(avatarId) != true)
            Invalid($"Character {avatarId} cannot be owned (trial or unreleased character)");
    }

    public static void EditAvatar(PlayerInstance player, int avatarId, AvatarEdit edit)
    {
        var avatar = player.AvatarManager!.GetFormalAvatar(avatarId) ?? NotFound<Database.Avatar.FormalAvatarInfo>($"Character {avatarId} is not owned");
        if (edit.Level is < 1 or > 80) Invalid("Character level must be 1-80");
        if (edit.Rank is < 0 or > 6) Invalid("Eidolon must be 0-6");

        avatar.Level = edit.Level;
        avatar.Promotion = edit.Promotion < 0 ? GameData.GetMinPromotionForLevel(edit.Level) : CheckPromotion(edit.Level, edit.Promotion);
        foreach (var path in avatar.PathInfos.Values) path.Rank = edit.Rank;
        if (!edit.MaxTraces) return;

        foreach (var (pathId, path) in avatar.PathInfos)
        {
            if (!GameData.AvatarConfigData.TryGetValue(pathId, out var excel)) continue;
            foreach (var talent in excel.SkillTree.GetValueOrDefault(path.EnhanceId, []))
                path.GetSkillTree()[talent.PointID] = talent.MaxLevel;
        }
    }

    /// <summary>Removes an owned character: off every lineup, its gear unequipped. The Trailblazer stays.</summary>
    public static void RemoveAvatar(PlayerInstance player, int avatarId)
    {
        var manager = player.AvatarManager!;
        var avatar = manager.GetFormalAvatar(avatarId) ?? NotFound<Database.Avatar.FormalAvatarInfo>($"Character {avatarId} is not owned");
        var baseId = avatar.BaseAvatarId;
        if (baseId == 8001) Invalid("The Trailblazer cannot be removed");

        var lineups = player.LineupManager!;
        foreach (var lineup in lineups.Data.Lineups.Values)
        {
            if (lineup.BaseAvatars == null) continue;
            if (lineup.BaseAvatars.All(slot => slot.BaseAvatarId == baseId && slot.SpecialAvatarId == 0) &&
                lineup.BaseAvatars.Count > 0 && lineup.LineupType == 0 && lineup == lineups.GetCurLineup())
                Invalid($"Character {avatarId} is the only member of the current team; change the team first");
        }

        foreach (var lineup in lineups.Data.Lineups.Values)
        {
            lineup.BaseAvatars?.RemoveAll(slot => slot.BaseAvatarId == baseId && slot.SpecialAvatarId == 0);
            if (lineup.LeaderAvatarId == baseId) lineup.LeaderAvatarId = lineup.BaseAvatars?.FirstOrDefault()?.BaseAvatarId ?? 0;
        }

        var inventory = player.InventoryManager!.Data;
        foreach (var item in inventory.EquipmentItems.Concat(inventory.RelicItems).Where(item => item.EquipAvatar == baseId))
            item.EquipAvatar = 0;

        manager.Data.FormalAvatars.RemoveAll(item => item.BaseAvatarId == baseId);
        manager.Data.ExRankIds.Remove(baseId);
        manager.Data.AssistAvatars.Remove(baseId);
        manager.Data.DisplayAvatars.Remove(baseId);
    }

    // Promotion p is reached at level 20 + 10(p-1); a level at a cap may still be before or after that ascension.
    private static int CheckPromotion(int level, int promotion)
    {
        var min = GameData.GetMinPromotionForLevel(level);
        var max = level is >= 20 and < 80 && level % 10 == 0 ? min + 1 : min;
        if (promotion < min || promotion > max)
            Invalid(min == max ? $"Level {level} needs ascension {min}" : $"Level {level} needs ascension {min} or {max}");
        return promotion;
    }

    #endregion

    #region Inventory

    public static async ValueTask<int> GrantItem(PlayerInstance player, ItemGrant grant)
    {
        if (!GameData.ItemConfigData.TryGetValue(grant.ItemId, out var config)) Invalid($"Item {grant.ItemId} does not exist");
        var inventory = player.InventoryManager!;
        switch (config.ItemMainType)
        {
            case ItemMainTypeEnum.Equipment:
                if (grant.Count is < 1 or > MaxGrantCopies) Invalid($"Light cone copies must be 1-{MaxGrantCopies}");
                if (grant.Level is < 1 or > 80) Invalid("Light cone level must be 1-80");
                if (grant.Rank is < 1 or > 5) Invalid("Superimposition must be 1-5");
                var promotion = grant.Promotion < 0 ? GameData.GetMinPromotionForLevel(grant.Level) : CheckPromotion(grant.Level, grant.Promotion);
                for (var i = 0; i < grant.Count; i++)
                    await inventory.AddItem(grant.ItemId, 1, false, grant.Rank, grant.Level, promotion, false);
                return grant.Count;
            case ItemMainTypeEnum.Relic:
                if (grant.Count is < 1 or > MaxGrantCopies) Invalid($"Relic copies must be 1-{MaxGrantCopies}");
                if (grant.Level is < 0 or > 15) Invalid("Relic level must be 0-15");
                for (var i = 0; i < grant.Count; i++)
                    await inventory.AddItem(grant.ItemId, 1, false, level: grant.Level, sync: false);
                return grant.Count;
            case ItemMainTypeEnum.AvatarCard:
                Invalid("Characters are added from the Characters section");
                return 0;
            default:
                if (grant.Count is < 1 or > 99_999_999) Invalid("Count must be 1-99999999");
                var added = await inventory.AddItem(grant.ItemId, grant.Count, false, sync: false);
                if (added == null) Invalid($"Item {grant.ItemId} cannot be granted");
                return grant.Count;
        }
    }

    /// <summary>Sets a stackable item (material, consumable, currency item) to an exact count; 0 removes it.</summary>
    public static async ValueTask SetItemCount(PlayerInstance player, int itemId, int count)
    {
        if (!GameData.ItemConfigData.TryGetValue(itemId, out var config)) Invalid($"Item {itemId} does not exist");
        if (config.ItemMainType is ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic or ItemMainTypeEnum.AvatarCard or
            ItemMainTypeEnum.Virtual)
            Invalid($"Item {itemId} is not a stackable item");
        if (count is < 0 or > 99_999_999) Invalid("Count must be 0-99999999");

        var inventory = player.InventoryManager!;
        var item = inventory.Data.MaterialItems.Find(entry => entry.ItemId == itemId);
        if (count == 0)
        {
            if (item != null) inventory.Data.MaterialItems.Remove(item);
            return;
        }

        if (item != null) item.Count = count;
        else await inventory.PutItem(itemId, count);
    }

    public static void EditLightCone(PlayerInstance player, int uniqueId, LightConeEdit edit)
    {
        var item = player.InventoryManager!.Data.EquipmentItems.Find(entry => entry.UniqueId == uniqueId) ??
                   NotFound<ItemData>($"Light cone {uniqueId} is not owned");
        if (edit.Level is < 1 or > 80) Invalid("Light cone level must be 1-80");
        if (edit.Rank is < 1 or > 5) Invalid("Superimposition must be 1-5");
        item.Level = edit.Level;
        item.Promotion = edit.Promotion < 0 ? GameData.GetMinPromotionForLevel(edit.Level) : CheckPromotion(edit.Level, edit.Promotion);
        item.Rank = edit.Rank;
        item.Exp = 0;
    }

    /// <summary>Removes light cones and relics by unique id, taking them off whoever wears them.</summary>
    public static int RemoveGear(PlayerInstance player, IReadOnlyCollection<int> uniqueIds)
    {
        var ids = uniqueIds.ToHashSet();
        var inventory = player.InventoryManager!.Data;
        foreach (var avatar in player.AvatarManager!.Data.FormalAvatars)
        foreach (var path in avatar.PathInfos.Values)
        {
            if (ids.Contains(path.EquipId)) path.EquipId = 0;
            foreach (var slot in path.Relic.Where(entry => ids.Contains(entry.Value)).Select(entry => entry.Key).ToList())
                path.Relic.Remove(slot);
        }

        return inventory.EquipmentItems.RemoveAll(item => ids.Contains(item.UniqueId)) +
               inventory.RelicItems.RemoveAll(item => ids.Contains(item.UniqueId));
    }

    #endregion

    #region Lineups

    /// <summary>
    ///     Sets normal team <paramref name="index" /> (0-8). Story trials can only stay or go: a trial the team does not
    ///     already hold is refused.
    /// </summary>
    public static void SetLineup(PlayerInstance player, int index, IReadOnlyList<LineupSlotEdit> slots, bool makeCurrent)
    {
        if (index is < 0 or >= GameConstants.MAX_LINEUP_COUNT) Invalid($"Team must be 0-{GameConstants.MAX_LINEUP_COUNT - 1}");
        if (slots.Count > 4) Invalid("A team holds at most 4 characters");
        var lineups = player.LineupManager!;
        lineups.PadNormalLineups();
        var lineup = lineups.Data.Lineups[index];
        var trials = (lineup.BaseAvatars ?? []).Where(slot => slot.SpecialAvatarId > 0).Select(slot => slot.SpecialAvatarId).ToHashSet();

        var result = new List<LineupAvatarInfo>();
        foreach (var slot in slots)
        {
            if (slot.SpecialAvatarId > 0)
            {
                if (!trials.Contains(slot.SpecialAvatarId)) Invalid($"Trial {slot.SpecialAvatarId} is not in team {index + 1}");
                result.Add(lineup.BaseAvatars!.First(item => item.SpecialAvatarId == slot.SpecialAvatarId));
                continue;
            }

            var avatar = player.AvatarManager!.GetFormalAvatar(slot.BaseAvatarId) ??
                         NotFound<Database.Avatar.FormalAvatarInfo>($"Character {slot.BaseAvatarId} is not owned");
            if (result.Any(item => item.SpecialAvatarId == 0 && item.BaseAvatarId == avatar.BaseAvatarId))
                Invalid($"Character {slot.BaseAvatarId} is in the team twice");
            result.Add(new LineupAvatarInfo { BaseAvatarId = avatar.BaseAvatarId });
        }

        if (result.Count == 0 && (makeCurrent || lineups.Data.CurLineup == index))
            Invalid("The current team cannot be empty");

        lineup.BaseAvatars = result;
        lineup.LeaderAvatarId = result.FirstOrDefault()?.BaseAvatarId ?? 0;
        if (makeCurrent) lineups.Data.CurLineup = index;
        DatabaseHelper.MarkDirty(player.Uid);
    }

    #endregion

    #region Story

    public static void FinishTutorials(PlayerInstance player, out int count)
    {
        count = 0;
        var tutorials = player.TutorialData!;
        foreach (var config in GameData.TutorialDataData.Values)
        {
            if (tutorials.Tutorials.TryGetValue(config.TutorialID, out var status) && status == TutorialStatus.TutorialFinish)
                continue;
            tutorials.Tutorials[config.TutorialID] = TutorialStatus.TutorialFinish;
            count++;
        }
    }

    /// <summary>Runs one mission tool on the save, the same operations as the /mission command. Returns its log.</summary>
    public static async ValueTask<List<string>> RunMissionAction(PlayerInstance player, MissionAction action)
    {
        var missions = player.MissionManager!;
        var log = new List<string>();
        switch (action.Action)
        {
            case "enable":
                missions.Data.EnableMissionOverride = true;
                log.Add("mission system on for this player");
                return log;
            case "disable":
                missions.Data.EnableMissionOverride = false;
                log.Add("mission system off for this player");
                return log;
        }

        if (!missions.Data.MissionEnabled) Invalid("The mission system is off for this player; turn it on first");
        switch (action.Action)
        {
            case "advance":
                if (action.Count is < 1 or > 10) Invalid("Advance 1-10 story steps at a time");
                for (var i = 0; i < action.Count; i++)
                {
                    var step = await missions.AdvanceStory(action.Strict);
                    log.Add(step.ToString());
                    if (!step.Mutated) break;
                }

                break;
            case "rollback":
                var (anchor, predecessor, error) = await missions.RollbackStory();
                if (error != null) Invalid(error);
                log.Add($"rolled back to {anchor} (before {predecessor})");
                break;
            case "repair":
                var report = await missions.RepairStalledMainMissions(false);
                log.Add($"resumed [{string.Join(", ", report.Resumed)}] closed [{string.Join(", ", report.Closed)}] " +
                        $"stalled [{string.Join(", ", report.Stalled)}]" +
                        (report.RetrackedTo > 0 ? $" tracking {report.RetrackedTo}" : ""));
                break;
            case "finish_sub":
                RequireSub(action.MissionId);
                await missions.FinishSubMission(action.MissionId);
                log.Add($"sub-mission {action.MissionId}: {missions.GetSubMissionStatus(action.MissionId)}");
                break;
            case "finish_main":
                RequireMain(action.MissionId);
                await missions.FinishMainMission(action.MissionId);
                log.Add($"main mission {action.MissionId}: {missions.GetMainMissionStatus(action.MissionId)}");
                break;
            case "accept_main":
                RequireMain(action.MissionId);
                await missions.AcceptMainMission(action.MissionId, false);
                log.Add($"main mission {action.MissionId}: {missions.GetMainMissionStatus(action.MissionId)}");
                break;
            case "reaccept_main":
                RequireMain(action.MissionId);
                await missions.ReAcceptMainMission(action.MissionId, false);
                log.Add($"main mission {action.MissionId}: {missions.GetMainMissionStatus(action.MissionId)}");
                break;
            case "remove_main":
                RequireMain(action.MissionId);
                await missions.RemoveMainMission(action.MissionId, false);
                log.Add($"main mission {action.MissionId}: {missions.GetMainMissionStatus(action.MissionId)}");
                break;
            case "track":
                RequireMain(action.MissionId);
                if (missions.GetMainMissionStatus(action.MissionId) != MissionPhaseEnum.Accept)
                    Invalid($"Main mission {action.MissionId} is not running");
                missions.Data.TrackingMainMissionId = action.MissionId;
                log.Add($"tracking {action.MissionId}");
                break;
            default:
                Invalid($"Unknown mission action {action.Action}");
                break;
        }

        return log;
    }

    private static void RequireMain(int id)
    {
        if (!GameData.MainMissionData.ContainsKey(id)) Invalid($"Main mission {id} does not exist");
    }

    private static void RequireSub(int id)
    {
        if (!GameData.SubMissionInfoData.ContainsKey(id)) Invalid($"Sub-mission {id} does not exist");
    }

    #endregion

    #region Raw tables

    /// <summary>The gameplay tables a save can hold, by table name.</summary>
    public static List<EntityStore> RawStores() => EntityStores.Map.Values
        .Where(store => DatabaseHelper.IsGameplayType(store.Type))
        .OrderBy(store => store.Table, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    ///     One table of <paramref name="uid" />'s save as JSON. Editable only when the JSON reads back into exactly the
    ///     stored row (a type the JSON form cannot carry is shown read-only).
    /// </summary>
    public static RawTableInfo? ReadRaw(int uid, string table)
    {
        var store = RequireStore(table);
        var row = DatabaseHelper.CloneUidGameplayData(uid).Find(item => item.GetType() == store.Type);
        if (row == null) return null;

        var json = JsonConvert.SerializeObject(row, store.Type, RawJson);
        return new RawTableInfo(store.Table, json, RoundTrips(store, row, json), RowVersion(store, row));
    }

    /// <summary>Replaces one table of <paramref name="uid" />'s save with <paramref name="json" />; the rest stays.</summary>
    public static async Task<bool> WriteRawAsync(int uid, string table, string json, string? version)
    {
        var store = RequireStore(table);
        BaseDatabaseDataHelper parsed;
        try
        {
            parsed = ParseRaw(store, json, uid);
        }
        catch (Exception e)
        {
            throw new PlayerEditException($"{store.Table} cannot be read from this JSON: {e.Message}");
        }

        return await OfflinePlayerSession.ReplaceAsync(uid, $"raw edit of {store.Table}", rows =>
        {
            var current = rows.Find(row => row.GetType() == store.Type) ??
                          NotFound<BaseDatabaseDataHelper>($"UID {uid} has no {store.Table} row");
            RequireVersion(version, RowVersion(store, current));
            if (!RoundTrips(store, current, JsonConvert.SerializeObject(current, store.Type, RawJson)))
                Invalid($"{store.Table} cannot be edited as JSON");
            rows.Remove(current);
            rows.Add(parsed);
            return rows;
        });
    }

    // Whether the row's JSON reads back into exactly the stored row; a type the JSON form cannot carry stays read-only.
    private static bool RoundTrips(EntityStore store, BaseDatabaseDataHelper row, string json)
    {
        try
        {
            return SameRow(store, row, ParseRaw(store, json, row.Uid));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static EntityStore RequireStore(string table) =>
        RawStores().Find(store => store.Table == table) ?? NotFound<EntityStore>($"Unknown table {table}");

    // Through the store's own column round trip, so the parsed row is what the database will hold.
    private static BaseDatabaseDataHelper ParseRaw(EntityStore store, string json, int uid)
    {
        var row = (BaseDatabaseDataHelper?)JsonConvert.DeserializeObject(json, store.Type, RawJson) ??
                  throw new JsonSerializationException("empty document");
        row.Uid = uid;
        return store.Import(store.Export(row));
    }

    private static bool SameRow(EntityStore store, BaseDatabaseDataHelper left, BaseDatabaseDataHelper right)
    {
        var a = store.Export(left);
        var b = store.Export(right);
        return a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    private static string RowVersion(EntityStore store, BaseDatabaseDataHelper row) =>
        Version(Encoding.UTF8.GetBytes(string.Join("\n",
            store.Export(row).OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"))));

    #endregion

    [DoesNotReturn]
    private static void Invalid(string message) => throw new PlayerEditException(message);

    [DoesNotReturn]
    private static T NotFound<T>(string message) => throw new PlayerEditException(message, PlayerEditErrorEnum.NotFound);
}
