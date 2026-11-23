using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>Random roles of base cost Cost (or a random cost in Cost..MaxCost when MaxCost is set).</summary>
public sealed record GridFightAugmentRoleGrant(uint Cost, uint Count, uint Star = 1, uint MaxCost = 0);

/// <summary>Roles whose combined 1-star cost reaches Gold, starting with one role of GuaranteedCost (人才大礼包).</summary>
public sealed record GridFightAugmentRoleBudget(uint Gold, uint GuaranteedCost);

/// <summary>One bundle of investment rewards. Values come from the 4.6.51 GridFightAugment EffectParamList.</summary>
public sealed record GridFightAugmentGrant
{
    public uint Gold { get; init; }
    public uint Exp { get; init; }
    public uint FreeRefresh { get; init; }
    public uint Hp { get; init; }
    public uint MaxHp { get; init; }
    public uint HpLoss { get; init; }
    public uint BasicEquipment { get; init; }
    public uint CraftableEquipment { get; init; }
    public uint RadiantEquipment { get; init; }
    public uint Emblems { get; init; }
    public uint AllRolesOfCost { get; init; }
    public uint? WinStreak { get; init; }
    public IReadOnlyList<(uint ItemId, uint Count)> Items { get; init; } = [];
    public IReadOnlyList<GridFightAugmentRoleGrant> Roles { get; init; } = [];
    public IReadOnlyList<(uint TraitId, uint Count)> TraitRoles { get; init; } = [];
    public GridFightAugmentRoleBudget? RoleBudget { get; init; }
    public IReadOnlyList<uint> SpecificRoles { get; init; } = [];
}

public enum GridFightAugmentTrigger
{
    EveryNode,      // 每次进入新节点时
    AfterNodes,     // 进入/经过 N 个节点后（一次）
    ForNextNodes,   // 接下来 N 次进入新节点时
    EveryNNodes,    // 每累计进入 N 个节点后
    BossNode,       // 进入首领节点时
    AfterBoss,      // 首领战后（进入首领之后的下一个节点）
    ChapterStart,   // 每个位面开始时
}

public sealed record GridFightAugmentRecurring(GridFightAugmentTrigger Trigger, uint Nodes, GridFightAugmentGrant Grant);

/// <summary>Result of one investment effect, synced with the investment selection or round begin.</summary>
public sealed record GridFightAugmentEffectResult(
    uint AugmentId,
    GridFightRewardApplication Application,
    bool ShopChanged,
    bool WinStreakChanged,
    bool IsPortal = false);

/// <summary>
/// Data-driven investment (augment) effects that run on the server: gold, exp, free refreshes, lineup HP,
/// random roles/equipment and fixed items, granted on selection or on node triggers.
/// Descriptions were matched by HexDesc hash against the 4.6.0 TextMap (text unchanged in 4.6.51) and the
/// numbers use the 4.6.51 EffectParamList. Effects that live in battle (strength, speed, crit, execute ...)
/// are executed by the client from SyncAugmentInfo and need nothing here. Investments whose drawback is not
/// implemented yet (max level 7, double damage, no interest ...) are deliberately left out so that a
/// partial implementation never hands out only the upside.
/// </summary>
public sealed partial class GridFightSession
{
    private static GridFightAugmentGrant G => new();

