using March7thHoney.GameServer.Game.Battle;

namespace March7thHoney.GameServer.Game.Challenge;

/// <summary>
///     The battle targets that drive an endgame battle's score header: Pure Fiction (FantasticStory) and Apocalyptic
///     Shadow battles, whether started by a challenge, a Tierce node or a Calyx override of a challenge stage.
/// </summary>
public static class ChallengeScoreTargets
{
    /// <summary>Battle target list that holds the score container.</summary>
    public const int ScoreListKey = 1;

    /// <summary>Battle target list that holds a Pure Fiction stage's own targets.</summary>
    public const int StoryTargetListKey = 5;

    /// <summary>
    ///     Pure Fiction continuous-wave score container: the client settles only after every wave clears. The legacy
    ///     container 10002 settles after wave 1, capping a full clear at ~2300 instead of ~40000.
    /// </summary>
    public const int StoryScoreContainerId = 10003;

    /// <summary>Apocalyptic Shadow score header.</summary>
    public const int BossScoreHeaderId = 90004;

    /// <summary>Apocalyptic Shadow score tail.</summary>
    public const int BossScoreTailId = 90005;

    /// <summary>
    ///     Adds the Pure Fiction score container and every stage target, each starting at <paramref name="score" />.
    /// </summary>
    public static void AddStoryTargets(BattleInstance battle, IEnumerable<int> stageTargetIds, int score)
    {
        battle.AddBattleTarget(ScoreListKey, StoryScoreContainerId, score);
        foreach (var id in stageTargetIds) battle.AddBattleTarget(StoryTargetListKey, id, score);
    }

    /// <summary>Adds the Apocalyptic Shadow score header and tail, which start at zero.</summary>
    public static void AddBossTargets(BattleInstance battle)
    {
        battle.AddBattleTarget(ScoreListKey, BossScoreHeaderId, 0);
        battle.AddBattleTarget(ScoreListKey, BossScoreTailId, 0);
    }
}
