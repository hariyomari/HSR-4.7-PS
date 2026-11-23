using March7thHoney.Data;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightTraitLayerState(uint TraitId, uint Layer, IReadOnlyList<GridFightRoleState> Members);

public enum GridFightSeasonTwoPendingKind
{
    DanHengChooseTrait,
    RangerAppoint,
    ClaraEnhance,
    ChimeraSelect,
    Bonfire,
    SpotLightSelect,
    AhaUltra,
}

public sealed record GridFightSeasonTwoPendingState(
    GridFightSeasonTwoPendingKind Kind,
    uint QueuePosition,
    uint TraitId,
    uint EffectId,
    IReadOnlyList<uint> Options);

/// <summary>Board/item side effects of a season-2 trait refresh that the client has to be told about.</summary>
public sealed class GridFightSeasonTwoChanges
{
    public List<GridFightRoleState> AddedRoles { get; } = [];
    public List<uint> RemovedRoleIds { get; } = [];
    public List<GridFightRoleState> UpdatedRoles { get; } = [];
    public List<GridFightEquipmentState> AddedEquipments { get; } = [];
    public List<GridFightEquipmentState> RemovedEquipments { get; } = [];
    public List<GridFightConsumableChange> ConsumableChanges { get; } = [];
    public List<GridFightRoleAcquisitionResult> RoleAcquisitions { get; } = [];
    public bool GoldChanged { get; set; }
    public bool PlayerLevelChanged { get; set; }
    public bool LineupHpChanged { get; set; }
    public bool ShopChanged { get; set; }
    public bool PendingStarted { get; set; }
    public GridFightSpotLightChanges SpotLight { get; } = new();

    public bool IsEmpty => AddedRoles.Count == 0 && RemovedRoleIds.Count == 0 && UpdatedRoles.Count == 0 &&
                           AddedEquipments.Count == 0 && RemovedEquipments.Count == 0 && ConsumableChanges.Count == 0 &&
                           RoleAcquisitions.Count == 0 &&
                           !GoldChanged && !PlayerLevelChanged && !LineupHpChanged && !ShopChanged && !PendingStarted &&
                           SpotLight.Added.Count == 0 && SpotLight.Updated.Count == 0 && SpotLight.Removed.Count == 0;

    public void Merge(GridFightRewardApplication application)
    {
        GoldChanged |= application.GoldChanged;
        PlayerLevelChanged |= application.PlayerLevelChanged || application.PlayerExpChanged;
        LineupHpChanged |= application.LineupHpChanged;
        AddedEquipments.AddRange(application.AddedEquipments);
        ConsumableChanges.AddRange(application.ConsumableChanges);
        RoleAcquisitions.AddRange(application.RoleAcquisitions);
    }
}

/// <summary>
///     Season-2 (4.6.51 client) unique/origin trait effects. Wire layouts come from disassembling the client's
///     GridFight*TraitEffect.OnUpdate / request senders; rules the client does not
///     encode (pools, triggers) follow the trait descriptions and are marked as such.
/// </summary>
public sealed partial class GridFightSession
{
    public const uint DanHengTraitId = 300102;
    public const uint DanHengChooseTraitEffectId = 3001021;
    public const uint DanHengTwoInOneEffectId = 3001022;
    public const uint DanHengMainRoleId = 121302;
    public const uint DanHengSubRoleId = 141402;
    public const uint PhainonTraitId = 300202;
    public const uint PhainonEffectId = 3002021;
    public const uint PhainonRoleId = 140802;
    public const string PhainonSavedValueKey = "GP_Avatar_S2_Phainon_00";
    public const uint RangerTraitId = 300302;
    public const uint RangerEffectId = 3003021;
    public const uint StellaronHunterTraitId = 100802;
    public const uint StellaronHunterEffectId = 1008021;
    public const uint GoldenHotelTraitId = 101202;
    public const uint GoldenHotelEffectId = 1012021;
    public const uint MemberLinkTraitId = 101002;
    public const uint MemberLinkEffectId = 1010021;
    public const uint CounterTraitId = 200802;
    public const uint CounterKingEffectId = 2008021;
    private const uint TraitEquipmentSource = 3;

