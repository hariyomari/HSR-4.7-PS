namespace March7thHoney.Util;

public static class GameConstants
{
    public const string GAME_VERSION = "4.6.51";
    public static bool IsVersion(string version) => string.Equals(GAME_VERSION, version, StringComparison.Ordinal);
    public const string AvatarDbVersion = "20250430";
    public const int GameVersionInt = 4600;
    public const int MAX_STAMINA = 300;
    public const int MAX_STAMINA_RESERVE = 2400;
    public const int STAMINA_RECOVERY_TIME = 360; // 6 minutes
    public const int STAMINA_RESERVE_RECOVERY_TIME = 1080; // 18 minutes
    public const int INVENTORY_MAX_EQUIPMENT = 1500;
    public const int INVENTORY_MAX_RELIC = 3000;
    public const int MAX_LINEUP_COUNT = 9;
    public const int MAX_LINEUP_AVATAR_COUNT = 4;

    /// <summary>Avatar HP is stored as a ratio of this value, which is a full bar.</summary>
    public const int FULL_HP = 10000;

    /// <summary>HP (20%) an HP recovery prop heals every avatar in the lineup by.</summary>
    public const int PROP_HP_RECOVER = 2000;

    /// <summary>HP (20%) a downed avatar is revived with.</summary>
    public const int REVIVE_HP = 2000;

    public const int LAST_TRAIN_WORLD_ID = 501;
    public const int AMBUSH_BUFF_ID = 1000102;
    public const int CHALLENGE_ENTRANCE = 100000103;
    public const int CHALLENGE_PEAK_ENTRANCE = 100000352;
    public const int CHALLENGE_STORY_ENTRANCE = 102020107;
    public const int CHALLENGE_BOSS_ENTRANCE = 1030402;

    public static readonly List<int> UpgradeWorldLevel = [20, 30, 40, 50, 60, 65];
    public static readonly List<int> AllowedChessRogueEntranceId = [8020701, 8020901, 8020401, 8020201];
}
