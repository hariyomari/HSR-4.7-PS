using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Net;
using March7thHoney.Internationalization;
using March7thHoney.Kcp.KcpSharp;
using March7thHoney.Util;

namespace March7thHoney.Kcp;

public class March7thHoneyConnection
{
    private static readonly Logger Logger = new("GameServer");
    public static readonly ConcurrentDictionary<int, string> LogMap = [];
    private static readonly ConcurrentDictionary<string, int> NameToCmdId = new(StringComparer.Ordinal);

    // Packets too frequent to be worth a packet log line; checked on every send and receive.
    private static readonly FrozenSet<int> IgnoreLog = new[]
    {
        CmdIds.PlayerHeartBeatCsReq, CmdIds.PlayerHeartBeatScRsp, CmdIds.SceneEntityMoveCsReq,
        CmdIds.SceneEntityMoveScRsp, CmdIds.GetShopListCsReq, CmdIds.GetShopListScRsp
    }.ToFrozenSet();

    protected readonly CancellationTokenSource CancelToken;
    protected readonly KcpConversation Conversation;
    public readonly IPEndPoint RemoteEndPoint;

    private readonly PacketLogFile _packetLog = new();

    public bool IsOnline { get; protected set; } = true;

    public March7thHoneyConnection(KcpConversation conversation, IPEndPoint remote)
    {
        Conversation = conversation;
        RemoteEndPoint = remote;
        CancelToken = new CancellationTokenSource();
        if (Crypto.PacketEncryptionEnabled) XorKey = Crypto.ClientSecretKey!.GetXorKey();
    }

    public byte[]? XorKey { get; set; }
    public ulong ClientSecretKeySeed { get; set; }

    public long? ConversationId => Conversation.ConversationId;

    public SessionStateEnum State { get; set; } = SessionStateEnum.INACTIVE;

    /// <summary>
    ///     Activates the connection. Called by the listener once the connection is registered — never from
    ///     the constructor, so overrides start no I/O before the object is fully built and reachable.
    /// </summary>
    public virtual void Start()
    {
        Logger.Debug($"New connection from {RemoteEndPoint}.");
        State = SessionStateEnum.WAITING_FOR_TOKEN;
    }

    public virtual void Stop()
    {
        Conversation.Dispose();
        try
        {
            CancelToken.Cancel();
            CancelToken.Dispose();
        }
        catch
        {
        }

        _packetLog.Dispose();
        IsOnline = false;
    }

    /// <summary>Sends this connection's packet log to <paramref name="path" /> when SavePersonalDebugFile is on.</summary>
    public void SetPacketLogFile(string path)
    {
        _packetLog.SetPath(path);
    }

    public void LogPacket(string sendOrRecv, ushort opcode, byte[] payload)
    {
        if (!ConfigManager.Config.ServerOption.LogOption.EnableGamePacketLog) return;
        if (IgnoreLog.Contains(opcode)) return;

        try
        {
            WritePacketLog(BuildPacketLogLine(sendOrRecv, opcode, payload));
        }
        catch
        {
            // A failure to write the log (typically a full disk) must never bring down the game server process.
            try
            {
                WritePacketLog(ShortPacketLogLine(sendOrRecv, opcode));
            }
            catch
            {
                // Swallowed on purpose: losing one packet log entry is far better than aborting the server process.
            }
        }
    }

    private static string BuildPacketLogLine(string sendOrRecv, ushort opcode, byte[] payload)
    {
        if (ConfigManager.Config.ServerOption.LogOption.DisableLogDetailPacket)
            return ShortPacketLogLine(sendOrRecv, opcode);
        if (!LogMap.TryGetValue(opcode, out var name))
            return ShortPacketLogLine(sendOrRecv, opcode);

        try
        {
            var asJson = PacketLogHelper.ConvertPacketToJson(opcode, payload);
            return $"{sendOrRecv}: {name}({opcode})\r\n{asJson}";
        }
        catch
        {
            // Unknown or unparseable payloads degrade to the short line instead of failing the send/recv path.
            return ShortPacketLogLine(sendOrRecv, opcode);
        }
    }

