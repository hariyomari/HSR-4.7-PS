using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Wealth Diamond (财富宝钻, 350701) population-cap compatibility.
/// Each crown grants +1 playable team slot and +1 backstage slot, but player team population
/// is hard-capped at 13 by the 4.5 client. The battle snapshot intentionally reports the
/// pre-crown deployment cap, matching the official capture where ActiveRoleCount may exceed it.
/// </summary>
public sealed partial class GridFightSession
{
    private uint _preparationPhaseCount;

    private void EnterPreparingPhase()
    {
        Phase = GridFightSessionPhase.Preparing;
        _preparationPhaseCount++;
        foreach (var equipment in Roles.SelectMany(GetEquippedEquipment).DistinctBy(item => item.UniqueId))
        {
            if (!GameData.GridFightEquipmentData.TryGetValue(equipment.EquipmentId, out var config) ||
                config.EquipFunc != GridFightEquipFuncTypeEnum.AvatarMaxNumberAdd) continue;
            var interval = (uint)Math.Clamp(config.ParamList.ElementAtOrDefault(0)?.Value ?? 0, 0, uint.MaxValue);
            var amount = (uint)Math.Clamp(config.ParamList.ElementAtOrDefault(1)?.Value ?? 0, 0, uint.MaxValue);
            if (interval != 0 && _preparationPhaseCount % interval == 0) AddGold(amount);
        }
    }

    public const uint WealthDiamondEquipmentId = 350701; // Wealth Diamond (财富宝钻)
    public const uint TeamPopulationLimit = 13;

    public uint WealthDiamondCount => (uint)Equipments.Count(equipment =>
        equipment.EquipmentId == WealthDiamondEquipmentId);

    private uint NonWealthEquipmentActiveRoleBonus => (uint)Math.Min(
        (ulong)uint.MaxValue,
        Equipments
            .Where(equipment => equipment.EquipmentId != WealthDiamondEquipmentId)
            .Select(equipment => GameData.GridFightEquipmentData.GetValueOrDefault(equipment.EquipmentId))
            .Where(equipment => equipment?.EquipFunc == GridFightEquipFuncTypeEnum.AvatarMaxNumberAdd)
            .Aggregate(0UL, (total, equipment) =>
                total + equipment!.EquipFuncParamList.FirstOrDefault()));

    public uint BattleMaxActiveRoles
    {
        get
        {
            var baseCount = _catalog.GetPlayerLevel(PlayerLevel).AvatarMaxNumber;
            return (uint)Math.Min(
                TeamPopulationLimit,
                (ulong)baseCount + NonWealthEquipmentActiveRoleBonus);
        }
    }

    public uint EffectiveWealthDiamondBonus
    {
        get
        {
            var withoutWealth = BattleMaxActiveRoles;
            if (withoutWealth >= TeamPopulationLimit) return 0;
            return Math.Min(WealthDiamondCount, TeamPopulationLimit - withoutWealth);
        }
    }
}
