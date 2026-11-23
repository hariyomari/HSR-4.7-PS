using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Portal 118, the labor dispatch contract (劳务派遣合同).
/// Official 4.5 capture behavior:
/// - when selected, grant one random emblem before the initial role supply;
/// - whenever a boss node is entered, grant one random role belonging to that emblem's trait.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint LaborDispatchPortalId = 118;
    public const uint LaborDispatchEffectId = 101;
    public const uint LaborDispatchEquipmentSource = 0;

    private GridFightRoleAcquisitionResult? _laborDispatchBossReward;

    public GridFightEquipmentState? LaborDispatchEmblem { get; private set; }
    public uint LaborDispatchTraitId { get; private set; }
    public bool HasLaborDispatchPortal => SelectedPortalId == LaborDispatchPortalId;

    private void InitializeLaborDispatchPortal()
    {
        LaborDispatchEmblem = null;
        LaborDispatchTraitId = 0;
        _laborDispatchBossReward = null;
        if (!HasLaborDispatchPortal) return;

        var emblemId = _catalog.RollLaborDispatchEmblem();
        if (emblemId == 0 ||
            !GameData.GridFightEquipmentData.TryGetValue(emblemId, out var config) ||
            !TryAddEquipment(emblemId, out var equipment, LaborDispatchEquipmentSource))
            return;

        var traitId = config.EquipFuncParamList.FirstOrDefault();
        if (traitId == 0)
        {
            Equipments.Remove(equipment);
            return;
        }

        LaborDispatchEmblem = equipment;
        LaborDispatchTraitId = traitId;
    }

    private void PrepareLaborDispatchBossReward()
    {
        _laborDispatchBossReward = null;
        if (!HasLaborDispatchPortal ||
            LaborDispatchTraitId == 0 ||
            CurrentNode.NodeType != GridFightNodeTypeEnum.Boss)
            return;

        var roleId = _catalog.RollRoleForTrait(LaborDispatchTraitId);
        if (roleId == 0 ||
            !GameData.GridFightRoleBasicInfoData.ContainsKey(roleId) ||
            !GameData.GridFightRoleStarData.ContainsKey(roleId << 4 | 1u))
            return;

        var role = new GridFightRoleState
        {
            UniqueId = _nextEntityUniqueId++,
            RoleId = roleId,
            Star = 1,
            Position = GetFreeBenchPosition(),
        };
        Roles.Add(role);
        RecalculateRoleSwitches();
        var mergeSteps = MergeRoles(role.RoleId);
        RecalculateRoleSwitches();
        _laborDispatchBossReward = FinalizeRoleAcquisition(new GridFightRoleAcquisitionResult(
            true,
            [role],
            mergeSteps,
            GetFinalMergedRoles(mergeSteps)));
    }

    public GridFightRoleAcquisitionResult? TakeLaborDispatchBossReward()
    {
        var reward = _laborDispatchBossReward;
        _laborDispatchBossReward = null;
        return reward;
    }
}
