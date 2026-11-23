using System.Buffers;
using System.Net;
using March7thHoney.Database;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server.ClientLua;
using March7thHoney.GameServer.Server.Packet;
using March7thHoney.Internationalization;
using March7thHoney.Kcp;
using March7thHoney.Kcp.KcpSharp;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server;

/// <summary>
///     A game client's connection: runs the receive loop and dispatches each request to its handler. Frame parsing,
///     the /debug packet script, the fallback replies and client Lua live in their own types.
/// </summary>
public class Connection : March7thHoneyConnection
{
    private static readonly Logger Logger = new("GameServer");

    public Connection(KcpConversation conversation, IPEndPoint remote) : base(conversation, remote)
    {
        ClientLua = new ClientLuaSession(this);
    }

    public PlayerInstance? Player { get; set; }

    /// <summary>Set when a new login of the same uid stops this session: that is not the player logging off.</summary>
    public bool ReplacedByLogin { get; set; }

    public CustomPacketQueue CustomPacketQueue { get; } = new();
    public ClientLuaSession ClientLua { get; }

    public override async void Start()
    {
        Logger.Debug($"New connection from {RemoteEndPoint}.");
        State = SessionStateEnum.WAITING_FOR_TOKEN;
        await ReceiveLoop();
    }

    public override void Stop()
    {
        if (!IsOnline) return;

        Player?.TrainCakeCatchManager?.LeaveSocialPlayRoom();
        Player?.OnLogout();
        March7thHoneyListener.UnregisterConnection(this);
        base.Stop();
        if (Player != null) AccountDeletion.OnSessionEnded(Player.Uid, ReplacedByLogin);
    }

    protected async Task ReceiveLoop()
    {
        try
        {
            while (!CancelToken.IsCancellationRequested)
            {
                KcpConversationReceiveResult result;
                try
                {
                    // WaitToReceiveAsync call completes when there is at least one message is received or the transport is closed.
                    result = await Conversation.WaitToReceiveAsync(CancelToken.Token);
                }
                catch (OperationCanceledException) when (CancelToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (result.TransportClosed)
                {
                    Logger.Debug("Connection was closed");
                    break;
                }

                var buffer = ArrayPool<byte>.Shared.Rent(result.BytesReceived);
                try
                {
                    // TryReceive should not return false here, unless the transport is closed.
                    // So we don't need to check for result.TransportClosed.
                    if (!Conversation.TryReceive(buffer, out result))
                    {
                        Logger.Debug("Failed to receive packet");
                        break;
                    }

                    await ProcessMessageAsync(buffer.AsMemory(0, result.BytesReceived));
                }
                catch (Exception ex)
                {
                    Logger.Debug("Packet parse error", ex);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(I18NManager.Translate("Server.Log.ConnectionCrashed", RemoteEndPoint.ToString()), ex);
        }

        Stop();
    }

    /// <summary>
    ///     Decrypts one KCP message and dispatches the game packet frames it holds. Called by reflection from
    ///     LoginSceneRegression, which feeds raw frames through it; keep the name and signature.
    /// </summary>
    private async Task ProcessMessageAsync(Memory<byte> data)
    {
        var gamePacket = data.ToArray();
        if (Crypto.PacketEncryptionEnabled)
            Crypto.Xor(gamePacket, XorKey!);

        try
        {
            var offset = 0;
            string? error;
            while (PacketFrameReader.TryRead(gamePacket, ref offset, out var frame, out error))
            {
                LogPacket("Recv", frame.CmdId, frame.Body);
                await HandlePacket(frame.CmdId, frame.Head, frame.Body);
            }

            if (error != null) Logger.Debug(error);
        }
        catch (Exception e)
        {
            Logger.Debug(e.Message, e);
        }
    }

    private async Task HandlePacket(ushort opcode, byte[] header, byte[] payload)
    {
        // Scoped to this request: everything sent while handling it echoes its packet id (see PacketHead).
        PacketHead.RequestPacketId.Value = PacketHead.ReadPacketId(header);
        if (Player != null && DatabaseHelper.IsUidDeleted(Player.Uid))
        {
            Stop();
            return;
        }

        if (CustomPacketQueue.TryAdvance(LogMap.GetValueOrDefault(opcode), out var scripted, out var interrupt))
        {
            foreach (var action in scripted)
                await SendPacket(CustomPacketQueue.ToPacket(action));
            if (interrupt) return;
        }

        var handler = HandlerManager.GetHandler(opcode);
        if (handler == null)
        {
            await UnhandledRequestResponder.SendEmptyResponseAsync(this, opcode);
            return;
        }

        // Make sure the session is ready for this packet.
        switch (State)
        {
            case SessionStateEnum.WAITING_FOR_TOKEN when opcode != CmdIds.PlayerGetTokenCsReq:
            case SessionStateEnum.WAITING_FOR_LOGIN when opcode != CmdIds.PlayerLoginCsReq:
            case SessionStateEnum.ACTIVE
                when opcode == CmdIds.PlayerGetTokenCsReq || opcode == CmdIds.PlayerLoginCsReq:
                return;
        }

        try
        {
            await handler.OnHandle(this, header, payload);
        }
        catch (Exception e)
        {
            Logger.Error(I18NManager.Translate("Server.Log.PacketHandlerFailed", LogMap.GetValueOrDefault(opcode, opcode.ToString())), e);
            await UnhandledRequestResponder.SendRetFailAsync(this, opcode, payload);
        }
    }

    // The 4.x client repaints its version/watermark overlay after the SetClientPaused answer lands.
    protected override async Task OnPacketSentAsync(BasePacket packet)
    {
        if (packet.CmdId == CmdIds.SetClientPausedScRsp)
            await ClientLua.SendClientUiAsync();
    }
}
