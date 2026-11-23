using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightGalaxyScholarRoundUpdate(
    uint Dna,
    IReadOnlyList<GridFightOrbState> RewardOrbs);

/// <summary>
/// Protocol state of Galaxy Scholar (银河学者, trait 1003). In official-server captures, ECEMNCJBFOP of effect 10031
/// holds the accumulated DNA, and the reward orbs are sent in the same TraitEffectUpdate when a level threshold is
/// crossed. Base DNA per member by star 1/2/3/4+ = 2/4/8/20; layer 4 gives 1.5x and layer 6 gives 2x.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint GalaxyScholarTraitId = 1003;
    public const uint GalaxyScholarTraitEffectId = 10031;

    private static readonly (uint Threshold, uint OrbId)[] GalaxyScholarRewards =
    [
        (6, 123001),
        (24, 123002),
        (60, 123003),
        (105, 123004),
        (180, 123005),
        (270, 123006),
        (390, 123007),
        (540, 123008),
        (780, 123009),
    ];

    private uint _galaxyScholarDna;
    private bool _galaxyScholarRoundUpdateDirty;
    private readonly HashSet<uint> _galaxyScholarGrantedThresholds = [];
    private readonly List<GridFightOrbState> _galaxyScholarPendingRewardOrbs = [];

    public uint GalaxyScholarDna => _galaxyScholarDna;

    private void PrepareGalaxyScholarBattleReward()
    {
        var activeRoles = Roles
            .Where(role => role.Position is >= 1 and <= 13)
            .OrderBy(role => role.Position)
            .ToList();
        var scholarRoles = activeRoles
            .Where(role => ResolveRoleTraitIds(role).Contains(GalaxyScholarTraitId))
            .ToList();
        var hasPepeMember = ResolvePepeTraitMembers().Any(entry => entry.TraitId == GalaxyScholarTraitId);
        var memberCount = (uint)scholarRoles.Count +
                          (hasPepeMember ? 1u : 0u) +
                          ResolveTraitExtraMemberCount(GalaxyScholarTraitId, activeRoles);
        var layer = Battle.GridFightBattleProtoBuilder.ResolveTraitLayer(GalaxyScholarTraitId, memberCount);
        if (layer < 2 || scholarRoles.Count == 0) return;

        var baseDna = scholarRoles.Aggregate(0u, (sum, role) => sum + (role.Star switch
        {
            1 => 2u,
            2 => 4u,
            3 => 8u,
            _ => 20u,
        }));
        var gained = layer >= 6
            ? baseDna * 2u
            : layer >= 4
                ? baseDna * 3u / 2u
                : baseDna;
        if (gained == 0) return;

        var previous = _galaxyScholarDna;
        _galaxyScholarDna = (uint)Math.Min(uint.MaxValue, (ulong)_galaxyScholarDna + gained);
        _galaxyScholarRoundUpdateDirty = true;

        foreach (var (threshold, orbId) in GalaxyScholarRewards)
        {
            if (previous >= threshold || _galaxyScholarDna < threshold ||
                _galaxyScholarGrantedThresholds.Contains(threshold))
                continue;
            if (!TryAddOrb(orbId, out var orb)) continue;
            _galaxyScholarGrantedThresholds.Add(threshold);
            _galaxyScholarPendingRewardOrbs.Add(orb);
        }
    }

    public GridFightGalaxyScholarRoundUpdate? TakeGalaxyScholarRoundUpdate()
    {
        if (!_galaxyScholarRoundUpdateDirty) return null;
        _galaxyScholarRoundUpdateDirty = false;
        var orbs = _galaxyScholarPendingRewardOrbs.ToList();
        _galaxyScholarPendingRewardOrbs.Clear();
        return new GridFightGalaxyScholarRoundUpdate(_galaxyScholarDna, orbs);
    }
}

public sealed partial class GridFightManager
{
    private void AppendGalaxyScholarRoundBeginResult(GridFightSyncUpdateResultScNotify notify)
    {
        var update = Session?.TakeGalaxyScholarRoundUpdate();
        if (update == null) return;

        var result = new GridFightSyncResultData
        {
            UpdateSource = GridFightUpdateSrcType.KgridFightSrcTraitEffectUpdate,
        };
        result.EffectParamList.Add(new[]
        {
            GridFightSession.GalaxyScholarTraitId,
            GridFightSession.GalaxyScholarTraitEffectId,
        });
        result.UpdateDynamicList.Add(new GridFightSyncData
        {
            TraitInfo = new GridFightTraitSyncInfo
            {
                TraitId = GridFightSession.GalaxyScholarTraitId,
                TraitEffectInfo = new GridFightTraitEffectInfo
                {
                    EffectId = GridFightSession.GalaxyScholarTraitEffectId,
                    ECEMNCJBFOP = update.Dna,
                },
            },
        });
        foreach (var orb in update.RewardOrbs)
            result.UpdateDynamicList.Add(new GridFightSyncData
            {
                OrbInfo = new GridFightOrbSyncInfo
                {
                    UniqueId = orb.UniqueId,
                    OrbItemId = orb.OrbItemId,
                },
            });
        notify.ResultList.Add(result);
    }
}
