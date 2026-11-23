using March7thHoney.Data;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed class GridFightElationSlotState
{
    public uint PresetEquipmentId { get; init; }
    public uint EquipmentUniqueId { get; set; }
}

/// <summary>
///     Season-2 Clara (机心巧匠, 3005021), Aha (幻乐之主, 300602) and Elation laugh points (欢愉, 2007021).
///     Upgrade cadence, option pools and Aha growth thresholds are
///     server-side JSON the client never sees; they follow the trait descriptions and are marked as guesses.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint ClaraTraitId = 300502;
    public const uint ClaraEffectId = 3005021;
    public const uint ClaraRoleId = 110702;
    private const uint ClaraDefaultEnhanceId = 4001;
    private const uint ClaraUltraEnhanceId = 5001;
    private const uint ClaraUltraUpgrades = 5;
    private const int ClaraEarlyUpgrades = 3;
    private const int ClaraOptionCount = 3;
    private static readonly uint[] ClaraModuleEntries = [1001, 2001, 3001];
    private static readonly uint[] ClaraSummonChain = [3001, 3002, 3003, 3004];
    private static readonly uint[] ClaraSummonChainTriggers = [3005, 3006, 3007];

    public const uint AhaTraitId = 300602;
    public const string AhaGrowthSavedValueKey = "GP_Avatar_S2_Aha_00";
    // Role IDs come from GridFightResourceCatalog; growth per absorbed copy is server-side.
    // OSBETAWin4.7.51 client GridFightAhaRarity confirms two star-less tiers (4/5 cost, no 151102):
    // the 5-cost form, 7-cost threshold and growth-bar cap all use growth 5.
    // 4.6.5x had three tiers (3/4/5 cost) and a confirmed 7-cost threshold of 9. The intermediate
    // thresholds are estimates because that version's server JSON is unavailable.
    private static readonly uint[] AhaLegacyTierGrowth = [0, 3, 6];
    private const uint AhaLegacySevenCostGrowth = 9;
    private static readonly uint[] AhaTierGrowth = [0, 5];
    private const uint AhaSevenCostGrowth = 5;
    private const uint AhaMaxGrowth = 5;
    private const int AhaLegacyTierCount = 3;

    public const uint ElationTraitId = 200702;
    public const uint ElationEffectId = 2007021;

    private readonly List<(uint EnhanceId, uint Num)> _claraEnhancements = [];
    private bool _claraFielded;
    private uint _claraPendingUpgrades;
    private uint _claraNodesSinceUpgrade;
    private uint _claraUpgradeCount;
    private (uint ChapterId, uint SectionId) _claraNode;
    private uint _ahaGrowth;
    private uint _elationTemplateId;
    private readonly List<GridFightElationSlotState> _elationSlots = [];

    public IReadOnlyList<(uint EnhanceId, uint Num)> ClaraEnhancements => _claraEnhancements;
    public uint AhaGrowth => _ahaGrowth;
    public uint ElationTemplateId => _elationTemplateId;
    public IReadOnlyList<GridFightElationSlotState> ElationSlots => _elationSlots;

    private void InitializeSeasonTwoElation()
    {
        var rows = GridFightSeasonTwoTables.ElationTemplates.Where(row => row.Weight > 0).ToList();
        if (rows.Count == 0) return;
        var roll = Random.Shared.NextInt64(rows.Sum(row => (long)row.Weight));
        var template = rows.First(row => (roll -= row.Weight) < 0);
        _elationTemplateId = template.Id;
        _elationSlots.AddRange(template.PreEquipList.Select(id => new GridFightElationSlotState { PresetEquipmentId = id }));
    }

    // ---------------------------------------------------------------- Clara

    private void RefreshClara(GridFightSeasonTwoChanges changes)
    {
        var fielded = ResolveTraitLayer(ClaraTraitId) > 0;
        if (fielded && !_claraFielded)
        {
            // Trait text: Clara gets her first upgrade as soon as she is fielded ("克拉拉上场时立即进行首次强化改装"); this
            // happens only the first time she is fielded.
            _claraFielded = true;
            _claraPendingUpgrades++;
            _claraNode = (ChapterId, SectionId);
        }
        if (!fielded || _claraNode == (ChapterId, SectionId)) return;
        _claraNode = (ChapterId, SectionId);
        // Trait text: one upgrade per #2[i] new nodes entered; the first #3[i] upgrades need only 1 new node each
        // ("每进入#2[i]个新节点改装一次，前#3[i]次改装仅需进入1个新节点。").
        _claraNodesSinceUpgrade++;
        var interval = _claraUpgradeCount < ClaraEarlyUpgrades ? 1u : 2u;
        if (_claraNodesSinceUpgrade < interval) return;
        _claraNodesSinceUpgrade = 0;
        _claraPendingUpgrades++;
    }

    private void IssueClaraPending(GridFightSeasonTwoChanges changes)
    {
        if (_claraPendingUpgrades == 0 || ResolveTraitLayer(ClaraTraitId) == 0) return;
        var options = RollClaraOptions();
        if (options.Count == 0)
        {
            _claraPendingUpgrades = 0;
            return;
        }
        StartSeasonTwoPending(GridFightSeasonTwoPendingKind.ClaraEnhance, ClaraTraitId, ClaraEffectId, options, changes);
    }

    private uint ClaraOwned(uint enhanceId) =>
        _claraEnhancements.Where(entry => entry.EnhanceId == enhanceId).Select(entry => entry.Num).FirstOrDefault();

    /// <summary>
    ///     First offer: the three module entries. Later: unobtained rows whose CostCoin is at most the number of
    ///     enhancements already owned in that module (reading of CostCoin as a module prerequisite is a guess),
    ///     the summon chain only through its link rows, the Ultra once after six upgrades, 4001 as filler.
    /// </summary>
    private List<uint> RollClaraOptions()
    {
        if (_claraEnhancements.Count == 0) return [.. ClaraModuleEntries];
        var ownedByCategory = GridFightSeasonTwoTables.ClaraEnhance
            .GroupBy(row => row.Category)
            .ToDictionary(group => group.Key, group => (uint)group.Count(row => ClaraOwned(row.Id) > 0));
        var pool = GridFightSeasonTwoTables.ClaraEnhance
            .Where(row => row.Id != ClaraDefaultEnhanceId && row.Id != ClaraUltraEnhanceId &&
                          !ClaraSummonChain.Skip(1).Contains(row.Id) &&
                          (row.Limit == 0 || ClaraOwned(row.Id) < row.Limit) &&
                          (ClaraModuleEntries.Contains(row.Id) || ownedByCategory.GetValueOrDefault(row.Category) > 0) &&
                          row.CostCoin <= ownedByCategory.GetValueOrDefault(row.Category))
            .Where(row => !ClaraSummonChainTriggers.Contains(row.Id) || ClaraOwned(ClaraSummonChain[^1]) == 0)
            .Select(row => row.Id)
            .ToList();
        if (_claraUpgradeCount >= 6 && ClaraOwned(ClaraUltraEnhanceId) == 0) pool.Add(ClaraUltraEnhanceId);
        var options = new List<uint>();
        while (options.Count < ClaraOptionCount && pool.Count > 0)
        {
            var id = pool[Random.Shared.Next(pool.Count)];
            pool.Remove(id);
            options.Add(id);
        }
        if (options.Count < ClaraOptionCount) options.Add(ClaraDefaultEnhanceId);
        return options;
    }

    /// <summary>HandlePendingAction 1651 { HAFOKMHCGFM = enhance id } (an index is accepted defensively).</summary>
    public uint SelectClaraEnhance(uint queuePosition, uint value, out bool success)
    {
        success = false;
        if (queuePosition == 0 || !MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.ClaraEnhance, queuePosition))
            return 0;
        var options = _seasonTwoPending!.Options;
        var enhanceId = options.Contains(value) ? value : value < options.Count ? options[(int)value] : 0;
        if (enhanceId == 0) return 0;

        AddClaraEnhance(enhanceId);
        // Picking a snap-command variant (响指指令, 3005/3006/3007) advances the 3001 -> 3004 chain
        // (GridFightClaraLinkRelation).
        if (ClaraSummonChainTriggers.Contains(enhanceId))
        {
            var next = ClaraSummonChain.FirstOrDefault(id => ClaraOwned(id) == 0);
            if (next != 0) AddClaraEnhance(next);
        }
        if (enhanceId == ClaraUltraEnhanceId) _claraPendingUpgrades += ClaraUltraUpgrades;
        _claraUpgradeCount++;
        if (_claraPendingUpgrades > 0) _claraPendingUpgrades--;
        success = true;
        return FinishSeasonTwoPending(GridFightSeasonTwoPendingKind.ClaraEnhance);
    }

    private void AddClaraEnhance(uint enhanceId)
    {
        var index = _claraEnhancements.FindIndex(entry => entry.EnhanceId == enhanceId);
        if (index < 0) _claraEnhancements.Add((enhanceId, 1));
        else _claraEnhancements[index] = (enhanceId, _claraEnhancements[index].Num + 1);
    }

    // ---------------------------------------------------------------- Aha

    private IReadOnlyList<uint> AhaGrowthRoleIds => _catalog.AhaGrowthRoleIds;
    private uint AhaSevenCostRoleId => _catalog.AhaSevenCostRoleId;

    /// <summary>
    ///     The Aha tier the shop sells and the Aha special good 346 (星神降临) hands out: the cheapest star-less tier; 0
    ///     without an Aha.
    /// </summary>
    public uint AhaBaseRoleId => AhaGrowthRoleIds.Count > 0 ? AhaGrowthRoleIds[0] : 0;

    private bool IsAhaRole(uint roleId) =>
        roleId != 0 && (roleId == AhaSevenCostRoleId || AhaGrowthRoleIds.Contains(roleId));

    private bool UsesLegacyAhaTiers => AhaGrowthRoleIds.Count >= AhaLegacyTierCount;

    /// <summary>Growth at which the star-less tier with this index is reached.</summary>
    private uint AhaTierGrowthAt(int tier)
    {
        var steps = UsesLegacyAhaTiers ? AhaLegacyTierGrowth : AhaTierGrowth;
        return steps[Math.Min(tier, steps.Length - 1)];
    }

    /// <summary>(growth the 7-cost tier asks for, where the growth bar ends; 0 = open-ended).</summary>
    public (uint SevenCostGrowth, uint MaxGrowth) AhaGrowthRule =>
        UsesLegacyAhaTiers ? (AhaLegacySevenCostGrowth, 0u) : (AhaSevenCostGrowth, AhaMaxGrowth);

    /// <summary>The cost the client shows for an Aha tier: its Rarity, 7 for the seven-price tier.</summary>
    private uint AhaCost(uint roleId) =>
        roleId == AhaSevenCostRoleId ? 7 : GameData.GridFightRoleBasicInfoData.GetValueOrDefault(roleId)?.Rarity ?? 0;

    /// <summary>
    ///     Aha growth (幻月之力; "获取星神★阿哈可积攒【幻月之力】"): the star-less Aha tiers
    ///     (GridFight_Aha_No_Star_Role_IDList) never merge, so every further copy is absorbed into growth, and growth
    ///     raises the cost tier on the same unique id. The 7-cost tier has stars ("达到3星7费"): once an Aha is 7-cost,
    ///     every other copy becomes a 1★ 7-cost copy and merges like any role. A copy of the cheapest tier is worth 1
    ///     growth and each tier above it one more (server-side value, not in the client tables; with the 4.7.51 tables
    ///     only the cheapest tier can be bought, so this is 1 per copy). Returns the roles whose id changed, the unique
    ///     ids of absorbed copies and the merges of 7-cost copies.
    /// </summary>
    public (IReadOnlyList<GridFightRoleState> Changed, IReadOnlyList<uint> Absorbed, IReadOnlyList<GridFightRoleMergeStep> Merges)
        AbsorbAhaCopies()
    {
        if (!IsSeasonTwoRules) return ([], [], []);
        var ahas = Roles.Where(role => IsAhaRole(role.RoleId)).ToList();
        if (ahas.Count == 0) return ([], [], []);
        var changed = new List<GridFightRoleState>();
        var sevenCostRoleId = AhaSevenCostRoleId;
        if (ahas.Any(role => role.RoleId == sevenCostRoleId))
        {
            // Before, a copy stayed at its own cost on the bench and only turned 7-cost once it was fielded as the
            // tier keeper; those copies never merged (a perfect projector later merged them all at once).
            foreach (var copy in ahas.Where(role => role.RoleId != sevenCostRoleId))
            {
                copy.RoleId = sevenCostRoleId;
                copy.Star = 1;
                changed.Add(copy);
            }
            var merges = MergeRoles(sevenCostRoleId);
            if (changed.Count > 0 || merges.Count > 0) RecalculateRoleSwitches();
            return (changed, [], merges);
        }

        var tiers = AhaGrowthRoleIds.ToList();
        var maxGrowth = AhaGrowthRule.MaxGrowth;
        var keeper = ahas.OrderByDescending(role => IsActivePosition(role.Position))
            .ThenByDescending(role => tiers.IndexOf(role.RoleId))
            .ThenBy(role => role.UniqueId)
            .First();
        var absorbed = new List<uint>();
        foreach (var copy in ahas.Where(role => role != keeper))
        {
            DestroyDiceFilledOnRole(copy);
            Roles.Remove(copy);
            absorbed.Add(copy.UniqueId);
            _ahaGrowth += (uint)tiers.IndexOf(copy.RoleId) + 1;
            if (maxGrowth != 0) _ahaGrowth = Math.Min(_ahaGrowth, maxGrowth);
        }
        var target = ResolveAhaTier(keeper.RoleId);
        if (target != keeper.RoleId)
        {
            keeper.RoleId = target;
            changed.Add(keeper);
        }
        if (absorbed.Count > 0 && !changed.Contains(keeper)) changed.Add(keeper);
        if (absorbed.Count > 0 || changed.Count > 0) RecalculateRoleSwitches();
        return (changed, absorbed, []);
    }

    private uint ResolveAhaTier(uint current)
    {
        var tiers = AhaGrowthRoleIds;
        var index = tiers.ToList().IndexOf(current);
        if (index < 0) return current;
        // The 7-cost step starts from the last star-less tier, one refresh after that tier is reached.
        if (index == tiers.Count - 1)
            return AhaSevenCostRoleId != 0 && _ahaGrowth >= AhaGrowthRule.SevenCostGrowth && HasAhaSevenCostCondition()
                ? AhaSevenCostRoleId
                : current;
        var reached = index;
        for (var tier = index + 1; tier < tiers.Count; tier++)
            if (_ahaGrowth >= AhaTierGrowthAt(tier))
                reached = tier;
        return tiers[reached];
    }

    /// <summary>
    ///     7-cost condition "达到5费后，提升欢愉或幻月游戏队员的总星级" (after reaching 5 cost, raise the total stars of Elation
    ///     or Moon Game members): trait 300602 desc params [10, 12] (assignment is a guess).
    /// </summary>
    private bool HasAhaSevenCostCondition() =>
        GetTraitTotalStars(ElationTraitId) >= 10 || GetTraitTotalStars(MoonGameTraitId) >= 12;

    private uint GetTraitTotalStars(uint traitId) =>
        ResolveTraitLayers().TryGetValue(traitId, out var state) ? (uint)state.Members.Sum(role => role.Star) : 0;

    private void ApplyAhaSavedValues(Dictionary<string, uint> values)
    {
        if (IsSeasonTwoRules && _ahaGrowth > 0) values[AhaGrowthSavedValueKey] = _ahaGrowth;
    }

    // ---------------------------------------------------------------- Elation (Aha's equipment bar, 阿哈装备栏)

    public uint ResolveElationUnlockedSlots()
    {
        var layer = ResolveTraitLayer(ElationTraitId);
        return layer == 0 ? 0 : (uint)GetTraitEffectLayerParams(ElationEffectId, layer).FirstOrDefault();
    }

    public bool IsEquipmentOnAha(uint uniqueId) =>
        uniqueId != 0 && _elationSlots.Any(slot => slot.EquipmentUniqueId == uniqueId);

    /// <summary>
    ///     DressTo (TraitUpdate 1595 / 8): the dragged equipment combines with the first unlocked slot's simple equipment
    ///     into the matching advanced item (阿哈装备栏中的简易装备依次合成对应进阶装备); otherwise it fills an empty slot.
    /// </summary>
    public GridFightRewardApplication DressElationEquipment(uint uniqueId, out GridFightEquipmentState? consumed)
    {
        consumed = null;
        var failed = new GridFightRewardApplication();
        if (Phase != GridFightSessionPhase.Preparing) return failed;
        var equipment = Equipments.FirstOrDefault(item => item.UniqueId == uniqueId);
        if (equipment == null || equipment.IsRestricted || FindEquipmentWearer(uniqueId) != null || IsEquipmentOnAha(uniqueId))
            return failed;
        var unlocked = (int)Math.Min(ResolveElationUnlockedSlots(), (uint)_elationSlots.Count);
        for (var index = 0; index < unlocked; index++)
        {
            var slot = _elationSlots[index];
            var current = slot.EquipmentUniqueId == 0
                ? slot.PresetEquipmentId
                : Equipments.FirstOrDefault(item => item.UniqueId == slot.EquipmentUniqueId)?.EquipmentId ?? 0;
            // Only a slot still holding its simple equipment can absorb the drag; crafted slots are final.
            if (slot.EquipmentUniqueId != 0) continue;
            var crafted = current == 0 ? 0 : ResolveCraftedEquipmentId(current, equipment.EquipmentId);
            if (crafted == 0) continue;
            if (!TryAddEquipment(crafted, out var result)) return failed;
            Equipments.Remove(equipment);
            slot.EquipmentUniqueId = result.UniqueId;
            consumed = equipment;
            var application = new GridFightRewardApplication { Success = true };
            application.AddedEquipments.Add(result);
            return application;
        }
        return failed;
    }

    public IReadOnlyList<(uint EquipmentId, uint UniqueId)> GetElationBattleEquipment()
    {
        var unlocked = (int)Math.Min(ResolveElationUnlockedSlots(), (uint)_elationSlots.Count);
        return _elationSlots.Take(unlocked).Select(slot =>
        {
            var equipment = Equipments.FirstOrDefault(item => item.UniqueId == slot.EquipmentUniqueId);
            return equipment == null ? (slot.PresetEquipmentId, 0u) : (equipment.EquipmentId, equipment.UniqueId);
        }).Where(entry => entry.Item1 != 0).ToList();
    }
}

