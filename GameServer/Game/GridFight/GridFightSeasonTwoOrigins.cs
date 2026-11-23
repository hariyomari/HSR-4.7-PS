using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed class GridFightChimeraState
{
    public uint UniqueId { get; init; }
    public uint ChimeraId { get; init; }
    public uint Star { get; set; } = 1;
}

/// <summary>Chimera select goods (AMFPHALPHMF): type 1 = chimera id, 2 = consumable item id, 3 = gold amount.</summary>
public sealed record GridFightChimeraGood(uint Type, uint Param);

/// <summary>
///     Season-2 origin traits with lobby state: Chimera (奇兽花园, 1002021), MoonGame (幻月游戏, 1003021) and the
///     battle-only Xianzhou (仙舟舰队) equipment upgrade (1001021).
/// </summary>
public sealed partial class GridFightSession
{
    public const uint ChimeraTraitId = 100202;
    public const uint ChimeraEffectId = 1002021;
    public const uint ChimeraGoodChimera = 1;
    public const uint ChimeraGoodConsumable = 2;
    public const uint ChimeraGoodGold = 3;
    private const uint ChimeraWorkaholicId = 402;
    private const uint ChimeraWorkaholicGold = 2;
    private const uint ChimeraSelectGoldAmount = 5;
    private const uint ChimeraUpgradeConsumableId = 350119;
    public const uint MoonGameTraitId = 100302;
    public const uint MoonGameWishSubTraitId = 1003021;
    public const uint MoonGameWishEffectId = 10030211;
    public const uint XianzhouTraitId = 100102;
    public const uint XianzhouEffectId = 1001021;
    private static readonly uint[] MoonGameSubTraitIds = [1003021, 1003022, 1003023];

    private uint _chimeraEnergy;
    private readonly List<GridFightChimeraState> _chimeras = [];
    private (uint ChapterId, uint SectionId) _chimeraNode;
    private readonly HashSet<uint> _chimeraFreeLayers = [];
    private uint _chimeraRecruits;
    private List<GridFightChimeraGood> _chimeraGoods = [];
    private uint _chimeraRerolls;
    private uint _chimeraPassiveGold;
    private uint _moonGameSubTraitId;
    private uint _moonGameWishPower;
    private readonly Dictionary<uint, uint> _xianzhouBattleSwaps = [];

    public uint ChimeraEnergy => _chimeraEnergy;
    public IReadOnlyList<GridFightChimeraState> Chimeras => _chimeras;
    public IReadOnlyList<GridFightChimeraGood> ChimeraGoods => _chimeraGoods;
    public uint ChimeraRerolls => _chimeraRerolls;
    public uint MoonGameSubTraitId => _moonGameSubTraitId;
    public uint MoonGameWishPower => _moonGameWishPower;

    /// <summary>Gold produced by passive chimeras since the last call (sync update_source 47).</summary>
    public uint TakeChimeraPassiveGold()
    {
        var gold = _chimeraPassiveGold;
        _chimeraPassiveGold = 0;
        return gold;
    }

    private void InitializeSeasonTwoOrigins()
    {
        // The MoonGame trait text promises different rules every run: one sub-trait per run (context value field 11).
        _moonGameSubTraitId = MoonGameSubTraitIds[Random.Shared.Next(MoonGameSubTraitIds.Length)];
        _chimeraNode = (ChapterId, SectionId);
    }

    // ---------------------------------------------------------------- Chimera (奇兽花园)

    private void RefreshChimera(GridFightSeasonTwoChanges changes)
    {
        var layer = ResolveTraitLayer(ChimeraTraitId);
        if (_chimeraNode != (ChapterId, SectionId))
        {
            _chimeraNode = (ChapterId, SectionId);
            if (layer > 0)
            {
                // Each node gives 1 Chimera Energy (奇美能量); once there is enough, it is spent on a recruit as the node
                // is entered.
                _chimeraEnergy++;
                var cost = (uint)Math.Max(1, GetTraitEffectLayerParams(ChimeraEffectId, layer).FirstOrDefault());
                if (_chimeraEnergy >= cost)
                {
                    _chimeraEnergy -= cost;
                    _chimeraRecruits++;
                }
            }
            if (_chimeras.Any(chimera => chimera.ChimeraId == ChimeraWorkaholicId))
            {
                Gold += ChimeraWorkaholicGold;
                _chimeraPassiveGold += ChimeraWorkaholicGold;
                changes.GoldChanged = true;
            }
        }
        // Trait text: the first activation of each bond level (3/5/7/10) gives one free recruit.
        if (layer > 0 && _chimeraFreeLayers.Add(layer)) _chimeraRecruits++;
    }

