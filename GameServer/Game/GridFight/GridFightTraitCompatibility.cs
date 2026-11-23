namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// 4.5 client trait layer compatibility for bundled-cache gaps. Thresholds below are taken from
/// the current Currency Wars text/packet samples. Trait 2011 (战技点) is overridden only for
/// its confirmed 2/4/6/8 layer thresholds; lottery probabilities remain client-authored.
/// </summary>
public static class GridFightTraitCompatibility
{
    public const uint BelobogTraitId = 1005;
    public const uint GroupAttackTraitId = 2003;
    public const uint DebuffTraitId = 2007;
    public const uint HealingTraitId = 2005;
    public const uint ReservedLayerOnlyTrait2006 = 2006;
    public const uint DotTraitId = 2008;
    public const uint QuantumSyncTraitId = 2009;
    public const uint SkillPointTraitId = 2011;
    public const uint ReservedLayerOnlyTrait3001 = 3001;

    private static readonly IReadOnlyDictionary<uint, uint[]> LayerThresholds =
        new Dictionary<uint, uint[]>
        {
            [GridFightSession.GalaxyScholarTraitId] = [2, 4, 6],
            [BelobogTraitId] = [2, 4],
            [GroupAttackTraitId] = [3, 5, 7, 9],
            [DebuffTraitId] = [2, 4, 6, 8],
            [HealingTraitId] = [2, 4, 6],
            [DotTraitId] = [2, 4, 6],
            [SkillPointTraitId] = [2, 4, 6, 8],
            [QuantumSyncTraitId] = [2, 3, 4, 5],
            [GridFightSession.FateDivinerTraitId] = [1],
        };

    // Official 4.5 battle packets carry these traits as trait id + layer + members only.
    // Do not synthesize TraitEffectList rows from the bundled cache for them: doing so changes
    // the wire shape and can route the client through the wrong GridFight trait runtime path.
    // 2003/2006/2011/3001 were added after cross-checking all supplied official captures.
    private static readonly HashSet<uint> LayerOnlyWireTraits =
    [
        BelobogTraitId,
        GroupAttackTraitId,
        HealingTraitId,
        ReservedLayerOnlyTrait2006,
        DebuffTraitId,
        DotTraitId,
        QuantumSyncTraitId,
        SkillPointTraitId,
        ReservedLayerOnlyTrait3001,
    ];

    public static bool UsesLayerOnlyWireShape(uint traitId) => LayerOnlyWireTraits.Contains(traitId);

    public static bool TryResolveLayer(uint traitId, uint memberCount, out uint layer)
    {
        layer = 0;
        if (!LayerThresholds.TryGetValue(traitId, out var thresholds)) return false;
        layer = thresholds.Where(threshold => threshold <= memberCount).DefaultIfEmpty().Max();
        return true;
    }
}
