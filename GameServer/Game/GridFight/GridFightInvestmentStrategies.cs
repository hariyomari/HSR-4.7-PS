using March7thHoney.Data;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightInvestmentItemReward(
    uint AugmentId,
    uint EffectId,
    GridFightEquipmentState? Equipment = null,
    GridFightForgeItemState? ForgeItem = null,
    uint? GoldValue = null,
    GridFightConsumableChange? Consumable = null);

public sealed record GridFightLaborNpcRemoval(
    GridFightNpcState Npc,
    GridFightEquipmentState Equipment,
    uint SavedValueAfterReset);

/// <summary>
/// Packet/Wiki-backed investment strategies that require delayed node-entry state.
/// Node counters are advanced in one place so battle, supply, encounter and investment nodes
/// all count identically.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint DiamondAfterNodesAugmentId = 131101; // 是钻石总会发光
    public const uint RedDiamondShineAugmentId = 231201;    // 红钻闪耀
    public const uint BlueDiamondShineAugmentId = 231301;   // 蓝钻闪耀
    public const uint ArmamentSupplyChainAugmentId = 330501;// 军备供应链
    public const uint UpstreamPrismaticAugmentId = 351301;  // 打通上下游·彩
    public const uint LuckyTokenSilverAugmentId = 130901;   // 好运令牌·银
    public const uint StarSoulAscensionAugmentId = 253101; // 星魂升华；官服仅通过 SyncAugmentInfo 让战斗配置执行前台星魂+1
    public const uint LizhiProcedureAugmentId = 204001;     // 狸智手续 (packet name supplied by capture notes)

    public const uint RawDiamondEquipmentId = 350601;
    public const uint BlueDiamondEquipmentId = 350602;
    public const uint LuckyTokenConsumableId = 350107;  // GridFightConsumables row, not an equipment
    public const uint SimpleArmamentBoxForgeId = 99999;
    public const uint AugmentEquipmentSource = 1;
    public const uint NpcEquipmentSource = 4;

    private readonly Dictionary<uint, uint> _investmentNodeCounters = [];
    private readonly List<GridFightInvestmentItemReward> _roundBeginInvestmentRewards = [];
    private readonly List<GridFightInvestmentItemReward> _investmentSelectionRewards = [];
    private readonly List<(uint AugmentId, uint EffectId, string Key, uint Value)> _roundBeginSavedValueUpdates = [];

    private readonly List<GridFightNpcState> _lizhiNpcs = [];
    private readonly List<GridFightLaborNpcRemoval> _lizhiNpcRemovals = [];
    private uint _lizhiPermanentBenchBonus;

    public IReadOnlyList<GridFightNpcState> LizhiNpcs => _lizhiNpcs;
    public uint LizhiActiveRoleBonus => (uint)_lizhiNpcs.Count;
    public uint LizhiBenchBonus => _lizhiPermanentBenchBonus;
    public uint UpstreamNodeCount => _investmentNodeCounters.GetValueOrDefault(UpstreamPrismaticAugmentId);
    public uint UpstreamStrengthBonusFixpoint => ActiveInvestmentIds.Contains(UpstreamPrismaticAugmentId)
        ? 150u + 30u * UpstreamNodeCount
        : 0u;

    private void PrepareInvestmentSelectionReward(uint augmentId)
    {
        _investmentSelectionRewards.Clear();
        switch (augmentId)
        {
            case PrismaticProcurementAugmentId:
                _investmentNodeCounters[augmentId] = 0;
                break;
            case DiamondAfterNodesAugmentId:
                _investmentNodeCounters[augmentId] = 0;
                break;
            case RedDiamondShineAugmentId:
                _investmentNodeCounters[augmentId] = 0;
                if (TryAddEquipment(RawDiamondEquipmentId, out var red, AugmentEquipmentSource))
                    _investmentSelectionRewards.Add(new(augmentId, 108, red));
                break;
            case BlueDiamondShineAugmentId:
                _investmentNodeCounters[augmentId] = 0;
                if (TryAddEquipment(BlueDiamondEquipmentId, out var blue, AugmentEquipmentSource))
                    _investmentSelectionRewards.Add(new(augmentId, 106, blue));
                break;
            case ArmamentSupplyChainAugmentId:
                _investmentNodeCounters[augmentId] = 0;
                if (TryAddForgeItem(SimpleArmamentBoxForgeId, out var initialBox))
                    _investmentSelectionRewards.Add(new(augmentId, 104, ForgeItem: initialBox));
                break;
            case LuckyTokenSilverAugmentId:
            case UpstreamPrismaticAugmentId:
                _investmentNodeCounters[augmentId] = 0;
                break;
            case LizhiProcedureAugmentId:
                _investmentNodeCounters[augmentId] = 0;
                InitializeLizhiProcedure();
                break;
            case AstaDogAugmentId:
                InitializeAstaDogInvestment();
                break;
        }
    }

    public IReadOnlyList<GridFightInvestmentItemReward> TakeInvestmentSelectionRewards()
    {
        if (_investmentSelectionRewards.Count == 0) return [];
        var result = _investmentSelectionRewards.ToList();
        _investmentSelectionRewards.Clear();
        return result;
    }

    public IReadOnlyList<GridFightInvestmentItemReward> TakeRoundBeginInvestmentRewards()
    {
        if (_roundBeginInvestmentRewards.Count == 0) return [];
        var result = _roundBeginInvestmentRewards.ToList();
        _roundBeginInvestmentRewards.Clear();
        return result;
    }

    public IReadOnlyList<(uint AugmentId, uint EffectId, string Key, uint Value)> TakeRoundBeginSavedValueUpdates()
    {
        if (_roundBeginSavedValueUpdates.Count == 0) return [];
        var result = _roundBeginSavedValueUpdates.ToList();
        _roundBeginSavedValueUpdates.Clear();
        return result;
    }

    public IReadOnlyList<GridFightLaborNpcRemoval> TakeLizhiNpcRemovals()
    {
        if (_lizhiNpcRemovals.Count == 0) return [];
        var result = _lizhiNpcRemovals.ToList();
        _lizhiNpcRemovals.Clear();
        return result;
    }

    private void OnInvestmentNodeEntered()
    {
        OnRewardInvestmentNodeEntered();
        if (ActiveInvestmentIds.Contains(DiamondAfterNodesAugmentId) &&
            _investmentNodeCounters.GetValueOrDefault(DiamondAfterNodesAugmentId) < 2)
        {
            var count = ++_investmentNodeCounters[DiamondAfterNodesAugmentId];
            if (count == 2)
            {
                var diamondId = Random.Shared.Next(2) == 0 ? RawDiamondEquipmentId : BlueDiamondEquipmentId;
                if (TryAddEquipment(diamondId, out var diamond, AugmentEquipmentSource))
                    _roundBeginInvestmentRewards.Add(new(DiamondAfterNodesAugmentId, 104, diamond));
            }
        }

        if (ActiveInvestmentIds.Contains(RedDiamondShineAugmentId) &&
            _investmentNodeCounters.GetValueOrDefault(RedDiamondShineAugmentId) < 3)
        {
            var count = ++_investmentNodeCounters[RedDiamondShineAugmentId];
            if (count == 3 && TryAddForgeItem(SimpleArmamentBoxForgeId, out var box))
                _roundBeginInvestmentRewards.Add(new(RedDiamondShineAugmentId, 109, ForgeItem: box));
        }

        if (ActiveInvestmentIds.Contains(BlueDiamondShineAugmentId) &&
            _investmentNodeCounters.GetValueOrDefault(BlueDiamondShineAugmentId) < 3)
        {
            var count = ++_investmentNodeCounters[BlueDiamondShineAugmentId];
            if (count == 3 && TryAddForgeItem(SimpleArmamentBoxForgeId, out var box))
                _roundBeginInvestmentRewards.Add(new(BlueDiamondShineAugmentId, 107, ForgeItem: box));
        }

        if (ActiveInvestmentIds.Contains(ArmamentSupplyChainAugmentId))
        {
            var count = _investmentNodeCounters.GetValueOrDefault(ArmamentSupplyChainAugmentId);
            if (count < 5)
            {
                _investmentNodeCounters[ArmamentSupplyChainAugmentId] = count + 1;
                if (TryAddForgeItem(SimpleArmamentBoxForgeId, out var box))
                    _roundBeginInvestmentRewards.Add(new(ArmamentSupplyChainAugmentId, 105, ForgeItem: box));
            }
        }

        if (ActiveInvestmentIds.Contains(LuckyTokenSilverAugmentId) &&
            _investmentNodeCounters.GetValueOrDefault(LuckyTokenSilverAugmentId) < 3)
        {
            var count = _investmentNodeCounters.GetValueOrDefault(LuckyTokenSilverAugmentId) + 1;
            _investmentNodeCounters[LuckyTokenSilverAugmentId] = count;
            if (count == 3 && TryAddConsumable(LuckyTokenConsumableId, out var token))
                _roundBeginInvestmentRewards.Add(new(LuckyTokenSilverAugmentId, 104, Consumable: token));
        }

        if (ActiveInvestmentIds.Contains(UpstreamPrismaticAugmentId))
        {
            var count = ++_investmentNodeCounters[UpstreamPrismaticAugmentId];
            _roundBeginSavedValueUpdates.Add((
                UpstreamPrismaticAugmentId,
                106,
                "Augment351301_SavedValue01",
                count));
        }

        if (ActiveInvestmentIds.Contains(LizhiProcedureAugmentId))
            AdvanceLizhiProcedureNode();
    }

    private void InitializeLizhiProcedure()
    {
        _lizhiNpcs.Clear();
        _lizhiNpcRemovals.Clear();
        _lizhiPermanentBenchBonus = 0;
        if (!ActiveInvestmentIds.Contains(LizhiProcedureAugmentId)) return;

        // Capture order/positions: NPC 102 at 13, 12, 11; one locked random simple equipment each.
        foreach (var pos in new uint[] { 13, 12, 11 })
        {
            var equipmentId = (uint)Random.Shared.Next(350201, 350209);
            if (!TryAddEquipment(equipmentId, out var equipment, NpcEquipmentSource, isLocked: true)) continue;
            var npc = new GridFightNpcState
            {
                UniqueId = _nextEntityUniqueId++,
                NpcId = 102,
                Position = pos,
            };
            npc.EquippedEquipmentUniqueIds.Add(equipment.UniqueId);
            _lizhiNpcs.Add(npc);
        }
        _lizhiPermanentBenchBonus = (uint)_lizhiNpcs.Count;
    }

    private void AdvanceLizhiProcedureNode()
    {
        var saved = _investmentNodeCounters.GetValueOrDefault(LizhiProcedureAugmentId);
        // Official packet removes an NPC when the previous saved value is 2, resets to 0,
        // then counts the just-entered node as 1 (110 -> 111 in the same sync batch).
        if (saved >= 2 && _lizhiNpcs.Count > 0)
        {
            var index = Random.Shared.Next(_lizhiNpcs.Count);
            var npc = _lizhiNpcs[index];
            _lizhiNpcs.RemoveAt(index);
            var equipment = npc.EquippedEquipmentUniqueIds
                .Select(id => Equipments.FirstOrDefault(item => item.UniqueId == id))
                .FirstOrDefault(item => item != null);
            if (equipment != null)
            {
                equipment.IsLocked = false;
                _lizhiNpcRemovals.Add(new(npc, equipment, 0));
            }
            saved = 0;
        }

        saved++;
        _investmentNodeCounters[LizhiProcedureAugmentId] = saved;
        _roundBeginSavedValueUpdates.Add((
            LizhiProcedureAugmentId,
            111,
            "Augment204001_SavedValue01",
            saved));
    }

    public void ApplyInvestmentPropertyScales(IDictionary<uint, uint> properties)
    {
        var upstream = UpstreamStrengthBonusFixpoint + SpotLightStrengthBonusFixpoint;
        if (upstream != 0)
        {
            properties.TryGetValue(1027, out var front);
            properties.TryGetValue(1030, out var back);
            properties[1027] = front + upstream;
            properties[1030] = back + upstream;
        }

        var ootdCount = OotdDistinctEquipmentCount;
        if (ootdCount == 0) return;
        properties.TryGetValue(1009, out var hp);
        properties.TryGetValue(1027, out var frontAfterUpstream);
        properties.TryGetValue(1030, out var backAfterUpstream);
        properties[1009] = hp + ootdCount * 30u;
        properties[1027] = frontAfterUpstream + ootdCount * 60u;
        properties[1030] = backAfterUpstream + ootdCount * 60u;
    }
}
