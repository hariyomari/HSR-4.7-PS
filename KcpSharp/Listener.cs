using System.Net;
using System.Net.Sockets;
using March7thHoney.Internationalization;
using March7thHoney.Kcp.KcpSharp;
using March7thHoney.Util;

namespace March7thHoney.Kcp;

public class March7thHoneyListener
{
    public delegate March7thHoneyConnection ConnectionCreatedHandler(KcpConversation conversation, IPEndPoint remote);

    private static UdpClient? UDPClient;
    private static IPEndPoint? ListenAddress;
    private static IKcpTransport<IKcpMultiplexConnection>? KCPTransport;
    private static readonly Logger Logger = new("GameServer");
    private static readonly object ConnectionsLock = new();
    private static readonly SortedList<long, March7thHoneyConnection> Connections = [];

    /// <summary>Thread-safe snapshot of currently registered connections, for external read-only enumeration.</summary>
    public static List<March7thHoneyConnection> GetSnapshot()
    {
        lock (ConnectionsLock)
            return [.. Connections.Values];
    }

    /// <summary>KCP MTU. Also fixes the maximum message size — see March7thHoneyConnection.MaxPacketBytes.</summary>
    public const int KcpMtu = 1400;

    // Size of the UDP handshake datagrams exchanged before KCP traffic starts; the transport routes every datagram of
    // exactly this size to HandleHandshake instead of KCP.
    private const int HandshakeBytes = 20;

    // Handshake codes carried in the UDP greeting.
    private const int HandshakeConnectCode = 0x000000FF;
    private const int HandshakeConnectCodeAlt = unchecked((int)0xC8520300); // variant sent by newer clients
    private const int HandshakeDisconnectCode = 0x00000194;
    private const int HandshakeResponseCode = 0x00000145;
    private const int HandshakeResponseTail = 0x14514545;
    private const int HandshakeDisconnectTail = 0x19419494;

    // Reason code written into the disconnect datagram that answers a client-initiated disconnect handshake.
    private const int ClientDisconnectReasonCode = 5;

    private static readonly KcpConversationOptions ConvOpt = new()
    {
        StreamMode = false,
        Mtu = KcpMtu,
        ReceiveWindow = 256,
        SendWindow = 256,
        NoDelay = true,
        UpdateInterval = 100,
        KeepAliveOptions = new KcpKeepAliveOptions(1000, 30000)
    };

    public static ConnectionCreatedHandler? CreateConnection { get; set; } = null;

    private static Socket? UDPListener => UDPClient?.Client;
    private static IKcpMultiplexConnection? Multiplex => KCPTransport?.Connection;
    private static uint PORT => ConfigManager.Config.GameServer.Port;

    public static March7thHoneyConnection? GetConnectionByEndPoint(IPEndPoint ep)
    {
        lock (ConnectionsLock)
            return Connections.Values.FirstOrDefault(c => c.RemoteEndPoint.Equals(ep));
    }

    public static void StartListener()
    {
        ListenAddress = new IPEndPoint(IPAddress.Parse(ConfigManager.Config.GameServer.BindAddress), (int)PORT);
        UDPClient = new UdpClient(ListenAddress);
        if (UDPListener == null) return;
        KCPTransport = KcpSocketTransport.CreateMultiplexConnection(UDPClient, KcpMtu, HandshakeBytes, HandleHandshake);
        KCPTransport.Start();
        Logger.Info(I18NManager.Translate("Server.ServerInfo.ServerRunning", I18NManager.Translate("Word.Game"),
            ConfigManager.Config.GameServer.GetDisplayAddress()));
    }

    public static void UnregisterConnection(March7thHoneyConnection con)
    {
        if (!con.ConversationId.HasValue) return;
        var convId = con.ConversationId.Value;
        bool removed;
        lock (ConnectionsLock)
            removed = Connections.Remove(convId);
        if (removed)
        {
            Multiplex?.UnregisterConversation(convId);
            Logger.Debug($"Connection with {con.RemoteEndPoint} has been closed");
        }
    }

