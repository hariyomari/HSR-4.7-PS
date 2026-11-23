namespace March7thHoney.Enums.Mission;

/// <summary>Which Space Anchors / teleport checkpoints a story checkpoint provision unlocks.</summary>
public enum StoryTeleportsModeEnum
{
    None = 0,

    // Every checkpoint on the floors the replayed story entered or played on, plus the target's floor.
    ReachedPlanes = 1,

    // Every checkpoint on every explorable floor.
    All = 2
}
