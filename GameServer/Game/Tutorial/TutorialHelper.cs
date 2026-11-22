using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Tutorial;

/// <summary>
///     Tutorial state rules for the mission-enabled server. The client drives tutorials (UnlockTutorialCsReq when
///     one becomes relevant, FinishTutorialCsReq when it clears); the server persists every transition. With
///     ServerOption.TutorialInputGuard on it also refuses to open a tutorial whose finish trigger it cannot guarantee.
/// </summary>
/// <remarks>
///     An open tutorial holds player input until its finish trigger fires. With the story enabled three shapes
///     froze players until logout: an EnterBattle-only finish (2012 → battle 20002004 never produced), a
///     FinishMainMission finish on a chapter never reached (3006 → 1000511), and an empty finish list without
///     AutoFinish (4003). <see cref="CanClose" /> classifies a tutorial by its config finish conditions. With the
///     guard on, an un-closable one is answered TUTORIAL_FINISH on unlock so it never grabs input, and a persisted
///     UNLOCK that fails the check is repaired to FINISH on the next GetTutorial. The guard is off by default so
///     every tutorial opens as on live; turning it on and relogging releases a player a tutorial has trapped.
/// </remarks>
public static class TutorialHelper
{
    /// <summary>
    ///     The system tutorials a fresh official account already reports as finished (4.4 capture part1.json idx 393).
    ///     Story tutorials (1xxx) are never pre-opened by the server - the client opens them itself.
    /// </summary>
    private static readonly int[] SeedFinishedTutorials = [4033, 4049, 4050, 4051, 4052, 4072];

    /// <summary>
    ///     Free-play tutorials whose level script finishes them itself once the player follows the guide; their
    ///     config finish condition is not what closes them. 3002 (warp tutorial, Tutorial_3002.json) unlocks after
    ///     main 1000301, which also unlocks the Warp feature and pays the Star Rail Pass; the script opens the
    ///     Stellar Warp, waits for the single-pull click and ends with TutorialFinish 3002. Its config finish (main
    ///     1000303) is the next story step and cannot be reached while the tutorial holds input.
    /// </summary>
    private static readonly HashSet<int> ScriptFinishedTutorials = [3002];

    public static void SeedFreshAccount(PlayerInstance player)
    {
        if (!player.MissionEnabled || player.TutorialData == null || player.TutorialData.Tutorials.Count > 0) return;
        foreach (var id in SeedFinishedTutorials) player.TutorialData.Tutorials[id] = TutorialStatus.TutorialFinish;
    }

    /// <summary>Persists a tutorial status; a finished tutorial stays finished.</summary>
    public static void RecordTutorial(PlayerInstance player, int tutorialId, TutorialStatus status)
    {
        if (player.TutorialData == null) return;
        if (player.TutorialData.Tutorials.TryGetValue(tutorialId, out var current) &&
            current == TutorialStatus.TutorialFinish) return;
        player.TutorialData.Tutorials[tutorialId] = status;
    }

    /// <summary>
    ///     Answers the client's UnlockTutorialCsReq: opens the tutorial (persisted UNLOCK) unless the input-lock guard
    ///     closes it on arrival, and returns the status to report. Opening the warp tutorial arms its Asta pull.
    /// </summary>
    public static TutorialStatus Unlock(PlayerInstance player, int tutorialId)
    {
        var status = MayStayOpen(player, tutorialId) ? TutorialStatus.TutorialUnlock : TutorialStatus.TutorialFinish;
        RecordTutorial(player, tutorialId, status);
        var stored = player.TutorialData?.Tutorials.GetValueOrDefault(tutorialId, status) ?? status;
        if (stored == TutorialStatus.TutorialUnlock) player.GachaManager?.OnTutorialOpened(tutorialId);
        return stored;
    }

    /// <summary>
    ///     Whether an open tutorial may stay open: always with ServerOption.TutorialInputGuard off, otherwise only when
    ///     <see cref="CanClose" /> holds.
    /// </summary>
    public static bool MayStayOpen(PlayerInstance player, int tutorialId)
    {
        return !ConfigManager.Config.ServerOption.TutorialInputGuard || CanClose(player, tutorialId);
    }

    /// <summary>Persists a guide-group status; a finished guide stays finished.</summary>
    public static void RecordGuide(PlayerInstance player, int groupId, TutorialStatus status, TutorialGuideType type)
    {
        if (player.TutorialGuideData == null) return;
        player.TutorialGuideData.Types[groupId] = (int)type;
        if (player.TutorialGuideData.Tutorials.TryGetValue(groupId, out var current) &&
            current == TutorialStatus.TutorialFinish) return;
        player.TutorialGuideData.Tutorials[groupId] = status;
    }

