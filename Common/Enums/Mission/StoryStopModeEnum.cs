namespace March7thHoney.Enums.Mission;

/// <summary>Where a story checkpoint provision stops relative to its target mission.</summary>
public enum StoryStopModeEnum
{
    // The target is accepted and tracked (with a target sub-mission: that sub is accepted, not finished).
    Before = 0,

    // The target is finished.
    After = 1
}
