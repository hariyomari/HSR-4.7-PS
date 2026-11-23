using System.Buffers.Binary;

namespace March7thHoney.Kcp;

/// <summary>One game packet frame as read off the wire: command id, protobuf head (see <see cref="PacketHead" />) and body.</summary>
public readonly record struct PacketFrame(ushort CmdId, byte[] Head, byte[] Body);

/// <summary>
///     Splits a decrypted KCP message into the game packet frames <see cref="BasePacket.BuildPacket" /> writes: header
///     magic, cmdId, head length and body length (big-endian), then the head, the body and the tail magic.
/// </summary>
public static class PacketFrameReader
{
    /// <summary>
    ///     Reads the frame at <paramref name="offset" /> and moves the offset past its body. Returns false at the end of
    ///     the message (fewer than <see cref="BasePacket.MinFrameBytes" /> bytes left) and on a malformed frame, for
    ///     which <paramref name="error" /> says what is wrong.
    /// </summary>
    /// <remarks>
    ///     The tail magic is neither checked nor skipped. The client sends one frame per KCP message, so reading stops at
    ///     the four tail bytes; a second frame in the same message would be rejected for its magic.
    /// </remarks>
    public static bool TryRead(ReadOnlySpan<byte> message, ref int offset, out PacketFrame frame, out string? error)
    {
        frame = default;
        error = null;
        if (message.Length - offset < BasePacket.MinFrameBytes) return false;

        var magic = BinaryPrimitives.ReadUInt32BigEndian(message[offset..]);
        if (magic != BasePacket.HeaderMagic)
        {
            error = $"Bad Data Package Received: got 0x{magic:X}, expect 0x{BasePacket.HeaderMagic:X}";
            return false;
        }

        var cmdId = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 4)..]);
        var headLength = BinaryPrimitives.ReadUInt16BigEndian(message[(offset + 6)..]);
        var bodyLength = BinaryPrimitives.ReadUInt32BigEndian(message[(offset + 8)..]);

        // Check the claimed lengths against what the message holds before copying anything, so a forged length
        // cannot make the server allocate an arbitrarily large buffer.
        var headStart = offset + BasePacket.MinFrameBytes;
        long remaining = message.Length - headStart;
        if (headLength + (long)bodyLength > remaining)
        {
            error = $"Bad Data Package Received: header/payload length ({headLength}+{bodyLength}) exceeds remaining buffer ({remaining})";
            return false;
        }

        var bodyStart = headStart + headLength;
        frame = new PacketFrame(cmdId, message.Slice(headStart, headLength).ToArray(),
            message.Slice(bodyStart, (int)bodyLength).ToArray());
        offset = bodyStart + (int)bodyLength;
        return true;
    }
}
