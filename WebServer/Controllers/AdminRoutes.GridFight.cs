using March7thHoney.Data;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.GameServer.Server;
using March7thHoney.Util;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;

namespace March7thHoney.WebServer.Controllers;

/// <summary>
///     Currency War (GridFight) panel. Both endpoints are read-only; the page changes a run through the existing
///     command endpoint with /grid subcommands, so every edit takes the same reward/sync path as the in-game
///     command and is logged like any other admin command.
/// </summary>
public static partial class AdminRoutes
{
    private static readonly Logger GridFightLogger = new("AdminGridFight");
    private static GridFightNameTable? _gridFightNames;

    private static void MapGridFightRoutes(IEndpointRouteBuilder api)
    {
        api.MapGet("/gridfight/catalog", GetGridFightCatalog);
        api.MapGet("/gridfight/{uid:int}", GetGridFightStatus);
    }

    private static IResult GetGridFightStatus(int uid)
    {
        var player = Listener.GetActiveConnection(uid)?.Player;
        if (player == null)
            return Success(new GridFightStatusData(uid, false, "", null));

        var nickname = player.Data?.Name ?? "";
        if (player.GridFightManager?.Session is not { } session)
            return Success(new GridFightStatusData(uid, true, nickname, null));

        // The game thread mutates the session while this request reads it; a list changed mid-copy throws, so take
        // the snapshot again rather than returning a torn view.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return Success(new GridFightStatusData(uid, true, nickname, SnapshotRun(session)));
            }
            catch (InvalidOperationException ex) when (attempt < 5)
            {
                GridFightLogger.Debug($"Snapshot of uid {uid} raced the game thread (attempt {attempt}): {ex.Message}");
            }
        }
    }

    private static GridFightRunData SnapshotRun(GridFightSession session)
    {
        var equipped = session.Roles.ToList().SelectMany(role => role.EquippedEquipmentUniqueIds.ToList()).ToHashSet();
        var roles = session.Roles.ToList()
            .Select(role =>
            {
                var info = GameData.GridFightRoleBasicInfoData.GetValueOrDefault(role.RoleId);
                return new GridFightRoleBrief(role.UniqueId, role.RoleId, info?.AvatarID ?? 0, info?.Rarity ?? 0,
                    role.Star, role.Position, role.Position is >= 1 and <= 13,
                    role.EquippedEquipmentUniqueIds.ToList()
                        .Select(id => session.Equipments.FirstOrDefault(item => item.UniqueId == id)?.EquipmentId ?? 0)
                        .Where(id => id != 0).ToList());
            })
            .OrderByDescending(role => role.on_board).ThenBy(role => role.position)
            .ToList();
        var traits = session.ResolveTraitLayers().Values
            .Select(state => new GridFightTraitBrief(state.TraitId, state.Layer, state.Members.Count))
            .OrderByDescending(trait => trait.layer).ThenByDescending(trait => trait.members).ThenBy(trait => trait.trait_id)
            .ToList();
        var equipments = session.Equipments.ToList()
            .Select(item => new GridFightEquipmentBrief(item.UniqueId, item.EquipmentId,
                GameData.GridFightEquipmentData.GetValueOrDefault(item.EquipmentId)?.EquipCategory.ToString() ?? "",
                equipped.Contains(item.UniqueId)))
            .ToList();
        var consumables = session.Consumables.ToList()
            .Select(item => new GridFightConsumableBrief(item.ItemId, item.Count))
            .ToList();

        return new GridFightRunData(
            session.Season, session.DivisionId, session.ChapterId, session.SectionId, session.Phase.ToString(),
            session.SelectedPortalId, session.Gold, session.PlayerLevel, session.PlayerExp, session.MaxPlayerLevel,
            session.LineupHp, session.MaxLineupHp, session.FreeRefreshCount, session.KeepWinCount, session.MaxActiveRoles,
            session.TrailblazeCoin, session.TrailblazeCostedCoin, session.ActiveInvestmentIds.ToList(),
            roles, traits, equipments, consumables);
    }

    private static IResult GetGridFightCatalog()
    {
        var season = GridFightSeasonScope.CurrentSeason;
        var roles = GameData.GridFightRoleBasicInfoData.Values
            .Where(role => role.SeasonID == season && !role.IsFakeAvatar)
            .OrderBy(role => role.Rarity).ThenBy(role => role.ID)
            .Select(role => new GridFightCatalogRole(role.ID, role.AvatarID, role.Rarity, role.TraitList.ToList()))
            .ToList();
        var seasonAugments = GameData.GridFightSeasonAugmentData.GetValueOrDefault(season)?
            .ToHashSet();
        var augments = GameData.GridFightAugmentData.Values
            .Where(augment => seasonAugments == null || seasonAugments.Contains(augment.ID))
            .OrderBy(augment => augment.Quality).ThenBy(augment => augment.ID)
            .Select(augment => new GridFightCatalogAugment(augment.ID, augment.Quality.ToString(), augment.CategoryID))
            .ToList();
        var traits = GameData.GridFightTraitBasicInfoData.Values
            .Where(trait => trait.SeasonID == season)
            .Select(trait => trait.ID).Order().ToList();
        var equipments = GameData.GridFightEquipmentData.Values
            .OrderBy(item => item.EquipCategory).ThenBy(item => item.ID)
            .Select(item => new GridFightCatalogEquipment(item.ID, item.EquipCategory.ToString()))
            .ToList();
        var consumables = GameData.GridFightConsumablesData.Keys.Order().ToList();

        return Success(new GridFightCatalogData(season, roles, augments, traits, equipments, consumables, LoadGridFightNames()));
    }

    /// <summary>
    ///     Config/Custom/GridFightNames.json maps ids to Chinese names (generated from the client tables and TextMap).
    ///     Optional: without it, or for ids it lacks, the page shows the bare id.
    /// </summary>
    private static GridFightNameTable LoadGridFightNames()
    {
        if (_gridFightNames != null) return _gridFightNames;
        var empty = new GridFightNameTable([], [], [], [], []);
        var path = Path.Combine(ConfigManager.Config.Path.ConfigPath, "Custom", "GridFightNames.json");
        try
        {
            if (File.Exists(path))
                return _gridFightNames = JsonConvert.DeserializeObject<GridFightNameTable>(File.ReadAllText(path)) ?? empty;
        }
        catch (Exception ex)
        {
            GridFightLogger.Debug($"Failed to read {path}: {ex.Message}");
        }

        return _gridFightNames = empty;
    }
}
