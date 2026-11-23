using March7thHoney.Data;
using March7thHoney.Data.Custom;
using March7thHoney.Database.ChessRogue;
using March7thHoney.Enums.Rogue;
using March7thHoney.GameServer.Game.Battle;
using March7thHoney.GameServer.Game.RogueCommon;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.ChessRogue;

/// <summary>
///     Dice-surface modifier effects, ported from the legacy handlers. Effect types the legacy code never implemented
///     confirm immediately without doing anything, as the legacy dispatcher did.
/// </summary>
public partial class ChessRogueManager
{
    private static readonly HashSet<ModifierEffectTypeEnum> SelectableCellEffects =
    [
        ModifierEffectTypeEnum.ChangeSelectCellType,
        ModifierEffectTypeEnum.ReRandomCellTypeGetMoney,
        ModifierEffectTypeEnum.ReplicateCurCellToSelectCell,
        ModifierEffectTypeEnum.ReplicateSelectCellToAround,
        ModifierEffectTypeEnum.ReplicateSelectCellToRandom,
        ModifierEffectTypeEnum.SetBlockTypeToAround,
        ModifierEffectTypeEnum.SetCellTypeAndTakeReward,
        ModifierEffectTypeEnum.SwapCellToCurAround
    ];

    private static readonly HashSet<ModifierEffectTypeEnum> ImplementedEffects =
    [
        .. SelectableCellEffects,
        ModifierEffectTypeEnum.AddMazeBuff,
        ModifierEffectTypeEnum.ReplicateCurCellToRandom,
        ModifierEffectTypeEnum.SetColCanMove,
        ModifierEffectTypeEnum.TrunAroundToEmptyGetBuff,
        ModifierEffectTypeEnum.TurnRandomCellBlockType
    ];

    private static string Param(ChessRogueDiceSurfaceContentEffect effect, string key, string fallback)
    {
        return effect.Params.GetValueOrDefault(key, fallback);
    }

    private static int IntParam(ChessRogueDiceSurfaceContentEffect effect, string key, int fallback)
    {
        return int.TryParse(Param(effect, key, ""), out var value) ? value : fallback;
    }

