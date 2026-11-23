using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Season-2-only investments (4.6.51). These have no public text: descriptions were read from the in-game
/// investment handbook (投资策略图鉴) and matched to GridFightAugment rows by handbook order and EffectParamList, e.g.
/// Batch Recruitment (分批扩招) = 305101 [3, 2] "获得3个随机4费角色。进入2个新节点后，再获得3个…进入首领节点时，再获得3个".
/// Spotlight / Chimera / Trailblaze mechanics (换角开演, 刷新连锁, 片场解散, 奇美成长记, 全速开拓) are not
/// implemented server-side, and pure battle effects are executed by the client.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint MoonGatheringAugmentId = 240301;   // 幻月集结
    public const uint InvestmentUpgradeAugmentId = 104201; // 投资升级
    public const uint ForesightAugmentId = 303601;          // 远见
    private const uint InternProjectorConsumableId = 350108; // 实习生投影仪（1费复制）

    /// <summary>
    /// Emblem sets (星徽套组): (augment, emblem group, avatar, equipment). The emblem is the season-2 emblem of the
    /// group's trait (355000000 + trait, the same item the matching invitation portal grants); the avatar is resolved
    /// to the running season's role. 0 = no equipment (gold sets, or prismatic rows whose equipment id has no
    /// GridFightEquipment row in the 4.6.51 dump: 35030108, 35030210, 47030100, 35030810, 35030504).
    /// The 355100600 sets (212001 gold, 312001 prismatic, avatar 1112) are gone: OSBETAWin4.7.51 deleted both
    /// GridFightAugment rows and has no invitation portal for that group either.
    /// </summary>
    // A property, not a field: the effect tables in another partial file read it during static initialization.
    private static (uint AugmentId, uint EmblemGroup, uint AvatarId, uint EquipmentId)[] EmblemSetAugments =>
    [
        (211501, 355100100, 1210, 0), (211601, 355100200, 1405, 0), (211701, 355100300, 1501, 0),
        (211801, 355100400, 1225, 0), (211901, 355100500, 1004, 0),
        (212101, 355100800, 1310, 0), (212201, 355100900, 1412, 0), (212301, 355101000, 1307, 0),
        (212401, 355101220, 1404, 0), (221201, 355200100, 1112, 0), (221301, 355200200, 1210, 0),
        (221401, 355200300, 1004, 0), (221501, 355200420, 1405, 0), (221601, 355200500, 1225, 0),
        (221701, 355200600, 1204, 0), (221801, 355200700, 1501, 0), (221901, 355200800, 1221, 0),
        (222001, 355200900, 1505, 0), (222101, 355201000, 1222, 0), (222201, 355201100, 1503, 0),
        (311501, 355100100, 1210, 47030108), (311601, 355100200, 1405, 35030204),
        (311701, 355100300, 1501, 35030204), (311801, 355100400, 1225, 35030708),
        (311901, 355100500, 1004, 35030204),
        (312101, 355100800, 1310, 35030708), (312201, 355100900, 1412, 0),
        (312301, 355101000, 1307, 0), (312401, 355101220, 1404, 0),
        (321201, 355200100, 1112, 35030304), (321301, 355200200, 1210, 47030108),
        (321401, 355200300, 1004, 35030204), (321501, 355200420, 1405, 35030204),
        (321601, 355200500, 1225, 35030708), (321701, 355200600, 1204, 35030204),
        (321801, 355200700, 1501, 35030204), (321901, 355200800, 1221, 35030204),
        (322001, 355200900, 1505, 0), (322101, 355201000, 1222, 0), (322201, 355201100, 1503, 0),
    ];

    public static uint ResolveEmblemSetEmblem(uint emblemGroup) => 355000000 + (emblemGroup - 355000000) / 100 * 100 + 2;

    private static IReadOnlyDictionary<uint, T> MergeEffects<T>(
        IReadOnlyDictionary<uint, T> extra,
        Dictionary<uint, T> baseTable)
    {
        foreach (var (key, value) in extra) baseTable.TryAdd(key, value);
        return baseTable;
    }

    private static IReadOnlyDictionary<uint, GridFightAugmentGrant> BuildSeasonTwoSelectionGrants()
    {
        var table = new Dictionary<uint, GridFightAugmentGrant>
        {
            // ---- Prismatic
            [ForesightAugmentId] = G with { Gold = 15 },                                 // 远见：另见 GridFightForesightAugment.cs
            [304701] = G with { Items = [(SimpleArmamentBoxForgeId, 2)] },               // 装备变现（出售装备未实现）
            [304702] = G with { Items = [(SimpleArmamentBoxForgeId, 3)] },               // 装备变现+
            [304901] = G with { Roles = [new(3, 1)] },                                   // 中坚接力
            [305001] = G with { Items = [(SimpleArmamentBoxForgeId, 1)], Gold = 3 },     // 再议一次·彩（延迟投资未实现）
            [305101] = G with { Roles = [new(4, 3)] },                                   // 分批扩招
            [332501] = G with { Items = [(350601, 1)], BasicEquipment = 3, Gold = 5 },   // 跨界礼包
            [340101] = G with { Emblems = 3, Gold = 4 },                                 // 人脉网络
            [340102] = G with { Emblems = 3, Items = [(350103, 1)], Gold = 4 },          // 人脉网络+
            [340201] = G with { Emblems = 1 },                                           // 扩编筹备
            [340301] = G with { Emblems = 1 },                                           // 人脉进阶（羁绊奖励阶梯未实现）
            [340401] = G with { TraitRoles = [(ElationTraitId, 3)] },                    // 独乐乐不如众乐乐
            // ---- Gold
            [204701] = G with { Emblems = 2 },                                           // 双星馈赠
            [204801] = G with { RoleBudget = new(12, 3) },                               // 人才大礼包
            [204802] = G with { RoleBudget = new(18, 4) },                               // 人才大礼包+
            [204803] = G with { RoleBudget = new(26, 5) },                               // 人才大礼包++
            [205301] = G with { Items = [(SimpleArmamentBoxForgeId, 1)] },               // 再议一次·金（延迟投资未实现）
            [240201] = G with { Items = [(SimpleArmamentBoxForgeId, 1)] },               // 舰装焕新（临时特权由战斗执行）
            [240601] = G with { Items = [(350208, 1)], Gold = 5 },                       // 终结加演
            [240701] = G with { Exp = 12 },                                              // 伙伴总动员（佩佩未实现）
            [240901] = G with { Items = [(SimpleArmamentBoxForgeId, 1)] },               // 阿哈大悦
            // ---- Silver
            [104301] = G with { Items = [(InternProjectorConsumableId, 1)] },           // 实习生培养计划
        };
        foreach (var (augmentId, emblemGroup, avatarId, equipmentId) in EmblemSetAugments)
            table[augmentId] = G with
            {
                Items = equipmentId == 0
                    ? [(ResolveEmblemSetEmblem(emblemGroup), 1)]
                    : [(ResolveEmblemSetEmblem(emblemGroup), 1), (equipmentId, 1)],
                SpecificRoles = [avatarId],
            };
        return table;
    }

    private static IReadOnlyDictionary<uint, GridFightAugmentRecurring[]> BuildSeasonTwoRecurringEffects() =>
        new Dictionary<uint, GridFightAugmentRecurring[]>
        {
            [304701] = [new(GridFightAugmentTrigger.ChapterStart, 0, G with { BasicEquipment = 1 })],  // 装备变现
            [304702] = [new(GridFightAugmentTrigger.ChapterStart, 0, G with { BasicEquipment = 1 })],  // 装备变现+
            [304901] = [new(GridFightAugmentTrigger.EveryNode, 0, G with { Roles = [new(3, 1)] })],    // 中坚接力
            [305101] =
            [
                new(GridFightAugmentTrigger.AfterNodes, 2, G with { Roles = [new(4, 3)] }),            // 分批扩招
                new(GridFightAugmentTrigger.BossNode, 0, G with { Roles = [new(4, 3)] }),
            ],
            [340201] = [new(GridFightAugmentTrigger.AfterNodes, 3, G with { Items = [(350701, 1)] })], // 扩编筹备：财富宝钻
            [204901] = [new(GridFightAugmentTrigger.EveryNode, 0, G with { Roles = [new(1, 1, MaxCost: 2)] })], // 新人报到
            [254001] = [new(GridFightAugmentTrigger.EveryNode, 0, G with { Exp = 2 })],                // 高端人才投资
            [104301] = [new(GridFightAugmentTrigger.AfterNodes, 4, G with { Items = [(InternProjectorConsumableId, 2)] })], // 实习生培养计划
            [MoonGatheringAugmentId] = [], // 幻月集结：只计节点数，第 3 个节点由 OnAugmentEffectNodeEntered 发放另一半成员
        };

    private readonly List<uint> _moonGatheringSecondHalf = [];
    private bool _investmentUpgradePending;

    private void ApplySeasonTwoSelectionSpecials(uint augmentId)
    {
        switch (augmentId)
        {
            case MoonGatheringAugmentId:
            {
                // "获得【幻月游戏】一半的成员，进入3个新节点后，获得另一半成员和【简易武装箱】"
                var members = _catalog.GetRolesByTrait(MoonGameTraitId, _temporarilyUnlockedRestrictedRoleIds)
                    .Select(role => role.ID)
                    .OrderBy(_ => _catalog.NextRandom(0, int.MaxValue))
                    .ToList();
                var firstHalf = members.Take((members.Count + 1) / 2).ToList();
                _moonGatheringSecondHalf.Clear();
                _moonGatheringSecondHalf.AddRange(members.Skip(firstHalf.Count));
                _augmentSelectionEffects.Add(ApplyAugmentGrant(augmentId, G with { SpecificRoles = firstHalf }));
                break;
            }
            case ForesightAugmentId:
                GrantForesightBonusAugment();
                break;
            case PepeCameoAugmentId:
                StartPepeCameo();
                break;
            case InvestmentUpgradeAugmentId:
                _investmentUpgradePending = true;
                break;
        }
    }

    private IReadOnlyList<uint> TakeMoonGatheringSecondHalf()
    {
        var result = _moonGatheringSecondHalf.ToList();
        _moonGatheringSecondHalf.Clear();
        return result;
    }

    /// <summary>
    /// Investment Upgrade (投资升级): the next ordinary investment offer is one quality higher (Silver → Gold →
    /// Prismatic).
    /// </summary>
    private GridFightAugmentQualityEnum ApplyInvestmentUpgrade(GridFightAugmentQualityEnum quality)
    {
        if (!_investmentUpgradePending) return quality;
        var upgraded = quality switch
        {
            GridFightAugmentQualityEnum.Silver => GridFightAugmentQualityEnum.Gold,
            GridFightAugmentQualityEnum.Gold => GridFightAugmentQualityEnum.Prismatic,
            _ => quality,
        };
        if (_catalog.RollInvestmentOffer(ChapterId, upgraded, InvestmentOfferCount, ActiveInvestmentIds).Count < InvestmentOfferCount)
            return quality;
        _investmentUpgradePending = false;
        return upgraded;
    }

    /// <summary>
    /// Talent Gift Pack (人才大礼包): one role of the guaranteed cost, then random roles until the 1-star cost budget is
    /// spent.
    /// </summary>
    private IReadOnlyList<uint> RollRoleBudget(GridFightAugmentRoleBudget budget)
    {
        var result = new List<uint>();
        var remaining = (int)budget.Gold;
        foreach (var role in _catalog.RollRolesByCost(budget.GuaranteedCost, 1, _temporarilyUnlockedRestrictedRoleIds))
        {
            result.Add(role.ID);
            remaining -= (int)role.Rarity;
        }
        for (var guard = 0; remaining > 0 && guard < 64; guard++)
        {
            var cost = (uint)_catalog.NextRandom(1, Math.Min(remaining, 5) + 1);
            var rolled = _catalog.RollRolesByCost(cost, 1, _temporarilyUnlockedRestrictedRoleIds);
            if (rolled.Count == 0) continue;
            result.Add(rolled[0].ID);
            remaining -= (int)rolled[0].Rarity;
        }
        return result;
    }
}