    private static string ShortPacketLogLine(string sendOrRecv, ushort opcode)
    {
        return $"{sendOrRecv}: {LogMap.GetValueOrDefault(opcode, "UnknownPacket")}({opcode})";
    }

    private void WritePacketLog(string output)
    {
        if (ConfigManager.Config.ServerOption.LogOption.LogPacketToConsole)
            Logger.Packet(output);

        if (!ConfigManager.Config.ServerOption.LogOption.SavePersonalDebugFile) return;
        _packetLog.WriteLine($"[{DateTime.Now:HH:mm:ss}] [GameServer] [DEBUG] " + output);
    }

    /// <summary>
    ///     Largest message KCP can carry in packet (non-stream) mode: the per-segment fragment counter is a
    ///     single byte, so a message may span at most 256 segments of <c>mtu - 24</c> bytes. Anything bigger
    ///     is rejected outright by the send queue — the client never sees it at all.
    /// </summary>
    public const int MaxPacketBytes = 256 * (March7thHoneyListener.KcpMtu - 24);

    public async Task SendPacket(byte[] packet, int cmdId = 0)
    {
        try
        {
            if (Crypto.PacketEncryptionEnabled)
                Crypto.Xor(packet, XorKey!);

            _ = await Conversation.SendAsync(packet, CancelToken.Token);
        }
        catch (Exception e)
        {
            // A dropped scene notification can leave the client stuck on the teleport loading screen.
            var name = cmdId != 0 ? LogMap.GetValueOrDefault(cmdId, cmdId.ToString()) : "<raw>";
            if (packet.Length > MaxPacketBytes)
                Logger.Error(I18NManager.Translate("Server.Log.PacketTooLarge", name, packet.Length.ToString(),
                    MaxPacketBytes.ToString()), e);
            else
                Logger.Debug($"Failed to send {name} ({packet.Length} bytes)", e);
        }
    }

    public async Task SendPacket(BasePacket packet)
    {
        if (packet.CmdId <= 0)
        {
            Logger.Debug("Tried to send packet with missing cmd id!");
            return;
        }

        LogPacket("Send", packet.CmdId, packet.Data);
        // Header: a reply sent while a request is handled carries that request's packet id.
        var requestPacketId = PacketHead.RequestPacketId.Value;
        var packetBytes = packet.BuildPacket(requestPacketId != 0 ? PacketHead.Encode(requestPacketId) : null);

        await SendPacket(packetBytes, packet.CmdId);
        await OnPacketSentAsync(packet);
    }

    /// <summary>
    ///     Runs after a packet has been handed to the transport. Game features that must react to a
    ///     specific outbound packet hook here instead of hard-coding their cmdId in the base send path.
    /// </summary>
    protected virtual Task OnPacketSentAsync(BasePacket packet) => Task.CompletedTask;

    public async Task SendPacket(int cmdId)
    {
        await SendPacket(new BasePacket((ushort)cmdId));
    }

    /// <summary>
    ///     Fills <see cref="LogMap" /> (cmdId → name) and the reverse <see cref="NameToCmdId" /> from the
    ///     generated <see cref="CmdIds" /> constants. One reflection pass at startup; the send/receive and
    ///     fallback-reply paths then work on plain dictionary lookups.
    /// </summary>
    public static void InitPacketNameMap()
    {
        foreach (var field in typeof(CmdIds).GetFields())
        {
            if (field.FieldType != typeof(int)) continue;
            var value = (int)field.GetValue(null)!;
            LogMap.TryAdd(value, field.Name);
            NameToCmdId.TryAdd(field.Name, value);
        }
    }

    /// <summary>Resolve a packet name to its cmdId in O(1); false when the name is unknown.</summary>
    public static bool TryGetCmdIdByName(string packetName, out int cmdId)
    {
        return NameToCmdId.TryGetValue(packetName, out cmdId);
    }
}
