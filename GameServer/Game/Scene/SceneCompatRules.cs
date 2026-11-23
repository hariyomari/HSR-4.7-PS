namespace March7thHoney.GameServer.Game.Scene;

/// <summary>
///     Exact scene identities that loading, interaction, battle and level task code special-cases because the
///     cached configuration alone does not reproduce the official behaviour.
/// </summary>
public static class SceneCompatRules
{
    /// <summary>Simulated Universe lobby floor.</summary>
    public const int RogueLobbyFloorId = 80112001;

    /// <summary>The ChangePropState sub mission that unlocks the Rogue gallery consoles in the lobby.</summary>
    public const int RogueGalleryUnlockSubMission = 403000222;

    /// <summary>Second Simulated Universe tutorial main mission; its finish arms the tutorial console.</summary>
    public const int RogueTutorialMainMission = 4030002;

    /// <summary>Scene buff the SpeedDestruct prop grants to a random avatar, and its duration in seconds.</summary>
    public const int SpeedDestructBuffId = 2041101;

    public const int SpeedDestructBuffDurationSec = 15;

    /// <summary>
    ///     The Rogue gallery consoles in the lobby (group 3, instances 300003 and 300008, MazeProp 1005). The exact
    ///     identity also covers exports without JsonPath and old saved Closed states.
    /// </summary>
    public static bool IsRogueGalleryConsole(int floorId, int groupId, int instId, int propId) =>
        floorId == RogueLobbyFloorId && groupId == 3 && instId is 300003 or 300008 && propId == 1005;

    /// <summary>
    ///     The tutorial console that opens once the tutorial finishes (MazeProp 1003, mapping 2220). The tutorial
    ///     console at mapping 2219 is an ordinary scene entity and gets no special state.
    /// </summary>
    public static bool IsRogueTutorialConsole(int propId, int mappingInfoId) =>
        propId == 1003 && mappingInfoId == 2220;

    /// <summary>MazeProps that are always loaded Open.</summary>
    public static bool IsAlwaysOpenProp(int propId) => propId is 104006 or 104005;

    /// <summary>A console (MazeProp 104039) whose interaction moves every prop of its group to the new state.</summary>
    public static bool IsGroupStateConsole(int propId) => propId == 104039;
}
