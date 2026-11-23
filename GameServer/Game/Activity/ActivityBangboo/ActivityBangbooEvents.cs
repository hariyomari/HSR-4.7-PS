using March7thHoney.Data;

namespace March7thHoney.GameServer.Game.Activity.ActivityBangboo;

// Payloads of the Planarcadia Reverie finish types (FinishWay BangbooLevelFinish .. BangbooMaxSimultaneousIdCount,
// 480500-480507). Typed so a handler never counts an argument that HandleAllFinishType fans out to every handler.

/// <summary>A stage won for the first time (BangbooLevelFinish, ParamIntList = stage ids).</summary>
public sealed record ActivityBangbooLevelCleared(int LevelId);

/// <summary>A post read for the first time (BangbooReadPost, ParamIntList = post ids).</summary>
public sealed record ActivityBangbooPostRead(int PostId);

/// <summary>An anecdote group watched for the first time (BangbooFinishNpcGroupPerformance, ParamIntList = group ids).</summary>
public sealed record ActivityBangbooPerformanceFinished(int GroupId);

/// <summary>One placement line of the settle statistics (MPAJNPPLKMP.BangbooPlaceCountList, MMPEJCJEEHO).</summary>
public readonly record struct ActivityBangbooPlacement(int BangbooId, int Star, int Count);

/// <summary>
///     The statistics a settled stage reported (MBCCKEIJAGF.FFCBOJAACMD): placements by Bangboo and star, kills by
///     Tactics Bangboo, energy left at the end and the most distinct Bangboo on the field at once.
/// </summary>
public sealed record ActivityBangbooSettled(
    int LevelId,
    bool IsWin,
    IReadOnlyList<ActivityBangbooPlacement> Placements,
    int TacticsKillCount,
    int EnergyAtEnd,
    int MaxSimultaneousBangbooIdCount)
{
    /// <summary>
    ///     Placements of the given Bangboo type (FinishWay ParamInt1: 1 Fight, 2 Tactics, 0 any) whose star is in
    ///     <paramref name="stars" /> (ParamIntList; empty = any star).
    /// </summary>
    public int CountPlacements(int bangbooType, IReadOnlyCollection<int> stars)
    {
        var config = GameData.ActivityBangbooConfig;
        return Placements.Where(p =>
                (bangbooType == 0 || config.GetBangboo(p.BangbooId)?.TypeValue == bangbooType) &&
                (stars.Count == 0 || stars.Contains(p.Star)))
            .Sum(p => Math.Max(p.Count, 0));
    }
}
