using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Lineup;
using March7thHoney.Enums.Activity;
using March7thHoney.Enums.Mission;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Lineup;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.Packet.Send.Activity.FinalityBattle;
using March7thHoney.Proto;
using LineupInfo = March7thHoney.Database.Lineup.LineupInfo;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Activity.FinalityBattle;

public class FinalityBattleManager(PlayerInstance player) : BasePlayerManager(player)
{
    // In-memory only by design: records reset on relogin.
    public Dictionary<int, FinalityBattleLevelRecord> Records { get; } = [];

    public List<Proto.FinalityBattleLevelRecord> ToProtoRecords()
    {
        return Records.Values.Select(ToProto).ToList();
    }

    private static Proto.FinalityBattleLevelRecord ToProto(FinalityBattleLevelRecord record)
    {
        var proto = new Proto.FinalityBattleLevelRecord
        {
            LevelId = (uint)record.LevelId,
            FinishedWave = (uint)record.FinishedWave,
            RemainingRound = (uint)record.RemainingRound
        };
        proto.FinishedBattleTargetIdList.AddRange(record.FinishedBattleTargetIds.Select(x => (uint)x));
        return proto;
    }

    public (Retcode, BattleInstance?) StartLevel(int levelId, int leadingRoleId, IReadOnlyList<FinalityBattleAvatarSelection> avatarList)
    {
        if (Player.BattleInstance != null) return (Retcode.RetInBattleNow, null);
        if (!GameData.FinalityBattleLevelData.TryGetValue(levelId, out var level)) return (Retcode.RetStageConfigNotExist, null);
        var group = GameData.ActivityFinalityBattleData.Values.FirstOrDefault(x => x.LevelList.Contains(levelId));
        if (group == null || !group.LeadingRoleList.Contains(leadingRoleId)) return (Retcode.RetStageConfigNotExist, null);
        if (!GameData.StageConfigData.TryGetValue(level.StageID, out var stage)) return (Retcode.RetStageConfigNotExist, null);
        if (avatarList.Any(x => x.AvatarType is not (AvatarType.AvatarTrialType or AvatarType.AvatarFormalType or AvatarType.AvatarUpgradeAvailableType)))
            return (Retcode.RetLineupAvatarTypeInvalid, null);

        var lineupOverride = new List<AvatarLineupData>();
        var baseAvatars = new List<LineupAvatarInfo>();
        // Rebuild trial avatars so the special-avatar form and state come fresh from the excel every run.
        Player.AvatarManager!.Data.TrialAvatars.RemoveAll(x =>
            x.SpecialAvatarId == leadingRoleId || avatarList.Any(a => a.AvatarType == AvatarType.AvatarTrialType && a.Id == x.SpecialAvatarId));
        var leading = Player.AvatarManager.GetTrialAvatarByWorldLevel(leadingRoleId, Player.Data.WorldLevel);
        if (leading == null) return (Retcode.RetStageConfigNotExist, null);
        AddAvatar(leading, AvatarType.AvatarTrialType);

        foreach (var entry in avatarList)
        {
            if ((int)entry.Id == leadingRoleId || lineupOverride.Count >= GameConstants.MAX_LINEUP_AVATAR_COUNT) continue;
            BaseAvatarInfo? avatar = entry.AvatarType == AvatarType.AvatarTrialType
                ? Player.AvatarManager.GetTrialAvatarByWorldLevel(entry.Id, Player.Data.WorldLevel)
                : Player.AvatarManager.GetFormalAvatar(entry.Id);
            if (avatar == null) return (Retcode.RetAvatarNotExist, null);
            if (lineupOverride.Any(x => x.AvatarInfo.BaseAvatarId == avatar.BaseAvatarId)) continue;
            AddAvatar(avatar, entry.AvatarType, avatar is FormalAvatarInfo ? entry.Id : null);
        }

        var lineup = new LineupInfo
        {
            LineupType = (int)ExtraLineupType.LineupActivity,
            LeaderAvatarId = leading.BaseAvatarId,
            BaseAvatars = baseAvatars,
            AvatarData = Player.AvatarManager.Data
        };

        var context = new FinalityBattleContext(group, level, lineupOverride);
        var battle = new BattleInstance(Player, lineup, [stage])
        {
            WorldLevel = Player.Data.WorldLevel,
            RoundLimit = GameData.FinalityBattleTurnLimit,
            LeaderIndexOverride = 0,
            FinalityContext = context
        };
        foreach (var targetId in level.BattleTargetList)
            if (GameData.BattleTargetConfigData.TryGetValue(targetId, out var target))
                battle.AddBattleTarget(5, targetId, 0, target.TargetParam);

        battle.OnBattleEnd += OnBattleEnd;
        Player.QuestManager!.OnBattleStart(battle);
        Player.BattleInstance = battle;
        return (Retcode.RetSucc, battle);

        void AddAvatar(BaseAvatarInfo avatar, AvatarType type, int? battleAvatarId = null)
        {
            lineupOverride.Add(new AvatarLineupData(avatar, type, battleAvatarId));
            baseAvatars.Add(avatar is SpecialAvatarInfo special
                ? new LineupAvatarInfo { BaseAvatarId = special.BaseAvatarId, SpecialAvatarId = special.SpecialAvatarId }
                : new LineupAvatarInfo { BaseAvatarId = avatar.BaseAvatarId });
        }
    }