    // Amphoreus Chrysos heirs of the season-2 pool (盗火行者: 黄金裔额外转化30%). 4.6.52 ships the client's list as
    // GridFightTraitConstValueS2 GridFight_PhanionEffectSpecialRoleIDList; caches built from 4.6.51 data lack it and
    // fall back to the list that followed the lore (it missed 141302).
    private static readonly HashSet<uint> FallbackChrysosHeirRoleIds =
    [
        140202, 140302, 140402, 140502, 140602, 140702, 140802, 140902, 141002, 141202, 141402, 141502,
    ];

    private static IReadOnlySet<uint> ChrysosHeirRoleIds =>
        GameData.GridFightConstValueData.GetValueOrDefault("GridFight_PhanionEffectSpecialRoleIDList")?.AsUInt32List() is
            { Count: > 0 } ids
            ? ids.ToHashSet()
            : FallbackChrysosHeirRoleIds;

    private readonly Dictionary<uint, uint> _traitTemplateIds = [];
    private readonly Dictionary<uint, List<uint>> _templateEquipmentUniqueIds = [];
    private uint _danHengChosenTraitId;
    private uint _danHengSubUniqueId;
    private uint _danHengMainUniqueId;
    private uint _phainonKindling;
    private uint _phainonConsumedCount;
    private readonly List<uint> _rangerRoleIds = [];
    private readonly List<uint> _memberLinkUniqueIds = [];
    private GridFightSeasonTwoPendingState? _seasonTwoPending;
    private readonly HashSet<uint> _finishedSeasonTwoQueuePositions = [];

    /// <summary>A season-2 prompt at this queue position was already answered (the client resent it).</summary>
    public bool IsFinishedSeasonTwoPending(uint queuePosition) =>
        queuePosition != 0 && _finishedSeasonTwoQueuePositions.Contains(queuePosition) &&
        _seasonTwoPending?.QueuePosition != queuePosition;

    public bool IsSeasonTwoRules => !_catalog.IsSeasonOne;
    public IReadOnlyDictionary<uint, uint> TraitTemplateIds => _traitTemplateIds;
    public GridFightSeasonTwoPendingState? SeasonTwoPending => _seasonTwoPending;
    public uint DanHengChosenTraitId => _danHengChosenTraitId;
    public uint DanHengSubUniqueId => _danHengSubUniqueId;
    public uint DanHengMainUniqueId => _danHengMainUniqueId;
    public uint PhainonKindling => _phainonKindling;
    public uint PhainonConsumedCount => _phainonConsumedCount;
    public IReadOnlyList<uint> MemberLinkUniqueIds => _memberLinkUniqueIds;

    public IReadOnlyList<uint> GetTemplateEquipmentUniqueIds(uint traitId) =>
        _templateEquipmentUniqueIds.TryGetValue(traitId, out var list) ? list : [];

    private void InitializeSeasonTwoTraits()
    {
        if (!IsSeasonTwoRules) return;
        foreach (var group in GridFightSeasonTwoTables.TraitEquipTemplates.GroupBy(row => row.TraitId))
        {
            var rows = group.Where(row => row.Weight > 0).ToList();
            if (rows.Count == 0) continue;
            var roll = Random.Shared.NextInt64(rows.Sum(row => (long)row.Weight));
            foreach (var row in rows)
            {
                roll -= row.Weight;
                if (roll >= 0) continue;
                _traitTemplateIds[group.Key] = row.TemplateId;
                break;
            }
        }
        InitializeSeasonTwoShops();
        InitializeSeasonTwoElation();
        InitializeBonfireNodes();
    }