    public static readonly IReadOnlyDictionary<uint, GridFightAugmentGrant> AugmentSelectionGrants = MergeEffects(
        BuildSeasonTwoSelectionGrants(),
        new Dictionary<uint, GridFightAugmentGrant>
        {
            // ---- Silver
            [100201] = G with { Exp = 10 },                                         // 经验到账
            [100301] = G with { FreeRefresh = 11 },                                 // 免费午餐
            [100401] = G with { Roles = [new(2, 4)] },                              // 二费援军
            [100402] = G with { Roles = [new(3, 4)] },                              // 三费援军
            [100403] = G with { Roles = [new(4, 4)] },                              // 四费援军
            [100702] = G with { BasicEquipment = 1, Gold = 4 },                     // 打捞人才库+（即时部分）
            [100901] = G with { WinStreak = 4, Gold = 4 },                          // 胜利，还是胜利
            [101202] = G with { Gold = 6 },                                         // 返利+（即时部分）
            [101401] = G with { Items = [(99999, 1)], Gold = 4 },                   // 军火贸易
            [101402] = G with { Items = [(99999, 1)], Gold = 8 },                   // 军火贸易+
            [101501] = G with { Gold = 6 },                                         // 以战养战（即时部分）
            [101601] = G with { Items = [(350105, 1)] },                            // 人才激励
            [101602] = G with { Items = [(350105, 1)] },                            // 人才激励+
            [102101] = G with { Gold = 2 },                                         // 公司人才流动（即时部分）
            [102201] = G with { FreeRefresh = 5 },                                  // 市场干预（即时部分）
            [102401] = G with { Roles = [new(1, 2), new(2, 2), new(3, 1)] },        // 扩充团队
            [102501] = G with { BasicEquipment = 1, Roles = [new(3, 2)] },          // 武装支援
            [102502] = G with { BasicEquipment = 1, Roles = [new(4, 1)], Gold = 6 },// 武装支援+
            [102701] = G with { Items = [(350101, 3)] },                            // 全员晋升（即时部分）
            [102901] = G with { FreeRefresh = 4 },                                  // 合并同类项（即时部分）
            [103001] = G with { Items = [(350205, 1)] },                            // 无伤通关（即时部分）
            [103101] = G with { Items = [(99994, 2)], Gold = 4 },                   // 招聘资金
            [103102] = G with { Items = [(99993, 2)], Gold = 5 },                   // 招聘资金+
            [103202] = G with { Exp = 8 },                                          // 气氛组+（即时部分）
            [103301] = G with { Hp = 30, MaxHp = 30 },                              // 健康充值
            [103401] = G with { Gold = 9 },                                         // 溜佩佩（战斗加成由客户端）
            [103402] = G with { Gold = 15 },                                        // 溜佩佩+
            [103601] = G with { Gold = 4 },                                         // 经验就是财富（即时部分）
            [103701] = G with { Exp = 4 },                                          // 成长的快乐（即时部分）
            [103802] = G with { Hp = 10, MaxHp = 10, Gold = 6 },                    // 二极管（即时部分）
            [104001] = G with { Items = [(350105, 1)] },                            // 星星相印（即时部分）
            [110101] = G with { Emblems = 1 },                                      // 买彩票
            [110102] = G with { Emblems = 1, Items = [(350103, 1)] },               // 买彩票+
            [130101] = G with { BasicEquipment = 1 },                               // 公司军火更新•银（即时部分）
            [130301] = G with { Items = [(35030404, 1)] },                          // 赌神•银
            [130601] = G with { BasicEquipment = 1 },                               // 自由市场（即时部分）
            [130701] = G with { HpLoss = 10 },                                      // 先亏后盈（3 节点后见下表）
            [130801] = G with { Items = [(350102, 1)], BasicEquipment = 1 },        // 精密拆装·银
            [131201] = G with { Items = [(352101, 1)], Gold = 8 },                  // 无害垃圾
            [151301] = G with { Exp = 30 },                                         // 免战牌（即时部分）
            // ---- Gold
            [200301] = G with { Gold = 8, FreeRefresh = 1 },                        // 加油站
            [200501] = G with { BasicEquipment = 1 },                               // 枪在手
            [200502] = G with { BasicEquipment = 2 },                               // 枪在手+
            [200601] = G with { Items = [(350105, 2)], Gold = 7 },                  // 小复制
            [200602] = G with { Items = [(350105, 2)], Gold = 13 },                 // 小复制+
            [201301] = G with { Gold = 7 },                                         // 长期主义
            [201302] = G with { Gold = 9 },                                         // 长期主义+
            [201801] = G with { Exp = 4, FreeRefresh = 2 },                         // 固定理财
            [201802] = G with { Exp = 6, FreeRefresh = 3 },                         // 固定理财+
            [202201] = G with { Gold = 12 },                                        // 四费晋升（即时部分）
            [202401] = G with { BasicEquipment = 1 },                               // 空仓（即时部分）
            [202402] = G with { BasicEquipment = 1, Exp = 10 },                     // 空仓+（即时部分）
            [203101] = G with { Items = [(350106, 1)], Gold = 10 },                 // 大扩招（即时部分）
            [203301] = G with { Items = [(99992, 2)], Gold = 4 },                   // 专家招募
            [203302] = G with { Items = [(99992, 2)], Gold = 12 },                  // 专家招募+
            [203601] = G with { AllRolesOfCost = 2 },                               // 广聚天下英才
            [230101] = G with { Items = [(350204, 1)] },                            // 幸运星祝福（战斗加成由客户端）
            [230201] = G with { Items = [(350202, 1)] },                            // 折叠小刀祝福
            [230301] = G with { Items = [(350203, 1)] },                            // 和平手枪祝福
            [230601] = G with { BasicEquipment = 2 },                               // 公司军火更新•金（即时部分）
            [230701] = G with { CraftableEquipment = 1 },                           // 回收计划
            [230702] = G with { CraftableEquipment = 1 },                           // 回收计划+
            [230801] = G with { Items = [(350103, 2)] },                            // 升级锅炉（即时部分）
            [230901] = G with { Items = [(99991, 1)], Gold = 4 },                   // 秘密典籍
            [231001] = G with { Items = [(35030203, 1)] },                          // 全武行
            [231101] = G with { Items = [(350701, 1)] },                            // 这么大的钻石
            [231401] = G with { Items = [(350102, 1), (350103, 1)], BasicEquipment = 2 }, // 精密拆装·金
            [231501] = G with { Items = [(350107, 1)], BasicEquipment = 3 },        // 好运令牌·金
            [231601] = G with { BasicEquipment = 1 },                               // 好运来（即时部分）
            [231701] = G with { Items = [(352201, 1)], Gold = 15 },                 // 黄金垃圾
            [251601] = G with { Items = [(350601, 1)] },                            // 钻石恒永久（即时部分）
            [260601] = G with { Gold = 8 },                                         // 退化（即时部分）
            // ---- Prismatic
            [300101] = G with { Gold = 10 },                                        // 开源节流（即时部分）
            [300501] = G with { Roles = [new(1, 1, 3)], Items = [(99999, 1)] },     // 天降救兵
            [300701] = G with { Roles = [new(4, 3, 2)], Items = [(350106, 1)], FreeRefresh = 4 }, // 及时雨
            [300801] = G with { Gold = 25 },                                        // 利息上调（即时部分）
            [303701] = G with { FreeRefresh = 1 },                                  // 搜打撤（当前节点）
            [303901] = G with { Gold = 5 },                                         // 着眼当下（即时部分）
            [330101] = G with { Items = [(350104, 1), (99999, 1)] },                // 优势火力论
            [330201] = G with { Items = [(350701, 1)] },                            // 财富就是力量
            [330301] = G with { Exp = 24 },                                         // 复印件（即时部分）
            [330401] = G with { Items = [(350204, 3)] },                            // 客制化服务（即时部分）
            [330601] = G with { RadiantEquipment = 1 },                             // 公司军火更新•彩（即时部分）
            [330701] = G with { Items = [(350203, 5)] },                            // 手枪发烧友
            [330901] = G with { Items = [(99991, 1)] },                             // 终身学习
            [331501] = G with { Items = [(350102, 1), (35040404, 1)] },             // 精密拆装·彩
            [331601] = G with { Items = [(350107, 1)] },                            // 好运令牌·彩
            [331801] = G with { Items = [(35030507, 1)] },                          // 分解万物（即时部分）
            [332301] = G with { Items = [(99999, 1)] },                             // 融合召唤（即时部分）
            [350402] = G with { Items = [(99999, 1)] },                             // 战术义眼+
            [350403] = G with { Items = [(99998, 1)] },                             // 战术义眼++
            [352101] = G with { FreeRefresh = 5, Gold = 10 },                       // 乱成一锅粥（即时部分）
            [352102] = G with { FreeRefresh = 7, Gold = 14 },                       // 乱成一锅粥+（即时部分）
        });

