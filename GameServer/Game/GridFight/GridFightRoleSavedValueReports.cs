namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Per-avatar battle growth such as Feixiao's rank (军衔) experience (GP_Avatar_S2_Feixiao_00) is counted by the
///     battle client and reported back in GridFightBattleResultStatistics.POCKGKDFLKK. Role saved values are rebuilt
///     from the table defaults on each sync, which alone would reset that growth after every battle, so the last
///     reported value overrides the default; server-owned values (Phainon's Kindling, 薪火) are still applied on top of
///     it afterwards.
/// </summary>
public sealed partial class GridFightSession
{
    private const string RoleSavedValuePrefix = "GP_Avatar_";
    private readonly Dictionary<string, uint> _reportedRoleSavedValues = new(StringComparer.Ordinal);

    private void RecordReportedRoleSavedValues(GridFightBattleStatistics statistics)
    {
        foreach (var (key, value) in statistics.SavedValues)
            if (key.StartsWith(RoleSavedValuePrefix, StringComparison.Ordinal))
                _reportedRoleSavedValues[key] = value;
    }

    private IReadOnlyDictionary<string, uint> OverlayReportedRoleSavedValues(IReadOnlyDictionary<string, uint> defaults)
    {
        if (_reportedRoleSavedValues.Count == 0 || !defaults.Keys.Any(_reportedRoleSavedValues.ContainsKey)) return defaults;
        var values = new Dictionary<string, uint>(defaults, StringComparer.Ordinal);
        foreach (var key in defaults.Keys)
            if (_reportedRoleSavedValues.TryGetValue(key, out var reported))
                values[key] = reported;
        return values;
    }
}