    /// <summary>Trait layers of the board, counted the same way as GridGameTraitInfo/GridFightTraitInfo.</summary>
    public IReadOnlyDictionary<uint, GridFightTraitLayerState> ResolveTraitLayers()
    {
        var activeRoles = Roles
            .Where(role => IsActivePosition(role.Position))
            .OrderBy(role => role.Position)
            .ToList();
        var roleTraits = activeRoles.ToDictionary(role => role, ResolveRoleTraitIds);
        var pepeTraits = ResolvePepeTraitMembers().Select(entry => entry.TraitId).ToHashSet();
        var result = new Dictionary<uint, GridFightTraitLayerState>();
        foreach (var traitId in roleTraits.Values.SelectMany(ids => ids).Concat(pepeTraits).Distinct())
        {
            var members = activeRoles.Where(role => roleTraits[role].Contains(traitId)).ToList();
            var count = (uint)members.Count + (PepeNpc != null && pepeTraits.Contains(traitId) ? 1u : 0u) +
                        ResolveTraitExtraMemberCount(traitId, activeRoles);
            result[traitId] = new GridFightTraitLayerState(
                traitId,
                Battle.GridFightBattleProtoBuilder.ResolveTraitLayer(traitId, count),
                members);
        }
        return result;
    }

    public uint ResolveTraitLayer(uint traitId) =>
        ResolveTraitLayers().TryGetValue(traitId, out var state) ? state.Layer : 0;

    public static IReadOnlyList<double> GetTraitEffectLayerParams(uint effectId, uint layer)
    {
        if (!GameData.GridFightTraitEffectLayerPaData.TryGetValue(effectId, out var layers) || layers.Count == 0)
            return [];
        var row = layers.Where(pair => pair.Key <= layer).OrderByDescending(pair => pair.Key)
            .Select(pair => pair.Value).FirstOrDefault() ?? layers.OrderBy(pair => pair.Key).First().Value;
        return row.EffectParamList.Select(param => param.Value).ToList();
    }

    private static IReadOnlyList<uint> BaseTraits(uint roleId) =>
        GameData.GridFightRoleBasicInfoData.GetValueOrDefault(roleId)?.TraitList ?? [];

    private bool IsBaseTraitOnBoard(uint traitId) =>
        Roles.Any(role => IsActivePosition(role.Position) && BaseTraits(role.RoleId).Contains(traitId));

    private static bool IsFrontPosition(uint position) => position is >= 1 and <= 4;

    /// <summary>Traits granted by season-2 effects (双龙吟 chosen trait, appointed 巡海游侠).</summary>
    private void AddSeasonTwoGrantedTraitIds(GridFightRoleState role, HashSet<uint> traits)
    {
        if (!IsSeasonTwoRules) return;
        // GridFightGameTrait.GetChooseTraitByManualTraitIDs: the chosen trait goes to every Dan Heng IL (丹恒•饮月) while
        // the Dan Heng trait (双龙吟) is active.
        if (_danHengChosenTraitId != 0 && role.RoleId == DanHengMainRoleId && IsBaseTraitOnBoard(DanHengTraitId))
            traits.Add(_danHengChosenTraitId);
        // GetAdditionalTraitRoleChooseTraitIDs: role ids in the appointment list carry 300302 while it is active.
        // A confirmed 3★ 7-cost Aha (星神★阿哈): every fielded role joins MoonGame (幻月游戏) and Elation (欢愉).
        if (_ahaUltraConfirmed && IsActivePosition(role.Position) && IsAhaMaxStarReached)
        {
            traits.Add(MoonGameTraitId);
            traits.Add(ElationTraitId);
        }
        AddSpotLightGrantedTraitIds(role, traits);
        if (!traits.Contains(RangerTraitId) && IsBaseTraitOnBoard(RangerTraitId) &&
            ResolveRangerRoleIds().Contains(role.RoleId))
            traits.Add(RangerTraitId);
    }

    public bool IsPopulationFreeRole(GridFightRoleState role) =>
        IsSeasonTwoRules && _danHengSubUniqueId != 0 && role.UniqueId == _danHengSubUniqueId;

    private int CountPopulationRoles(IEnumerable<(uint UniqueId, uint Position)> roles) =>
        roles.Count(role => IsActivePosition(role.Position) &&
                            !(IsSeasonTwoRules && _danHengSubUniqueId != 0 && role.UniqueId == _danHengSubUniqueId));

