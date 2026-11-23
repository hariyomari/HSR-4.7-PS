using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed partial class GridFightSession
{
    public bool HasPendingSelection => EquipmentRecommendation != null || HeadPlayerPending != null || FateDivinerPending != null ||
                                       SeasonTwoPending != null;

    /// <summary>
    ///     A Spotlight (主角光环) selection is open. The role is picked by dragging a board role onto the spotlight stage,
    ///     and the panel can be paused (返回备战界面) to rearrange the team, so it must not lock the board.
    /// </summary>
    public bool IsSpotLightSelecting => SeasonTwoPending?.Kind == GridFightSeasonTwoPendingKind.SpotLightSelect;

    /// <summary>A pending selection that blocks preparation edits (moving, buying, selling, equipment).</summary>
    public bool HasBlockingPendingSelection => HasPendingSelection && !IsSpotLightSelecting;

    public (bool TraitActivated, bool PendingStarted) PrepareNextHeadPlayerEnhancement() =>
        RefreshHeadPlayerAfterRoleStateChange();
}

public sealed partial class GridFightManager
{
    private void AppendNextTraitEnhancement(GridFightSyncUpdateResultScNotify notify)
    {
        if (Session == null) return;
        var head = Session.PrepareNextHeadPlayerEnhancement();
        if (head.TraitActivated) AppendSilverWolfTraitActivationResults(notify);
        if (head.PendingStarted) AppendHeadPlayerPendingResult(notify);
        AppendFateDivinerInitialActivationResults(notify);
    }
}