    private bool TryStartChimeraPending(GridFightSeasonTwoChanges changes)
    {
        if (_chimeraRecruits == 0 || ResolveTraitLayer(ChimeraTraitId) == 0) return false;
        _chimeraGoods = RollChimeraGoods();
        if (_chimeraGoods.Count == 0)
        {
            _chimeraRecruits = 0;
            return false;
        }
        _chimeraRecruits--;
        _chimeraRerolls = 1;
        StartSeasonTwoPending(GridFightSeasonTwoPendingKind.ChimeraSelect, ChimeraTraitId, ChimeraEffectId, [], changes);
        return true;
    }

    /// <summary>
    ///     Candidate rule (not in the client): role-exclusive chimeras of owned Chimera roles that are not maxed,
    ///     generic chimeras once layer param 4 unlocks them, then a gold or upgrade-consumable fallback.
    /// </summary>
    private List<GridFightChimeraGood> RollChimeraGoods()
    {
        var layer = ResolveTraitLayer(ChimeraTraitId);
        var allowGeneric = GetTraitEffectLayerParams(ChimeraEffectId, layer).ElementAtOrDefault(4) > 0;
        var ownedRoleIds = Roles.Select(role => role.RoleId).ToHashSet();
        var candidates = GridFightSeasonTwoTables.Chimera
            .Where(row => row.Star == 1)
            .Where(row => row.RoleId != 0
                ? ownedRoleIds.Contains(row.RoleId) &&
                  _chimeras.Where(chimera => chimera.ChimeraId == row.Id).All(chimera => chimera.Star < GetChimeraMaxStar(row.Id))
                : allowGeneric && _chimeras.All(chimera => chimera.ChimeraId != row.Id))
            .Select(row => row.Id)
            .Distinct()
            .ToList();
        var goods = new List<GridFightChimeraGood>();
        while (goods.Count < 2 && candidates.Count > 0)
        {
            var id = candidates[Random.Shared.Next(candidates.Count)];
            candidates.Remove(id);
            goods.Add(new GridFightChimeraGood(ChimeraGoodChimera, id));
        }
        goods.Add(_chimeras.Any(chimera => chimera.Star < GetChimeraMaxStar(chimera.ChimeraId) &&
                                           GridFightSeasonTwoTables.Chimera.Any(row => row.Id == chimera.ChimeraId && row.RoleId != 0)) &&
                  GameData.GridFightConsumablesData.ContainsKey(ChimeraUpgradeConsumableId)
            ? new GridFightChimeraGood(ChimeraGoodConsumable, ChimeraUpgradeConsumableId)
            : new GridFightChimeraGood(ChimeraGoodGold, ChimeraSelectGoldAmount));
        return goods;
    }

    private static uint GetChimeraMaxStar(uint chimeraId) =>
        GridFightSeasonTwoTables.Chimera.Where(row => row.Id == chimeraId).Select(row => row.Star).DefaultIfEmpty(1u).Max();

    /// <summary>
    ///     Chimera upgrade consumable (奇巧零食, 350119, UpgradeChimera): "选择1个角色专属奇美拉，使其提升1星". Only
    ///     role-exclusive chimeras below their max star qualify; the chimera snapshot reaches the client through the
    ///     post-request trait sync.
    /// </summary>
    public bool UpgradeChimeraByConsumable(uint chimeraUniqueId)
    {
        var chimera = _chimeras.FirstOrDefault(entry => entry.UniqueId == chimeraUniqueId);
        if (chimera == null || ResolveChimeraRoleId(chimera.ChimeraId) == 0 ||
            chimera.Star >= GetChimeraMaxStar(chimera.ChimeraId))
            return false;
        chimera.Star++;
        return true;
    }

    public uint ResolveChimeraRoleId(uint chimeraId) =>
        GridFightSeasonTwoTables.Chimera.FirstOrDefault(row => row.Id == chimeraId)?.RoleId ?? 0;