    public static readonly IReadOnlyDictionary<uint, GridFightAugmentRecurring[]> AugmentRecurringEffects = MergeEffects(
        BuildSeasonTwoRecurringEffects(),
        new Dictionary<uint, GridFightAugmentRecurring[]>
        {
            [100101] = [new(GridFightAugmentTrigger.BossNode, 0, G with { Gold = 7 })],               // 特战资金
            [100102] = [new(GridFightAugmentTrigger.BossNode, 0, G with { Gold = 11 })],              // 特战资金+
            [101601] = [new(GridFightAugmentTrigger.AfterNodes, 4, G with { Items = [(350105, 1)] })], // 人才激励
            [101602] = [new(GridFightAugmentTrigger.AfterNodes, 3, G with { Items = [(350105, 1)] })], // 人才激励+
            [130201] = [new(GridFightAugmentTrigger.AfterNodes, 7, G with { Items = [(99997, 1)] })],  // 许诺特权
            [130701] = [new(GridFightAugmentTrigger.AfterNodes, 3, G with { Items = [(99999, 2)] })],  // 先亏后盈
            [200301] = [new(GridFightAugmentTrigger.EveryNode, 0, G with { FreeRefresh = 1 })],       // 加油站
            [200501] = [new(GridFightAugmentTrigger.AfterBoss, 0, G with { BasicEquipment = 1 })],    // 枪在手
            [200502] = [new(GridFightAugmentTrigger.AfterBoss, 0, G with { BasicEquipment = 1 })],    // 枪在手+
            [201301] = [new(GridFightAugmentTrigger.ForNextNodes, 3, G with { Gold = 7 })],           // 长期主义
            [201302] = [new(GridFightAugmentTrigger.ForNextNodes, 3, G with { Gold = 9 })],           // 长期主义+
            [201801] = [new(GridFightAugmentTrigger.ChapterStart, 0, G with { Exp = 4, FreeRefresh = 2 })], // 固定理财
            [201802] = [new(GridFightAugmentTrigger.ChapterStart, 0, G with { Exp = 6, FreeRefresh = 3 })], // 固定理财+
            [230701] = [new(GridFightAugmentTrigger.AfterNodes, 5, G with { BasicEquipment = 1 })],   // 回收计划
            [230702] = [new(GridFightAugmentTrigger.AfterNodes, 4, G with { BasicEquipment = 1 })],   // 回收计划+
            [303701] = [new(GridFightAugmentTrigger.EveryNode, 0, G with { FreeRefresh = 1 })],       // 搜打撤
            [330901] = [new(GridFightAugmentTrigger.ChapterStart, 0, G with { Items = [(99991, 1)] })], // 终身学习
            [331601] = [new(GridFightAugmentTrigger.EveryNNodes, 4, G with { Items = [(350107, 1)] })], // 好运令牌·彩
        });

