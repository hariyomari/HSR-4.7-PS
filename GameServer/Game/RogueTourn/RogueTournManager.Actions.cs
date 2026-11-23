using March7thHoney.Data;
using March7thHoney.Database.RogueTourn;
using March7thHoney.Enums.Rogue;
using March7thHoney.Enums.TournRogue;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.RogueTourn;

public partial class RogueTournManager
{
    // The official run opens with three Epic Equations of its season.
    private const int StarterFormulaCount = 3;

    // Every measured blessing / gift / miracle window carries three options.
    private const int DraftBuffCount = 3;
    private const int GiftChoiceCount = 3;
    private const int MiracleChoiceCount = 3;

    // The exit chooser offers up to three room cards.
    private const int ExitCardCount = 3;

    /// <summary>
    ///     The Persona style's exp → level curve: index k is the cumulative exp at which the style reaches level k+1.
    ///     Identical in the settle payloads of both 4.6 captures and reproduces all 12 (exp, level) pairs on the wire.
    /// </summary>
    private static readonly int[] StyleLevelCurve =
        [0, 30, 130, 230, 380, 530, 680, 830, 1030, 1230, 1430, 1630, 1880, 2130, 2380, 2630, 2930, 3230, 3530, 3830];

    // The official room grants are 15 × {2, 5, 8, 11} with no table behind them; 75 is the mode of the 12 samples.
    private const int StyleExpPerRoom = 75;

    #region Style

    // Only the Arcadian Chronicles season (Tourn3) carries a Persona style, and a Guide-group area stages
    // on the Guide game data even when its mode is Tourn3; sending the style select there NREs the client.
    public static bool UsesPersonaStyle(RogueTournRunData run)
    {
        if (run.TournMode != (int)RogueTournModeEnum.Tourn3) return false;
        return !(GameData.RogueTournAreaData.TryGetValue(run.AreaId, out var area) &&
                 area.AreaGroupID == RogueTournAreaGroupIDEnum.Guide);
    }

    private static int StyleLevelAt(int exp)
    {
        var level = 1;
        for (var k = 0; k < StyleLevelCurve.Length; k++)
            if (exp >= StyleLevelCurve[k])
                level = k + 1;
        return level;
    }

    /// <summary>
    ///     A room entry advances the run's Persona style; the state + gain pair is buffered for the handler that owns
    ///     the result stream. The client's Persona pages render from this state and blank without it. Non-Persona
    ///     runs get no rows: applying a style result on a Guide/Classic run wedges the client's action queue.
    /// </summary>
    private static void GrantRoomStyleExp(RogueTournRunData run)
    {
        if (!UsesPersonaStyle(run)) return;
        var prevExp = run.StyleExp;
        var prevLevel = run.StyleLevel == 0 ? 1 : run.StyleLevel;
        run.StyleExp = prevExp + StyleExpPerRoom;
        run.StyleLevel = StyleLevelAt(run.StyleExp);
        run.StyleRows.Add(new RogueTournStyleRow(run.StyleExp, run.StyleLevel, prevExp, prevLevel, StyleExpPerRoom));
    }

    // The rows are stamped with the position of the action the request resolves (the current ActionSeq).
    public List<RogueTournActionResult> TakeStyleRows()
    {
        var run = RunInstance;
        if (run == null || run.StyleRows.Count == 0) return [];
        List<RogueTournActionResult> results = [];
        foreach (var row in run.StyleRows)
        {
            results.Add(new RogueTournActionResult
            {
                Kind = RogueTournActionResultKind.StyleExp, Value = row.Exp, Level = row.Level,
                PrevExp = row.PrevExp, PrevLevel = row.PrevLevel, QueuePos = run.ActionSeq
            });
            results.Add(new RogueTournActionResult
            {
                Kind = RogueTournActionResultKind.StyleExpGain, Value = row.Gain, QueuePos = run.ActionSeq
            });
        }

        run.StyleRows.Clear();
        return results;
    }

    private static void OpenStyleAction(RogueTournRunData run)
    {
        if (run.PersonaStyle == 0) return;
        run.Pending = new RogueTournPendingActionData
        {
            QueuePos = run.NextActionPos(),
            Kind = RogueTournPendingKind.Style,
            Options = [run.PersonaStyle]
        };
    }

