using System.Text.Json.Nodes;

internal static class MissionProbe
{
    // The two target records have the same presence masks and empty optional values as the
    // nearby reference missions. Reject other shapes instead of dropping unknown fields.
    public static JsonObject Decode(ReadOnlySpan<byte> bytes, int expectedMission)
    {
        var data = bytes;
        var mask = Next(ref data);
        if (mask is not (0x12ad or 0x22d)) throw new InvalidDataException($"Unsupported MissionInfo mask: {mask:x}");
        var id = Next(ref data);
        if (id != expectedMission) throw new InvalidDataException($"MissionInfo ID {id} does not match {expectedMission}");
        var node = new JsonObject
        {
            ["MainMissionID"] = id,
            ["StartSubMissionList"] = SingleId(ref data),
            ["FinishSubMissionList"] = SingleId(ref data),
            ["MissionCustomValueList"] = new JsonArray()
        };
        var extended = mask == 0x12ad;
        if (!data.SequenceEqual(extended ? "\0\0\0\0"u8 : "\0\0"u8))
            throw new InvalidDataException("Unsupported nonempty or trailing MissionInfo fields");
        if (extended) node["SubMissionRelationList"] = new JsonArray();
        node["IsShow"] = false;
        if (extended) node["AssistWayPointPackList"] = new JsonArray();
        return node;
    }

    private static JsonArray SingleId(ref ReadOnlySpan<byte> data)
    {
        if (Next(ref data) != 2) throw new InvalidDataException("Expected one MissionInfo list entry");
        return new JsonArray(JsonValue.Create(Next(ref data)));
    }

    private static uint Next(ref ReadOnlySpan<byte> data)
    {
        uint value = 0;
        for (int shift = 0; shift <= 28 && !data.IsEmpty; shift += 7)
        {
            var b = data[0];
            data = data[1..];
            if (shift == 28 && b > 15) break;
            value |= (uint)(b & 127) << shift;
            if (b < 128) return value;
        }
        throw new InvalidDataException("Invalid MissionInfo variable-length integer");
    }
}
