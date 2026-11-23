using March7thHoney.Data;
using March7thHoney.Data.Activity;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.MechCraft;

namespace March7thHoney.GameServer.Game.Activity.MechCraft;

public sealed record MechCraftEvaluationPiece(int ChipId, int X, int Y, MechCraftRotationType Rotation, bool IsLocked);

public sealed class MechCraftEvaluationResult
{
    public bool LayoutValid { get; internal set; }
    public bool RequirementsMet { get; internal set; }
    public string InvalidReason { get; internal set; } = string.Empty;
    public Dictionary<MechCraftAttributeType, int> Attributes { get; } = [];
    public Dictionary<MechCraftAttributeType, int> AttributeLevels { get; } = [];
    public List<MechCraftChipTraitType> Traits { get; } = [];
    public List<int> CompletedConditionIDs { get; } = [];
    public int IntegrationPercent { get; internal set; }
    public int BoardWidth { get; internal set; }
    public int BoardHeight { get; internal set; }
}

public static partial class MechCraftEvaluator
{
    private sealed record PlacedChip(MechCraftEvaluationPiece Piece, MechCraftChipExcel Chip,
        HashSet<(int X, int Y)> Cells, List<MechCraftEffectExcel> Effects);

    public static MechCraftEvaluationResult EvaluateOrder(int orderId, IReadOnlyList<MechCraftEvaluationPiece> pieces,
        IReadOnlyCollection<int> activeTalentPointIds)
    {
        return GameData.MechCraftOrderData.TryGetValue(orderId, out var order)
            ? Evaluate(order.RequirementID, pieces, activeTalentPointIds, null)
            : Invalid("Unknown order.");
    }

    public static MechCraftEvaluationResult EvaluateExam(int examId, IReadOnlyList<MechCraftEvaluationPiece> pieces,
        IReadOnlyCollection<int> activeTalentPointIds)
    {
        return GameData.MechCraftExamData.TryGetValue(examId, out var exam)
            ? Evaluate(exam.RequirementID, pieces, activeTalentPointIds, exam)
            : Invalid("Unknown exam.");
    }