    #endregion

    #region Drafts

    // The window rotates by queue position so consecutive drafts differ while staying deterministic.
    private static void OpenFormulaDraft(RogueTournRunData run)
    {
        var owned = run.Formulas.ToHashSet();
        var pool = RogueTournResources.FormulaIds((RogueTournModeEnum)run.TournMode, RogueFormulaCategoryEnum.Epic)
            .Where(id => !owned.Contains(id))
            .ToList();
        if (pool.Count == 0) return;

        var start = pool.Count > StarterFormulaCount ? run.ActionSeq % pool.Count : 0;
        List<int> options = [];
        for (var n = 0; n < StarterFormulaCount && n < pool.Count; n++) options.Add(pool[(start + n) % pool.Count]);

        run.Pending = new RogueTournPendingActionData
        {
            QueuePos = run.NextActionPos(),
            Kind = RogueTournPendingKind.Formula,
            Options = options
        };
    }

    /// <summary>
    ///     The 4.6 post-battle blessing draft: up to three blessings the run does not hold from its generation's
    ///     pools, each with the level on offer. The window rotates by queue position; canRoll is shown on every
    ///     official draft (the reroll itself is not modelled).
    /// </summary>
    private static void OpenBuffDraft(RogueTournRunData run)
    {
        var pool = RogueTournResources.BuffPool((RogueTournModeEnum)run.TournMode)
            .Where(buff => !run.Buffs.ContainsKey(buff.MazeBuffID))
            .ToList();
        if (pool.Count == 0)
        {
            Logger.Debug($"Blessing draft skipped: the {(RogueTournModeEnum)run.TournMode} pool has nothing left");
            return;
        }

        var start = pool.Count > DraftBuffCount ? run.ActionSeq % pool.Count : 0;
        List<RogueTournBuffOffer> offers = [];
        for (var n = 0; n < DraftBuffCount && n < pool.Count; n++)
        {
            var buff = pool[(start + n) % pool.Count];
            offers.Add(new RogueTournBuffOffer { BuffId = buff.MazeBuffID, Level = Math.Max(buff.MazeBuffLevel, 1) });
        }

        run.Pending = new RogueTournPendingActionData
        {
            QueuePos = run.NextActionPos(),
            Kind = RogueTournPendingKind.Buff,
            Options = offers.Select(offer => offer.BuffId).ToList(),
            BuffOffers = offers,
            CanRoll = true
        };
    }

    /// <summary>
    ///     Deals what a cleared room still owes, in the season policy's order, and only then the room chooser. Drafts
    ///     and chooser share the single pending slot, so the sequence is state (DealtStep), not timing; a draft with
    ///     nothing eligible is skipped rather than opened empty.
    /// </summary>
    private static bool OpenClearedRoomAction(RogueTournRunData run)
    {
        if (run.Pending != null) return false;
        var room = run.CurRoom();
        if (room == null || room.Status != RogueTournStates.RoomFinish) return false;

        var policy = RogueTournResources.SeasonPlan((RogueTournModeEnum)run.TournMode)?.ClearedRoomPlan()
                     ?? new RogueTournSeasonPlan().ClearedRoomPlan();
        while (room.DealtStep < policy.Drafts.Count)
        {
            var name = policy.Drafts[room.DealtStep];
            room.DealtStep++;
            if (OpenNamedDraft(run, policy, name)) return true;
        }

        OpenExitChoice(run);
        return run.Pending != null;
    }

    private static bool OpenNamedDraft(RogueTournRunData run, RogueTournClearedRoomPlan policy, string name)
    {
        var pending = new RogueTournPendingActionData();
        switch (name)
        {
            case "gift":
            case "gift_core":
                // The window is filtered to the style the run declared: every official window shares one.
                pending.Kind = RogueTournPendingKind.Gift;
                pending.Options = RogueTournResources.GiftChoices(run.Gifts, run.PersonaStyle, name == "gift_core",
                    GiftChoiceCount);
                break;
            case "miracle":
                pending.Kind = RogueTournPendingKind.Miracle;
                pending.DropGroup = policy.MiracleGroup;
                pending.Options = RogueTournResources.MiracleChoices((RogueTournModeEnum)run.TournMode, run.Miracles,
                    policy.MiracleCount > 0 ? policy.MiracleCount : MiracleChoiceCount);
                break;
            default:
                Logger.Debug($"Unknown cleared-room draft '{name}' in the room plan, skipped");
                return false;
        }

        if (pending.Options.Count == 0) return false;
        pending.QueuePos = run.NextActionPos();
        run.Pending = pending;
        return true;
    }

