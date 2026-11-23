using March7thHoney.Data.Config;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Activity.ActivityBangboo;
using March7thHoney.GameServer.Game.Player;

namespace March7thHoney.GameServer.Game.Mission.FinishType.Handler;

// Planarcadia Reverie finish types (OSBETAWin4.7.51 FinishWay, values from the client's MissionFinishType enum).
// The quests that use them are the activity quests 6094001-6094105, the achievements 4055801-4055805 and the daily
// quests 2000498/2000499; the arguments are raised by ActivityBangbooManager. Count types add the occurrence, "Max"
// types keep the best single-stage value.

internal static class ActivityBangbooFinish
{
    /// <summary>ListContain rows match a listed id; an empty list matches any.</summary>
    public static bool Matches(List<int>? list, int id) => list is not { Count: > 0 } || list.Contains(id);

    public static async ValueTask AddSubMission(PlayerInstance player, SubMissionInfo info, int count)
    {
        if (count <= 0) return;
        var mission = player.MissionManager!;
        await mission.AddMissionProgress(info.ID, count);
        if (mission.GetMissionProgress(info.ID) >= Math.Max(info.Progress, 1)) await mission.FinishSubMission(info.ID);
    }

    public static async ValueTask ReachSubMission(PlayerInstance player, SubMissionInfo info, int value)
    {
        if (value >= Math.Max(info.Progress, 1)) await player.MissionManager!.FinishSubMission(info.ID);
    }

    /// <summary>BangbooDeployCount / BangbooMaxDeployCountInLevel: ParamInt1 Bangboo type, ParamIntList stars.</summary>
    public static int Deployed(ActivityBangbooSettled e, int bangbooType, List<int>? stars) =>
        e.CountPlacements(bangbooType, stars ?? []);
}

/// <summary>Clear stages (ParamIntList) for the first time, e.g. "Complete 6 stages in Story Mode".</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooLevelFinish)]
public class MissionHandlerBangbooLevelFinish : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooLevelCleared e && ActivityBangbooFinish.Matches(info.ParamIntList, e.LevelId))
            await ActivityBangbooFinish.AddSubMission(player, info, 1);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is ActivityBangbooLevelCleared e && ActivityBangbooFinish.Matches(excel.ParamIntList, e.LevelId))
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}

/// <summary>Read posts (ParamIntList) on the intelligence network for the first time.</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooReadPost)]
public class MissionHandlerBangbooReadPost : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooPostRead e && ActivityBangbooFinish.Matches(info.ParamIntList, e.PostId))
            await ActivityBangbooFinish.AddSubMission(player, info, 1);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is ActivityBangbooPostRead e && ActivityBangbooFinish.Matches(excel.ParamIntList, e.PostId))
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}

/// <summary>Place Bangboo of type ParamInt1 at a star in ParamIntList, summed over stages ("Upgrade Combat Bangboo").</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooDeployCount)]
public class MissionHandlerBangbooDeployCount : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooSettled e)
            await ActivityBangbooFinish.AddSubMission(player, info,
                ActivityBangbooFinish.Deployed(e, info.ParamInt1, info.ParamIntList));
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is not ActivityBangbooSettled e) return;
        var count = ActivityBangbooFinish.Deployed(e, excel.ParamInt1, excel.ParamIntList);
        if (count > 0) await player.QuestManager!.AddQuestProgress(quest.QuestID, count);
    }
}

/// <summary>The same placements counted within one stage; the best stage counts.</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooMaxDeployCountInLevel)]
public class MissionHandlerBangbooMaxDeployCountInLevel : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooSettled e)
            await ActivityBangbooFinish.ReachSubMission(player, info,
                ActivityBangbooFinish.Deployed(e, info.ParamInt1, info.ParamIntList));
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is ActivityBangbooSettled e)
            await player.QuestManager!.UpdateQuestProgress(quest.QuestID,
                ActivityBangbooFinish.Deployed(e, excel.ParamInt1, excel.ParamIntList));
    }
}

/// <summary>Watch anecdote groups (ParamIntList) to the end for the first time.</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooFinishNpcGroupPerformance)]
public class MissionHandlerBangbooFinishNpcGroupPerformance : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooPerformanceFinished e && ActivityBangbooFinish.Matches(info.ParamIntList, e.GroupId))
            await ActivityBangbooFinish.AddSubMission(player, info, 1);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is ActivityBangbooPerformanceFinished e && ActivityBangbooFinish.Matches(excel.ParamIntList, e.GroupId))
            await player.QuestManager!.AddQuestProgress(quest.QuestID, 1);
    }
}

/// <summary>Enemies defeated by Tactics Bangboo, summed over stages.</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooTacticsKillCount)]
public class MissionHandlerBangbooTacticsKillCount : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooSettled e) await ActivityBangbooFinish.AddSubMission(player, info, e.TacticsKillCount);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is ActivityBangbooSettled { TacticsKillCount: > 0 } e)
            await player.QuestManager!.AddQuestProgress(quest.QuestID, e.TacticsKillCount);
    }
}

/// <summary>Energy left when a stage is won; the best win counts.</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooMaxEnergyAtWin)]
public class MissionHandlerBangbooMaxEnergyAtWin : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooSettled { IsWin: true } e)
            await ActivityBangbooFinish.ReachSubMission(player, info, e.EnergyAtEnd);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is ActivityBangbooSettled { IsWin: true } e)
            await player.QuestManager!.UpdateQuestProgress(quest.QuestID, e.EnergyAtEnd);
    }
}

/// <summary>The most distinct Bangboo on the field at once in a stage of ParamIntList; the best stage counts.</summary>
[MissionFinishType(MissionFinishTypeEnum.BangbooMaxSimultaneousIdCount)]
public class MissionHandlerBangbooMaxSimultaneousIdCount : MissionFinishTypeHandler
{
    public override async ValueTask HandleMissionFinishType(PlayerInstance player, SubMissionInfo info, object? arg)
    {
        if (arg is ActivityBangbooSettled e && ActivityBangbooFinish.Matches(info.ParamIntList, e.LevelId))
            await ActivityBangbooFinish.ReachSubMission(player, info, e.MaxSimultaneousBangbooIdCount);
    }

    public override async ValueTask HandleQuestFinishType(PlayerInstance player, QuestDataExcel quest,
        FinishWayExcel excel, object? arg)
    {
        if (arg is ActivityBangbooSettled e && ActivityBangbooFinish.Matches(excel.ParamIntList, e.LevelId))
            await player.QuestManager!.UpdateQuestProgress(quest.QuestID, e.MaxSimultaneousBangbooIdCount);
    }
}
