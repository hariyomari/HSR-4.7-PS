namespace March7thHoney.Enums;

/// <summary>
///     Which history banners make up a version phase (ServerOption.Warps.PhaseLineup), wherever a phase is served
///     whole: Rotation and Load phase.
/// </summary>
public enum WarpPhaseLineupEnum
{
    // Only the banners that opened in the phase.
    Started = 0,

    // Also every earlier banner still running when the phase opened (live behaviour: 4.7's Aha runs through both
    // phases, and the collab pools ran on for a long time).
    Overlap = 1
}
