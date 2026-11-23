using March7thHoney.Data.Excel;

namespace March7thHoney.GameServer.Game.Rogue;

public class RogueBuffSelectMenu
{
    private const int OfferCount = 3;

    // One reroll per draft, as the legacy rogue's base reroll count.
    private const int DefaultRerollLimit = 1;

    private List<BaseRogueBuffExcel> Pool { get; set; } = [];
    private int OfferSize { get; set; } = OfferCount;
    private int RerollLimit { get; set; }
    private int RerollCount { get; set; }

    public List<BaseRogueBuffExcel> SelectBuffs { get; private set; } = [];
    public bool HasSelection => SelectBuffs.Count > 0;
    public bool CanRoll => RerollCount < RerollLimit;

    public static RogueBuffSelectMenu Create(RogueInstance inst, List<BaseRogueBuffExcel> pool)
    {
        return Create(inst, pool, OfferCount, DefaultRerollLimit);
    }

    public static RogueBuffSelectMenu Create(RogueInstance inst, List<BaseRogueBuffExcel> pool, int offerSize,
        int rerollLimit)
    {
        var menu = new RogueBuffSelectMenu { Pool = pool, OfferSize = offerSize, RerollLimit = rerollLimit };
        menu.Draw(inst);
        return menu;
    }

    public bool Reroll(RogueInstance inst)
    {
        if (!HasSelection || !CanRoll) return false;

        RerollCount++;
        Draw(inst);
        return true;
    }

    public BaseRogueBuffExcel? Select(RogueInstance inst, int buffId)
    {
        var chosen = SelectBuffs.FirstOrDefault(x => x.MazeBuffID == buffId);
        if (chosen == null) return null;

        inst.AddBuff(chosen);
        SelectBuffs = [];
        return chosen;
    }

    private void Draw(RogueInstance inst)
    {
        SelectBuffs = Pool.Where(x => !inst.Buffs.ContainsKey(x.MazeBuffID))
            .OrderBy(_ => Random.Shared.Next())
            .Take(OfferSize)
            .OrderBy(x => x.MazeBuffID)
            .ToList();
    }
}