    private async ValueTask OnBattleEnd(BattleInstance battle, PVEBattleResultCsReq req)
    {
        var context = battle.FinalityContext;
        if (context == null) return;

        var isWin = req.EndStatus == BattleEndStatus.BattleEndWin;
        var result = new FinalityBattleLevelRecord
        {
            LevelId = context.Level.ID,
            RemainingRound = isWin ? Math.Max(battle.RoundLimit - (int)req.Stt.RoundCnt, 0) : 0,
            FinishedWave = isWin ? battle.Stages[0].MonsterList.Count : (int)req.Stt.CocoonDeadWave
        };
        foreach (var list in req.Stt.BattleTargetInfo.Values)
        foreach (var target in list.BattleTargetList_)
            if (context.Level.BattleTargetList.Contains((int)target.Id) && IsTargetFinished(target))
                result.FinishedBattleTargetIds.Add((int)target.Id);
        context.FinishedTargetIds.UnionWith(result.FinishedBattleTargetIds);

        var isNewRecord = false;
        if (isWin)
        {
            if (!Records.TryGetValue(result.LevelId, out var best))
            {
                Records[result.LevelId] = best = result;
                isNewRecord = true;
            }
            else
            {
                if (result.RemainingRound > best.RemainingRound || result.FinishedWave > best.FinishedWave)
                    isNewRecord = true;
                best.RemainingRound = Math.Max(best.RemainingRound, result.RemainingRound);
                best.FinishedWave = Math.Max(best.FinishedWave, result.FinishedWave);
                if (!result.FinishedBattleTargetIds.IsSubsetOf(best.FinishedBattleTargetIds)) isNewRecord = true;
                best.FinishedBattleTargetIds.UnionWith(result.FinishedBattleTargetIds);
            }
        }

        await Player.SendPacket(new PacketFinalityBattleSettleScNotify(req.EndStatus, ToProto(result), isNewRecord));

        if (!isWin) return;
        await Player.MissionManager!.HandleFinishType(MissionFinishTypeEnum.FinalityBattleLevelFinish, battle);
        await Player.MissionManager.HandleFinishType(MissionFinishTypeEnum.FinalityBattleHardLevelPerfectFinishCnt, battle);
    }

    private static bool IsTargetFinished(BattleTarget target)
    {
        return target.TotalProgress > 0 ? target.Progress >= target.TotalProgress : target.Progress > 0;
    }

    public int CountPerfectHardLevels()
    {
        var count = 0;
        foreach (var record in Records.Values)
        {
            if (!GameData.FinalityBattleLevelData.TryGetValue(record.LevelId, out var level)) continue;
            if (level.DifficultyLevel != FinalityBattleDifficultyLevel.Hard) continue;
            var group = GameData.ActivityFinalityBattleData.Values.FirstOrDefault(x => x.LevelList.Contains(level.ID));
            if (group == null || record.RemainingRound < group.PerfectRound) continue;
            if (level.BattleTargetList.All(record.FinishedBattleTargetIds.Contains)) count++;
        }

        return count;
    }
}
