namespace March7thHoney.Kcp;

/// <summary>
///     The protobuf head between a game packet's fixed header and its body (GOCBELPFLHO in the 4.8 dump). The client
///     writes each outgoing packet's id into field 1 (NetPacket.Serialize stores NetPacket._PacketID there). Requests
///     made through the client's awaitable send API (RPG.Client.PendingRspEntry, used by the 4.8 activities such as
///     DiceyDungeon and ActivityBangboo) are completed only by a reply whose head carries the same packet id; without
///     it the request times out after 300 s even though the reply arrived. Older modules match replies by command id
///     and ignore the head, and a reply whose id matches no pending request is dropped by the matcher, so echoing the
///     id on everything sent while a request is handled is safe for them.
/// </summary>
public static class PacketHead
{
    /// <summary>Packet id of the request being handled on the current async flow; 0 outside a request.</summary>
    public static readonly AsyncLocal<uint> RequestPacketId = new();

    /// <summary>Field 1 (packet id) of a head, or 0 when the head is empty, malformed or has none.</summary>
    public static uint ReadPacketId(ReadOnlySpan<byte> head)
    {
        var pos = 0;
        while (pos < head.Length)
        {
            if (!TryReadVarint(head, ref pos, out var tag)) return 0;
            var field = tag >> 3;
            switch ((int)(tag & 7))
            {
                case 0:
                    if (!TryReadVarint(head, ref pos, out var value)) return 0;
                    if (field == 1) return (uint)value;
                    break;
                case 1:
                    pos += 8;
                    break;
                case 2:
                    if (!TryReadVarint(head, ref pos, out var length) || length > (ulong)(head.Length - pos)) return 0;
                    pos += (int)length;
                    break;
                case 5:
                    pos += 4;
                    break;
                default:
                    return 0;
            }
        }

        return 0;
    }

    /// <summary>A head holding only the packet id (field 1).</summary>
    public static byte[] Encode(uint packetId)
    {
        var bytes = new List<byte>(6) { 0x08 };
        var value = packetId;
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }

        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int pos, out ulong value)
    {
        value = 0;
        for (var shift = 0; shift < 64 && pos < data.Length; shift += 7)
        {
            var b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
        }

        return false;
    }
}
