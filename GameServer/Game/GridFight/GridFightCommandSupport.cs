using March7thHoney.Data;
using March7thHoney.Proto;
using System.Text;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Entry points for the /grid console command. They reuse the investment-effect grant path so every change goes
///     through the same reward application and client sync as in-game rewards.
/// </summary>
public sealed partial class GridFightSession
{
    public uint MaxPlayerLevel => _catalog.MaxPlayerLevel;

    public GridFightAugmentEffectResult ApplyCommandGrant(GridFightAugmentGrant grant) => ApplyAugmentGrant(0, grant);

    /// <summary>Adds an investment as if it had been picked, including its on-pick effects and later node triggers.</summary>
    public bool TryGrantInvestmentByCommand(uint augmentId)
    {
        if (!GameData.GridFightAugmentData.ContainsKey(augmentId) || ActiveInvestmentIds.Contains(augmentId))
            return false;
        OnForesightInvestmentSelected(augmentId);
        ActiveInvestmentIds.Add(augmentId);
        PrepareInvestmentSelectionReward(augmentId);
        OnRewardInvestmentSelected(augmentId);
        ApplyAugmentSelectionEffects(augmentId);
        if (Phase == GridFightSessionPhase.Preparing) TryBeginBonusInvestment(Phase);
        return true;
    }

    public uint AdjustTrailblazeCoin(int delta)
    {
        _trailblazeCoin = (uint)Math.Clamp((long)_trailblazeCoin + delta, 0, uint.MaxValue);
        return _trailblazeCoin;
    }

    public string DescribeForCommand()
    {
        var text = new StringBuilder();
        text.AppendLine($"赛季 {Season}  段位 {DivisionId}  节点 {ChapterId}-{SectionId}  阶段 {Phase}");
        text.AppendLine($"投资环境 {SelectedPortalId}  金币 {Gold}  等级 {PlayerLevel} ({PlayerExp} exp)  " +
                        $"生命 {LineupHp}/{MaxLineupHp}  免费刷新 {FreeRefreshCount}  连胜 {KeepWinCount}");
        text.AppendLine($"投资策略 ({ActiveInvestmentIds.Count}): {string.Join(", ", ActiveInvestmentIds)}");
        var board = Roles.Where(role => IsActivePosition(role.Position)).ToList();
        text.AppendLine($"场上 {board.Count}/{MaxActiveRoles}: " +
                        string.Join(", ", board.Select(role => $"{role.RoleId}★{role.Star}")));
        text.AppendLine($"备战席: {string.Join(", ", Roles.Where(role => !IsActivePosition(role.Position)).Select(role => $"{role.RoleId}★{role.Star}"))}");
        var traits = ResolveTraitLayers().Values.Where(state => state.Layer > 0)
            .OrderBy(state => state.TraitId).Select(state => $"{state.TraitId}:{state.Layer}");
        text.Append($"激活羁绊: {string.Join(", ", traits)}");
        if (IsSeasonTwoRules && TrailblazeCoin > 0) text.Append($"\n开拓力 {TrailblazeCoin}  累计消耗 {TrailblazeCostedCoin}");
        return text.ToString();
    }
}

public sealed partial class GridFightManager
{
    public GridFightSyncUpdateResultScNotify BuildCommandGrantSync(GridFightAugmentEffectResult effect)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var result = new GridFightSyncResultData();
        AppendRewardApplication(result, effect.Application);
        AppendLeadingAcquiredRoles(result, effect.Application.RoleAcquisitions);
        if (effect.ShopChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        if (effect.WinStreakChanged)
            result.UpdateDynamicList.Add(new GridFightSyncData { KeepWinCount = Session.KeepWinCount });
        notify.ResultList.Add(result);
        AppendRoleAcquisitionMerges(notify, GridFightUpdateSrcType.KgridFightSrcNone, [], effect.Application.RoleAcquisitions,
            unlockEffects: true);
        if (effect.Application.RoleAcquisitions.Count > 0)
            AppendRoleDerivedStateResult(notify, GridFightUpdateSrcType.KgridFightSrcNone);
        return notify;
    }

    /// <summary>The investment-pick sync without finishing a pending action (see BuildInvestmentSelectionSync).</summary>
    public GridFightSyncUpdateResultScNotify BuildCommandInvestmentSync(uint augmentId)
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        foreach (var itemReward in Session.TakeInvestmentSelectionRewards())
            AppendInvestmentItemReward(notify, itemReward);
        AppendSpecialInvestmentSelectionResults(notify, augmentId);
        AppendInvestmentSelectionResults(notify, augmentId);
        AppendAugmentEffectResults(notify, Session.TakeAugmentSelectionEffects());
        AppendEconomyEffects(notify);
        AppendProcurementRefreshSync(notify);
        var selection = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcSelectAugment };
        selection.UpdateDynamicList.Add(new GridFightSyncData
        {
            AddAugmentInfo = new GridFightGameAugmentAdd { UpdateAugmentInfo = BuildInvestment(augmentId) },
        });
        notify.ResultList.Add(selection);
        if (BuildPendingAction() is { } pending)
        {
            var next = new GridFightSyncResultData();
            next.UpdateDynamicList.Add(new GridFightSyncData { LockInfo = BuildPendingLockInfo() });
            next.UpdateDynamicList.Add(new GridFightSyncData { PendingAction = pending });
            notify.ResultList.Add(next);
        }
        return notify;
    }

    public GridFightSyncUpdateResultScNotify BuildCommandShopSync()
    {
        var notify = new GridFightSyncUpdateResultScNotify();
        if (Session == null) return notify;
        var result = new GridFightSyncResultData();
        result.UpdateDynamicList.Add(new GridFightSyncData { ShopInfo = BuildShopSyncInfo() });
        notify.ResultList.Add(result);
        return notify;
    }
}