    /// <summary>The guide type the client sent when it unlocked/finished the group (the official server echoes it).</summary>
    public static TutorialGuideType GuideTypeOf(PlayerInstance player, int groupId)
    {
        return player.TutorialGuideData != null && player.TutorialGuideData.Types.TryGetValue(groupId, out var type)
            ? (TutorialGuideType)type
            : TutorialGuideType.TutorialGuideDefault;
    }

    /// <summary>
    ///     Whether the server can safely leave the tutorial open: every finish condition must be one the client
    ///     or the mission system demonstrably fires.
    /// </summary>
    public static bool CanClose(PlayerInstance player, int tutorialId)
    {
        if (tutorialId == 0) return true;
        if (!GameData.TutorialDataData.TryGetValue(tutorialId, out var config)) return true; // unknown row: client decides
        return CanClose(player, config, []);
    }

    private static bool CanClose(PlayerInstance player, TutorialDataExcel config, HashSet<int> seen)
    {
        if (!seen.Add(config.TutorialID)) return true; // cycle guard
        if (ScriptFinishedTutorials.Contains(config.TutorialID)) return true;

        if (config.FinishTriggerParams.Count == 0)
            // No finish condition: only a self-closing AutoFinish tutorial is safe.
            return config.RestoreType == "AutoFinish";

        // A tutorial popped from the maze main page holds free-play input until it closes, so a target mission
        // that is merely running can never be played to its finish while the popup is open: require it finished.
        // Battle-triggered tutorials open inside a battle that ends on its own and keep Accept-or-Finish.
        var strict = OpensInFreePlay(config);

        foreach (var finish in config.FinishTriggerParams)
            switch (finish.TriggerType)
            {
                case "PlayerLevel":
                case "AnyAvatarToLevel":
                    break; // client-watched level-ups close these itself
                case "FinishMainMission":
                case "TakeMainMission":
                    if (!MainMissionClosable(player, finish.TriggerParam, strict)) return false;
                    break;
                case "FinishSubMission":
                    if (!SubMissionClosable(player, finish.TriggerParam, strict)) return false;
                    break;
                case "TakeSubMission":
                    // The take fires as the sub's owner main progresses; the client's watcher keys off the
                    // target's Doing status in missionSync (story tutorials 1002/1003 rely on this).
                    if (!SubMissionTakeClosable(player, finish.TriggerParam, strict)) return false;
                    break;
                case "TutorialFinish":
                    if (!int.TryParse(finish.TriggerParam, out var target) || target == 0) break;
                    if (GameData.TutorialDataData.TryGetValue(target, out var targetConfig) &&
                        !CanClose(player, targetConfig, seen)) return false;
                    break;
                default:
                    // EnterBattle and anything else: the exact battle/event may never be produced.
                    return false;
            }

        return true;
    }

    private static bool OpensInFreePlay(TutorialDataExcel config)
    {
        return config.TriggerParams.Any(trigger => trigger.TriggerType == "OnMazeMainPage");
    }

    private static bool Closable(MissionPhaseEnum status, bool strict)
    {
        return status == MissionPhaseEnum.Finish || (!strict && status == MissionPhaseEnum.Accept);
    }

    private static bool MainMissionClosable(PlayerInstance player, string target, bool strict)
    {
        if (!int.TryParse(target, out var id) || id == 0) return true;
        return Closable(player.MissionManager?.GetMainMissionStatus(id) ?? MissionPhaseEnum.None, strict);
    }

    private static bool SubMissionClosable(PlayerInstance player, string target, bool strict)
    {
        if (!int.TryParse(target, out var id) || id == 0) return true;
        return Closable(player.MissionManager?.GetSubMissionStatus(id) ?? MissionPhaseEnum.None, strict);
    }

    private static bool SubMissionTakeClosable(PlayerInstance player, string target, bool strict)
    {
        if (!int.TryParse(target, out var id) || id == 0) return true;
        if (!GameData.SubMissionInfoData.TryGetValue(id, out var sub) || sub.SubMissionInfo == null ||
            sub.SubMissionInfo.MainMissionID == 0) return true;
        return MainMissionClosable(player, sub.SubMissionInfo.MainMissionID.ToString(), strict);
    }
}
