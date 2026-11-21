namespace March7thHoney.Enums.Fight;

/// <summary>
///     How a won battle is classified for the progression battle bonus (ServerOption.ProgressionRewards.BattleKinds).
///     The first match wins, in declaration order.
/// </summary>
public enum ProgressionBattleKindEnum
{
    // Every monster in the fight came from /spawn.
    GmSpawn,

    // Currency Wars (GridFight).
    CurrencyWars,

    Finality,

    // Trial activity stages and Battle College (combat tutorial) fights.
    Trial,

    // Simulated Universe and every other rogue-like mode (Swarm, Gold and Gears, Divergent Universe, ...).
    Rogue,

    // Forgotten Hall, Pure Fiction, Apocalyptic Shadow and other challenge modes.
    Challenge,

    // Calyx, Cavern of Corrosion, Echo of War, Stagnant Shadow and other domains that spend Trailblaze Power.
    Domain,

    // Raid scenes, including story instances that run as a raid.
    Raid,

    // Open-world and story fights on town, maze and train maps.
    Story,

    // Anything else (event and minigame scenes).
    Other
}