    private static List<int> BlockTypes(string value)
    {
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var type) ? type : 0)
            .Where(type => type > 0)
            .ToList();
    }

    // The board collapses from the left: a cell at or behind the current column can no longer change.
    private static bool IsCollapsed(ChessRogueRunData run, ChessRogueCellData cell)
    {
        return run.CurCell() is not { } cur || cur.PosX >= cell.PosX;
    }

    private static List<ChessRogueCellData> OpenCells(ChessRogueRunData run, List<int> types)
    {
        return run.Cells.Values.Where(cell => types.Contains(cell.BlockType) && !IsCollapsed(run, cell)).ToList();
    }

    private static bool IsAround(ChessRogueCellData a, ChessRogueCellData b)
    {
        return Math.Abs(a.PosX - b.PosX) <= 1 && Math.Abs(a.PosY - b.PosY) <= 1;
    }

    private async ValueTask ModifierOnConfirmed(ChessRogueRunData run, ChessRogueModifierData modifier,
        ChessRogueDiceSurfaceContentEffect effect)
    {
        if (SelectableCellEffects.Contains(effect.EffectType))
        {
            modifier.SelectableCells.AddRange(OpenCells(run, BlockTypes(Param(effect, "SourceType", "3")))
                .Select(cell => cell.CellId));
            return;
        }

        switch (effect.EffectType)
        {
            case ModifierEffectTypeEnum.ReplicateCurCellToRandom:
            {
                await SendModifierStageStart();
                var targets = OpenCells(run, BlockTypes(Param(effect, "SourceType", "3")));
                var curType = run.CurCell()?.BlockType ?? (int)RogueDLCBlockTypeEnum.Empty;
                List<ChessRogueCellData> updated = [];
                for (var i = 0; i < IntParam(effect, "Count", 1) && targets.Count > 0; i++)
                {
                    var target = targets.RandomElement();
                    target.BlockType = curType;
                    targets.Remove(target);
                    updated.Add(target);
                }

                await SendCellUpdate(run, updated, RogueModifierSourceType.RogueModifierSourceDiceRoll, true);
                modifier.IsConfirmed = true;
                break;
            }
            case ModifierEffectTypeEnum.SetColCanMove:
            {
                var curCol = run.CurCell()?.PosX ?? 0;
                foreach (var col in Param(effect, "Col", "0").Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    // "~1" is relative to the current column.
                    var relative = col.StartsWith('~');
                    if (!int.TryParse(relative ? col[1..] : col, out var value)) continue;
                    var target = relative ? curCol + value : value;
                    run.CanMoveCellIds.AddRange(run.Cells.Values
                        .Where(cell => cell.PosX == target && !IsCollapsed(run, cell))
                        .Select(cell => cell.CellId));
                }

                break;
            }
            case ModifierEffectTypeEnum.TurnRandomCellBlockType:
            {
                await SendModifierStageStart();
                var targets = OpenCells(run, BlockTypes(Param(effect, "SourceType", "3")));
                var targetTypes = BlockTypes(Param(effect, "TargetType", "3"));
                List<ChessRogueCellData> updated = [];
                for (var i = 0; i < IntParam(effect, "Count", 1) && targets.Count > 0 && targetTypes.Count > 0; i++)
                {
                    var target = targets.RandomElement();
                    target.BlockType = targetTypes.RandomElement();
                    targets.Remove(target);
                    updated.Add(target);
                }

                await SendCellUpdate(run, updated, RogueModifierSourceType.RogueModifierSourceDiceRoll, true);
                modifier.IsConfirmed = true;
                break;
            }
            case ModifierEffectTypeEnum.AddMazeBuff:
                modifier.IsConfirmed = true;
                break;
            case ModifierEffectTypeEnum.TrunAroundToEmptyGetBuff:
                break;
            default:
                modifier.IsConfirmed = true;
                break;
        }
    }

    private async ValueTask ModifierSelectModifierCell(ChessRogueRunData run, ChessRogueModifierData modifier,
        int cellId)
    {
        var effect = ChessRogueResources.SurfaceEffect(modifier.SurfaceId);
        if (effect == null || !ImplementedEffects.Contains(effect.EffectType))
        {
            modifier.IsConfirmed = true;
            return;
        }

        if (!SelectableCellEffects.Contains(effect.EffectType) || !run.Cells.TryGetValue(cellId, out var target))
            return;

        await SendModifierStageStart();
        modifier.SelectedCell = cellId;
        modifier.IsConfirmed = true;

        List<ChessRogueCellData> updated = [];
        var sourceTypes = BlockTypes(Param(effect, "SourceType", "3"));
        var targetTypes = BlockTypes(Param(effect, "TargetType", "3"));
        switch (effect.EffectType)
        {
            case ModifierEffectTypeEnum.ChangeSelectCellType:
                if (targetTypes.Count > 0) target.BlockType = targetTypes.RandomElement();
                updated.Add(target);
                break;
            case ModifierEffectTypeEnum.ReRandomCellTypeGetMoney:
            {
                var sameType = target.BlockType;
                var refresh = run.Cells.Values.Where(cell => cell.BlockType == sameType && !IsCollapsed(run, cell))
                    .ToList();
                if (targetTypes.Count > 0)
                    foreach (var cell in refresh)
                        cell.BlockType = targetTypes.RandomElement();
                await GainMoney(run, IntParam(effect, "Count", 0));
                updated.AddRange(refresh);
                break;
            }
            case ModifierEffectTypeEnum.ReplicateCurCellToSelectCell:
                target.BlockType = run.CurCell()?.BlockType ?? (int)RogueDLCBlockTypeEnum.Empty;
                updated.Add(target);
                break;
            case ModifierEffectTypeEnum.ReplicateSelectCellToAround:
            case ModifierEffectTypeEnum.SetBlockTypeToAround:
                updated.AddRange(ReplicateTo(OpenCells(run, sourceTypes).Where(cell => IsAround(cell, target)), target,
                    IntParam(effect, "Count", 1)));
                break;
            case ModifierEffectTypeEnum.ReplicateSelectCellToRandom:
                updated.AddRange(ReplicateTo(OpenCells(run, sourceTypes), target, IntParam(effect, "Count", 1)));
                break;
            case ModifierEffectTypeEnum.SetCellTypeAndTakeReward:
                await TakeCellReward(run, effect, target);
                if (targetTypes.Count > 0) target.BlockType = targetTypes.RandomElement();
                updated.Add(target);
                break;
            case ModifierEffectTypeEnum.SwapCellToCurAround:
            {
                // Boss cells (with monster candidates) never swap.
                var around = run.Cells.Values.Where(cell => cell != target && IsAround(cell, target) &&
                                                             cell.Monsters.Count == 0 && !IsCollapsed(run, cell))
                    .ToList();
                if (around.Count == 0) return;
                var other = around.RandomElement();
                (other.BlockType, target.BlockType) = (target.BlockType, other.BlockType);
                updated.Add(other);
                updated.Add(target);
                break;
            }
        }

        await SendCellUpdate(run, updated, RogueModifierSourceType.RogueModifierSourceDiceRoll, true);
    }

    private static List<ChessRogueCellData> ReplicateTo(IEnumerable<ChessRogueCellData> candidates,
        ChessRogueCellData source, int count)
    {
        var pool = candidates.Where(cell => cell != source).ToList();
        List<ChessRogueCellData> updated = [];
        for (var i = 0; i < count && pool.Count > 0; i++)
        {
            var cell = pool.RandomElement();
            cell.BlockType = source.BlockType;
            pool.Remove(cell);
            updated.Add(cell);
        }

        return updated;
    }

    private async ValueTask TakeCellReward(ChessRogueRunData run, ChessRogueDiceSurfaceContentEffect effect,
        ChessRogueCellData cell)
    {
        foreach (var reward in Param(effect, "Reward", "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = reward.Split(',');
            if (parts.Length != 2) continue;

            switch (parts[0])
            {
                case "Area" when cell.BlockType == (int)RogueDLCBlockTypeEnum.MonsterElite:
                    OpenDraft(run, RogueDraftGroups.Elite);
                    await GainMoney(run, Random.Shared.Next(70, 150));
                    break;
                case "Area":
                    OpenDraft(run, ChessRogueResources.EventDraftGroupId);
                    await GainMoney(run, Random.Shared.Next(40, 60));
                    break;
                case "Event" when parts[1] == "Buff":
                    OpenDraft(run, ChessRogueResources.EventDraftGroupId);
                    break;
                case "Event" when parts[1] == "Money":
                    await GainMoney(run, IntParam(effect, "Count", 10));
                    break;
            }
        }

        await SendPendingNotify(run);
    }

    private async ValueTask ModifierSelectCell(ChessRogueRunData run, ChessRogueModifierData modifier, int cellId)
    {
        var effect = ChessRogueResources.SurfaceEffect(modifier.SurfaceId);
        if (effect == null || !ImplementedEffects.Contains(effect.EffectType))
        {
            modifier.IsConfirmed = true;
            return;
        }

        if (effect.EffectType != ModifierEffectTypeEnum.TrunAroundToEmptyGetBuff ||
            !run.Cells.TryGetValue(cellId, out var target)) return;

        await SendModifierStageStart();
        modifier.IsConfirmed = true;

        var around = OpenCells(run, BlockTypes(Param(effect, "SourceType", "3")))
            .Where(cell => IsAround(cell, target))
            .ToList();
        var emptied = around.Count(cell => cell.BlockType != (int)RogueDLCBlockTypeEnum.Empty);
        foreach (var cell in around) cell.BlockType = (int)RogueDLCBlockTypeEnum.Empty;

        await SendCellUpdate(run, around, RogueModifierSourceType.RogueModifierSourceDiceRoll, true);
        if (emptied > 0)
        {
            OpenDraft(run, ChessRogueResources.EventDraftGroupId);
            await SendPendingNotify(run);
        }
    }

    private void ModifierBeforeBattle(ChessRogueRunData run, ChessRogueModifierData modifier, BattleInstance battle)
    {
        var effect = ChessRogueResources.SurfaceEffect(modifier.SurfaceId);
        if (effect?.EffectType != ModifierEffectTypeEnum.AddMazeBuff) return;
        if (!int.TryParse(Param(effect, "BuffId", ""), out var buffId)) return;

        var buff = new MazeBuff(buffId, 1, -1) { WaveFlag = -1 };
        if (GameData.MazeBuffData.TryGetValue(buffId * 10 + 1, out var buffExcel) &&
            GameData.AdventureModifierData.TryGetValue(buffExcel.ModifierName, out var config))
            foreach (var task in config.OnBeforeBattle)
            {
                var type = task.Type.Replace("RPG.GameCore.", "");
                if (!type.StartsWith("SetDynamicValueBy")) continue;

                var (key, value) = type.Replace("SetDynamicValueBy", "") switch
                {
                    "ItemNum" => ("ItemNumber", run.Money),
                    "RogueBuffNumWithType" => ("RogueBuffNumberWithType", run.Buffs.Count),
                    "RogueModifierCount" => ("RogueModifierCount", 1),
                    "RogueLayer" => ("_RogueLayer", run.CurLayerIndex + 1),
                    "RogueMiracleNum" => ("RogueMiracleNumber", 0),
                    var other => (other, 0)
                };
                buff.DynamicValues[key] = value;
            }

        battle.Buffs.Add(buff);
    }
}
