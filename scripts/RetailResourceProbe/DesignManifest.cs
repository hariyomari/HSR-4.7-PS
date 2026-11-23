using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

internal sealed record DesignBlock(string Directory, string Digest, int Size);
internal readonly record struct DesignSlice(DesignBlock Block, int Offset, int Length);

// Retail Windows DesignV v4. Manifest fields are big-endian, unlike the SRMI pointer.
internal sealed record DesignManifest(string Layer, uint Revision, string Digest, int BlockCount,
    Dictionary<ulong, DesignSlice> Entries)
{
    public static DesignManifest Open(string game, string layer)
    {
        var root = Path.Combine(game, "StarRail_Data", layer, "DesignData", "Windows");
        var pointer = File.ReadAllBytes(Path.Combine(root, "M_DesignV.bytes"));
        if (pointer.Length != 66 || !pointer.AsSpan(0, 8).SequenceEqual("SRMI\0\x03\0\x01"u8))
            throw new InvalidDataException($"Unsupported {layer} DesignV pointer");

        var revision = BinaryPrimitives.ReadUInt32LittleEndian(pointer.AsSpan(24));
        Span<byte> digest = stackalloc byte[16];
        for (int i = 0; i < digest.Length; i += 4)
            BinaryPrimitives.WriteUInt32BigEndian(digest[i..], BinaryPrimitives.ReadUInt32LittleEndian(pointer.AsSpan(28 + i)));
        var hex = Convert.ToHexStringLower(digest);
        var bytes = File.ReadAllBytes(Path.Combine(root, $"DesignV_{hex}.bytes"));
        if ((ulong)bytes.Length != BinaryPrimitives.ReadUInt64LittleEndian(pointer.AsSpan(44)) ||
            !MD5.HashData(bytes).AsSpan().SequenceEqual(digest))
            throw new InvalidDataException($"{layer} manifest does not match its pointer");

        var cursor = new Cursor(bytes);
        if (cursor.Int32() != 255 || cursor.Int32() != 4) throw new InvalidDataException("Unsupported DesignV index version");
        int blocks = cursor.Int32(), count = cursor.Int32();
        if (blocks < 0 || count < 0 || count > bytes.Length / 16) throw new InvalidDataException("Invalid DesignV counts");
        var entries = new Dictionary<ulong, DesignSlice>(count);
        for (int i = 0; i < blocks; i++)
        {
            cursor.Take(8); // block name hash, not a resource path
            var blockHash = Convert.ToHexStringLower(cursor.Take(16));
            var size = checked((int)cursor.UInt64());
            var n = cursor.Int32();
            if (n < 0 || n > count - entries.Count) throw new InvalidDataException("Invalid block entry count");
            var slices = new Cursor(cursor.Take(checked(n * 16)));
            var locale = Encoding.ASCII.GetString(cursor.Take(cursor.UInt16()));
            if (locale.Any(c => c is < 'a' or > 'z') || cursor.Take(1)[0] != 128)
                throw new InvalidDataException("Unsupported block descriptor");
            var block = new DesignBlock(Path.Combine(root, locale), blockHash, size);
            for (int j = 0; j < n; j++)
            {
                var key = slices.UInt64();
                int length = slices.Int32(), offset = slices.Int32();
                if (length < 0 || offset < 0 || offset > size - length || !entries.TryAdd(key, new(block, offset, length)))
                    throw new InvalidDataException($"Invalid or duplicate slice {key:x16} in {layer}");
            }
        }
        if (!cursor.Done || entries.Count != count) throw new InvalidDataException("DesignV totals disagree");
        return new(layer, revision, hex, blocks, entries);
    }

    private ref struct Cursor(ReadOnlySpan<byte> data)
    {
        private ReadOnlySpan<byte> rest = data;
        public readonly bool Done => rest.IsEmpty;
        public ReadOnlySpan<byte> Take(int size)
        {
            if ((uint)size > (uint)rest.Length) throw new InvalidDataException("Truncated DesignV index");
            var value = rest[..size];
            rest = rest[size..];
            return value;
        }
        public int Int32() => BinaryPrimitives.ReadInt32BigEndian(Take(4));
        public ushort UInt16() => BinaryPrimitives.ReadUInt16BigEndian(Take(2));
        public ulong UInt64() => BinaryPrimitives.ReadUInt64BigEndian(Take(8));
    }
}

internal sealed class BlockReader
{
    private readonly HashSet<string> verified = new(StringComparer.Ordinal);

    public byte[] Read(DesignSlice slice)
    {
        var file = Path.Combine(slice.Block.Directory, slice.Block.Digest + ".bytes");
        using var stream = File.OpenRead(file);
        if (!verified.Contains(file))
        {
            if (stream.Length != slice.Block.Size || Convert.ToHexStringLower(MD5.HashData(stream)) != slice.Block.Digest)
                throw new InvalidDataException($"Content hash or size mismatch: {file}");
            verified.Add(file);
        }
        stream.Position = slice.Offset;
        byte[] bytes = GC.AllocateUninitializedArray<byte>(slice.Length);
        stream.ReadExactly(bytes);
        return bytes;
    }
}
