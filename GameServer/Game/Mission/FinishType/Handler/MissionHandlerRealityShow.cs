using March7thHoney.Data.Config;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Activity.RealityShow;
using March7thHoney.GameServer.Game.Player;

namespace March7thHoney.GameServer.Game.Mission.FinishType.Handler;

// Interastral Peace Gala: Road to Fame quests 6091001-6091028 (FinishWay ParamType NoPara): ParamInt1 is the
// challenge id, ParamInt2 the level to reach. The argument is raised by RealityShowManager after each stage settles
// and carries the challenge's current buff level and best grade, so a later stage that reaches a higher level also
// completes the lower ones.

internal static class RealityShowFinish
{
    public static async ValueTask Reach(PlayerInstance player, SubMissionInfo info, bool reached)
    {
        if (reached) await player.MissionManager!.FinishSubMission(info.ID);
    }
}

/// <summary>Warm-up of challenge ParamInt1 earns buff level ParamInt2 or higher.</summary>
[MissionFinishType(MissionFinishTypeEnum.RealityShowBuffNum)]
public class MissionHandlerRealityShowBuffNum : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is RealityShowStageSettled e)
            await RealityShowFinish.Reach(player, info, e.ChallengeId == info.ParamInt1 && e.BuffLevel >= info.ParamInt2);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is RealityShowStageSettled e && e.ChallengeId == excel.ParamInt1 && e.BuffLevel >= excel.ParamInt2)
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}

/// <summary>Final stage of challenge ParamInt1 graded ParamInt2 or higher (1 B, 2 A, 3 S, 4 SS).</summary>
[MissionFinishType(MissionFinishTypeEnum.RealityShowResult)]
public class MissionHandlerRealityShowResult : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is RealityShowStageSettled e)
            await RealityShowFinish.Reach(player, info, e.ChallengeId == info.ParamInt1 && e.ResultLevel >= info.ParamInt2);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is RealityShowStageSettled e && e.ChallengeId == excel.ParamInt1 && e.ResultLevel >= excel.ParamInt2)
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}