    /// <summary>
    ///     The room chooser the official opens once a room's doors are open. A next slot carrying an authored preset
    ///     is confirmed (no cards); otherwise up to three room cards are offered, scanning forward across layers
    ///     (a layer's tail alone cannot fill the 3-wide door) and skipping preset slots. The picked card is applied to
    ///     the slot being entered, so the walk stays linear.
    /// </summary>
    private static void OpenExitChoice(RogueTournRunData run)
    {
        if (run.Pending != null) return;
        var (startLayer, _, nextRoom) = run.NextRoomSlot();
        if (nextRoom == null) return;

        if (nextRoom.PresetId != 0)
        {
            run.Pending = new RogueTournPendingActionData
            {
                QueuePos = run.NextActionPos(),
                Kind = RogueTournPendingKind.Exit,
                ExitPreset = nextRoom.PresetId
            };
            return;
        }

        List<RogueTournRoomData> targets = [];
        for (var li = startLayer; li < run.Layers.Count && targets.Count < ExitCardCount; li++)
        {
            var layer = run.Layers[li];
            for (var ri = layer.CurRoomIndex; ri < layer.Rooms.Count && targets.Count < ExitCardCount; ri++)
            {
                var room = layer.Rooms[ri];
                if (room.UniqueId == 0 || room.Status != RogueTournStates.RoomNone || room.PresetId != 0) continue;
                targets.Add(room);
            }
        }

        if (targets.Count == 0)
        {
            Logger.Debug($"Room chooser has no candidate (layer {run.CurLayerIndex})");
            return;
        }

        run.Pending = new RogueTournPendingActionData
        {
            QueuePos = run.NextActionPos(),
            Kind = RogueTournPendingKind.Exit,
            Options = targets.Select(room => room.UniqueId).ToList(),
            OptionLevels = targets.ToDictionary(room => room.UniqueId, room => room.Level)
        };
    }

    // The trigger handler deals what the room the player stands in owes before it walks the run on.
    public bool DealClearedRoomAction()
    {
        var run = RunInstance;
        if (run == null) return false;
        var dealt = OpenClearedRoomAction(run);
        if (dealt) Save();
        return dealt;
    }

    #endregion

    #region Answers