    private static MechCraftEvaluationResult Evaluate(int requirementId, IReadOnlyList<MechCraftEvaluationPiece> pieces,
        IReadOnlyCollection<int> activeTalentPointIds, MechCraftExamExcel? exam)
    {
        if (!GameData.MechCraftRequirementData.TryGetValue(requirementId, out var requirement))
            return Invalid("Missing requirement resource.");
        var talentEffects = activeTalentPointIds.Order().SelectMany(id =>
            GameData.MechCraftTalentPointData.TryGetValue(id, out var point) ? point.EffectIDList : [])
            .Select(id => GameData.MechCraftEffectData.GetValueOrDefault(id)).OfType<MechCraftEffectExcel>().ToList();
        var expansion = talentEffects.LastOrDefault(effect => effect.Type == MechCraftEffectType.ExpandBoard);
        var width = exam?.BoardWidth is > 0 ? exam.BoardWidth : expansion?.ParamList.ElementAtOrDefault(0)
            ?? MechCraftResourceCatalog.IntConstant("BoardInitialWidth");
        var height = exam?.BoardHeight is > 0 ? exam.BoardHeight : expansion?.ParamList.ElementAtOrDefault(1)
            ?? MechCraftResourceCatalog.IntConstant("BoardInitialHeight");
        if (width <= 0 || height <= 0) return Invalid("Invalid board dimensions.");
        var result = new MechCraftEvaluationResult { BoardWidth = width, BoardHeight = height };
        var maxOverlap = talentEffects.LastOrDefault(effect => effect.Type == MechCraftEffectType.CellOverlapCount)
            ?.ParamList.FirstOrDefault() ?? 0;
        var baseValue = talentEffects.LastOrDefault(effect => effect.Type == MechCraftEffectType.BaseAttributeValue)
            ?.ParamList.FirstOrDefault() ?? 0;
        var placed = new List<PlacedChip>();
        foreach (var piece in pieces)
        {
            if (!GameData.MechCraftChipData.TryGetValue(piece.ChipId, out var chip)) return Invalid("Unknown chip.");
            if ((int)piece.Rotation is < 0 or > 3) return Invalid("Invalid chip rotation.");
            var cells = chip.Cells.Select(cell => Transform(cell.X, cell.Y, piece)).ToHashSet();
            if (cells.Count == 0 || cells.Any(cell => cell.X < 1 || cell.X > width || cell.Y < 1 || cell.Y > height))
                return Invalid("Chip is outside the board.");
            var effects = new List<MechCraftEffectExcel>();
            foreach (var id in chip.EffectIDList)
            {
                if (!GameData.MechCraftEffectData.TryGetValue(id, out var effect)) return Invalid("Missing chip effect.");
                effects.Add(effect);
            }
            placed.Add(new PlacedChip(piece, chip, cells, effects));
        }

        if (placed.Count(chip => chip.Effects.Count > 0) > MechCraftResourceCatalog.IntConstant("BuffChipMaxUseCount"))
            return Invalid("Too many buff chips.");
        if (!ValidateLockedPieces(exam, pieces)) return Invalid("Exam starting pieces were changed.");
        var coverage = placed.SelectMany(chip => chip.Cells).GroupBy(cell => cell)
            .ToDictionary(group => group.Key, group => group.Count());
        if (coverage.Values.Any(count => count > 2)) return Invalid("A cell contains more than two chips.");
        var ordinaryCoverage = placed.Where(chip => !chip.Effects.Any(effect => effect.Type == MechCraftEffectType.BuffChipIgnoreOverlapLimit))
            .SelectMany(chip => chip.Cells).GroupBy(cell => cell);
        if (ordinaryCoverage.Count(group => group.Count() >= 2) > maxOverlap) return Invalid("Too many overlapping cells.");
        var banned = GetBannedCells(requirement, width, height);
        if (coverage.Keys.Any(banned.Contains)) return Invalid("Chip covers a blocked cell.");
        var denominator = MechCraftResourceCatalog.IntConstant("Integration_BaseCellCount");
        var step = MechCraftResourceCatalog.IntConstant("Integration_PercentPerLevel");
        if (denominator <= 0 || step <= 0) return Invalid("Missing integration constants.");
        result.IntegrationPercent = step * (int)MathF.Floor(coverage.Count * 100f / denominator / step);
        foreach (var type in Enum.GetValues<MechCraftAttributeType>().Where(type => type != MechCraftAttributeType.None))
            result.Attributes[type] = baseValue;
        foreach (var target in placed)
        {
            var percent = 0;
            foreach (var source in placed.Where(source => source != target))
                foreach (var effect in source.Effects)
                {
                    if (effect.Type == MechCraftEffectType.BuffChipOverlappedChipAttributePercentIncrease && source.Cells.Overlaps(target.Cells))
                        percent += effect.ParamList[0];
                    else if (effect.Type == MechCraftEffectType.BuffChipDirectionalAttributePercentIncrease &&
                             DirectionalCells(source.Cells, effect.ParamList[1], width, height).Overlaps(target.Cells))
                        percent += effect.ParamList[0];
                    else if (effect.Type == MechCraftEffectType.BuffChipAdjacentAttributePercentIncrease &&
                             AdjacentCells(source.Cells).Overlaps(target.Cells))
                        percent += effect.ParamList[0];
                }
            foreach (var (type, value) in target.Chip.Attributes)
                if (type != MechCraftAttributeType.None)
                    result.Attributes[type] += (int)MathF.Ceiling(value * (1 + percent / 100f));
            foreach (var effect in target.Effects.Where(effect => effect.Type == MechCraftEffectType.BuffChipFullLineBaseAttributeValueIncrease))
            {
                if (effect.ParamList.Count == 0) return Invalid("Missing full-line effect values.");
                var rows = target.Cells.Select(cell => cell.Y).Distinct().Count(y =>
                    Enumerable.Range(1, width).All(x => banned.Contains((x, y)) || coverage.ContainsKey((x, y))));
                var columns = target.Cells.Select(cell => cell.X).Distinct().Count(x =>
                    Enumerable.Range(1, height).All(y => banned.Contains((x, y)) || coverage.ContainsKey((x, y))));
                var value = effect.ParamList[Math.Min(rows + columns, effect.ParamList.Count - 1)];
                foreach (var type in result.Attributes.Keys.ToList()) result.Attributes[type] += value;
            }
        }

        foreach (var (type, value) in result.Attributes)
            result.AttributeLevels[type] = MechCraftResourceCatalog.AttributeLevelThresholds
                .Where(threshold => value >= threshold.Value).Select(threshold => threshold.Key).DefaultIfEmpty(0).Max();
        var traitCounts = placed.Where(chip => chip.Chip.TraitType != MechCraftChipTraitType.None)
            .GroupBy(chip => chip.Chip.TraitType).ToDictionary(group => group.Key, group => group.Count());
        var traitMinimum = MechCraftResourceCatalog.IntConstant("ProductTraitMinChipCount");
        result.Traits.AddRange(requirement.TraitTypes.Distinct().Where(type => traitCounts.GetValueOrDefault(type) >= traitMinimum));
        foreach (var conditionId in requirement.ConditionIDs)
            if (EvaluateCondition(conditionId, placed, coverage.Keys.ToHashSet(), result, traitCounts, []))
                result.CompletedConditionIDs.Add(conditionId);
        result.LayoutValid = true;
        result.RequirementsMet = requirement.AttributeLevels.All(attribute => result.AttributeLevels.GetValueOrDefault(attribute.Key) >= attribute.Value)
            && requirement.TraitTypes.All(result.Traits.Contains)
            && (!requirement.IsConditionRequired || requirement.ConditionIDs.All(result.CompletedConditionIDs.Contains));
        if (!result.RequirementsMet) result.InvalidReason = "Crafting requirements are not met.";
        return result;
    }

