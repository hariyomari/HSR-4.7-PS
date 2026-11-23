using March7thHoney.Kcp;
using March7thHoney.Proto;
using Google.Protobuf;

namespace March7thHoney.GameServer.Server.Packet.Send.Player;

/// <summary>
///     Pushes a Lua chunk for the client to run. Every push carries a new version, counted up from the server's
///     start-up time.
/// </summary>
public class PacketClientDownloadDataScNotify : BasePacket
{
    private static long VersionCounter = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public PacketClientDownloadDataScNotify(byte[] lua) : base(CmdIds.ClientDownloadDataScNotify)
    {
        var downloadData = new ClientDownloadData
        {
            Data = ByteString.CopyFrom(lua),
            Version = unchecked((uint)Interlocked.Increment(ref VersionCounter)),
            Time = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        var notify = new ClientDownloadDataScNotify
        {
            DownloadData = downloadData
        };

        SetData(notify);
    }
}
