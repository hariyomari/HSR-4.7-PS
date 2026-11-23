namespace March7thHoney.GameServer.Game.RogueCommon;

/// <summary>
///     Classic RogueBuffGroup blessing pools that the Simulated Universe and its board DLC (ChessRogue) both deal from.
/// </summary>
public static class RogueDraftGroups
{
    /// <summary>The draft after an ordinary combat.</summary>
    public const int Normal = 100005;

    /// <summary>
    ///     The draft after an elite fight: a classic elite room and a ChessRogue elite cell's area reward. ChessRogue
    ///     also deals it after a layer-boss (MonsterBoss) cell.
    /// </summary>
    public const int Elite = 100003;
}