    private static bool ValidateLockedPieces(MechCraftExamExcel? exam, IReadOnlyList<MechCraftEvaluationPiece> pieces)
    {
        var expected = new List<MechCraftEvaluationPiece>();
        if (exam?.EndgamePlaceChipSchemeID is > 0)
        {
            if (!GameData.MechCraftPlaceChipSchemeData.TryGetValue(exam.EndgamePlaceChipSchemeID, out var scheme)) return false;
            expected.AddRange(scheme.Select(row => new MechCraftEvaluationPiece(row.ChipID, row.Position.X, row.Position.Y,
                (MechCraftRotationType)((-row.RotationDegrees / 90) & 3), true)));
        }
        var actual = pieces.Where(piece => piece.IsLocked).ToList();
        foreach (var piece in expected)
            if (!actual.Remove(piece)) return false;
        return actual.Count == 0;
    }

    private static (int X, int Y) Transform(int x, int y, MechCraftEvaluationPiece piece)
    {
        x--;
        y--;
        var rotated = piece.Rotation switch
        {
            MechCraftRotationType.Ccw0Degrees => (x, y),
            MechCraftRotationType.Ccw90Degrees => (y, -x),
            MechCraftRotationType.Ccw180Degrees => (-x, -y),
            MechCraftRotationType.Ccw270Degrees => (-y, x),
            _ => throw new ArgumentOutOfRangeException(nameof(piece))
        };
        return (piece.X + rotated.Item1, piece.Y + rotated.Item2);
    }

    private static HashSet<(int X, int Y)> DirectionalCells(HashSet<(int X, int Y)> cells, int direction, int width, int height)
    {
        var affected = new HashSet<(int X, int Y)>();
        var horizontal = direction is (int)MechCraftDirectionType.Left or (int)MechCraftDirectionType.Right;
        var positive = direction is (int)MechCraftDirectionType.Right or (int)MechCraftDirectionType.Below;
        foreach (var line in cells.GroupBy(cell => horizontal ? cell.Y : cell.X))
        {
            var values = line.Select(cell => horizontal ? cell.X : cell.Y);
            var boundary = positive ? values.Max() : values.Min();
            for (var value = boundary + (positive ? 1 : -1); value >= 1 && value <= (horizontal ? width : height); value += positive ? 1 : -1)
                affected.Add(horizontal ? (value, line.Key) : (line.Key, value));
        }
        return affected;
    }

    private static HashSet<(int X, int Y)> AdjacentCells(HashSet<(int X, int Y)> cells)
    {
        var affected = cells.SelectMany(cell => new[] { (cell.X + 1, cell.Y), (cell.X - 1, cell.Y),
            (cell.X, cell.Y + 1), (cell.X, cell.Y - 1) }).ToHashSet();
        affected.ExceptWith(cells);
        return affected;
    }

