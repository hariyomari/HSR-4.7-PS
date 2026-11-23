using March7thHoney.Enums.GridFight;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Pepe Cameo (佩佩客串, 331001): "佩佩闪亮登场！每完美通关一个战斗节点，佩佩就会穿戴一件进阶装备，进入5个节点后，阿兰接走佩佩们并留下所有装备。"
///     Reuses the season-1 Pepe carrier (NPC 7 at slot 11, see GridFightPepePortal) so team, battle and settlement
///     syncs already include it. Pepe holds up to three locked advanced (Craftable) items; further clears hand the
///     item straight to the player. The add/update/remove packets follow the captured Lizhi Procedure (狸智手续) NPC
///     sequence and are sent with the next round-begin sync. No official capture of this augment exists.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint PepeCameoAugmentId = 331001;
    private const uint PepeCameoNodeCount = 5;
    private const int PepeCameoEquipmentSlots = 3;

    private readonly List<GridFightPepeCameoEvent> _pepeCameoEvents = [];
    private uint _pepeCameoNodes;
    private bool _pepeCameoActive;

    private void StartPepeCameo()
    {
        if (PepeNpc != null || _pepeCameoActive) return;
        PepeNpc = new GridFightNpcState
        {
            UniqueId = _nextEntityUniqueId++,
            NpcId = PepeNpcId,
            Position = PepeNpcPosition,
        };
        _pepeCameoActive = true;
        _pepeCameoNodes = 0;
        _pepeCameoEvents.Add(new GridFightPepeCameoEvent(GridFightPepeCameoEventKind.Arrive, PepeNpc, []));
    }

    private void OnPepeCameoBattleCleared(bool isPerfectFinish)
    {
        if (!_pepeCameoActive || PepeNpc == null || !isPerfectFinish) return;
        var equipmentId = _catalog.RollEquipment(GridFightEquipCategoryEnum.Craftable, 0);
        if (equipmentId == 0) return;
        var keep = PepeNpc.EquippedEquipmentUniqueIds.Count < PepeCameoEquipmentSlots;
        if (!TryAddEquipment(equipmentId, out var equipment, PepeEquipmentSource, isLocked: keep)) return;
        if (keep) PepeNpc.EquippedEquipmentUniqueIds.Add(equipment.UniqueId);
        _pepeCameoEvents.Add(new GridFightPepeCameoEvent(
            keep ? GridFightPepeCameoEventKind.Equip : GridFightPepeCameoEventKind.Overflow, PepeNpc, [equipment]));
    }

    private void OnPepeCameoNodeEntered()
    {
        if (!_pepeCameoActive || PepeNpc == null || ++_pepeCameoNodes < PepeCameoNodeCount) return;
        var npc = PepeNpc;
        var released = npc.EquippedEquipmentUniqueIds
            .Select(id => Equipments.FirstOrDefault(item => item.UniqueId == id))
            .OfType<GridFightEquipmentState>()
            .ToList();
        foreach (var item in released) item.IsLocked = false;
        PepeNpc = null;
        _pepeCameoActive = false;
        _pepeCameoEvents.Add(new GridFightPepeCameoEvent(GridFightPepeCameoEventKind.Leave, npc, released));
    }

    public IReadOnlyList<GridFightPepeCameoEvent> TakePepeCameoEvents() => Drain(_pepeCameoEvents);
}

public enum GridFightPepeCameoEventKind
{
    Arrive,
    Equip,
    Overflow,
    Leave,
}

public sealed record GridFightPepeCameoEvent(
    GridFightPepeCameoEventKind Kind,
    GridFightNpcState Npc,
    IReadOnlyList<GridFightEquipmentState> Equipment);

public sealed partial class GridFightManager
{
    private void AppendPepeCameoResults(GridFightSyncUpdateResultScNotify notify)
    {
        var session = Session;
        if (session == null) return;
        foreach (var cameo in session.TakePepeCameoEvents())
        {
            var result = new GridFightSyncResultData { UpdateSource = GridFightUpdateSrcType.KgridFightSrcAugment };
            result.EffectParamList.Add(GridFightSession.PepeCameoAugmentId);
            if (cameo.Equipment.Count > 0)
            {
                var items = new GridFightGameItemSyncInfo();
                items.GridFightEquipmentList.Add(cameo.Equipment.Select(BuildEquipment));
                result.UpdateDynamicList.Add(cameo.Kind == GridFightPepeCameoEventKind.Leave
                    ? new GridFightSyncData { UpdateGameItemInfo = items }
                    : new GridFightSyncData { AddGameItemInfo = items });
            }
            switch (cameo.Kind)
            {
                case GridFightPepeCameoEventKind.Arrive:
                    result.UpdateDynamicList.Add(new GridFightSyncData { EJMDHDJCGDI = BuildPepeNpc(cameo.Npc, 0) });
                    result.UpdateDynamicList.Add(new GridFightSyncData { DHLEOLKNAMI = BuildPepeNpc(cameo.Npc) });
                    result.UpdateDynamicList.Add(new GridFightSyncData { JLOCMDIDPCI = session.OffFieldMaxCount });
                    break;
                case GridFightPepeCameoEventKind.Equip:
                    result.UpdateDynamicList.Add(new GridFightSyncData { DHLEOLKNAMI = BuildPepeNpc(cameo.Npc) });
                    break;
                case GridFightPepeCameoEventKind.Leave:
                    result.UpdateDynamicList.Add(new GridFightSyncData { DHLEOLKNAMI = BuildPepeNpc(cameo.Npc, 0) });
                    result.UpdateDynamicList.Add(new GridFightSyncData { NJCEFEJBEOP = cameo.Npc.UniqueId });
                    result.UpdateDynamicList.Add(new GridFightSyncData { JLOCMDIDPCI = session.OffFieldMaxCount });
                    break;
            }
            notify.ResultList.Add(result);
        }
    }
}
