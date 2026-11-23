using March7thHoney.Data.Custom;
using March7thHoney.Enums.Server;
using March7thHoney.Kcp;

namespace March7thHoney.GameServer.Server;

/// <summary>
///     A scripted packet exchange loaded with /debug: once the client sends the packet a WaitForPacket step names, the
///     SendPacket steps after it, up to the next WaitForPacket, are sent in order. The command thread replaces the
///     script while the connection's receive loop consumes it, so every access takes the lock.
/// </summary>
public sealed class CustomPacketQueue
{
    private readonly Lock _lock = new();
    private readonly List<PacketActionData> _actions = [];

    public void Replace(IEnumerable<PacketActionData> actions)
    {
        lock (_lock)
        {
            _actions.Clear();
            _actions.AddRange(actions);
        }
    }

    /// <summary>
    ///     When the next step waits for <paramref name="packetName" />, removes it together with the steps up to the next
    ///     WaitForPacket and returns their SendPacket steps in order. <paramref name="interrupt" /> is the waiting
    ///     step's InterruptFormalHandler: whether the regular handler must be skipped.
    /// </summary>
    public bool TryAdvance(string? packetName, out List<PacketActionData> sends, out bool interrupt)
    {
        sends = [];
        interrupt = false;
        lock (_lock)
        {
            if (_actions.Count == 0) return false;
            var waiting = _actions[0];
            if (waiting.Action != PacketActionTypeEnum.WaitForPacket || waiting.Param.PacketName != packetName)
                return false;

            interrupt = waiting.Param.InterruptFormalHandler;
            var taken = 1;
            for (; taken < _actions.Count && _actions[taken].Action != PacketActionTypeEnum.WaitForPacket; taken++)
                if (_actions[taken].Action == PacketActionTypeEnum.SendPacket)
                    sends.Add(_actions[taken]);

            _actions.RemoveRange(0, taken);
            return true;
        }
    }

    /// <summary>
    ///     The packet a SendPacket step describes: its packet name resolved to a cmdId (0 when the name is unknown, which
    ///     the send path refuses) and its base64 body.
    /// </summary>
    public static BasePacket ToPacket(PacketActionData action)
    {
        var cmdId = March7thHoneyConnection.TryGetCmdIdByName(action.Param.PacketName, out var id) ? (ushort)id : (ushort)0;
        var packet = new BasePacket(cmdId);
        packet.SetData(action.Param.PacketData);
        return packet;
    }
}