    private readonly Dictionary<uint, uint> _augmentEffectNodeCounters = [];
    private readonly List<GridFightAugmentEffectResult> _augmentSelectionEffects = [];
    private readonly List<GridFightAugmentEffectResult> _augmentNodeEffects = [];

    public void AdjustFreeRefresh(int delta) =>
        _freeShopRefreshCount = (uint)Math.Clamp((long)_freeShopRefreshCount + delta, 0, uint.MaxValue);

    private void ApplyAugmentSelectionEffects(uint augmentId)
    {
        _augmentEffectNodeCounters.Remove(augmentId);
        if (AugmentSelectionGrants.TryGetValue(augmentId, out var grant))
            _augmentSelectionEffects.Add(ApplyAugmentGrant(augmentId, grant));
        ApplySeasonTwoSelectionSpecials(augmentId);
        InitializeEconomyAugment(augmentId);
        InitializeOwlbert(augmentId);
    }

    public IReadOnlyList<GridFightAugmentEffectResult> TakeAugmentSelectionEffects() => Drain(_augmentSelectionEffects);
    public IReadOnlyList<GridFightAugmentEffectResult> TakeAugmentNodeEffects() => Drain(_augmentNodeEffects);

    private static IReadOnlyList<T> Drain<T>(List<T> list)
    {
        if (list.Count == 0) return [];
        var result = list.ToList();
        list.Clear();
        return result;
    }

