using March7thHoney.Data;
using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

public sealed record GridFightBonfireNodeState(uint ChapterId, uint SectionId, uint EventId, uint PetPalEffectId);

/// <summary>One offered bonfire option (MHCOIBONODA): show type, role parameter, (event, option) key.</summary>
public sealed record GridFightBonfireOption(uint ShowType, uint RoleId, uint EventId, uint OptionId);

/// <summary>
///     Bonfire (幕后茶歇) reward node used by many partner (本局奇遇) effects ("第X位面的最后的奖励节点将替换为幕后茶歇").
///     Option effects live in server JSON that is not
///     shipped; options whose table params name a reward (HP-cost coffees, items, projectors, copies) are applied,
///     rule-changing options are accepted without effect.
/// </summary>
public sealed partial class GridFightSession
{
    private const uint BonfireOptionShowCopyMaxStar = 2;
    private const uint BonfireOptionShowCopyMaxRarity = 3;
    private const uint BonfireOptionShowCopyMaxEquip = 4;
    private const uint BonfireOptionShowHpCondition = 5;
    private const uint BonfireOptionShowPreGenBonus = 6;
    private const uint BonfireHealNum = 20; // GridFightBonfireHeal: 20 for chapters 1-3
    private const uint BonfireCommissionOrbId = 203;

    private static readonly Dictionary<uint, uint> BonfireEffectPlane = new()
    {
        [102004] = 1, [102005] = 1, [102006] = 1, [103005] = 1, [103006] = 1, [104001] = 1, [104003] = 1,
        [106003] = 1, [106008] = 1, [107001] = 1, [108004] = 1, [111001] = 1, [111006] = 1, [112002] = 1,
        [111003] = 1,
        [103007] = 2, [103008] = 2, [104002] = 2, [104004] = 2, [106004] = 2, [106009] = 2, [108005] = 2,
        [111007] = 2, [112003] = 2,
        [112004] = 3,
    };

    private readonly List<GridFightBonfireNodeState> _bonfireNodes = [];
    private readonly HashSet<(uint ChapterId, uint SectionId)> _bonfireResolved = [];
    private List<GridFightBonfireOption> _bonfireOptions = [];
    private bool _bonfireSelected;

    public IReadOnlyList<GridFightBonfireNodeState> BonfireNodes => _bonfireNodes;
    public IReadOnlyList<GridFightBonfireOption> BonfireOptions => _bonfireOptions;
    public bool BonfireSelected => _bonfireSelected;

    public GridFightBonfireNodeState? FindBonfireNode(uint chapterId, uint sectionId) =>
        _bonfireNodes.FirstOrDefault(node => node.ChapterId == chapterId && node.SectionId == sectionId);

    public GridFightBonfireNodeState? CurrentBonfireNode =>
        _bonfireResolved.Contains((ChapterId, SectionId)) ? null : FindBonfireNode(ChapterId, SectionId);

    /// <summary>Places the partner's bonfire events on the last reward (Monster) node of their plane.</summary>
    private void InitializeBonfireNodes()
    {
        if (!IsSeasonTwoRules || PetPalEffectId == 0) return;
        var effect = GridFightSeasonTwoTables.PetPalEffects.FirstOrDefault(row => row.Id == PetPalEffectId);
        if (effect == null || effect.Params.Length == 0) return;
        var events = effect.Params.Select(value => (uint)value).ToList();
        var plans = new List<(uint Chapter, uint EventId)>();
        if (PetPalEffectId == 112001)
            plans.AddRange(events.Take(3).Select((eventId, index) => ((uint)index + 1, eventId)));
        else if (BonfireEffectPlane.TryGetValue(PetPalEffectId, out var plane))
            plans.Add((plane, PetPalEffectId == 107001 && events.Count > 1
                // 107001 (菇菇兔) disguises itself as another partner: a random event from the rest of its param list.
                ? events[1 + Random.Shared.Next(events.Count - 1)]
                : events[0]));
        foreach (var (chapter, eventId) in plans)
        {
            if (!GridFightSeasonTwoTables.BonfireOptions.Any(row => row.EventId == eventId)) continue;
            var node = _catalog.StandardRoute
                .Where(entry => entry.ChapterId == chapter && entry.NodeType == GridFightNodeTypeEnum.Monster)
                .OrderBy(entry => entry.SectionId)
                .LastOrDefault();
            if (node != null) _bonfireNodes.Add(new GridFightBonfireNodeState(chapter, node.SectionId, eventId, PetPalEffectId));
        }
    }