    /// <summary>HandlePendingAction 1370 { MLKBOBIEAPL = true }: re-sends the action with new goods.</summary>
    public bool RerollChimeraGoods(uint queuePosition)
    {
        if (!MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.ChimeraSelect, queuePosition) || queuePosition == 0 ||
            _chimeraRerolls == 0)
            return false;
        _chimeraRerolls--;
        _chimeraGoods = RollChimeraGoods();
        return true;
    }

    /// <summary>HandlePendingAction 1370 { OBPJLMEMFHA = goods index }.</summary>
    public GridFightRewardApplication SelectChimeraGood(uint queuePosition, uint index, out uint finishedQueuePosition)
    {
        finishedQueuePosition = 0;
        var failed = new GridFightRewardApplication();
        if (queuePosition == 0 || !MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.ChimeraSelect, queuePosition) ||
            index >= _chimeraGoods.Count)
            return failed;
        var good = _chimeraGoods[(int)index];
        GridFightRewardApplication application;
        switch (good.Type)
        {
            case ChimeraGoodChimera:
                var owned = _chimeras.FirstOrDefault(chimera => chimera.ChimeraId == good.Param);
                if (owned != null && ResolveChimeraRoleId(good.Param) != 0 && owned.Star < GetChimeraMaxStar(good.Param))
                    owned.Star++; // 角色专属奇美拉还可以二合一升星
                else if (owned == null)
                    _chimeras.Add(new GridFightChimeraState { UniqueId = _nextEntityUniqueId++, ChimeraId = good.Param });
                else
                    return failed;
                application = new GridFightRewardApplication { Success = true };
                break;
            case ChimeraGoodConsumable:
                application = ApplyRewards([new GridFightRewardEntry(GridFightBonusTypeEnum.Item, good.Param, 1)]);
                break;
            case ChimeraGoodGold:
                Gold += good.Param;
                application = new GridFightRewardApplication { Success = true, GoldChanged = true };
                break;
            default:
                return failed;
        }
        if (!application.Success) return failed;
        _chimeraGoods = [];
        finishedQueuePosition = FinishSeasonTwoPending(GridFightSeasonTwoPendingKind.ChimeraSelect);
        return application;
    }

    // ---------------------------------------------------------------- MoonGame (幻月游戏)

    /// <summary>Wish power (愿力, sub-trait 1003021): layer param per finished battle, doubled on a perfect win.</summary>
    private void GrantMoonGameBattlePower(bool perfect)
    {
        if (!IsSeasonTwoRules || _moonGameSubTraitId != MoonGameWishSubTraitId) return;
        var layer = ResolveTraitLayer(MoonGameTraitId);
        if (layer == 0) return;
        var gain = (uint)GetTraitEffectLayerParams(MoonGameWishEffectId, layer).FirstOrDefault();
        _moonGameWishPower += perfect ? gain * 2 : gain;
    }

    // ---------------------------------------------------------------- Xianzhou (仙舟舰队)

    /// <summary>
    ///     UpgradeBasicEquipBeforeBattle has no client class: the battle simply receives upgraded equipment.
    ///     Layer params [count, grade] (99 = all): grade 1 recommended, 2 random advanced, 3 radiant (inferred).
    /// </summary>
    private void PrepareXianzhouBattleSwaps()
    {
        _xianzhouBattleSwaps.Clear();
        if (!IsSeasonTwoRules) return;
        var layers = ResolveTraitLayers();
        if (!layers.TryGetValue(XianzhouTraitId, out var state) || state.Layer == 0) return;
        var layerParams = GetTraitEffectLayerParams(XianzhouEffectId, state.Layer);
        var count = (int)layerParams.ElementAtOrDefault(0);
        var grade = (uint)layerParams.ElementAtOrDefault(1);
        if (count <= 0) return;
        var seasonEquipment = GridFightSeasonScope.Equipment(Season).ToList();
        foreach (var role in state.Members)
        {
            var basics = GetEquippedEquipment(role)
                .Where(item => GameData.GridFightEquipmentData.GetValueOrDefault(item.EquipmentId)?.EquipCategory ==
                               GridFightEquipCategoryEnum.Basic)
                .Take(count);
            foreach (var item in basics)
            {
                var pool = grade switch
                {
                    >= 3 => seasonEquipment.Where(row => row.EquipCategory == GridFightEquipCategoryEnum.Radiant),
                    _ => seasonEquipment.Where(row => row.EquipCategory == GridFightEquipCategoryEnum.Craftable),
                };
                var candidates = pool.Select(row => row.ID).ToList();
                if (grade == 1 && GameData.GridFightRoleRecommendEquipData.TryGetValue(role.RoleId, out var recommends))
                {
                    var recommended = recommends.SelectMany(row => row.FirstRecommendEquipList.Concat(row.SecondRecommendEquipList))
                        .Where(candidates.Contains)
                        .ToList();
                    if (recommended.Count > 0) candidates = recommended;
                }
                if (candidates.Count == 0) continue;
                _xianzhouBattleSwaps[item.UniqueId] = candidates[Random.Shared.Next(candidates.Count)];
            }
        }
    }

    private uint ResolveXianzhouBattleEquipment(uint uniqueId, uint equipmentId) =>
        _xianzhouBattleSwaps.TryGetValue(uniqueId, out var swapped) ? swapped : equipmentId;
}
