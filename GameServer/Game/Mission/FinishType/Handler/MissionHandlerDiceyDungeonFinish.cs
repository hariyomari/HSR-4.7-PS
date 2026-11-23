using March7thHoney.Data.Config;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Player;

namespace March7thHoney.GameServer.Game.Mission.FinishType.Handler;

// Containment Starts With a Roll quests (FinishWay ParamType ListContain, ParamIntList = node or level ids). The
// argument is the node id won / the level id finished, raised by DiceyDungeonManager.FinishNode.

[MissionFinishType(MissionFinishTypeEnum.DiceyDungeonNodeFinish)]
public class MissionHandlerDiceyDungeonNodeFinish : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        await ValueTask.CompletedTask;
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is int nodeId && excel.ParamIntList.Contains(nodeId))
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}

[MissionFinishType(MissionFinishTypeEnum.DiceyDungeonLevelFinish)]
public class MissionHandlerDiceyDungeonLevelFinish : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        await ValueTask.CompletedTask;
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is int levelId && excel.ParamIntList.Contains(levelId))
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}
