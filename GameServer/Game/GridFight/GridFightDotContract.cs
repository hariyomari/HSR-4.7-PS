using March7thHoney.GameServer.Game.Battle;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Portal 127, the DoT contract (持续伤害契约). 4.5 capture shows the counter is authored by the
/// battle client in GridFightBattleResultStatistics.POCKGKDFLKK and echoed by the server.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint DotContractPortalId = 127;
    public const string DotContractSavedValueKey = "Portal127_SavedValue01";
    public const uint DotContractJiaoqiuOrbId = 101218;
    public const uint DotContractKafkaOrbId = 101005;
    public const uint DotContractBlackSwanOrbId = 101307;
    public const uint DotContractBattleBuffId = 35500127;

    private readonly List<GridFightOrbState> _dotContractInitialOrbs = [];
    private readonly List<GridFightOrbState> _dotContractBattleRewards = [];
    private bool _dotContractBlackSwanGranted;

    public IReadOnlyList<GridFightOrbState> DotContractInitialOrbs => _dotContractInitialOrbs;

    private void InitializeDotContractPortal()
    {
        if (SelectedPortalId != DotContractPortalId || _dotContractInitialOrbs.Count != 0) return;
        foreach (var orbId in new[] { DotContractJiaoqiuOrbId, DotContractKafkaOrbId })
            if (TryAddOrb(orbId, out var orb))
                _dotContractInitialOrbs.Add(orb);
    }

    public void ApplyDotContractBattleBuff(BattleInstance battle)
    {
        if (!_activePortalIds.Contains(DotContractPortalId) ||
            battle.Buffs.Any(buff => buff.BuffID == (int)DotContractBattleBuffId))
            return;

        battle.Buffs.Add(new MazeBuff((int)DotContractBattleBuffId, 1, -1)
        {
            WaveFlag = -1,
            IncludeOwnerInTargetList = false,
        });
    }

    private void ApplyDotContractBattleStatistics(GridFightBattleStatistics statistics)
    {
        if (!_activePortalIds.Contains(DotContractPortalId)) return;
        if (!statistics.SavedValues.TryGetValue(DotContractSavedValueKey, out var value))
            return;

        _portalSavedValues[DotContractSavedValueKey] = value;
        if (_dotContractBlackSwanGranted || value < 180) return;
        if (TryAddOrb(DotContractBlackSwanOrbId, out var orb))
        {
            _dotContractBlackSwanGranted = true;
            _dotContractBattleRewards.Add(orb);
        }
    }

    public IReadOnlyList<GridFightOrbState> TakeDotContractBattleRewards()
    {
        if (_dotContractBattleRewards.Count == 0) return [];
        var result = _dotContractBattleRewards.ToList();
        _dotContractBattleRewards.Clear();
        return result;
    }
}

public sealed partial class GridFightManager
{
    private void AppendDotContractInitialResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null || session.SelectedPortalId != GridFightSession.DotContractPortalId ||
            session.DotContractInitialOrbs.Count == 0) return;

        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcPreInitialSupplySelect,
        };
        result.EffectParamList.Add(new[] { GridFightSession.DotContractPortalId, 101u });
        foreach (var orb in session.DotContractInitialOrbs)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                OrbInfo = new GridFightOrbSyncInfo { UniqueId = orb.UniqueId, OrbItemId = orb.OrbItemId },
            });
        notify.ResultList.Add(result);
    }

    private void AppendDotContractBattleResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null || !session.ActivePortalIds.Contains(GridFightSession.DotContractPortalId)) return;

        if (session.PortalSavedValues.TryGetValue(GridFightSession.DotContractSavedValueKey, out var value))
        {
            var battleEnd = notify.ResultList.FirstOrDefault(result =>
                result.UpdateSource == GridFightUpdateSrcType.KgridFightSrcBattleEnd &&
                result.UpdateDynamicList.Any(data => data.DamageStat != null));
            if (battleEnd != null)
            {
                var saved = new GridFightSavedValueUpdate();
                saved.SavedValues[GridFightSession.DotContractSavedValueKey] = value;
                battleEnd.UpdateDynamicList.Add(new GridFightSyncData { SavedValueUpdate = saved });
            }
        }

        foreach (var orb in session.TakeDotContractBattleRewards())
        {
            var result = new GridFightSyncResultData
            {
                UpdateSource = GridFightUpdateSrcType.KgridFightSrcPortalBuff,
            };
            result.EffectParamList.Add(new[] { GridFightSession.DotContractPortalId, 104u });
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                OrbInfo = new GridFightOrbSyncInfo { UniqueId = orb.UniqueId, OrbItemId = orb.OrbItemId },
            });
            notify.ResultList.Add(result);
        }
    }
}
