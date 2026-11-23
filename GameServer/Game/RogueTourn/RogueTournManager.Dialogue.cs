using March7thHoney.Database.RogueTourn;
using March7thHoney.Enums.TournRogue;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Game.RogueTourn;

/// <summary>
///     A Divergent Universe Event room is a dialogue the CLIENT executes off the room NPC's Act graph. The server
///     announces the event's option pool, answers each pick and tells the client when the talk is over. Wire shapes
///     and ordering follow the two 4.6 official runs: finish notify, then the response, then the live-set snapshot
///     (an empty list is the clear).
/// </summary>
public partial class RogueTournManager
{
    /// <summary>
    ///     Gives an Event room its talk on first use, so re-entering (or the trigger that fires on every room load)
    ///     announces the same dialogue. Null for any other room, a season without an EventPool, or no usable graph.
    /// </summary>
    private RogueTournRoomDialogueData? EnsureRoomDialogue(RogueTournRunData run, RogueTournRoomData? room)
    {
        if (room == null || room.UniqueId == 0 || room.RoomType != (int)RogueTournRoomTypeEnum.Event) return null;
        if (run.RoomDialogues.TryGetValue(room.UniqueId, out var existing)) return existing;

        var pool = RogueTournResources.SeasonPlan((RogueTournModeEnum)run.TournMode)?.EventPool ?? [];
        if (pool.Count == 0) return null;

        // Round-robin from the dialogues the run already has, so a resumed run replays the same assignment. A used
        // id is not skipped: the official repeats one event across rooms.
        var start = run.RoomDialogues.Count;
        for (var offset = 0; offset < pool.Count; offset++)
        {
            var npcId = pool[(start + offset) % pool.Count];
            var graph = RogueTournEventDialogue.For(npcId);
            if (graph == null || graph.OptionIds.Count == 0) continue;

            var dialogue = new RogueTournRoomDialogueData
            {
                Handle = ++run.DialogueSeq,
                NpcId = npcId,
                Options = [.. graph.OptionIds]
            };
            run.RoomDialogues[room.UniqueId] = dialogue;
            Save();
            return dialogue;
        }

        Logger.Debug($"Event room {room.RoomId} found no usable dialogue in the authored pool");
        return null;
    }

    private static List<RogueTournRoomDialogueData> LiveDialogues(RogueTournRunData run)
    {
        return run.RoomDialogues.Values.Where(dialogue => !dialogue.Closed).OrderBy(dialogue => dialogue.Handle).ToList();
    }

    // Announces the current room's talk (when it is an Event room) by pushing the run's live dialogue set.
    public async ValueTask SendDialogueSnapshot()
    {
        var run = RunInstance;
        if (run == null || run.Status != RogueTournStates.RunDoing) return;
        if (EnsureRoomDialogue(run, run.CurRoom()) == null) return;
        await SendDialogueSet(run);
    }

    private async ValueTask SendDialogueSet(RogueTournRunData run)
    {
        var notify = new SyncRogueCommonDialogueDataScNotify();
        notify.DialogueList.AddRange(LiveDialogues(run).Select(RogueTournProtoBuilder.BuildDialogueRow));
        await Player.SendPacket(BasePacket.Create(CmdIds.SyncRogueCommonDialogueDataScNotify, notify));
    }

    // The client's dialogue pull (also the reconnect path); null when no run owns it.
    public GetRogueCommonDialogueDataScRsp? GetDialogueData()
    {
        var run = RunInstance;
        if (run == null) return null;
        var rsp = new GetRogueCommonDialogueDataScRsp();
        rsp.DialogueList.AddRange(LiveDialogues(run).Select(RogueTournProtoBuilder.BuildDialogueRow));
        return rsp;
    }

    /// <summary>
    ///     Answers a pick on one of the run's talks; null when the handle is not ours. A finishing option ends the talk
    ///     on the client alone; otherwise the pick ends it only when the best dialogue event it reports leaves the
    ///     player on the finishing branch. When the talk ends the room is released like any other cleared room.
    /// </summary>
    public async ValueTask<SelectRogueCommonDialogueOptionScRsp?> SelectDialogueOption(int handle, int optionId)
    {
        var run = RunInstance;
        if (run == null || handle == 0) return null;
        var dialogue = run.RoomDialogues.Values.FirstOrDefault(entry => !entry.Closed && entry.Handle == handle);
        if (dialogue == null) return null;

        List<int> events = [];
        var closes = false;
        var graph = RogueTournEventDialogue.For(dialogue.NpcId);
        if (graph != null && dialogue.Options.Contains(optionId))
        {
            dialogue.Selected.Add(optionId);
            if (graph.IsFinishingOption(optionId))
            {
                closes = true;
            }
            else
            {
                events = graph.EventsForOption(optionId);
                closes = events.Count > 0 && graph.ClosesOnReport(optionId);
            }

            if (closes) dialogue.Closed = true;
            Save();
        }

        await Player.SendPacket(BasePacket.Create(CmdIds.SyncRogueCommonDialogueOptionFinishScNotify,
            new SyncRogueCommonDialogueOptionFinishScNotify
            {
                AIAFEKGBFIF = (uint)handle,
                HAFOKMHCGFM = (uint)optionId,
                FJPGAOHGEBH = RogueTournProtoBuilder.BuildDialogueOption(optionId, false)
            }));

        // The dialogue-event ids ride only the pick that advances the graph.
        var rsp = new SelectRogueCommonDialogueOptionScRsp
        {
            AIAFEKGBFIF = (uint)handle,
            HAFOKMHCGFM = (uint)optionId,
            ONOLNGOHEMF = RogueTournProtoBuilder.BuildDialogueRow(dialogue)
        };
        rsp.CGINGCNCPCO.AddRange(events.Select(id => (uint)id));

        if (closes)
            // The finish rides after the response the caller sends; schedule the clear and the room release now so
            // they follow it in order.
            _pendingDialogueClose = true;

        return rsp;
    }

    private bool _pendingDialogueClose;

    // Sent after SelectRogueCommonDialogueOptionScRsp: the live-set clear, then the room's release.
    public async ValueTask FlushDialogueClose()
    {
        var run = RunInstance;
        if (!_pendingDialogueClose || run == null) return;
        _pendingDialogueClose = false;
        await SendDialogueSet(run);

        if (FinishRoomByGate()) await SendLevelNotify();
        if (Player.SceneInstance is { } scene && RogueTournEntityLoader.IsTournRoomScene(Player))
            await RogueTournEntityLoader.OpenRoomDoors(scene, true);
        await SendPendingNotify();
    }
}