    public bool HasSeasonTwoPending => _seasonTwoPending != null;

    /// <summary>
    ///     Keeps season-2 trait state consistent with the board (Tenglong summon, template equipment, appointment
    ///     prompts...). Called after every GridFight request and before a battle is built.
    /// </summary>
    public GridFightSeasonTwoChanges RefreshSeasonTwoTraits()
    {
        var changes = new GridFightSeasonTwoChanges();
        if (!IsSeasonTwoRules) return changes;
        if (Phase is GridFightSessionPhase.Preparing or GridFightSessionPhase.AwaitingBackToPrepare)
            RefreshDanHengSub(changes);
        PruneMemberLink();
        if (Phase == GridFightSessionPhase.Preparing)
        {
            EnsureAhaModifier();
            RefreshSpotLight(changes.SpotLight);
            var aha = AbsorbAhaCopies();
            changes.RemovedRoleIds.AddRange(aha.Absorbed);
            changes.UpdatedRoles.AddRange(aha.Changed);
            if (aha.Merges.Count > 0)
                changes.RoleAcquisitions.Add(new GridFightRoleAcquisitionResult(true, [], aha.Merges, GetFinalMergedRoles(aha.Merges)));
            GrantTemplateEquipment(changes);
            GrantPetPalRewards(changes);
            RefreshSeasonTwoShops(changes);
            RefreshClara(changes);
            RefreshChimera(changes);
            IssueSeasonTwoPending(changes);
        }
        return changes;
    }

    /// <summary>
    ///     The summoned Tenglong (丹恒•腾荒) never uses population (client GridFightTeam.IsRoleUsePopulation) and brings its
    ///     own back cell, like the Pepe/Asta NPCs: the client only unlocks back cells from the server's MaxBackRoleNum, so
    ///     without it the summon takes a cell from the population and, with all cells full, lands in a locked one
    ///     (invisible).
    /// </summary>
    private uint DanHengSubBackCellBonus => IsSeasonTwoRules && _danHengSubUniqueId != 0 ? 1u : 0u;

    /// <summary>Back positions 5..(4 + OffFieldMaxCount) are the unlocked cells (Pepe/Asta sit in the cell their +1 opens).</summary>
    private uint LastUnlockedBackPosition(uint extraCells = 0) => 4u + Math.Min(9u, OffFieldMaxCount + extraCells);

    private void RefreshDanHengSub(GridFightSeasonTwoChanges changes)
    {
        var main = Roles
            .Where(role => role.RoleId == DanHengMainRoleId && IsActivePosition(role.Position))
            .OrderByDescending(role => role.Star)
            .ThenBy(role => role.Position)
            .FirstOrDefault();
        var sub = _danHengSubUniqueId == 0 ? null : Roles.FirstOrDefault(role => role.UniqueId == _danHengSubUniqueId);
        if (sub == null) _danHengSubUniqueId = 0;
        _danHengMainUniqueId = main?.UniqueId ?? 0;
        if (main == null)
        {
            if (sub == null) return;
            var vacated = sub.Position;
            Roles.Remove(sub);
            _danHengSubUniqueId = 0; // the summon's back cell closes from here on
            changes.RemovedRoleIds.Add(sub.UniqueId);
            // Whoever stands in the cell that just locked moves into the summon's old cell.
            var last = LastUnlockedBackPosition();
            if (IsBackPosition(vacated) && vacated <= last &&
                Roles.FirstOrDefault(role => IsBackPosition(role.Position) && role.Position > last) is { } stranded)
            {
                stranded.Position = vacated;
                changes.UpdatedRoles.Add(stranded);
            }
            RecalculateRoleSwitches();
            return;
        }
        if (sub != null)
        {
            var changed = false;
            // A summon placed by an older build may sit in a locked back cell.
            var last = LastUnlockedBackPosition();
            if (sub.Position > last && GetFreeBackPosition(last) is var free and > 0)
            {
                sub.Position = free;
                changed = true;
            }
            if (sub.Star != main.Star)
            {
                sub.Star = main.Star;
                // A star change re-sends the role; equipment beyond the star-based cap goes back to the bag.
                var cap = (int)Math.Clamp(sub.Star, 1u, 3u);
                if (sub.EquippedEquipmentUniqueIds.Count > cap)
                    sub.EquippedEquipmentUniqueIds.RemoveRange(cap, sub.EquippedEquipmentUniqueIds.Count - cap);
                changed = true;
            }
            if (changed) changes.UpdatedRoles.Add(sub);
            return;
        }
        // GridFightTwoInOneEffect: the summoned Tenglong has the main's star and must stay in the back area, inside the
        // unlocked cells including the one it brings.
        var position = GetFreeBackPosition(LastUnlockedBackPosition(extraCells: 1));
        if (position == 0) position = GetFreeBackPosition();
        if (position == 0) return;
        var summoned = new GridFightRoleState
        {
            UniqueId = _nextEntityUniqueId++,
            RoleId = DanHengSubRoleId,
            Star = main.Star,
            Position = position,
        };
        Roles.Add(summoned);
        _danHengSubUniqueId = summoned.UniqueId;
        changes.AddedRoles.Add(summoned);
        RecalculateRoleSwitches();
    }

