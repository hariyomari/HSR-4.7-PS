namespace March7thHoney.Enums.Mission;

/// <summary>How a story checkpoint provision settles the tutorials of the fast-forwarded story.</summary>
public enum StoryTutorialsModeEnum
{
    // Tutorials whose missions are already behind the player are finished; the target's own stay untouched.
    FinishPrior = 0,

    // Only the fresh-account seed; every story tutorial can still open.
    KeepOpen = 1,

    // Every configured tutorial is finished.
    FinishAll = 2
}
