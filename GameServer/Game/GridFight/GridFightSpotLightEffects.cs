using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Lobby effects of individual spotlights (主角光环). Their official server JSON is not shipped; each entry
///     follows the in-game description and its GridFightSpotLight EffectParamList.
///     "刷新视界" (100100, [1, 0.015]): "进入新节点时，该角色若参与上次战斗，获得1次免费刷新。刷新时，我方小队前/后台强度提高1.5%".
///     The strength bonus stacks per shop refresh while the spotlight is held and uses the front/back strength
///     properties 1027/1030 in permille, like "上游供应" (150 = 15%).
/// </summary>
public sealed partial class GridFightSession
{
    public const uint RefreshVisionSpotLightId = 100100;
    public const uint AdvancedRefreshVisionSpotLightId = 100106;
    public const uint NanoModificationSpotLightId = 100101;
    public const uint HardBattleTreasureSpotLightId = 120202;
    // "战场学者" (100102, [2, 0.07]) and "战场学者·精英" (100103, [4, 0.07]): on entering a new node the character
    // gains N EXP if it fought in the last battle, and each shop level raises the squad's front/back strength by 7%.
    private static readonly IReadOnlyDictionary<uint, uint> FieldScholarExp = new Dictionary<uint, uint>
    {
        [100102] = 2,
        [100103] = 4,
    };
    private const uint FieldScholarStrengthPerLevel = 70;
    private const uint RefreshVisionFreeRefresh = 1;
    private const uint RefreshVisionStrengthPerRefresh = 15;

    private readonly HashSet<uint> _lastBattleBoardUniqueIds = [];
    private uint _refreshVisionRefreshCount;
    private uint _advancedRefreshVisionRefreshCount;

    private bool HasActiveSpotLight(uint spotLightId) =>
        _spotLights.Any(entry => entry.SpotLightId == spotLightId && !entry.IsLoss);

    public void ApplySpotLightPropertyScales(IDictionary<uint, uint> properties, uint roleUniqueId)
    {
        if (GetSpotLightIdForRole(roleUniqueId) != HardBattleTreasureSpotLightId) return;
        properties[1013] = (properties.TryGetValue(1013, out var heal) ? heal : 0) + 200;
        properties[1019] = (properties.TryGetValue(1019, out var shield) ? shield : 0) + 200;
    }

    public uint SpotLightStrengthBonusFixpoint =>
        (HasActiveSpotLight(RefreshVisionSpotLightId) ? RefreshVisionStrengthPerRefresh * _refreshVisionRefreshCount : 0) +
        (HasActiveSpotLight(AdvancedRefreshVisionSpotLightId) ? 20 * _advancedRefreshVisionRefreshCount : 0) +
        (FieldScholarExp.Keys.Any(HasActiveSpotLight) ? FieldScholarStrengthPerLevel * PlayerLevel : 0);

    private void RecordSpotLightBattleParticipants()
    {
        _lastBattleBoardUniqueIds.Clear();
        foreach (var role in Roles.Where(role => IsActivePosition(role.Position)))
            _lastBattleBoardUniqueIds.Add(role.UniqueId);
    }

    private void OnSpotLightNodeEntered()
    {
        if (_lastBattleBoardUniqueIds.Count == 0) return;
        var participated = _spotLights.Count(entry => entry.SpotLightId is RefreshVisionSpotLightId or AdvancedRefreshVisionSpotLightId && !entry.IsLoss &&
                                                      _lastBattleBoardUniqueIds.Contains(entry.RoleUniqueId));
        var scholarExp = _spotLights
            .Where(entry => !entry.IsLoss && _lastBattleBoardUniqueIds.Contains(entry.RoleUniqueId))
            .Sum(entry => (long)FieldScholarExp.GetValueOrDefault(entry.SpotLightId));
        _lastBattleBoardUniqueIds.Clear();
        if (participated > 0) AdjustFreeRefresh((int)(RefreshVisionFreeRefresh * participated));
        if (scholarExp > 0) AddPlayerExp((uint)scholarExp);
    }

    private void OnSpotLightShopRefreshed()
    {
        if (HasActiveSpotLight(RefreshVisionSpotLightId)) _refreshVisionRefreshCount++;
        if (HasActiveSpotLight(AdvancedRefreshVisionSpotLightId)) _advancedRefreshVisionRefreshCount++;
    }

    /// <summary>One worn advanced item becomes its privileged counterpart for this battle only.</summary>
    private void ApplyNanoModification(GridFightRoleState role, List<(uint EquipmentId, uint UniqueId)> equipment)
    {
        if (GetSpotLightIdForRole(role.UniqueId) != NanoModificationSpotLightId) return;
        for (var index = 0; index < equipment.Count; index++)
        {
            var item = equipment[index];
            if (GameData.GridFightEquipmentData.GetValueOrDefault(item.EquipmentId)?.EquipCategory != GridFightEquipCategoryEnum.Craftable ||
                !GameData.GridFightEquipUpgradeData.TryGetValue(item.EquipmentId, out var upgrade) ||
                GameData.GridFightEquipmentData.GetValueOrDefault(upgrade.UpgradeID)?.EquipCategory != GridFightEquipCategoryEnum.Radiant)
                continue;
            equipment[index] = (upgrade.UpgradeID, item.UniqueId);
            return;
        }
    }
}