    private static HashSet<(int X, int Y)> GetBannedCells(MechCraftRequirementExcel requirement, int width, int height)
    {
        var scheme = requirement.BanCellSchemeIDs.Select(id => GameData.MechCraftBanCellSchemeData.GetValueOrDefault(id))
            .FirstOrDefault(candidate => candidate != null && (candidate.Width == 0 || candidate.Width == width)
                && (candidate.Height == 0 || candidate.Height == height));
        if (scheme == null) return [];
        return scheme.Cells.Where(cell => cell.X >= 1 && cell.X <= width && cell.Y >= 1 && cell.Y <= height)
            .Select(cell => scheme.AxesDirection switch
            {
                MechCraftBanCellAxesDirectionType.None or MechCraftBanCellAxesDirectionType.TopLeftToBottomRight => (cell.X, cell.Y),
                MechCraftBanCellAxesDirectionType.TopRightToBottomLeft => (width + 1 - cell.X, cell.Y),
                MechCraftBanCellAxesDirectionType.BottomLeftToTopRight => (cell.X, height + 1 - cell.Y),
                MechCraftBanCellAxesDirectionType.BottomRightToTopLeft => (width + 1 - cell.X, height + 1 - cell.Y),
                _ => throw new InvalidDataException("Invalid MechCraft blocked-cell orientation.")
            }).ToHashSet();
    }

    private static bool EvaluateCondition(int conditionId, List<PlacedChip> placed, HashSet<(int X, int Y)> coverage,
        MechCraftEvaluationResult result, Dictionary<MechCraftChipTraitType, int> traitCounts, HashSet<int> visited)
    {
        if (!visited.Add(conditionId) || !GameData.MechCraftConditionData.TryGetValue(conditionId, out var condition)) return false;
        var args = condition.ParamList;
        var first = args.FirstOrDefault();
        var second = args.ElementAtOrDefault(1);
        var types = placed.Select(chip => chip.Chip.ID).Distinct().Count();
        var value = condition.Type switch
        {
            MechCraftConditionType.None => true,
            MechCraftConditionType.And => args.All(id => EvaluateCondition(id, placed, coverage, result, traitCounts, visited)),
            MechCraftConditionType.AllAttributeLevelReach => result.AttributeLevels.Values.All(level => level >= first),
            MechCraftConditionType.ChipCountGE => placed.Count >= first,
            MechCraftConditionType.ChipCountLE => placed.Count <= first,
            MechCraftConditionType.ChipCount => placed.Count == first,
            MechCraftConditionType.ChipTypeCountGE => types >= first,
            MechCraftConditionType.ChipTypeCountLE => types <= first,
            MechCraftConditionType.ChipTypeCount => types == first,
            MechCraftConditionType.ChipTraitCountGE => traitCounts.GetValueOrDefault((MechCraftChipTraitType)first) >= second,
            MechCraftConditionType.ChipTraitCountLE => traitCounts.GetValueOrDefault((MechCraftChipTraitType)first) <= second,
            MechCraftConditionType.IntegrationPercentGE => result.IntegrationPercent >= first,
            MechCraftConditionType.TargetChipCount => placed.Count(chip => chip.Chip.ID == first) == second,
            MechCraftConditionType.TargetChipUsed => placed.Any(chip => chip.Chip.ID == first),
            MechCraftConditionType.TargetChipsOnly => placed.All(chip => args.Contains(chip.Chip.ID)),
            MechCraftConditionType.CoverageSymmetrical => first switch
            {
                (int)MechCraftSymmetryType.Horizontal => coverage.All(cell => coverage.Contains((result.BoardWidth + 1 - cell.X, cell.Y))),
                (int)MechCraftSymmetryType.Vertical => coverage.All(cell => coverage.Contains((cell.X, result.BoardHeight + 1 - cell.Y))),
                _ => false
            },
            _ => false
        };
        visited.Remove(conditionId);
        return value;
    }

    private static MechCraftEvaluationResult Invalid(string reason) => new() { InvalidReason = reason };
}
