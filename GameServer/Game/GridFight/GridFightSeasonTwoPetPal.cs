using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
///     Season-2 partner (本局奇遇, GridFightPetPalEff) effects that only change existing server rules: supply
///     contents, battle settlement, exp purchases, crafting and run-start gifts. The rules follow each effect's
///     SwitchDesc; partners whose effects need the bonfire node (幕后茶歇) or client-side state are handled elsewhere.
/// </summary>
public sealed partial class GridFightSession
{
    private const uint PetPalStartEquipmentEffectId = 105001; // 杨叔: 开局获得【坍缩奇点】
    private const uint PetPalStartEquipmentId = 353005;
    private static readonly HashSet<uint> PetPalApologyEffectIds = [106005, 106006]; // 周天鸽鸽了: 赔礼 22007
    private const uint PetPalApologyBonusId = 22007;

    private bool _petPalStartGranted;
    private bool _petPalGuardUsed;
    private readonly HashSet<uint> _petPalCraftRefundChapters = [];
    private readonly List<uint> _petPalPendingEquipment = [];

    private bool IsPetPalEffect(params uint[] effectIds) =>
        IsSeasonTwoRules && PetPalEffectId != 0 && effectIds.Contains(PetPalEffectId);

    // ---------------------------------------------------------------- Feifei Fox (绯绯狐)
    // Fan gifts appear in supplies.

    /// <summary>Applies the Feifei Fox supply rules (effects 109xxx) to a freshly rolled supply offer.</summary>
    private IReadOnlyList<GridFightSupplyOfferState> ApplyPetPalSupplyRules(IReadOnlyList<GridFightSupplyOfferState> offers)
    {
        if (!IsSeasonTwoRules || PetPalEffectId / 1000 != 109) return offers;
        var chapter = ChapterId;
        var emblems = GridFightSeasonScope.Equipment(Season)
            .Where(row => row.EquipCategory == GridFightEquipCategoryEnum.Emblem)
            .Select(row => row.ID)
            .ToList();
        uint RandomEmblem() => emblems.Count == 0 ? 0 : emblems[Random.Shared.Next(emblems.Count)];
        List<uint> RolesOfRarity(uint rarity) => _catalog.RolePool
            .Where(role => role.Rarity == rarity && IsRestrictedRoleUnlockedForRun(role.ID))
            .Select(role => role.ID)
            .ToList();

        var result = new List<GridFightSupplyOfferState>();
        var usedRoles = new HashSet<uint>();
        foreach (var offer in offers)
        {
            var updated = offer;
            switch (PetPalEffectId)
            {
                case 109001 when chapter == 1: // 第一位面补给的装备变为星徽
                case 109003 when chapter == 2: // 第二位面补给的装备变为星徽
                    if (RandomEmblem() is var emblem and > 0) updated = updated with { EquipmentId = emblem };
                    break;
                case 109002 when chapter == 1: // 第一位面补给的装备变为2个随机星徽
                    if (RandomEmblem() is var first and > 0)
                        updated = updated with { EquipmentId = first, ExtraEquipmentIds = [RandomEmblem()] };
                    break;
                case 109004: // 本场对局的所有补给变为2星角色补给
                    updated = updated with { Star = 2 };
                    break;
                case 109005 when chapter <= 2: // 第一和第二个位面的补给变为3星1费角色补给
                    updated = ReplaceSupplyRole(updated, RolesOfRarity(1), usedRoles) with { Star = 3 };
                    break;
                case 109006 when chapter == 1: // 第一位面补给中角色全变为4费角色
                    updated = ReplaceSupplyRole(updated, RolesOfRarity(4), usedRoles);
                    break;
                case 109007 when chapter == 2: // 第二位面补给中角色全变为5费角色
                    updated = ReplaceSupplyRole(updated, RolesOfRarity(5), usedRoles);
                    break;
            }
            usedRoles.Add(updated.RoleId);
            result.Add(updated);
        }
        return result;
    }

    private static GridFightSupplyOfferState ReplaceSupplyRole(
        GridFightSupplyOfferState offer,
        List<uint> candidates,
        HashSet<uint> usedRoles)
    {
        var free = candidates.Where(id => !usedRoles.Contains(id)).ToList();
        if (free.Count == 0) free = candidates;
        return free.Count == 0 ? offer : offer with { RoleId = free[Random.Shared.Next(free.Count)] };
    }

    // ---------------------------------------------------------------- Wangwang Dan (汪汪丹)
    // The first lost battle becomes a perfect clear.

    private uint ApplyPetPalGuard(uint progressPercent)
    {
        if (progressPercent >= 100 || _petPalGuardUsed || !IsSeasonTwoRules) return progressPercent;
        var fromChapter = PetPalEffectId switch
        {
            102001 => 1u,
            102002 => 2u,
            102003 => 3u,
            _ => 0u,
        };
        if (fromChapter == 0 || ChapterId < fromChapter) return progressPercent;
        _petPalGuardUsed = true;
        return 100;
    }

    // ---------------------------------------------------------------- March Rabbit (三月兔)
    // Buying exp gives 1 extra exp.

    private uint PetPalBuyExpBonus() => PetPalEffectId switch
    {
        103002 when IsSeasonTwoRules && ChapterId == 2 => 1,
        103004 when IsSeasonTwoRules && ChapterId == 3 => 1,
        _ => 0,
    };

    // ---------------------------------------------------------------- Bandage Cat (绷带猫)
    // The first equipment craft in each plane returns one simple equipment.

    private void ApplyPetPalCraftRefund(IReadOnlyList<GridFightEquipmentState> materials)
    {
        if (!IsPetPalEffect(110001) || !_petPalCraftRefundChapters.Add(ChapterId)) return;
        var basic = materials.FirstOrDefault(item =>
            GameData.GridFightEquipmentData.GetValueOrDefault(item.EquipmentId)?.EquipCategory ==
            GridFightEquipCategoryEnum.Basic);
        if (basic != null) _petPalPendingEquipment.Add(basic.EquipmentId);
    }

    // ---------------------------------------------------------------- run-start gifts and deferred grants

    private void GrantPetPalRewards(GridFightSeasonTwoChanges changes)
    {
        if (!_petPalStartGranted)
        {
            _petPalStartGranted = true;
            if (IsPetPalEffect(PetPalStartEquipmentEffectId)) _petPalPendingEquipment.Add(PetPalStartEquipmentId);
            if (PetPalApologyEffectIds.Contains(PetPalEffectId) &&
                GridFightSpecialFeatures.TryResolveBonus(PetPalApologyBonusId, out var gift))
            {
                var application = ApplySeasonTwoRewards(gift);
                if (application.Success) changes.Merge(application);
            }
        }
        foreach (var equipmentId in _petPalPendingEquipment)
            if (TryAddEquipment(equipmentId, out var equipment))
                changes.AddedEquipments.Add(equipment);
        _petPalPendingEquipment.Clear();
    }
}