    /// <summary>
    ///     Resolves a HandleRogueCommonPendingActionCsReq (5611) answer to an open Divergent Universe action.
    ///     Returns null when the run does not own the answer, so the caller falls through to classic Rogue.
    ///     Pushes the result and next-action notifies itself; the caller only sends the returned ScRsp.
    /// </summary>
    public async ValueTask<HandleRogueCommonPendingActionScRsp?> HandlePendingAction(OFEDNNJIJFL req)
    {
        var run = RunInstance;
        if (run == null) return null;

        // A 4.6 client leaves a room by answering the chooser, never by RogueTournEnterRoomCsReq.
        if (req.EKNBICFHDJP != null) return await HandleExitChoice(req);

        RogueTournPendingKind kind;
        int value;
        if (req.DPAMEGCFNAJ != null)
        {
            kind = RogueTournPendingKind.Style;
            value = (int)req.DPAMEGCFNAJ.EOOLCIACHFJ;
        }
        else if (req.POHIHFCHKNP != null || req.FMDNOHNKHDP != null)
        {
            kind = RogueTournPendingKind.Formula;
            value = (int)(req.POHIHFCHKNP?.JMNMFMHPCKD ?? req.FMDNOHNKHDP!.JMNMFMHPCKD);
        }
        else if (req.CCCIFGFHGIL != null)
        {
            kind = RogueTournPendingKind.Miracle;
            value = (int)req.CCCIFGFHGIL.OJPGDDFJCHB;
        }
        else if (req.JGOLAJCHKAG != null)
        {
            kind = RogueTournPendingKind.Gift;
            value = (int)req.JGOLAJCHKAG.PGDEHMAGGLF;
        }
        else if (req.DLKIKBIDJDB != null)
        {
            kind = RogueTournPendingKind.Buff;
            value = (int)req.DLKIKBIDJDB.BAOFIGFFMLN;
        }
        else
        {
            return null;
        }

        var pending = run.Pending;
        if (pending == null || pending.Kind != kind) return null;

        // A stale queue position is accepted while the arm still matches: the client re-sends mid-refresh.
        var queuePos = req.MPLGEEGOAMC;
        var ret = Retcode.RetSucc;
        List<RogueTournActionResult> results = [];

        if (!pending.Options.Contains(value))
        {
            // A bogus option must still consume the action or the confirm dialog wedges forever.
            run.Pending = null;
            ret = Retcode.RetRogueSelectBuffNotExist;
        }
        else
        {
            run.Pending = null;
            switch (kind)
            {
                case RogueTournPendingKind.Style:
                    run.PersonaStyle = value;
                    Data.PersonaStyle = value;
                    results.Add(new RogueTournActionResult { Kind = RogueTournActionResultKind.Style, Value = value });
                    // The style confirmation mints the run's Persona modifier pair: the armed style row first.
                    if (run.StyleModifier == 0)
                    {
                        run.StyleModifier = run.AddModifier();
                        run.ModifierAdds.Add(new RogueTournModifierAdd(run.StyleModifier, true));
                        run.ModifierAdds.Add(new RogueTournModifierAdd(run.AddModifier(), false));
                    }

                    // The official run follows the style confirmation with the starter Equation draft.
                    OpenFormulaDraft(run);
                    break;
                case RogueTournPendingKind.Formula:
                    if (!run.Formulas.Contains(value)) run.Formulas.Add(value);
                    RogueTournHandbookData.Discover(Handbook.Formulas, value);
                    results.Add(new RogueTournActionResult { Kind = RogueTournActionResultKind.Formula, Value = value });
                    break;
                case RogueTournPendingKind.Buff:
                {
                    // The answer carries only the id; the level is the one the draft offered.
                    var level = pending.BuffOffers.FirstOrDefault(offer => offer.BuffId == value)?.Level ?? 1;
                    run.Buffs[value] = level;
                    RogueTournHandbookData.Discover(Handbook.Buffs, value);
                    var previousTier = run.StyleTier();
                    run.BlessingPicks++;
                    if (run.StyleTier() > previousTier) run.StyleTierUp = true;
                    results.Add(new RogueTournActionResult
                        { Kind = RogueTournActionResultKind.Buff, Value = value, Level = level });
                    results.Add(new RogueTournActionResult
                        { Kind = RogueTournActionResultKind.BuffTypes, Types = BuffTypeCounts(run) });
                    break;
                }
                case RogueTournPendingKind.Gift:
                    run.Gifts.Add(value);
                    // Every gift a run acquires is also an unarmed PersonaStyle modifier row.
                    run.ModifierAdds.Add(new RogueTournModifierAdd(run.AddModifier(), false));
                    results.Add(new RogueTournActionResult
                        { Kind = RogueTournActionResultKind.Gift, Value = value, EntryId = run.Gifts.Count });
                    break;
                case RogueTournPendingKind.Miracle:
                    if (!run.Miracles.Contains(value)) run.Miracles.Add(value);
                    RogueTournHandbookData.Discover(Handbook.Miracles, value);
                    results.Add(new RogueTournActionResult { Kind = RogueTournActionResultKind.Miracle, Value = value });
                    break;
            }

            // A row closing an action carries that action's position; gift-entry and scene rows stay unbound.
            foreach (var result in results.Where(result => result.Kind != RogueTournActionResultKind.Gift))
                result.QueuePos = pending.QueuePos;
        }

        // A room entry's style rows never outlive the request that buffered them (the opening room's go here).
        results.AddRange(TakeStyleRows());

        // A cleared room re-offers what it still owes (gift, then chooser) after every answer except the blessing:
        // the first post-battle offer is trigger-gated, the client's room-select trigger paces it.
        if (run.CurRoom() is { Status: RogueTournStates.RoomFinish } && kind != RogueTournPendingKind.Buff)
            OpenClearedRoomAction(run);
        Save();

        // Official order: result rows, the style modifier pair / tier update, the next action, then the response.
        // Without the result push the client re-sends the same answer every ~150 ms.
        await SendActionResults(results);
        await SendModifierAdds();
        await SendStyleTierUpdate();
        await SendPendingNotify();
        if (kind == RogueTournPendingKind.Formula && ret == Retcode.RetSucc)
            await Player.SendPacket(BasePacket.Create(CmdIds.RogueTournHandBookNotify,
                new RogueTournHandBookNotify { ICNOEGEEPOF = new MJBODJKDPPD { KAHMGPGPOII = (uint)value } }));

        // The official response echoes the position in both fields.
        return new HandleRogueCommonPendingActionScRsp
        {
            Retcode = (uint)ret,
            QueuePosition = queuePos,
            BFNEPPCNOOI = queuePos
        };
    }

