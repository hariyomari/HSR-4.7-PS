using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Inventory;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.Database.Scene;
using March7thHoney.Database.Tutorial;
using March7thHoney.Enums.Item;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Proto;
using March7thHoney.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

/// <summary>
///     The player editor: one save, section by section. Reads come from the stored save (the live one while the player
///     is online); every change runs offline through <see cref="OfflinePlayerSession" />, which kicks an online
///     player, refuses logins meanwhile and keeps the save from before the change as the undo point.
/// </summary>
public static partial class AdminRoutes
{
    private const string EditorPrefix = "/admin/api/players/{uid:int}/editor";
    private static readonly Logger EditorLogger = new("PlayerEditor");

    private static void MapPlayerEditorRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/api/editor/catalog", GetEditorCatalog);
        app.MapGet(EditorPrefix, GetEditorSummary);
        app.MapPost(EditorPrefix + "/undo", UndoEditorChange);
        app.MapGet(EditorPrefix + "/profile", GetEditorProfile);
        app.MapPut(EditorPrefix + "/profile", SetEditorProfile);
        app.MapGet(EditorPrefix + "/avatars", GetEditorAvatars);
        app.MapPost(EditorPrefix + "/avatars", AddEditorAvatar);
        app.MapPut(EditorPrefix + "/avatars/{avatarId:int}", EditEditorAvatar);
        app.MapDelete(EditorPrefix + "/avatars/{avatarId:int}", RemoveEditorAvatar);
        app.MapGet(EditorPrefix + "/inventory", GetEditorInventory);
        app.MapPost(EditorPrefix + "/inventory/grant", GrantEditorItem);
        app.MapPut(EditorPrefix + "/inventory/items/{itemId:int}", SetEditorItemCount);
        app.MapPut(EditorPrefix + "/inventory/light-cones/{uniqueId:int}", EditEditorLightCone);
        app.MapPost(EditorPrefix + "/inventory/remove", RemoveEditorGear);
        app.MapGet(EditorPrefix + "/lineups", GetEditorLineups);
        app.MapPut(EditorPrefix + "/lineups/{index:int}", SetEditorLineup);
        app.MapGet(EditorPrefix + "/story", GetEditorStory);
        app.MapPost(EditorPrefix + "/story/missions", RunEditorMissionAction);
        app.MapPost(EditorPrefix + "/story/teleports", UnlockEditorTeleports);
        app.MapPost(EditorPrefix + "/story/tutorials", FinishEditorTutorials);
        app.MapPut(EditorPrefix + "/position", SetEditorPosition);
        app.MapGet(EditorPrefix + "/raw", GetEditorRawTables);
        app.MapGet(EditorPrefix + "/raw/{table}", GetEditorRawTable);
        app.MapPut(EditorPrefix + "/raw/{table}", SetEditorRawTable);
    }

    #region Catalog and summary

    private static IResult GetEditorCatalog(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;

        var avatars = GameData.AvatarConfigData.Values
            .Where(avatar => avatar.MaxRank >= 6)
            .Select(avatar => new EditorCatalogAvatar(avatar.AvatarID,
                GameData.MultiplePathAvatarConfigData.GetValueOrDefault(avatar.AvatarID)?.BaseAvatarID ?? avatar.AvatarID,
                avatar.Rarity.ToString().LastOrDefault() is var digit && char.IsDigit(digit) ? digit - '0' : 0))
            .OrderBy(avatar => avatar.id)
            .ToList();
        List<int> Items(Func<ItemMainTypeEnum, bool> filter) => GameData.ItemConfigData.Values
            .Where(item => filter(item.ItemMainType)).Select(item => item.ID).Order().ToList();
        var entrances = GameData.MapEntranceData.Values
            .Select(entrance => new EditorCatalogEntrance(entrance.ID, entrance.PlaneID, entrance.FloorID,
                GameData.MazePlaneData.GetValueOrDefault(entrance.PlaneID)?.PlaneType.ToString() ?? ""))
            .OrderBy(entrance => entrance.id)
            .ToList();

        return Success(new EditorCatalogData(avatars,
            Items(type => type == ItemMainTypeEnum.Equipment),
            Items(type => type == ItemMainTypeEnum.Relic),
            Items(type => type is not (ItemMainTypeEnum.Equipment or ItemMainTypeEnum.Relic or ItemMainTypeEnum.AvatarCard)),
            entrances));
    }

    private static IResult GetEditorSummary(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (DatabaseHelper.Instance == null) return Error("Database is not initialized");

        var account = AccountData.GetAccountByUid(uid);
        if (account == null) return Error("Account not found", statusCode: StatusCodes.Status404NotFound);
        var player = DatabaseHelper.Instance.GetInstance<PlayerData>(uid);
        var missions = DatabaseHelper.Instance.GetInstance<MissionData>(uid);
        AccountSnapshotMeta? undo = null;
        try
        {
            undo = AccountSnapshotService.GetUndoPoint(uid);
        }
        catch (AccountSnapshotException)
        {
            // An unreadable undo file is reported as no undo point; undoing would fail the same way.
        }

        return Success(new EditorSummaryData(uid, player != null, Listener.GetActiveConnection(uid) != null,
            OfflinePlayerSession.IsHeld(uid), missions?.MissionEnabled ?? ConfigManager.Config.ServerOption.EnableMission,
            undo == null ? null : new EditorUndoView(undo.Name, undo.CreatedAt), PlayerDataEditor.MaxPlayerLevel,
            PlayerDataEditor.MaxWorldLevel, account.DeleteOnLogout));
    }

    private static async Task<IResult> UndoEditorChange(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        return await EditorCall(async () => new EditorSaved(await OfflinePlayerSession.UndoAsync(uid)));
    }

    #endregion

    #region Profile

    private static IResult GetEditorProfile(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var view = ProfileView(uid);
        return view == null
            ? Error("Player has no save", statusCode: StatusCodes.Status404NotFound)
            : Success(new EditorProfileData(view, ViewVersion(view, WebJsonContext.Default.EditorProfileView)));
    }

    private static async Task<IResult> SetEditorProfile(HttpContext ctx, int uid, AdminEditorProfileRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (request == null) return Error("Missing profile");

        var edit = new ProfileEdit(request.name ?? "", request.signature ?? "", request.head_icon, request.level,
            request.exp, request.world_level, request.scoin, request.hcoin, request.mcoin, request.stamina,
            request.stamina_reserve, request.month_card_expire);
        return await EditorEdit(uid, "profile", async player =>
        {
            await PlayerDataEditor.SetProfile(player, edit);
            return true;
        }, () => RequireViewVersion(request.version, ProfileView(uid), WebJsonContext.Default.EditorProfileView),
            (saved, _) => new EditorSaved(saved));
    }

    private static EditorProfileView? ProfileView(int uid)
    {
        var data = DatabaseHelper.Instance?.GetInstance<PlayerData>(uid);
        return data == null
            ? null
            : new EditorProfileView(data.Name ?? "", data.Signature ?? "", data.HeadIcon, data.Level, data.Exp,
                data.WorldLevel, data.Scoin, data.Hcoin, data.Mcoin, data.Stamina, data.StaminaReserve,
                data.MonthCardExpireTime, data.IsGenderSet, data.CurrentGender.ToString());
    }

    #endregion

    #region Characters

    private static IResult GetEditorAvatars(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var avatars = AvatarsView(uid);
        return Success(new EditorAvatarsData(avatars, ViewVersion(avatars, WebJsonContext.Default.ListEditorAvatarView)));
    }

    private static async Task<IResult> AddEditorAvatar(HttpContext ctx, int uid, AdminEditorAvatarRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var avatarId = request?.avatar_id ?? 0;
        return await EditorEdit(uid, $"add character {avatarId}", async player =>
        {
            await PlayerDataEditor.AddAvatar(player, avatarId);
            return true;
        }, null, (saved, _) => new EditorSaved(saved));
    }

    private static async Task<IResult> EditEditorAvatar(HttpContext ctx, int uid, int avatarId,
        AdminEditorAvatarRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (request == null) return Error("Missing character values");

        var edit = new AvatarEdit(request.level, request.promotion ?? -1, request.rank, request.max_traces);
        return await EditorEdit(uid, $"edit character {avatarId}", player =>
            {
                PlayerDataEditor.EditAvatar(player, avatarId, edit);
                return ValueTask.FromResult(true);
            }, () => RequireViewVersion(request.version, AvatarsView(uid), WebJsonContext.Default.ListEditorAvatarView),
            (saved, _) => new EditorSaved(saved));
    }

    private static async Task<IResult> RemoveEditorAvatar(HttpContext ctx, int uid, int avatarId)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        return await EditorEdit(uid, $"remove character {avatarId}", player =>
        {
            PlayerDataEditor.RemoveAvatar(player, avatarId);
            return ValueTask.FromResult(true);
        }, null, (saved, _) => new EditorSaved(saved));
    }

    private static List<EditorAvatarView> AvatarsView(int uid)
    {
        var avatars = DatabaseHelper.Instance?.GetInstance<AvatarData>(uid);
        var inventory = DatabaseHelper.Instance?.GetInstance<InventoryData>(uid);
        if (avatars == null) return [];

        return avatars.FormalAvatars
            .DistinctBy(avatar => avatar.BaseAvatarId)
            .Select(avatar =>
            {
                var path = avatar.PathInfos.GetValueOrDefault(avatar.AvatarId) ?? avatar.PathInfos.Values.FirstOrDefault();
                var lightCone = path == null ? null : inventory?.EquipmentItems.Find(item => item.UniqueId == path.EquipId);
                return new EditorAvatarView(avatar.BaseAvatarId, avatar.AvatarId, avatar.Level, avatar.Promotion,
                    path?.Rank ?? 0, avatar.PathInfos.Keys.Order().ToList(), lightCone?.UniqueId ?? 0,
                    lightCone?.ItemId ?? 0, path?.Relic.Count ?? 0, path != null && TracesMaxed(path));
            })
            .OrderBy(avatar => avatar.id)
            .ToList();
    }

    private static bool TracesMaxed(PathInfo path)
    {
        if (!GameData.AvatarConfigData.TryGetValue(path.PathId, out var excel)) return false;
        var tree = path.EnhanceInfos.GetValueOrDefault(path.EnhanceId)?.SkillTree;
        return tree != null && excel.SkillTree.GetValueOrDefault(path.EnhanceId, [])
            .All(talent => tree.GetValueOrDefault(talent.PointID) >= talent.MaxLevel);
    }

    #endregion

    #region Inventory

    private static IResult GetEditorInventory(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var inventory = DatabaseHelper.Instance?.GetInstance<InventoryData>(uid);
        if (inventory == null) return Error("Player has no inventory", statusCode: StatusCodes.Status404NotFound);

        static EditorGearView Gear(ItemData item) => new(item.UniqueId, item.ItemId, item.Level, item.Promotion,
            item.Rank, item.MainAffix, item.SubAffixes.Count, item.EquipAvatar, item.Locked);
        return Success(new EditorInventoryData(
            inventory.MaterialItems.Select(item => new EditorMaterialView(item.ItemId, item.Count)).OrderBy(item => item.item_id).ToList(),
            inventory.EquipmentItems.Select(Gear).OrderBy(item => item.unique_id).ToList(),
            inventory.RelicItems.Select(Gear).OrderBy(item => item.unique_id).ToList(),
            GameConstants.INVENTORY_MAX_EQUIPMENT, GameConstants.INVENTORY_MAX_RELIC));
    }

    private static async Task<IResult> GrantEditorItem(HttpContext ctx, int uid, AdminEditorItemRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (request == null) return Error("Missing item");

        var grant = new ItemGrant(request.item_id, request.count ?? 1, request.level ?? 1, request.rank ?? 1,
            request.promotion ?? -1);
        return await EditorEdit(uid, $"grant item {grant.ItemId}",
            player => PlayerDataEditor.GrantItem(player, grant), null,
            (saved, count) => new EditorCountResult(count, saved));
    }

    private static async Task<IResult> SetEditorItemCount(HttpContext ctx, int uid, int itemId,
        AdminEditorItemRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var count = request?.count ?? 0;
        return await EditorEdit(uid, $"set item {itemId} to {count}", async player =>
        {
            await PlayerDataEditor.SetItemCount(player, itemId, count);
            return count;
        }, null, (saved, value) => new EditorCountResult(value, saved));
    }

    private static async Task<IResult> EditEditorLightCone(HttpContext ctx, int uid, int uniqueId,
        AdminEditorItemRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (request == null) return Error("Missing light cone values");

        var edit = new LightConeEdit(request.level ?? 1, request.promotion ?? -1, request.rank ?? 1);
        return await EditorEdit(uid, $"edit light cone {uniqueId}", player =>
        {
            PlayerDataEditor.EditLightCone(player, uniqueId, edit);
            return ValueTask.FromResult(true);
        }, null, (saved, _) => new EditorSaved(saved));
    }

    private static async Task<IResult> RemoveEditorGear(HttpContext ctx, int uid, AdminEditorRemoveRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var ids = request?.unique_ids ?? [];
        if (ids.Count is < 1 or > 3000) return Error("Select 1-3000 light cones or relics");
        return await EditorEdit(uid, $"remove {ids.Count} light cones/relics",
            player => ValueTask.FromResult(PlayerDataEditor.RemoveGear(player, ids)), null,
            (saved, count) => new EditorCountResult(count, saved));
    }

    #endregion

    #region Lineups

    private static IResult GetEditorLineups(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var lineups = LineupsView(uid);
        if (lineups == null) return Error("Player has no teams", statusCode: StatusCodes.Status404NotFound);
        var data = DatabaseHelper.Instance!.GetInstance<LineupData>(uid)!;
        return Success(new EditorLineupsData(lineups, data.CurLineup, data.CurExtraLineup,
            ViewVersion(lineups, WebJsonContext.Default.ListEditorLineupView)));
    }

    private static async Task<IResult> SetEditorLineup(HttpContext ctx, int uid, int index,
        AdminEditorLineupRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (request?.slots == null) return Error("Missing team");

        var slots = request.slots.Select(slot => new LineupSlotEdit(slot.base_avatar_id, slot.special_avatar_id)).ToList();
        return await EditorEdit(uid, $"team {index + 1}", player =>
            {
                PlayerDataEditor.SetLineup(player, index, slots, request.make_current);
                return ValueTask.FromResult(true);
            }, () => RequireViewVersion(request.version, LineupsView(uid), WebJsonContext.Default.ListEditorLineupView),
            (saved, _) => new EditorSaved(saved));
    }

    private static List<EditorLineupView>? LineupsView(int uid)
    {
        var data = DatabaseHelper.Instance?.GetInstance<LineupData>(uid);
        if (data == null) return null;
        return Enumerable.Range(0, GameConstants.MAX_LINEUP_COUNT)
            .Select(index =>
            {
                var lineup = data.Lineups.GetValueOrDefault(index);
                return new EditorLineupView(index, lineup?.Name ?? "",
                    (lineup?.BaseAvatars ?? []).Select(slot => new EditorLineupSlot(slot.BaseAvatarId, slot.SpecialAvatarId)).ToList(),
                    data.CurLineup == index);
            })
            .ToList();
    }

    #endregion

    #region Story and position

    private static IResult GetEditorStory(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var story = StoryView(uid);
        return story == null ? Error("Player has no save", statusCode: StatusCodes.Status404NotFound) : Success(story);
    }

    private static async Task<IResult> RunEditorMissionAction(HttpContext ctx, int uid,
        AdminEditorMissionRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (string.IsNullOrWhiteSpace(request?.action)) return Error("Missing mission action");

        var action = new MissionAction(request.action.Trim(), request.mission_id, request.count ?? 1, request.strict);
        var label = action.MissionId > 0 ? $"mission {action.Action} {action.MissionId}" : $"mission {action.Action}";
        return await EditorEdit(uid, label,
            player => PlayerDataEditor.RunMissionAction(player, action), null,
            (saved, log) => new EditorMissionResult(log, StoryView(uid)!, saved));
    }

    private static async Task<IResult> UnlockEditorTeleports(HttpContext ctx, int uid, AdminEditorModeRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (!TryParseOption(request?.mode, "reached_planes", out var mode,
                ("reached_planes", StoryTeleportsModeEnum.ReachedPlanes), ("all", StoryTeleportsModeEnum.All)))
            return Error("Invalid mode");
        return await EditorEdit(uid, "unlock teleports",
            player => StoryCheckpointProvisioner.UnlockTeleports(player, mode), null,
            (saved, count) => new EditorCountResult(count, saved));
    }

    private static async Task<IResult> FinishEditorTutorials(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        return await EditorEdit(uid, "finish tutorials", player =>
        {
            PlayerDataEditor.FinishTutorials(player, out var count);
            return ValueTask.FromResult(count);
        }, null, (saved, count) => new EditorCountResult(count, saved));
    }

    private static async Task<IResult> SetEditorPosition(HttpContext ctx, int uid, AdminEditorPositionRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var entryId = request?.entry_id ?? 0;
        return await EditorEdit(uid, $"move to entrance {entryId}", player =>
        {
            PlayerDataEditor.SetPosition(player, entryId);
            return ValueTask.FromResult(true);
        }, null, (saved, _) => new EditorSaved(saved));
    }

    private static EditorStoryData? StoryView(int uid)
    {
        var database = DatabaseHelper.Instance;
        var player = database?.GetInstance<PlayerData>(uid);
        var missions = database?.GetInstance<MissionData>(uid);
        if (player == null || missions == null) return null;
        var scene = database!.GetInstance<SceneData>(uid);
        var tutorials = database.GetInstance<TutorialData>(uid);

        var running = missions.RunningMainMissionIds
            .Distinct()
            .Order()
            .Select(mainId => new EditorRunningMission(mainId,
                GameData.MainMissionData.TryGetValue(mainId, out var main)
                    ? main.SubMissionIds.Where(subId => missions.GetSubMissionStatus(subId) == MissionPhaseEnum.Accept).Order().ToList()
                    : []))
            .ToList();
        return new EditorStoryData(missions.MissionEnabled, missions.TrackingMainMissionId, running,
            missions.PreparedMainMissionIds.Distinct().Order().ToList(),
            missions.FinishedMainMissionIds.Distinct().Order().ToList(), missions.FinishedSubMissionIds.Count,
            player.PlaneId, player.FloorId, player.EntryId,
            scene?.UnlockedTeleportData.Values.Sum(list => list.Count) ?? 0,
            tutorials?.Tutorials.Count(entry => entry.Value == TutorialStatus.TutorialFinish) ?? 0,
            GameData.TutorialDataData.Count);
    }

    #endregion

    #region Raw tables

    private static IResult GetEditorRawTables(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var present = DatabaseHelper.CloneUidGameplayData(uid).Select(row => row.GetType()).ToHashSet();
        return Success(new EditorRawTablesData(PlayerDataEditor.RawStores()
            .Select(store => new EditorRawTableView(store.Table, present.Contains(store.Type)))
            .ToList()));
    }

    private static IResult GetEditorRawTable(HttpContext ctx, int uid, string table)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        try
        {
            var raw = PlayerDataEditor.ReadRaw(uid, table);
            return raw == null
                ? Error($"UID {uid} has no {table} row", statusCode: StatusCodes.Status404NotFound)
                : Success(new EditorRawTableData(raw.Table, raw.Json, raw.Editable, raw.Version));
        }
        catch (PlayerEditException ex)
        {
            return EditorError(ex);
        }
    }

    private static async Task<IResult> SetEditorRawTable(HttpContext ctx, int uid, string table,
        AdminEditorRawRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (string.IsNullOrWhiteSpace(request?.json)) return Error("Missing JSON");
        return await EditorCall(async () =>
            new EditorSaved(await PlayerDataEditor.WriteRawAsync(uid, table, request.json, request.version)));
    }

    #endregion

    #region Shared

    // Runs one offline edit and shapes the answer from its result and whether the player was kicked for it.
    private static Task<IResult> EditorEdit<T, TResult>(int uid, string label, Func<PlayerInstance, ValueTask<T>> edit,
        Action? verify, Func<bool, T, TResult> result) =>
        EditorCall(async () =>
        {
            if (AccountData.GetAccountByUid(uid) == null)
                throw new PlayerEditException("Account not found", PlayerEditErrorEnum.NotFound);
            var outcome = await OfflinePlayerSession.EditAsync(uid, label, edit, verify);
            return result(outcome.WasOnline, outcome.Value);
        });

    private static async Task<IResult> EditorCall<T>(Func<Task<T>> call)
    {
        try
        {
            return Success(await call());
        }
        catch (PlayerEditException ex)
        {
            return EditorError(ex);
        }
        catch (AccountSnapshotException ex)
        {
            return SnapshotError(ex);
        }
        catch (Exception ex)
        {
            // A failed edit has put the previous save back; the message tells the operator what broke.
            EditorLogger.Error("Player editor change failed", ex);
            return Error($"{ex.GetType().Name}: {ex.Message}", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static IResult EditorError(PlayerEditException ex) => ex.Kind switch
    {
        PlayerEditErrorEnum.NotFound => Error(ex.Message, statusCode: StatusCodes.Status404NotFound),
        PlayerEditErrorEnum.Conflict or PlayerEditErrorEnum.Busy => Error(ex.Message, statusCode: StatusCodes.Status409Conflict),
        _ => Error(ex.Message)
    };

    private static string ViewVersion<T>(T view, JsonTypeInfo<T> info) =>
        PlayerDataEditor.Version(JsonSerializer.SerializeToUtf8Bytes(view, info));

    private static void RequireViewVersion<T>(string? expected, T? view, JsonTypeInfo<T> info)
    {
        if (view == null) throw new PlayerEditException("Player has no save", PlayerEditErrorEnum.NotFound);
        PlayerDataEditor.RequireVersion(expected, ViewVersion(view, info));
    }

    #endregion

    public class AdminEditorProfileRequest
    {
        public string? name { get; set; }
        public string? signature { get; set; }
        public int head_icon { get; set; }
        public int level { get; set; }
        public int exp { get; set; }
        public int world_level { get; set; }
        public int scoin { get; set; }
        public int hcoin { get; set; }
        public int mcoin { get; set; }
        public int stamina { get; set; }
        public double stamina_reserve { get; set; }
        public long month_card_expire { get; set; }
        public string? version { get; set; }
    }

    public class AdminEditorAvatarRequest
    {
        public int avatar_id { get; set; }
        public int level { get; set; }
        public int? promotion { get; set; }
        public int rank { get; set; }
        public bool max_traces { get; set; }
        public string? version { get; set; }
    }

    public class AdminEditorItemRequest
    {
        public int item_id { get; set; }
        public int? count { get; set; }
        public int? level { get; set; }
        public int? rank { get; set; }
        public int? promotion { get; set; }
    }

    public class AdminEditorRemoveRequest
    {
        public List<int>? unique_ids { get; set; }
    }

    public class AdminEditorLineupRequest
    {
        public List<EditorLineupSlot>? slots { get; set; }
        public bool make_current { get; set; }
        public string? version { get; set; }
    }

    public class AdminEditorMissionRequest
    {
        public string? action { get; set; }
        public int mission_id { get; set; }
        public int? count { get; set; }
        public bool strict { get; set; }
    }

    public class AdminEditorModeRequest
    {
        public string? mode { get; set; }
    }

    public class AdminEditorPositionRequest
    {
        public int entry_id { get; set; }
    }

    public class AdminEditorRawRequest
    {
        public string? json { get; set; }
        public string? version { get; set; }
    }
}
