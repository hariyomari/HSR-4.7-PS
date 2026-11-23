using March7thHoney.Enums.GridFight;

namespace March7thHoney.GameServer.Game.GridFight;

/// <summary>
/// Foresight (远见, 303601), as its description reads: one random gold augment and 15 gold; every later investment
/// node then offers a single random prismatic augment (no reroll) and does not raise enemy difficulty. The 15 gold
/// comes from the selection grant table; this file owns the bonus augment, the single prismatic offer and the
/// difficulty exemption.
/// </summary>
public partial class GridFightSession
{
    private readonly List<uint> _foresightBonusAugments = [];
    private readonly HashSet<uint> _difficultyExemptInvestmentIds = [];

    public bool IsForesightActive => ActiveInvestmentIds.Contains(ForesightAugmentId);

    private void GrantForesightBonusAugment()
    {
        var bonus = _catalog.RollInvestmentOffer(ChapterId, GridFightAugmentQualityEnum.Gold, 1, ActiveInvestmentIds)
            .SingleOrDefault();
        if (bonus == 0) return;
        ActiveInvestmentIds.Add(bonus);
        _foresightBonusAugments.Add(bonus);
        PrepareInvestmentSelectionReward(bonus);
        OnRewardInvestmentSelected(bonus);
        ApplyAugmentSelectionEffects(bonus);
    }

    public IReadOnlyList<uint> TakeForesightBonusAugments() => Drain(_foresightBonusAugments);

    /// <summary>Returns the one-prismatic offer while Foresight is active, or null to use the ordinary offer.</summary>
    private IReadOnlyList<uint>? RollForesightInvestmentOffer()
    {
        if (!IsForesightActive) return null;
        foreach (var quality in new[] { GridFightAugmentQualityEnum.Prismatic, GridFightAugmentQualityEnum.Gold })
        {
            var offer = _catalog.RollInvestmentOffer(ChapterId, quality, 1, ActiveInvestmentIds);
            if (offer.Count > 0) return offer;
        }
        return [];
    }

    private void OnForesightInvestmentSelected(uint augmentId)
    {
        if (augmentId != ForesightAugmentId && IsForesightActive)
            _difficultyExemptInvestmentIds.Add(augmentId);
    }
}
