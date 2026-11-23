using March7thHoney.GameServer.Game.Battle;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Augment 203801, Asta's dog (艾丝妲的猛犬！). Capture: NPC 8 at pos 11, locked equipment 352607,
/// and battle buff 35402052. The client battle config owns the 400-speed / 2000%*luck formula.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint AstaDogAugmentId = 203801;
    public const uint AstaDogNpcId = 8;
    public const uint AstaDogNpcPosition = 11;
    public const uint AstaDogEquipmentId = 352607;
    public const uint AstaDogBattleBuffId = 35402052;
    public const uint AstaDogEffectId = 112;

    public GridFightNpcState? AstaDogNpc { get; private set; }

    public IReadOnlyList<GridFightEquipmentState> GetAstaDogEquipment()
    {
        if (AstaDogNpc == null) return [];
        return AstaDogNpc.EquippedEquipmentUniqueIds
            .Select(uid => Equipments.FirstOrDefault(item => item.UniqueId == uid))
            .Where(item => item != null).Cast<GridFightEquipmentState>().ToList();
    }

    private void InitializeAstaDogInvestment()
    {
        if (AstaDogNpc != null) return;
        if (!TryAddEquipment(AstaDogEquipmentId, out var equipment, NpcEquipmentSource, isLocked: true)) return;
        var npc = new GridFightNpcState
        {
            UniqueId = _nextEntityUniqueId++,
            NpcId = AstaDogNpcId,
            Position = AstaDogNpcPosition,
        };
        npc.EquippedEquipmentUniqueIds.Add(equipment.UniqueId);
        AstaDogNpc = npc;
    }

    public void ApplyAstaDogBattleBuff(BattleInstance battle)
    {
        if (AstaDogNpc == null || !ActiveInvestmentIds.Contains(AstaDogAugmentId) ||
            battle.Buffs.Any(buff => buff.BuffID == (int)AstaDogBattleBuffId)) return;
        battle.Buffs.Add(new MazeBuff((int)AstaDogBattleBuffId, 1, -1)
        {
            WaveFlag = -1,
            IncludeOwnerInTargetList = false,
        });
    }
}

public sealed partial class GridFightManager
{
    private void AppendAstaDogSelectionResults(GridFightSyncUpdateResultScNotify notify, uint augmentId)
    {
        var session = Session;
        var npc = session?.AstaDogNpc;
        if (session == null || npc == null || augmentId != GridFightSession.AstaDogAugmentId) return;
        var equipment = session.GetAstaDogEquipment().FirstOrDefault();
        if (equipment == null) return;

        var add = NewAugmentResult(augmentId, GridFightSession.AstaDogEffectId);
        // Official packet publishes the player-role cap before adding the NPC, plus the unchanged bench cap.
        add.UpdateDynamicList.Add(new GridFightSyncData { MaxActiveRoleCount = session.MaxActiveRoles });
        add.UpdateDynamicList.Add(new GridFightSyncData { JLOCMDIDPCI = session.OffFieldMaxCount });
        var items = new GridFightGameItemSyncInfo();
        items.GridFightEquipmentList.Add(BuildEquipment(equipment));
        add.UpdateDynamicList.Add(new GridFightSyncData { AddGameItemInfo = items });
        notify.ResultList.Add(add);

        var bind = NewAugmentResult(augmentId, GridFightSession.AstaDogEffectId);
        bind.UpdateDynamicList.Add(new GridFightSyncData { EJMDHDJCGDI = BuildPepeNpc(npc, 0) });
        bind.UpdateDynamicList.Add(new GridFightSyncData { DHLEOLKNAMI = BuildPepeNpc(npc) });
        notify.ResultList.Add(bind);

        var population = NewAugmentResult(augmentId, GridFightSession.AstaDogEffectId);
        population.UpdateDynamicList.Add(new GridFightSyncData
        {
            MaxActiveRoleCount = Math.Min(GridFightSession.TeamPopulationLimit, session.MaxActiveRoles + 1),
        });
        notify.ResultList.Add(population);
    }
}
