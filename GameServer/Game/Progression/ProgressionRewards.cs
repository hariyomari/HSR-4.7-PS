using March7thHoney.Configuration;
using March7thHoney.Data;
using March7thHoney.Data.Excel;
using March7thHoney.Database.Inventory;
using March7thHoney.Enums.Fight;
using March7thHoney.Enums.Mission;
using March7thHoney.Enums.Scene;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Progression;

/// <summary>
///     Bonus items for finishing missions and winning battles (ServerOption.ProgressionRewards), paid on top of the
///     official rewards. Missions pay on their Accept -> Finish transition, which happens once per acceptance.
/// </summary>
public static class ProgressionRewards
{
    private static readonly AsyncLocal<bool> InCommand = new();

    private static ProgressionRewardsOption Option => ConfigManager.Config.ServerOption.ProgressionRewards;

    /// <summary>
    ///     Marks the current async flow as a GM command. Missions it finishes pay only with IncludeCommandFinishes.
    /// </summary>
    public static CommandScope EnterCommand()
    {
        var previous = InCommand.Value;
        InCommand.Value = true;
        return new CommandScope(previous);
    }

    public static ValueTask<List<ItemData>> GrantMainMission(PlayerInstance player, MainMissionExcel mission)
    {
        // A row without sub-missions has nothing to play; the server closes those itself (display-only Gap rows,
        // empty Branch rows), so they are not progress.
        var option = Option;
        return mission.MissionInfo.SubMissionList.Count > 0 && PaysMission(option, mission)
            ? Grant(player, option.MainMission)
            : ValueTask.FromResult(new List<ItemData>());
    }

    public static ValueTask<List<ItemData>> GrantSubMission(PlayerInstance player, int subMissionId)
    {
        var option = Option;
        var mission = GameData.SubMissionInfoData.GetValueOrDefault(subMissionId) is { } sub
            ? GameData.MainMissionData.GetValueOrDefault(sub.MainMissionId)
            : null;
        return mission != null && PaysMission(option, mission)
            ? Grant(player, option.SubMission)
            : ValueTask.FromResult(new List<ItemData>());
    }

    // Call before the battle's end handlers run: they tear down the challenge / rogue state the classifier reads.
    public static ValueTask<List<ItemData>> GrantBattleWin(PlayerInstance player, BattleInstance battle)
    {
        var option = Option;
        return option.Enabled && IsEnabled(option.BattleKinds, Classify(player, battle))
            ? Grant(player, option.BattleWin)
            : ValueTask.FromResult(new List<ItemData>());
    }

    public static ProgressionBattleKindEnum Classify(PlayerInstance player, BattleInstance battle)
    {
        if (battle.IsGmSpawnedEncounter) return ProgressionBattleKindEnum.GmSpawn;
        if (battle.GridFightContext != null) return ProgressionBattleKindEnum.CurrencyWars;
        if (battle.FinalityContext != null) return ProgressionBattleKindEnum.Finality;

        var mode = player.SceneInstance?.GameModeType ?? GameModeTypeEnum.Unknown;
        if (battle.IsTrialActivityBattle || battle.CollegeConfigExcel != null || mode == GameModeTypeEnum.TrialActivity)
            return ProgressionBattleKindEnum.Trial;
        if (battle.IsTournRogue || battle.MagicInfo != null || player.RogueManager?.RogueInstance != null ||
            mode is GameModeTypeEnum.RogueExplore or GameModeTypeEnum.RogueChallenge or GameModeTypeEnum.RogueAeonRoom
                or GameModeTypeEnum.ChessRogue or GameModeTypeEnum.TournRogue or GameModeTypeEnum.RelicRogue
                or GameModeTypeEnum.ArcadeRogue or GameModeTypeEnum.MagicRogue)
            return ProgressionBattleKindEnum.Rogue;
        if (player.ChallengeManager?.ChallengeInstance != null || player.ChallengeTierceManager?.CurrentInstance != null ||
            mode is GameModeTypeEnum.Challenge or GameModeTypeEnum.ChallengeActivity)
            return ProgressionBattleKindEnum.Challenge;
        if (battle.CocoonId > 0 || battle.MappingInfoId > 0 || battle.StaminaCost > 0 || battle.CalyxOverride != null ||
            mode == GameModeTypeEnum.FarmRelic)
            return ProgressionBattleKindEnum.Domain;
        if (player.RaidManager?.Data.CurRaidId > 0 || mode == GameModeTypeEnum.Raid)
            return ProgressionBattleKindEnum.Raid;
        return mode is GameModeTypeEnum.Town or GameModeTypeEnum.Maze or GameModeTypeEnum.Train or GameModeTypeEnum.TownRoom
            ? ProgressionBattleKindEnum.Story
            : ProgressionBattleKindEnum.Other;
    }

    public static bool IsEnabled(ProgressionBattleKindsOption kinds, ProgressionBattleKindEnum kind)
    {
        return kind switch
        {
            ProgressionBattleKindEnum.GmSpawn => kinds.GmSpawn,
            ProgressionBattleKindEnum.CurrencyWars => kinds.CurrencyWars,
            ProgressionBattleKindEnum.Finality => kinds.Finality,
            ProgressionBattleKindEnum.Trial => kinds.Trial,
            ProgressionBattleKindEnum.Rogue => kinds.Rogue,
            ProgressionBattleKindEnum.Challenge => kinds.Challenge,
            ProgressionBattleKindEnum.Domain => kinds.Domain,
            ProgressionBattleKindEnum.Raid => kinds.Raid,
            ProgressionBattleKindEnum.Story => kinds.Story,
            _ => kinds.Other
        };
    }

    private static bool PaysMission(ProgressionRewardsOption option, MainMissionExcel mission)
    {
        return option.Enabled &&
               (option.IncludeCommandFinishes || !InCommand.Value) &&
               (option.IncludeDailyMissions || mission.Type != MainMissionTypeEnum.Daily);
    }

    private static async ValueTask<List<ItemData>> Grant(PlayerInstance player, List<ProgressionRewardItem> items)
    {
        // The settings panel replaces these lists instead of mutating them, so this iterates a stable snapshot.
        var granted = new List<ItemData>();
        foreach (var item in items)
        {
            if (item.Count <= 0) continue;
            var result = await player.InventoryManager!.AddItem(item.ItemId, item.Count, false);
            if (result != null) granted.Add(result);
        }

        return granted;
    }

    public readonly struct CommandScope(bool previous) : IDisposable
    {
        public void Dispose()
        {
            InCommand.Value = previous;
        }
    }
}