    private bool TryStartBonfirePending(GridFightSeasonTwoChanges changes)
    {
        if (CurrentBonfireNode is not { } node) return false;
        _bonfireOptions = BuildBonfireOptions(node.EventId);
        _bonfireSelected = false;
        StartSeasonTwoPending(GridFightSeasonTwoPendingKind.Bonfire, 0, node.PetPalEffectId, [], changes);
        return true;
    }

    private static IReadOnlyList<GridFightBonfireOptionRow> BonfireRows(uint eventId) =>
        GridFightSeasonTwoTables.BonfireOptions.Where(row => row.EventId == eventId).ToList();

    private List<GridFightBonfireOption> BuildBonfireOptions(uint eventId)
    {
        var own = BonfireRows(eventId);
        // Events whose own rows carry no params draw their options from the following sub-event pools
        // (10400101 -> 10400102 + 10400103, 10800401 -> two of 10800402).
        if (own.Count > 0 && own.All(row => row.Params.Length == 0 && row.ShowType == "None") &&
            BonfireRows(eventId + 1).Count > 0)
        {
            var first = BonfireRows(eventId + 1).ToList();
            var second = BonfireRows(eventId + 2).ToList();
            var picks = new List<GridFightBonfireOptionRow>();
            if (second.Count > 0)
            {
                picks.Add(first[Random.Shared.Next(first.Count)]);
                picks.Add(second[Random.Shared.Next(second.Count)]);
            }
            else
            {
                while (picks.Count < 2 && first.Count > 0)
                {
                    var row = first[Random.Shared.Next(first.Count)];
                    first.Remove(row);
                    picks.Add(row);
                }
            }
            return picks.Select(row => new GridFightBonfireOption(ShowTypeOf(row), 0, row.EventId, row.OptionId)).ToList();
        }
        return own.Select(row => new GridFightBonfireOption(ShowTypeOf(row), ResolveCopyRole(ShowTypeOf(row)),
            row.EventId, row.OptionId)).ToList();
    }

    private static uint ShowTypeOf(GridFightBonfireOptionRow row) => row.ShowType switch
    {
        "CopyRoleMaxStar" => BonfireOptionShowCopyMaxStar,
        "CopyRoleMaxRarity" => BonfireOptionShowCopyMaxRarity,
        "CopyRoleMaxEquipCnt" => BonfireOptionShowCopyMaxEquip,
        "HpCondition" => BonfireOptionShowHpCondition,
        "PreGenBonus" => BonfireOptionShowPreGenBonus,
        _ => 0,
    };

    private GridFightRoleState? FindCopySource(uint showType)
    {
        var owned = Roles.Where(role => !IsPopulationFreeRole(role)).ToList();
        return showType switch
        {
            BonfireOptionShowCopyMaxStar => owned.OrderByDescending(role => role.Star).ThenBy(role => role.Position).FirstOrDefault(),
            BonfireOptionShowCopyMaxRarity => owned
                .OrderByDescending(role => GameData.GridFightRoleBasicInfoData.GetValueOrDefault(role.RoleId)?.Rarity ?? 0)
                .ThenByDescending(role => role.Star).FirstOrDefault(),
            BonfireOptionShowCopyMaxEquip => owned.OrderByDescending(role => role.EquippedEquipmentUniqueIds.Count)
                .ThenByDescending(role => role.Star).FirstOrDefault(),
            _ => null,
        };
    }

    private uint ResolveCopyRole(uint showType) => FindCopySource(showType)?.RoleId ?? 0;

    /// <summary>SelectOption (oneof 1659 / 9 / 8 { event_id, option id }).</summary>
    public GridFightRewardApplication SelectBonfireOption(uint queuePosition, uint eventId, uint optionId)
    {
        var failed = new GridFightRewardApplication();
        if (!MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.Bonfire, queuePosition) || queuePosition == 0 ||
            _bonfireSelected)
            return failed;
        var option = _bonfireOptions.FirstOrDefault(entry => entry.EventId == eventId && entry.OptionId == optionId);
        var row = GridFightSeasonTwoTables.BonfireOptions.FirstOrDefault(entry =>
            entry.EventId == eventId && entry.OptionId == optionId);
        if (option == null || row == null) return failed;