public sealed partial class GridFightManager
{
    private GridFightTraitEffectInfo BuildClaraEffectInfo(GridFightTraitEffectInfo info)
    {
        var session = Session!;
        info.PFEBKFJOGKO = new JBGJFECBCCL();
        info.PFEBKFJOGKO.EMAGFIBLNCJ.Add(session.ClaraEnhancements.Select(entry => new NJGBAAOPAOI
        {
            IGOHOJEJIGP = entry.EnhanceId,
            Num = entry.Num,
        }));
        return info;
    }

    private GridFightTraitEffectInfo? BuildElationEffectInfo(GridFightTraitEffectInfo info)
    {
        var session = Session!;
        var unlocked = session.ResolveElationUnlockedSlots();
        info.IFDICIIOHHI = new HDCJJFDLHIO();
        info.IFDICIIOHHI.POJGKJENENA.Add(session.ElationSlots.Select((slot, index) => new JHCEEAPHHGK
        {
            NHLBAOGCIAN = slot.PresetEquipmentId,
            IsUnlock = index < unlocked,
            // Both fields identify equipment configs. The slot is empty on the client when its
            // current config is zero; an inventory unique id cannot resolve to an equipment icon.
            ACAPGLCONJH = session.Equipments.FirstOrDefault(item => item.UniqueId == slot.EquipmentUniqueId)
                ?.EquipmentId ?? slot.PresetEquipmentId,
            Pos = (uint)index,
        }));
        return info;
    }

    private static GridFightPendingAction BuildClaraPendingAction(
        GridFightSession session,
        GridFightSeasonTwoPendingState pending,
        GridFightPendingAction action)
    {
        action.PFEBKFJOGKO = new FOCOJIDPAGO { TraitId = pending.TraitId, EffectId = pending.EffectId };
        action.PFEBKFJOGKO.EGJDILDEAKP.Add(pending.Options);
        return action;
    }

    private static void FillClaraBattleTraitEffect(GridFightSession session, BattleGridFightTraitEffectInfo effect)
    {
        // Clara's enhancements are separate stage-ability MazeBuffs (GetSeasonTwoBattleEffectBuffs).
        // This trait member carries only the Elation equipment bar.
        if (effect.EffectId != GridFightSession.ElationEffectId) return;
        effect.IFDICIIOHHI = new BFMDDFLMAKD();
        effect.IFDICIIOHHI.GridFightEquipmentList.Add(session.GetElationBattleEquipment().Select(entry =>
            new BattleGridFightEquipInfo { GridFightEquipmentId = entry.EquipmentId, UniqueId = entry.UniqueId }));
    }
}
