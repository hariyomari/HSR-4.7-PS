using March7thHoney.Data.Excel;

namespace March7thHoney.Data.GridFight;

/// <summary>Materializes the split season table into the existing cached camp model.</summary>
public static class GridFightTableCompatibility
{
    public static void Apply(IReadOnlyList<ExcelResource> rows)
    {
        var seasons = rows.OfType<GridFightCampSeasonExcel>().ToList();
        if (seasons.Count == 0) return; // Legacy combined tables keep their existing layout.
        var currentSeason = GameData.GridFightConstValueData.GetValueOrDefault("GridFight_Season_ID")?.AsUInt32() ?? 0;
        if (currentSeason == 0) throw new InvalidDataException("Split GridFight tables require a current season.");
        var basics = rows.OfType<GridFightCampExcel>().ToDictionary(row => row.ID);
        var materialized = new Dictionary<uint, GridFightCampExcel>();
        foreach (var season in seasons)
        {
            if (season.SeasonID == 0 || season.CampID == 0 || !basics.TryGetValue(season.CampID, out var basic))
                throw new InvalidDataException($"GridFight camp season references an invalid camp: {season.SeasonID}/{season.CampID}.");
            var camp = new GridFightCampExcel
            {
                ID = basic.ID, SeasonID = season.SeasonID, BossBattleArea = basic.BossBattleArea,
                IfRandomEnabled = basic.IfRandomEnabled, InitialRandomCode = basic.InitialRandomCode,
                BattleAreaList = [.. basic.BattleAreaList], MonsterList = [.. season.MonsterList],
            };
            // Public IDs resolve to the active season. Inactive seasons retain
            // distinct internal keys; no protocol ID or cached object ID is changed.
            var key = camp.SeasonID == currentSeason ? camp.ID : SeasonKey(camp.SeasonID, camp.ID);
            if (!materialized.TryAdd(key, camp))
                throw new InvalidDataException($"Duplicate GridFight camp season: {camp.SeasonID}/{camp.ID}.");
            if (camp.MonsterList.Any(id => !GameData.GridFightMonsterData.ContainsKey(id)))
                throw new InvalidDataException($"GridFight camp {camp.SeasonID}/{camp.ID} references missing monsters.");
            camp.AfterAllDone();
        }
        GameData.GridFightCampData.Clear();
        foreach (var pair in materialized) GameData.GridFightCampData.Add(pair.Key, pair.Value);
        foreach (var role in GameData.GridFightRoleBasicInfoData.Values)
            role.UsesNativeSkillOverrides = role.RoleSavedValueList.Count == 0;
    }

    private static uint SeasonKey(uint season, uint id)
    {
        if (season >= 4096 || id >= 1 << 20)
            throw new InvalidDataException($"GridFight camp key exceeds the supported range: {season}/{id}.");
        return season << 20 | id;
    }
}