    /// <summary>
    ///     The chooser answer walks the run: the picked card takes the next slot's identity (a preset door confirms
    ///     the slot's own preset instead), the offered triple leaves the deck, and the room is entered the way the
    ///     official orders it: deck zone, scene, room status, scene push, scene row, style rows, next action.
    /// </summary>
    private async ValueTask<HandleRogueCommonPendingActionScRsp> HandleExitChoice(OFEDNNJIJFL req)
    {
        var queuePos = req.MPLGEEGOAMC;
        var rsp = new HandleRogueCommonPendingActionScRsp { QueuePosition = queuePos, BFNEPPCNOOI = queuePos };
        var ret = SelectExit((int)req.EKNBICFHDJP.UniqueId, (int)req.EKNBICFHDJP.JAMNNBEPPKE);
        if (ret != Retcode.RetSucc || RunInstance == null)
        {
            rsp.Retcode = (uint)(ret == Retcode.RetSucc ? Retcode.RetRogueStatusNotMatch : ret);
            return rsp;
        }

        await SendRoomCardZone();
        if (await EnterRoomScene(RunInstance.CurRoom()))
        {
            await SendLevelNotify();
            await PushSceneChange();
            await SendSceneResult();
        }

        await SendActionResults(TakeStyleRows());
        await SendPendingNotify();
        return rsp;
    }

    public Retcode SelectExit(int uniqueId, int preset)
    {
        var run = RunInstance;
        if (run == null || run.Status != RogueTournStates.RunDoing) return Retcode.RetRogueStatusNotMatch;
        var pending = run.Pending;
        if (pending == null || pending.Kind != RogueTournPendingKind.Exit) return Retcode.RetRogueStatusNotMatch;

        if (uniqueId == 0)
        {
            // A preset door: the client echoes the preset; the room behind it is the next slot and no card is spent.
            if (pending.ExitPreset == 0 || pending.ExitPreset != preset) return Retcode.RetRogueStatusNotMatch;
            var layer = run.CurLayer();
            if (layer == null || layer.CurRoomIndex >= layer.Rooms.Count) return Retcode.RetRogueAreaInvalid;
            var target = layer.Rooms[layer.CurRoomIndex];
            if (target.PresetId != preset) return Retcode.RetRogueAreaInvalid;
            run.Pending = null;
            return EnterRoom(target.RoomIndex);
        }

        if (!pending.Options.Contains(uniqueId)) return Retcode.RetRogueStatusNotMatch;
        var (_, card) = run.RoomByUniqueId(uniqueId);
        var (startLayer, _, slot) = run.NextRoomSlot();
        if (card == null || slot == null) return Retcode.RetRogueAreaInvalid;

        run.Pending = null;
        // The door opens onto the next slot, which takes the picked card's identity; geometry stays with the slot.
        if (!ReferenceEquals(slot, card))
        {
            (slot.UniqueId, card.UniqueId) = (card.UniqueId, slot.UniqueId);
            (slot.RoomType, card.RoomType) = (card.RoomType, slot.RoomType);
            (slot.CompositionType, card.CompositionType) = (card.CompositionType, slot.CompositionType);
            (slot.Level, card.Level) = (card.Level, slot.Level);
            // A dialogue belongs to the card's id, which moved with it.
        }

        // The whole triple is spent on this entry; the two unchosen cards never return.
        run.ConsumeDeck(pending.Options);
        return startLayer == run.CurLayerIndex - 1 ? EnterRoom(slot.RoomIndex) : EnterLayer();
    }