    private uint GetFreeBackPosition(uint lastPosition = 13)
    {
        for (var position = 5u; position <= lastPosition; position++)
            if (Roles.All(role => role.Position != position) &&
                PepeNpc?.Position != position && AstaDogNpc?.Position != position &&
                LizhiNpcs.All(npc => npc.Position != position) &&
                OwlbertNpcs.All(npc => npc.Position != position))
                return position;
        return 0;
    }

    private void GrantTemplateEquipment(GridFightSeasonTwoChanges changes)
    {
        var layers = ResolveTraitLayers();
        foreach (var (traitId, templateId) in _traitTemplateIds)
        {
            var template = GridFightSeasonTwoTables.TraitEquipTemplates
                .FirstOrDefault(row => row.TraitId == traitId && row.TemplateId == templateId);
            if (template == null) continue;
            if (!_templateEquipmentUniqueIds.TryGetValue(traitId, out var granted))
                _templateEquipmentUniqueIds[traitId] = granted = [];
            var layer = layers.TryGetValue(traitId, out var state) ? state.Layer : 0;
            var effectId = traitId == StellaronHunterTraitId ? StellaronHunterEffectId : GoldenHotelEffectId;
            // GetTraitEffectLayerParams falls back to the lowest row, so an inactive bond is handled here.
            var unlocked = layer == 0 ? 0 : Math.Min((int)GetTraitEffectLayerParams(effectId, layer).FirstOrDefault(),
                template.EquipList.Length);
            // Stellaron Hunter (星核猎手) "按羁绊等级解锁并分配" its Script Equipment: the pieces follow the bond level and
            // leave when it drops. Golden Hotel (大饭店) meal components stay once handed out.
            var bondBound = traitId == StellaronHunterTraitId;
            if (bondBound)
            {
                foreach (var equipment in Equipments.Where(item => granted.Contains(item.UniqueId)))
                    equipment.IsTemporary = true; // runs saved before the flag existed
                while (granted.Count > unlocked)
                {
                    var uniqueId = granted[^1];
                    granted.RemoveAt(granted.Count - 1);
                    RemoveTemporaryEquipment(uniqueId, changes);
                }
            }
            if (layer == 0) continue;
            // GridFightTemplateEquipTraitEffect.IsEquipExtraDescActive: the idx-th template item unlocks with the
            // idx-th layer threshold.
            for (var index = granted.Count; index < unlocked; index++)
            {
                if (!TryAddEquipment(template.EquipList[index], out var equipment, TraitEquipmentSource)) break;
                equipment.IsTemporary = bondBound;
                granted.Add(equipment.UniqueId);
                changes.AddedEquipments.Add(equipment);
            }
        }
    }

