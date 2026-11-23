using March7thHoney.Data;
using March7thHoney.Data.Excel;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Season ownership for the GridFight tables that hold several seasons at once. From 4.6.51 on,
/// GridFightRoleBasicInfo/TraitBasicInfo/Equipment/Consumables/Forge/CraftConfig keep season 1 and
/// season 2 rows side by side, so every pool must be filtered by the running season. Rows without
/// season information (camps lost SeasonID, caches that predate GridFightSeasonItem, synthetic test
/// rows) are shared by every season.
/// </summary>
public static class GridFightSeasonScope
{
    public const string SeasonConstName = "GridFight_Season_ID";

    public static uint CurrentSeason =>
        GameData.GridFightConstValueData.GetValueOrDefault(SeasonConstName)?.AsUInt32() ?? 0;

    public static bool IsRoleInSeason(GridFightRoleBasicInfoExcel role, uint season) =>
        season == 0 || role.SeasonID == 0 || role.SeasonID == season;

    public static bool IsRoleInSeason(uint roleId, uint season) =>
        GameData.GridFightRoleBasicInfoData.TryGetValue(roleId, out var role) && IsRoleInSeason(role, season);

    public static bool IsTraitInSeason(uint traitId, uint season) =>
        season == 0 ||
        !GameData.GridFightTraitBasicInfoData.TryGetValue(traitId, out var trait) ||
        trait.SeasonID == 0 || trait.SeasonID == season;

    public static bool IsItemInSeason(uint itemId, uint season) =>
        season == 0 ||
        !GameData.GridFightSeasonItemData.TryGetValue(itemId, out var seasons) ||
        seasons.Contains(season);

    public static bool IsCraftInSeason(GridFightCraftConfigExcel recipe, uint season) =>
        season == 0 ||
        (!GameData.GridFightSeasonCraftData.TryGetValue(recipe.CraftID, out var seasons) || seasons.Contains(season)) &&
        IsItemInSeason(recipe.CraftEquipID, season);

    public static bool IsCampInSeason(GridFightCampExcel camp, uint season) =>
        season == 0 || camp.SeasonID == 0 || camp.SeasonID == season;

    public static bool IsPortalInSeason(uint portalId, uint season) =>
        season == 0 ||
        GameData.GridFightSeasonPortalData.GetValueOrDefault(season)?.Contains(portalId) == true;

    public static bool IsAugmentInSeason(uint augmentId, uint season) =>
        season == 0 ||
        GameData.GridFightSeasonAugmentData.GetValueOrDefault(season)?.Contains(augmentId) == true;

    /// <summary>
    /// GridFightStageRoute has no season column: each season numbers its division routes from season * 1000
    /// (S1 1000-1800 and 1801, 23 nodes; S2 2000-2800 and 2801, 16 nodes); ids below 1000 are tutorial and
    /// first-play routes.
    /// </summary>
    public static bool IsRouteInSeason(uint routeId, uint season) =>
        season == 0 || routeId / 1000 == season;

    public static IEnumerable<GridFightEquipmentExcel> Equipment(uint season) =>
        GameData.GridFightEquipmentData.Values.Where(row => IsItemInSeason(row.ID, season));

    public static IEnumerable<GridFightCraftConfigExcel> Crafts(uint season) =>
        GameData.GridFightCraftConfigData.Values.Where(row => IsCraftInSeason(row, season));

    public static IEnumerable<GridFightRoleBasicInfoExcel> Roles(uint season) =>
        GameData.GridFightRoleBasicInfoData.Values.Where(row => IsRoleInSeason(row, season));

    public const string InitialExceptRolesConstName = "GridFight_InitialExceptRoleIdList";

    /// <summary>
    /// Roles that are never handed out on their own, as GridFightResourceCatalog.RolePool treats them for the
    /// shop: from season 2 on GridFight_InitialExceptRoleIdList seeds the client's GridFightTeam._ExceptedRoleIDs.
    /// Tenglong (丹恒•腾荒, 141402) exists only as the Dan Heng trait (双龙吟) summon, and the Aha (星神★阿哈) tiers
    /// above the sold one are upgrade products. Season 1 keeps its capture-backed pools and applies the list to
    /// the opening roles only.
    /// </summary>
    public static HashSet<uint> RewardExcludedRoleIds(uint season) =>
        season == 1
            ? []
            : (GameData.GridFightConstValueData.GetValueOrDefault(InitialExceptRolesConstName)?.AsUInt32List() ?? [])
                .ToHashSet();

    /// <summary>
    /// SpecificAvatar bonuses carry an avatar id. Season 1 role ids equal their avatar id, while
    /// season 2 roles are numbered avatar * 100 + 2 (plus cost variants), so map the parameter to
    /// the running season's role for that avatar.
    /// </summary>
    public static uint ResolveRoleId(uint roleOrAvatarId, uint season)
    {
        if (GameData.GridFightRoleBasicInfoData.TryGetValue(roleOrAvatarId, out var direct) &&
            IsRoleInSeason(direct, season))
            return roleOrAvatarId;
        var seasonal = Roles(season)
            .Where(role => role.AvatarID == roleOrAvatarId)
            .OrderByDescending(role => role.IsInPool)
            .ThenBy(role => role.ID)
            .FirstOrDefault();
        return seasonal?.ID ?? 0;
    }
}