    // The run's blessings per RogueBuffType, the histogram row the official sends after every grant.
    private static Dictionary<int, int> BuffTypeCounts(RogueTournRunData run)
    {
        Dictionary<int, int> counts = [];
        foreach (var (buffId, level) in run.Buffs)
        {
            var row = RogueTournResources.BuffRow(buffId, level) ?? RogueTournResources.BuffRow(buffId, 1);
            if (row == null || row.RogueBuffType == 0) continue;
            counts[row.RogueBuffType] = counts.GetValueOrDefault(row.RogueBuffType) + 1;
        }

        return counts;
    }

    private async ValueTask SendModifierAdds()
    {
        var run = RunInstance;
        if (run == null || run.ModifierAdds.Count == 0) return;
        foreach (var add in run.ModifierAdds)
            await Player.SendPacket(BasePacket.Create(CmdIds.RogueModifierAddNotify,
                new RogueModifierAddNotify { Modifier = RogueTournProtoBuilder.BuildModifierRow(add.Id, true, add.StyleArm, 0) }));
        run.ModifierAdds.Clear();
    }

    // A blessing pick that stepped the tier re-pushes ONLY the style row, carrying the new tier.
    private async ValueTask SendStyleTierUpdate()
    {
        var run = RunInstance;
        if (run == null || run.StyleModifier == 0 || !run.StyleTierUp) return;
        run.StyleTierUp = false;
        await Player.SendPacket(BasePacket.Create(CmdIds.RogueModifierUpdateNotify,
            new RogueModifierUpdateNotify
            {
                Modifier = RogueTournProtoBuilder.BuildModifierRow(run.StyleModifier, true, true, run.StyleTier())
            }));
    }

    /// <summary>
    ///     The /du blessall primitive: every blessing the run's generation pools, at the highest level any pool row
    ///     offers, through the same bookkeeping a real pick does (BlessingPicks drives the style tier). A blessing
    ///     already held at that level costs no pick. Returns the number granted.
    /// </summary>
    public async ValueTask<int> GrantAllBlessings()
    {
        var run = RunInstance;
        if (run == null) return 0;

        Dictionary<int, int> best = [];
        var tags = GameData.RogueBuffGroupData.Values.OfType<March7thHoney.Data.Excel.RogueTournBuffGroupExcel>()
            .Where(group => group.TournMode == (RogueTournModeEnum)run.TournMode)
            .SelectMany(group => group.RogueBuffDrop)
            .ToHashSet();
        foreach (var buff in GameData.RogueBuffData.Values.OfType<March7thHoney.Data.Excel.RogueTournBuffExcel>())
            if (tags.Contains(buff.RogueBuffTag) && buff.MazeBuffID != 0 &&
                buff.MazeBuffLevel > best.GetValueOrDefault(buff.MazeBuffID))
                best[buff.MazeBuffID] = Math.Max(buff.MazeBuffLevel, 1);
        if (best.Count == 0) return 0;

        var previousTier = run.StyleTier();
        List<RogueTournActionResult> results = [];
        foreach (var (buffId, level) in best.OrderBy(entry => entry.Key))
        {
            var owned = run.Buffs.TryGetValue(buffId, out var held);
            run.Buffs[buffId] = Math.Max(level, held);
            if (owned && held >= level) continue;
            run.BlessingPicks++;
            RogueTournHandbookData.Discover(Handbook.Buffs, buffId);
            results.Add(new RogueTournActionResult
                { Kind = RogueTournActionResultKind.Buff, Value = buffId, Level = level, QueuePos = run.ActionSeq });
        }

        if (run.StyleTier() > previousTier) run.StyleTierUp = true;
        if (results.Count == 0) return 0;
        results.Add(new RogueTournActionResult
            { Kind = RogueTournActionResultKind.BuffTypes, Types = BuffTypeCounts(run), QueuePos = run.ActionSeq });
        Save();

        await SendActionResults(results);
        await SendStyleTierUpdate();
        return results.Count - 1;
    }

    #endregion
}