    private void RemoveTemporaryEquipment(uint uniqueId, GridFightSeasonTwoChanges changes)
    {
        var equipment = Equipments.FirstOrDefault(item => item.UniqueId == uniqueId);
        if (equipment == null) return;
        if (FindEquipmentWearer(uniqueId) is { } wearer)
        {
            wearer.EquippedEquipmentUniqueIds.Remove(uniqueId);
            if (!changes.UpdatedRoles.Contains(wearer)) changes.UpdatedRoles.Add(wearer);
        }
        Equipments.Remove(equipment);
        changes.AddedEquipments.Remove(equipment);
        changes.RemovedEquipments.Add(equipment);
    }

    // ---------------------------------------------------------------- pending prompts

    private void IssueSeasonTwoPending(GridFightSeasonTwoChanges changes)
    {
        if (_seasonTwoPending != null || HasPendingSelection) return;
        // The bonfire (幕后茶歇) replaces the node itself, so it opens before any trait prompt.
        if (TryStartBonfirePending(changes)) return;
        if (TryStartAhaUltraPending(changes)) return;
        // Spotlight (主角光环) is not pushed here: the client opens it itself through 8508 (see StartSpotLightFromEnter).
        var layers = ResolveTraitLayers();
        if (layers.TryGetValue(DanHengTraitId, out var danHeng) && danHeng.Layer > 0 && _danHengChosenTraitId == 0 &&
            Roles.Any(role => role.RoleId == DanHengMainRoleId && IsActivePosition(role.Position)))
        {
            var candidates = GetDanHengCandidateTraits(danHeng.Layer);
            if (candidates.Count > 0)
            {
                StartSeasonTwoPending(GridFightSeasonTwoPendingKind.DanHengChooseTrait, DanHengTraitId,
                    DanHengChooseTraitEffectId, candidates, changes);
                return;
            }
        }
        if (layers.TryGetValue(RangerTraitId, out var ranger) && ranger.Layer > 0 && _rangerRoleIds.Count == 0 &&
            !HasThreeStarMortenaxOnBoard())
        {
            var candidates = GetRangerCandidates();
            if (candidates.Count > 0)
            {
                StartSeasonTwoPending(GridFightSeasonTwoPendingKind.RangerAppoint, RangerTraitId, RangerEffectId,
                    candidates, changes);
                return;
            }
        }
        if (TryStartChimeraPending(changes)) return;
        IssueClaraPending(changes);
    }

    private void StartSeasonTwoPending(
        GridFightSeasonTwoPendingKind kind,
        uint traitId,
        uint effectId,
        IReadOnlyList<uint> options,
        GridFightSeasonTwoChanges changes)
    {
        _seasonTwoPending = new GridFightSeasonTwoPendingState(kind, PendingQueuePosition, traitId, effectId, options);
        changes.PendingStarted = true;
    }

    /// <summary>Finishes the season-2 pending prompt; returns its queue position (0 if none was open).</summary>
    private uint FinishSeasonTwoPending(GridFightSeasonTwoPendingKind kind)
    {
        var pending = _seasonTwoPending;
        if (pending == null || pending.Kind != kind) return 0;
        _seasonTwoPending = null;
        _finishedSeasonTwoQueuePositions.Add(pending.QueuePosition);
        if (PendingQueuePosition == pending.QueuePosition) PendingQueuePosition++;
        return pending.QueuePosition;
    }