    private static async Task HandleHandshake(UdpReceiveResult rcv)
    {
        try
        {
            var con = GetConnectionByEndPoint(rcv.RemoteEndPoint);
            await using MemoryStream? ms = new(rcv.Buffer);
            using BinaryReader? br = new(ms);
            var code = br.ReadInt32BE();
            br.ReadUInt32();
            br.ReadUInt32();
            var enet = br.ReadInt32BE();
            br.ReadUInt32();
            switch (code)
            {
                case HandshakeConnectCode:
                case HandshakeConnectCodeAlt:
                    if (con != null)
                    {
                        Logger.Debug($"Duplicate handshake from {con.RemoteEndPoint}");
                        return;
                    }

                    await AcceptConnection(rcv, enet);
                    break;
                case HandshakeDisconnectCode:
                    if (con == null)
                    {
                        Logger.Debug($"Inexistent connection asked for disconnect from {rcv.RemoteEndPoint}");
                        return;
                    }

                    await SendDisconnectPacket(con, ClientDisconnectReasonCode);
                    break;
                default:
                    Logger.Debug($"Invalid handshake code received {code}");
                    return;
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Failed to handle handshake: {ex}");
        }
    }

    private static async Task AcceptConnection(UdpReceiveResult rcv, int enet)
    {
        March7thHoneyConnection? con;
        long convId;
        lock (ConnectionsLock)
        {
            var create = CreateConnection;
            var multiplex = Multiplex;
            if (create == null || multiplex == null) return;
            var config = ConfigManager.Config.GameServer;
            // Check limits and duplicates under the same lock as allocation/registration. Rejected
            // unauthenticated handshakes allocate no KCP queues and do not evict existing players.
            if (!KcpAdmissionPolicy.CanAccept(Connections.Values, rcv.RemoteEndPoint,
                    config.MaxConnections, config.MaxPendingConnections, config.MaxPendingConnectionsPerIp,
                    static c => c.RemoteEndPoint, static c => c.State == SessionStateEnum.ACTIVE))
                return;

            convId = Connections.GetNextAvailableIndex();
            var convo = multiplex.CreateConversation(convId, rcv.RemoteEndPoint, ConvOpt);
            if (convo == null) return;
            try
            {
                con = create(convo, rcv.RemoteEndPoint);
                if (con.ConversationId != convId)
                {
                    multiplex.UnregisterConversation(convId);
                    convo.Dispose();
                    return;
                }
                Connections.Add(con.ConversationId.Value, con);
                // Activation is explicit: the connection is registered (and therefore reachable for
                // lookups and disconnects) before its receive loop sees any traffic.
                con.Start();
            }
            catch
            {
                Connections.Remove(convId);
                multiplex.UnregisterConversation(convId);
                convo.Dispose();
                throw;
            }
        }

        Logger.Debug($"Accepting game handshake from {rcv.RemoteEndPoint}. conv_id={convId} enet={enet}");
        await SendHandshakeResponse(con, enet);
    }

    private static async Task SendHandshakeResponse(March7thHoneyConnection user, int enet)
    {
        if (user == null || UDPClient == null || !user.ConversationId.HasValue) return;
        var convId = user.ConversationId.Value;
        await using MemoryStream? ms = new();
        await using BinaryWriter? bw = new(ms);
        bw.WriteInt32BE(HandshakeResponseCode);
        bw.WriteConvID(convId);
        bw.WriteInt32BE(enet);
        bw.WriteInt32BE(HandshakeResponseTail);
        var data = ms.ToArray();
        await UDPClient.SendAsync(data, data.Length, user.RemoteEndPoint);
    }

    public static async Task SendDisconnectPacket(March7thHoneyConnection user, int code)
    {
        if (user == null || UDPClient == null || !user.ConversationId.HasValue) return;
        var convId = user.ConversationId.Value;
        await using MemoryStream? ms = new();
        await using BinaryWriter? bw = new(ms);
        bw.WriteInt32BE(HandshakeDisconnectCode);
        bw.WriteConvID(convId);
        bw.WriteInt32BE(code);
        bw.WriteInt32BE(HandshakeDisconnectTail);
        var data = ms.ToArray();
        await UDPClient.SendAsync(data, data.Length, user.RemoteEndPoint);
    }
}