        var snapshot = CaptureSeasonTwoRewardState();
        var application = ApplyBonfireOption(option, row);
        if (!application.Success)
        {
            RestoreSeasonTwoRewardState(snapshot);
            return failed;
        }
        _bonfireSelected = true;
        return application;
    }

    private GridFightRewardApplication ApplyBonfireOption(GridFightBonfireOption option, GridFightBonfireOptionRow row)
    {
        var p = row.Params.Select(value => (uint)value).ToArray();
        IReadOnlyList<GridFightRewardEntry> Bonus(uint id, uint count)
        {
            var result = new List<GridFightRewardEntry>();
            for (var i = 0u; i < Math.Max(count, 1u); i++)
            {
                if (GridFightSpecialFeatures.TryResolveBonus(id, out var rewards)) result.AddRange(rewards);
                else if (GameData.GridFightEquipmentData.ContainsKey(id) || GameData.GridFightConsumablesData.ContainsKey(id))
                    result.Add(new GridFightRewardEntry(GridFightBonusTypeEnum.Item, id, 1));
            }
            return result;
        }

        switch (option.ShowType)
        {
            case BonfireOptionShowCopyMaxStar or BonfireOptionShowCopyMaxRarity or BonfireOptionShowCopyMaxEquip:
            {
                // Copy options (临摹同伴): a copy of the matching role at its star level.
                var source = FindCopySource(option.ShowType);
                return source == null
                    ? new GridFightRewardApplication { Success = true }
                    : ApplySeasonTwoRewards([new GridFightRewardEntry(GridFightBonusTypeEnum.SpecificAvatar, source.RoleId, 1, source.Star)]);
            }
            case BonfireOptionShowHpCondition:
            {
                // [bonus, count, hp cost]; the coffee without params (无名氏咖啡) is one of the others at random.
                if (p.Length < 3)
                {
                    var others = BonfireRows(row.EventId).Where(entry => entry.Params.Length >= 3).ToList();
                    if (others.Count == 0) return new GridFightRewardApplication { Success = true };
                    p = others[Random.Shared.Next(others.Count)].Params.Select(value => (uint)value).ToArray();
                }
                var gains = Bonus(p[0], p[1]);
                var application = ApplySeasonTwoRewards(gains);
                if (!application.Success) return application;
                var previous = LineupHp;
                LineupHp = previous > p[2] ? previous - p[2] : 1;
                application.LineupHpChanged = true;
                return application;
            }
            case BonfireOptionShowPreGenBonus:
                // Commission option (不死狼委托): the commission reward is handed out right away (the pray-quest tracking
                // is not modelled).
                return TryAddOrb(BonfireCommissionOrbId, out var orb)
                    ? new GridFightRewardApplication { Success = true, AddedOrbs = { orb } }
                    : new GridFightRewardApplication { Success = true };
        }

        // Single-option rows: [heal] (猫爪咖啡 99), [bonus/item, count] (boots, projectors, coffees).
        return p.Length switch
        {
            1 when row.EventId is 10400301 or 10400401 => ApplySeasonTwoRewards(
                [new GridFightRewardEntry(GridFightBonusTypeEnum.GlobalHp, 0, p[0])]),
            2 => ApplySeasonTwoRewards(Bonus(p[0], p[1])),
            _ => new GridFightRewardApplication { Success = true },
        };
    }

    /// <summary>HealHp (oneof 1659 / 9 / 3): the rest card heals GridFightBonfireHeal[chapter].</summary>
    public bool HealAtBonfire(uint queuePosition)
    {
        if (queuePosition == 0 || !MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.Bonfire, queuePosition) ||
            _bonfireSelected)
            return false;
        LineupHp = (uint)Math.Min(MaxLineupHp, (ulong)LineupHp + BonfireHealNum);
        _bonfireSelected = true;
        return true;
    }

    /// <summary>TakeDrop (oneof 1659 / 10): pays the node's basic gold, leaves the bonfire and moves on.</summary>
    public uint LeaveBonfire(uint queuePosition, out uint basicGold)
    {
        basicGold = 0;
        if (queuePosition == 0 || !MatchesSeasonTwoPending(GridFightSeasonTwoPendingKind.Bonfire, queuePosition))
            return 0;
        basicGold = CurrentNode.BasicGoldReward;
        AddGold(basicGold);
        _bonfireResolved.Add((ChapterId, SectionId));
        _bonfireOptions = [];
        var finished = FinishSeasonTwoPending(GridFightSeasonTwoPendingKind.Bonfire);
        if (AdvanceToNextNode())
        {
            Phase = GridFightSessionPhase.AwaitingBattleRound;
        }
        return finished;
    }
}