    private bool MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind kind, uint queuePosition) =>
        queuePosition == 0 || _seasonTwoPending is { } pending && pending.Kind == kind &&
        pending.QueuePosition == queuePosition;

    // ---------------------------------------------------------------- Dan Heng (双龙吟)

    public IReadOnlyList<uint> GetDanHengCandidateTraits(uint layer)
    {
        // _GetCandidateTraitIDs: the last four values of the 3001021 layer params (first one is the receiving role).
        var values = GetTraitEffectLayerParams(DanHengChooseTraitEffectId, Math.Max(layer, 1))
            .Select(value => (uint)value)
            .ToList();
        return values.Count <= 1 ? [] : values.Skip(Math.Max(1, values.Count - 4)).ToList();
    }

    /// <summary>
    ///     ChooseTraitByManual: HandlePendingAction 1142 (queuePosition = pending) or TraitUpdate 1946 (edit, 0).
    /// </summary>
    public uint SelectDanHengTrait(uint queuePosition, uint traitId, out bool success)
    {
        success = false;
        if (Phase != GridFightSessionPhase.Preparing ||
            !MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.DanHengChooseTrait, queuePosition) ||
            !GetDanHengCandidateTraits(Math.Max(ResolveTraitLayer(DanHengTraitId), 1)).Contains(traitId))
            return 0;
        _danHengChosenTraitId = traitId;
        success = true;
        return FinishSeasonTwoPending(GridFightSeasonTwoPendingKind.DanHengChooseTrait);
    }

    // ---------------------------------------------------------------- Ranger (巡海游侠)

    private bool HasThreeStarMortenaxOnBoard() =>
        Roles.Any(role => IsActivePosition(role.Position) && role.Star >= 3 &&
                          BaseTraits(role.RoleId).Contains(RangerTraitId));

    private IReadOnlyList<uint> GetRangerCandidates() =>
        Roles.Where(role => IsFrontPosition(role.Position) && !BaseTraits(role.RoleId).Contains(RangerTraitId))
            .OrderBy(role => role.Position)
            .Select(role => role.RoleId)
            .Distinct()
            .ToList();

    /// <summary>
    ///     Appointed role ids. With a 3★ Mortenax the client disables editing and the description speaks of several
    ///     rangers, so every front role is appointed then (not capture-verified).
    /// </summary>
    public IReadOnlyList<uint> ResolveRangerRoleIds()
    {
        if (!HasThreeStarMortenaxOnBoard()) return _rangerRoleIds;
        return Roles.Where(role => IsFrontPosition(role.Position) && !BaseTraits(role.RoleId).Contains(RangerTraitId))
            .Select(role => role.RoleId)
            .Distinct()
            .ToList();
    }

    /// <summary>AdditionalTraitRoleChoose: HandlePendingAction 1216 or TraitUpdate 1975 with a role config id.</summary>
    public uint AppointRanger(uint queuePosition, uint roleId, out bool success)
    {
        success = false;
        if (Phase != GridFightSessionPhase.Preparing ||
            !MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.RangerAppoint, queuePosition) ||
            !IsBaseTraitOnBoard(RangerTraitId) || BaseTraits(roleId).Contains(RangerTraitId) ||
            !Roles.Any(role => role.RoleId == roleId && IsFrontPosition(role.Position)))
            return 0;
        _rangerRoleIds.Clear();
        _rangerRoleIds.Add(roleId);
        success = true;
        return FinishSeasonTwoPending(GridFightSeasonTwoPendingKind.RangerAppoint);
    }

    // ---------------------------------------------------------------- Phainon (盗火行者)

    public bool PhainonCanConsume =>
        IsSeasonTwoRules && Phase == GridFightSessionPhase.Preparing &&
        Roles.Any(role => role.RoleId == PhainonRoleId && IsActivePosition(role.Position));

    public static uint GetPhainonKindlingGain(uint roleId, uint star)
    {
        var rarity = GameData.GridFightRoleBasicInfoData.GetValueOrDefault(roleId)?.Rarity ?? 0;
        var row = GridFightSeasonTwoTables.PhanionUnique.FirstOrDefault(entry => entry.Rarity == rarity && entry.Star == star) ??
                  GridFightSeasonTwoTables.PhanionUnique
                      .Where(entry => entry.Rarity == rarity && entry.Star <= star)
                      .OrderByDescending(entry => entry.Star)
                      .FirstOrDefault();
        if (row == null) return 0;
        if (!ChrysosHeirRoleIds.Contains(roleId)) return row.SavedValueAdd;
        // Phainon trait text: a converted Chrysos heir (黄金裔) gives 30% more (MazeBuff 130020201 #14 = TraitBasicInfo
        // 300202 BaseDescParamList[0] = 0.3).
        // The table's Special column is <= ceil(1.3x) everywhere except (4,3) 1000, (5,3) 6330 and (5,4) 33550
        // (1.39x/1.89x/2.05x, beta data slips); clamp those to the documented bonus. 3★ 5-cost: 3355 -> 4362.
        var withBonus = (uint)Math.Min(uint.MaxValue, ((ulong)row.SavedValueAdd * 13 + 9) / 10);
        return Math.Min(row.SpecialSavedValueAdd, withBonus);
    }

    /// <summary>
    ///     ConsumeRole (TraitUpdate 1278): the dragged role is absorbed into kindling (薪火), stored in Phainon's role
    ///     saved value GP_Avatar_S2_Phainon_00. Equipment of the absorbed role goes back to the bag.
    /// </summary>
    public GridFightSessionChange ConsumeRoleForPhainon(uint uniqueId, out uint kindlingGain)
    {
        kindlingGain = 0;
        if (!PhainonCanConsume || HasPendingSelection) return GridFightSessionChange.Failed;
        var role = Roles.FirstOrDefault(candidate => candidate.UniqueId == uniqueId);
        if (role == null || role.RoleId == PhainonRoleId || IsPopulationFreeRole(role)) return GridFightSessionChange.Failed;
        kindlingGain = GetPhainonKindlingGain(role.RoleId, role.Star);
        DestroyDiceFilledOnRole(role);
        Roles.Remove(role);
        _memberLinkUniqueIds.Remove(uniqueId);
        _phainonKindling = (uint)Math.Min(uint.MaxValue, (ulong)_phainonKindling + kindlingGain);
        _phainonConsumedCount++;
        RecalculateRoleSwitches();
        var promoted = PromoteOverflowItemsToBench();
        return new GridFightSessionChange(true, [], [uniqueId], promoted.Roles, promoted.ForgeItems);
    }

    private void ApplySeasonTwoRoleSavedValues(uint roleId, Dictionary<string, uint> values)
    {
        if (IsSeasonTwoRules && roleId == PhainonRoleId)
            values[PhainonSavedValueKey] = _phainonKindling;
    }

    // ---------------------------------------------------------------- Member Link (梦境交织) / Counter (反击)

    /// <summary>TraitMemberLink: TraitUpdate 1698 / HandlePendingAction 1765 with two member unique ids.</summary>
    public bool SetMemberLink(IReadOnlyList<uint> uniqueIds)
    {
        if (Phase != GridFightSessionPhase.Preparing || uniqueIds.Count != 2 || uniqueIds[0] == uniqueIds[1]) return false;
        var layers = ResolveTraitLayers();
        if (!layers.TryGetValue(MemberLinkTraitId, out var state) || state.Layer == 0) return false;
        if (!uniqueIds.All(uid => state.Members.Any(role => role.UniqueId == uid))) return false;
        _memberLinkUniqueIds.Clear();
        _memberLinkUniqueIds.AddRange(uniqueIds);
        return true;
    }

    private void PruneMemberLink()
    {
        if (_memberLinkUniqueIds.Count == 0) return;
        if (_memberLinkUniqueIds.All(uid => Roles.Any(role => role.UniqueId == uid))) return;
        _memberLinkUniqueIds.Clear();
    }

    /// <summary>Counter King (反击王): the front Counter member with the most equipment (ties: lower position).</summary>
    public GridFightRoleState? ResolveCounterKing()
    {
        if (!IsSeasonTwoRules) return null;
        var layers = ResolveTraitLayers();
        if (!layers.TryGetValue(CounterTraitId, out var state) || state.Layer == 0) return null;
        return state.Members
            .Where(role => IsFrontPosition(role.Position))
            .OrderByDescending(role => role.EquippedEquipmentUniqueIds.Count)
            .ThenBy(role => role.Position)
            .FirstOrDefault();
    }
}
