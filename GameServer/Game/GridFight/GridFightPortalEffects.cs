using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Investment-environment (portal) effects shared by seasons 1 and 2 whose descriptions are known
/// (4.6.0 TextMap matched by hash, values from 4.6.51 GridFightPortalBuff.EffectParamList).
/// Battle-side parts (stat growth, speed/damage bonuses) are executed by the client from the portal buff list.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint PrivilegeClassPortalId = 102;     // 特权阶级：第二位面开始时，获得1个特权武装箱
    public const uint SecondHandMarketPortalId = 106;   // 二手市场：商店刷新20次后，获得30金币和【员工投影仪】
    public const uint PrismaticEraPortalId = 110;       // 彩虹时代：这局的投资策略均为棱彩品质
    public const uint GoldenEraPortalId = 111;          // 黄金时代：这局的投资策略均为黄金品质
    public const uint SilverEraPortalId = 112;          // 白银时代：这局的投资策略均为白银品质
    public const uint LongTermBullPortalId = 120;       // 长线利好：花费金币进行30次刷新后，获得20金币，之后只需要1金币就能刷新
    public const uint FirstPrizePortalId = 123;         // 头彩：第一个投资策略是棱彩品质
    public const uint LastPrizePortalId = 124;          // 尾彩：第三个投资策略是棱彩品质
    public const uint SuccessExperiencePortalId = 138;  // 成功经验：升到8级后，进入接下来3个节点时，获得12经验
    public const uint TalentReservePortalId = 144;      // 人才储备：开局补给替换为3个随机3费角色
    public const uint StrategyMasterPortalId = 147;     // 策略大师：获取投资策略时，获得（2*已拥有投资策略数）的金币

    private const uint PrivilegeArmamentBoxForgeId = 99997;
    private const uint EmployeeProjectorConsumableId = 350105;

    public static readonly IReadOnlyDictionary<uint, GridFightAugmentGrant> PortalOpeningGrants =
        new Dictionary<uint, GridFightAugmentGrant>
        {
            [131] = G with { Items = [(350601, 1), (SimpleArmamentBoxForgeId, 1)] }, // 红钻贵族
            [132] = G with { Items = [(350602, 1), (SimpleArmamentBoxForgeId, 1)] }, // 蓝钻贵族
            [136] = G with { Roles = [new(1, 1, 2)] },                               // 战力提升
            [137] = G with { Roles = [new(2, 1, 2)] },                               // 战力飙升
        };

    private readonly List<GridFightAugmentEffectResult> _portalOpeningEffects = [];
    private readonly List<GridFightAugmentEffectResult> _portalRefreshEffects = [];
    private uint _manualRefreshCount;
    private uint _paidRefreshCount;
    private bool _longTermRefreshDiscount;
    private uint _successExperienceNodes;
    private uint _ordinaryInvestmentOffers;

    private bool HasPortal(uint portalId) => _activePortalIds.Contains(portalId);

    private uint BaseRefreshCost => _longTermRefreshDiscount ? Math.Min(1u, _catalog.RefreshCost) : _catalog.RefreshCost;

    public IReadOnlyList<GridFightAugmentEffectResult> TakePortalOpeningEffects() => Drain(_portalOpeningEffects);
    public IReadOnlyList<GridFightAugmentEffectResult> TakePortalRefreshEffects() => Drain(_portalRefreshEffects);

    private void ApplyPortalOpeningEffects()
    {
        foreach (var portalId in _activePortalIds)
            if (PortalOpeningGrants.TryGetValue(portalId, out var grant))
            {
                var effect = ApplyAugmentGrant(portalId, grant, isPortal: true);
                _portalOpeningEffects.Add(effect);
                if (portalId is PowerGrowthPortalId or 137)
                    foreach (var role in effect.Application.RoleAcquisitions.SelectMany(acquisition => acquisition.AcquiredRoles)
                                 .Where(Roles.Contains))
                        role.HasPowerGrowth = true;
            }
    }

    private IReadOnlyList<uint>? RollTalentReserveInitialRoleIds() =>
        HasPortal(TalentReservePortalId)
            ? _catalog.RollRolesByCost(3, 3, _temporarilyUnlockedRestrictedRoleIds).Select(role => role.ID).ToList()
            : null;

    private void OnPortalShopRefreshed(bool paidWithGold)
    {
        _manualRefreshCount++;
        if (paidWithGold) _paidRefreshCount++;
        if (HasPortal(SecondHandMarketPortalId) && _manualRefreshCount == 20)
            _portalRefreshEffects.Add(ApplyAugmentGrant(SecondHandMarketPortalId,
                G with { Gold = 30, Items = [(EmployeeProjectorConsumableId, 1)] }, isPortal: true));
        if (HasPortal(LongTermBullPortalId) && _paidRefreshCount == 30 && !_longTermRefreshDiscount)
        {
            _longTermRefreshDiscount = true;
            // Force a shop sync so the client shows the new 1-gold refresh cost.
            var result = ApplyAugmentGrant(LongTermBullPortalId, G with { Gold = 20 }, isPortal: true);
            _portalRefreshEffects.Add(result with { ShopChanged = true });
        }
    }

    private void OnPortalNodeEntered(bool chapterStarted)
    {
        GrowPowerPortalRoles();
        if (HasPortal(PrivilegeClassPortalId) && chapterStarted && CurrentNode.ChapterId == 2)
            _augmentNodeEffects.Add(ApplyAugmentGrant(PrivilegeClassPortalId,
                G with { Items = [(PrivilegeArmamentBoxForgeId, 1)] }, isPortal: true));
        if (HasPortal(SuccessExperiencePortalId) && PlayerLevel >= 8 && _successExperienceNodes < 3)
        {
            _successExperienceNodes++;
            _augmentNodeEffects.Add(ApplyAugmentGrant(SuccessExperiencePortalId, G with { Exp = 12 }, isPortal: true));
        }
    }

    private void OnPortalInvestmentSelected()
    {
        if (HasPortal(StrategyMasterPortalId))
            _augmentSelectionEffects.Add(ApplyAugmentGrant(StrategyMasterPortalId,
                G with { Gold = 2u * (uint)ActiveInvestmentIds.Count }, isPortal: true));
    }

    /// <summary>
    /// Quality forced by the Prismatic/Golden/Silver Era portals (彩虹/黄金/白银时代) and the First/Last Prize portals
    /// (头彩/尾彩); null keeps the normal random roll.
    /// </summary>
    private GridFightAugmentQualityEnum? ResolvePortalInvestmentQuality(uint offerIndex)
    {
        if (HasPortal(PrismaticEraPortalId)) return GridFightAugmentQualityEnum.Prismatic;
        if (HasPortal(GoldenEraPortalId)) return GridFightAugmentQualityEnum.Gold;
        if (HasPortal(SilverEraPortalId)) return GridFightAugmentQualityEnum.Silver;
        if (HasPortal(FirstPrizePortalId) && offerIndex == 1) return GridFightAugmentQualityEnum.Prismatic;
        if (HasPortal(LastPrizePortalId) && offerIndex == 3) return GridFightAugmentQualityEnum.Prismatic;
        return null;
    }
}
