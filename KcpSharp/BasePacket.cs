using March7thHoney.Util;
using Google.Protobuf;

namespace March7thHoney.Kcp;

public class BasePacket(ushort cmdId)
{
    /// <summary>Magic word opening every game packet frame; validated on receive.</summary>
    public const uint HeaderMagic = 0x9d74c714;

    /// <summary>Magic word closing every game packet frame.</summary>
    public const uint TailMagic = 0xd7a152c8;

    /// <summary>Fixed part of a frame before the header payload: magic + cmdId + headerLen + payloadLen.</summary>
    public const int MinFrameBytes = 12;

    public ushort CmdId { get; set; } = cmdId;
    public byte[] Data { get; set; } = [];

    /// <summary>
    ///     Builds a packet that carries one already-built protobuf message under its command id, for responses and
    ///     notifies that need no packet-specific assembly.
    /// </summary>
    public static BasePacket Create(int cmdId, IMessage message)
    {
        var packet = new BasePacket((ushort)cmdId);
        packet.SetData(message);
        return packet;
    }

    public void SetData(byte[] data)
    {
        Data = data;
    }

    public void SetData(IMessage message)
    {
        Data = message.ToByteArray();
    }

    public void SetData(string base64)
    {
        SetData(Convert.FromBase64String(base64));
    }

    /// <param name="head">Serialized packet head (see <see cref="PacketHead" />); none when null.</param>
    public byte[] BuildPacket(byte[]? head = null)
    {
        using MemoryStream? ms = new();
        using BinaryWriter? bw = new(ms);

        head ??= [];
        bw.WriteUInt32BE(HeaderMagic);
        bw.WriteUInt16BE(CmdId);
        bw.WriteUInt16BE((ushort)head.Length);
        bw.WriteUInt32BE((uint)Data.Length);
        if (head.Length > 0) bw.Write(head);
        if (Data.Length > 0) bw.Write(Data);
        bw.WriteUInt32BE(TailMagic);

        var packet = ms.ToArray();

        return packet;
    }
}