    private void OnAugmentEffectNodeEntered(GridFightRouteNode previousNode)
    {
        ResetGoldOwlbertClock();
        OnEconomyNodeEntered();
        var chapterStarted = CurrentNode.ChapterId != previousNode.ChapterId;
        foreach (var augmentId in ActiveInvestmentIds.ToList())
        {
            if (!AugmentRecurringEffects.TryGetValue(augmentId, out var effects)) continue;
            var count = _augmentEffectNodeCounters.GetValueOrDefault(augmentId) + 1;
            _augmentEffectNodeCounters[augmentId] = count;
            foreach (var effect in effects)
            {
                var due = effect.Trigger switch
                {
                    GridFightAugmentTrigger.EveryNode => true,
                    GridFightAugmentTrigger.AfterNodes => count == effect.Nodes,
                    GridFightAugmentTrigger.ForNextNodes => count <= effect.Nodes,
                    GridFightAugmentTrigger.EveryNNodes => effect.Nodes > 0 && count % effect.Nodes == 0,
                    GridFightAugmentTrigger.BossNode => CurrentNode.NodeType == GridFightNodeTypeEnum.Boss,
                    GridFightAugmentTrigger.AfterBoss => previousNode.NodeType == GridFightNodeTypeEnum.Boss,
                    GridFightAugmentTrigger.ChapterStart => chapterStarted,
                    _ => false,
                };
                if (due) _augmentNodeEffects.Add(ApplyAugmentGrant(augmentId, effect.Grant));
            }
            if (augmentId == MoonGatheringAugmentId && count == 3)
                _augmentNodeEffects.Add(ApplyAugmentGrant(augmentId, G with
                {
                    Items = [(SimpleArmamentBoxForgeId, 1)],
                    SpecificRoles = TakeMoonGatheringSecondHalf(),
                }));
        }
        OnPortalNodeEntered(chapterStarted);
    }

    /// <summary>
    /// Applies a grant entry by entry. A failing entry (e.g. a full bench) is skipped instead of discarding
    /// rewards that were already applied, so the synced state always matches the session.
    /// </summary>
    private GridFightAugmentEffectResult ApplyAugmentGrant(uint augmentId, GridFightAugmentGrant grant, bool isPortal = false)
    {
        var total = new GridFightRewardApplication { Success = true, SourceItemId = augmentId };
        var entries = new List<GridFightRewardEntry>();
        if (grant.Gold > 0) entries.Add(new(GridFightBonusTypeEnum.Gold, 0, grant.Gold));
        if (grant.Exp > 0) entries.Add(new(GridFightBonusTypeEnum.Exp, 0, grant.Exp));
        foreach (var (itemId, count) in grant.Items)
            entries.Add(new(GridFightBonusTypeEnum.Item, itemId, count));
        foreach (var roleGrant in grant.Roles)
            for (var index = 0u; index < roleGrant.Count; index++)
            {
                var cost = roleGrant.MaxCost > roleGrant.Cost
                    ? (uint)_catalog.NextRandom((int)roleGrant.Cost, (int)roleGrant.MaxCost + 1)
                    : roleGrant.Cost;
                foreach (var role in _catalog.RollRolesByCost(cost, 1, _temporarilyUnlockedRestrictedRoleIds))
                    entries.Add(new(GridFightBonusTypeEnum.SpecificAvatar, role.ID, 1, roleGrant.Star));
            }
        foreach (var (traitId, count) in grant.TraitRoles)
        foreach (var role in _catalog.RollRolesByTrait(traitId, (int)count, _temporarilyUnlockedRestrictedRoleIds))
            entries.Add(new(GridFightBonusTypeEnum.SpecificAvatar, role.ID, 1, 1));
        // Role ids or avatar ids; avatars map to the running season's role (season 2 = avatar * 100 + 2).
        foreach (var roleId in grant.SpecificRoles.Select(id => GridFightSeasonScope.ResolveRoleId(id, Season)).Where(id => id != 0))
            entries.Add(new(GridFightBonusTypeEnum.SpecificAvatar, roleId, 1, 1));
        if (grant.RoleBudget is { } budget)
            foreach (var roleId in RollRoleBudget(budget))
                entries.Add(new(GridFightBonusTypeEnum.SpecificAvatar, roleId, 1, 1));
        if (grant.AllRolesOfCost > 0)
            foreach (var role in _catalog.GetRolesByCost(grant.AllRolesOfCost, _temporarilyUnlockedRestrictedRoleIds))
                entries.Add(new(GridFightBonusTypeEnum.SpecificAvatar, role.ID, 1, 1));
        AddRolledEquipment(entries, GridFightEquipCategoryEnum.Basic, grant.BasicEquipment);
        AddRolledEquipment(entries, GridFightEquipCategoryEnum.Craftable, grant.CraftableEquipment);
        AddRolledEquipment(entries, GridFightEquipCategoryEnum.Radiant, grant.RadiantEquipment);
        foreach (var emblemId in _catalog.RollPepeEmblems((int)grant.Emblems))
            entries.Add(new(GridFightBonusTypeEnum.Item, emblemId, 1));

        foreach (var entry in entries)
        {
            var applied = ApplyRewards([entry], 0, augmentId);
            if (applied.Success) MergeRewardApplication(total, applied);
        }

        if (grant.MaxHp > 0)
        {
            MaxLineupHp = (uint)Math.Min(uint.MaxValue, (ulong)MaxLineupHp + grant.MaxHp);
            total.LineupHpChanged = true;
        }
        if (grant.Hp > 0)
        {
            ApplyHpHealing(GridFightHpChangeKind.AugmentHealing, grant.Hp, augmentId);
            total.LineupHpChanged = true;
        }
        if (grant.HpLoss > 0)
        {
            // Investments never end the run by themselves; keep at least 1 lineup HP.
            ApplyHpLoss(GridFightHpChangeKind.AugmentDamage, Math.Min(grant.HpLoss, LineupHp > 0 ? LineupHp - 1 : 0));
            total.LineupHpChanged = true;
        }
        if (grant.FreeRefresh > 0) AdjustFreeRefresh((int)grant.FreeRefresh);
        if (grant.WinStreak is { } streak) KeepWinCount = streak;
        return new GridFightAugmentEffectResult(augmentId, total, grant.FreeRefresh > 0, grant.WinStreak.HasValue, isPortal);
    }

    private void AddRolledEquipment(List<GridFightRewardEntry> entries, GridFightEquipCategoryEnum category, uint count)
    {
        for (var index = 0u; index < count; index++)
        {
            var equipmentId = _catalog.RollEquipment(category, 0);
            if (equipmentId != 0) entries.Add(new(GridFightBonusTypeEnum.Item, equipmentId, 1));
        }
    }

    private static void MergeRewardApplication(GridFightRewardApplication total, GridFightRewardApplication applied)
    {
        total.GoldChanged |= applied.GoldChanged;
        total.PlayerLevelChanged |= applied.PlayerLevelChanged;
        total.PlayerExpChanged |= applied.PlayerExpChanged;
        total.LineupHpChanged |= applied.LineupHpChanged;
        total.RolePropertiesChanged |= applied.RolePropertiesChanged;
        total.Drops.AddRange(applied.Drops);
        total.RoleAcquisitions.AddRange(applied.RoleAcquisitions);
        total.AddedEquipments.AddRange(applied.AddedEquipments);
        total.ConsumableChanges.AddRange(applied.ConsumableChanges);
        total.AddedOrbs.AddRange(applied.AddedOrbs);
        total.AddedForgeItems.AddRange(applied.AddedForgeItems);
    }
}
