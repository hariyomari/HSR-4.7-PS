using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Tables
{
    private const string Bonus = "GridFightBasicBonusPoolV2";
    private const string Trait = "GridFightTraitEffect";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static JsonObject Analyze(string tableName, ReadOnlySpan<byte> payload, string referenceJsonPath)
    {
        bool bonus = tableName switch
        {
            Bonus => true,
            Trait => false,
            _ => throw new ArgumentOutOfRangeException(nameof(tableName), tableName, "No verified wire layout")
        };
        string key = bonus ? "BonusID" : "ID";
        string enumField = bonus ? "BonusType" : "TraitEffectType";
        var issues = new JsonArray();
        var result = new JsonObject
        {
            ["table"] = tableName,
            ["rowsSource"] = "Retail manifest slice only. Absent field bits are omitted; enum fields contain signed numeric wire values.",
            ["payloadSha256"] = Convert.ToHexStringLower(SHA256.HashData(payload)),
            ["referenceJsonPath"] = Path.GetFullPath(referenceJsonPath),
            ["enumNameSource"] = "Names are not stored in the payload. The fixed numeric-to-name concordance used for comparison was inferred by matching IDs against TurnBasedGameData dad9155eb00fdeda31fb8456507c53590d49071f. It is not inferred anew from the comparison input or taken from a server enum.",
            ["concordanceRetailPayloadSha256"] = bonus ? "1f23a4b7f9e9fa6f88868628ddd4228678d350f6718eb44c53bdeb171746f496" : "5b7960b4f2c9f1b28c986073510046440928ce562b01d08a3296fcdc33d6e5b1",
            ["concordanceReferenceSha256"] = bonus ? "75e3323652ebf5cc6ea65a5c0617c9c8057f6517edc1bb9ef1716e99eae546f4" : "95fd37e02ea86c9b406a56afe4d6e8dfb73a0ec1a6736c416a2b122a6aed91c4",
            ["matchesReference"] = false,
            ["issues"] = issues
        };
        JsonArray rows;
        try
        {
            rows = Decode(payload, bonus);
        }
        catch (InvalidDataException ex)
        {
            result["decodeStatus"] = "failed";
            issues.Add(ex.Message);
            return result;
        }
        result["decodeStatus"] = "complete";
        result["retailRowCount"] = rows.Count;
        result["rows"] = rows;
        var evidence = new SortedDictionary<int, (uint FirstId, int Count)>();
        foreach (JsonObject row in rows.Cast<JsonObject>())
        {
            if (row[enumField] is not JsonValue value) continue;
            int number = value.GetValue<int>();
            evidence[number] = evidence.TryGetValue(number, out var prior)
                ? (prior.FirstId, prior.Count + 1) : (row[key]!.GetValue<uint>(), 1);
        }
        var enumEvidence = new JsonArray();
        foreach (var (number, count) in evidence)
            enumEvidence.Add(new JsonObject { ["retailValue"] = number, ["exampleRetailId"] = count.FirstId,
                ["retailRowCount"] = count.Count, ["inferredName"] = EnumName(bonus, number) });
        result["enumEvidence"] = enumEvidence;

        try
        {
            byte[] reference = File.ReadAllBytes(referenceJsonPath);
            result["referenceSha256"] = Convert.ToHexStringLower(SHA256.HashData(reference));
            ReadOnlySpan<byte> json = reference;
            if (json.StartsWith(Encoding.UTF8.Preamble)) json = json[Encoding.UTF8.Preamble.Length..];
            if (JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false }) is not JsonArray expected)
                throw new InvalidDataException("Expected a reference JSON array.");
            result["referenceRowCount"] = expected.Count;
            Compare(rows, expected, bonus, key, enumField, issues);
            result["matchesReference"] = issues.Count == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            issues.Add($"Reference comparison failed: {ex.Message}");
        }
        return result;
    }

    private static JsonArray Decode(ReadOnlySpan<byte> payload, bool bonus)
    {
        var reader = new Reader(payload);
        if (reader.UInt() != 0) throw reader.Error("expected leading zero");
        int count = reader.Count();
        var rows = new JsonArray();
        var seen = new HashSet<uint>();
        for (int i = 0; i < count; i++)
        {
            uint bits = reader.UInt();
            uint supported = bonus ? 0x1fu : 0x1bu;
            if ((bits & ~supported) != 0) throw reader.Error($"row {i}: unsupported field mask 0x{bits:x} (supported bits 0x{supported:x})");
            if ((bits & 1) == 0) throw reader.Error($"row {i}: missing primary ID bit");
            uint id = reader.UInt();
            if (!seen.Add(id)) throw reader.Error($"row {i}: duplicate retail ID {id}");
            var row = new JsonObject();
            if (bonus)
            {
                row["BonusID"] = id;
                if ((bits & 2) != 0) row["Value"] = reader.UInt();
                if ((bits & 4) != 0) row["BonusType"] = reader.Signed();
                if ((bits & 8) != 0) row["BonusTypeParam"] = reader.UInt();
                if ((bits & 16) != 0)
                {
                    var list = new JsonArray();
                    int n = reader.Count();
                    for (int j = 0; j < n; j++) list.Add(reader.UInt());
                    row["BonusTypeParamList"] = list;
                }
            }
            else
            {
                row["ID"] = id;
                if ((bits & 2) != 0) row["TraitEffectType"] = reader.Signed();
                if ((bits & 8) != 0) row["TraitEffectJson"] = reader.String();
                if ((bits & 16) != 0) row["TraitEffectIconPath"] = reader.String();
            }
            rows.Add(row);
        }
        if (reader.Remaining != 0) throw reader.Error($"{reader.Remaining} unconsumed bytes");
        return rows;
    }

    private static void Compare(JsonArray rows, JsonArray reference, bool bonus, string key, string enumField, JsonArray issues)
    {
        var byId = new Dictionary<uint, JsonObject>(reference.Count);
        for (int i = 0; i < reference.Count; i++)
        {
            if (reference[i] is not JsonObject entry || entry[key] is not JsonValue value || !value.TryGetValue<uint>(out var id))
                issues.Add($"Reference row {i} lacks a valid unsigned {key}.");
            else if (!byId.TryAdd(id, entry))
                issues.Add($"Duplicate reference {key} {id}.");
        }
        foreach (JsonObject row in rows.Cast<JsonObject>())
        {
            uint id = row[key]!.GetValue<uint>();
            if (!byId.Remove(id, out var expected))
            {
                issues.Add($"Retail {key} {id} absent from reference.");
                continue;
            }
            foreach (var (field, wire) in row)
            {
                if (!expected.TryGetPropertyValue(field, out var other))
                {
                    issues.Add($"{key} {id}.{field}: retail {wire?.ToJsonString()}, absent in reference.");
                    continue;
                }
                if (field == enumField && other is JsonValue label && label.TryGetValue<string>(out var name))
                {
                    int number = wire!.GetValue<int>();
                    string? inferred = EnumName(bonus, number);
                    if (name != inferred) issues.Add($"{key} {id}.{field}: retail {number} ({inferred ?? "unknown name"}), reference {name}.");
                }
                else if (!JsonNode.DeepEquals(wire, other))
                    issues.Add($"{key} {id}.{field}: retail {wire?.ToJsonString()}, reference {other?.ToJsonString() ?? "null"}.");
            }
            foreach (var (field, other) in expected)
                if (!row.ContainsKey(field)) issues.Add($"{key} {id}.{field}: absent in retail, reference {other?.ToJsonString() ?? "null"}.");
        }
        foreach (var id in byId.Keys) issues.Add($"Reference {key} {id} absent from retail.");
    }

    // Only names with aligned IDs in the pinned reference/slice pair above are known.
    // A missing field stays absent; it is not evidence of a named enum member with value zero.
    private static string? EnumName(bool bonus, int number) => bonus ? number switch
    {
        2 => "Refresh", 5 => "SpecificAvatar", 6 => "RandomAvatar", 7 => "Item", 8 => "Exp", 9 => "Orb",
        13 => "RandomEquipByCategory", 14 => "RandomEquipByFunc", 16 => "SpecificAvatarWithEquip",
        17 => "SpecificAvatarWithRandomEquip", 18 => "SpotLightReplaceCnt", 20 => "SpotLightFreeRefreshCnt",
        21 => "TrailblazeShopCoin", 22 => "ElationGoods", 23 => "GlobalHp", 24 => "ElationItem", 25 => "AllAvatarByRarity",
        _ => null
    } : number switch
    {
        1 => "TempEquip", 2 => "TraitBonus", 5 => "CoreRoleChoose", 6 => "SPSlot", 7 => "CoreRoleByEquipNum",
        8 => "TraitEffectByJson", 9 => "SelectEnhance", 10 => "GainFrontTrait", 11 => "BackStrengthenFront",
        12 => "Elation", 13 => "HolyGrailPray", 14 => "PartnerRoleChoose", 15 => "ChooseTraitByModule",
        16 => "TempEquipFromTemplate", 17 => "TraitMemberLink", 18 => "PhanionUnique", 20 => "Astropolis",
        21 => "UpgradeBasicEquipBeforeBattle", 22 => "ComboSlot", 23 => "Trailblaze", 24 => "MoonGame",
        25 => "MoonGameSpotLight", 26 => "ElationLaughPoint", 27 => "Chimera", 28 => "DanHengTwoInOne",
        29 => "JadeShop", 30 => "ChooseTraitByManual", 31 => "ClaraEnhance", 32 => "AdditionalTraitRoleChoose",
        33 => "AhaUnique", 34 => "GainOtherRoleTrait", _ => null
    };

    private ref struct Reader(ReadOnlySpan<byte> source)
    {
        private readonly ReadOnlySpan<byte> data = source;
        private int offset;
        public int Remaining => data.Length - offset;
        public InvalidDataException Error(string message) => new($"Retail slice near byte {offset}: {message}.");

        public uint UInt()
        {
            uint value = 0;
            for (int shift = 0; shift <= 28; shift += 7)
            {
                if (Remaining == 0) throw Error("truncated varint");
                byte b = data[offset++];
                if (shift == 28 && b > 15) break;
                value |= (uint)(b & 127) << shift;
                if (b < 128) return value;
            }
            throw Error("varint exceeds 32 bits");
        }

        public int Signed()
        {
            uint n = UInt();
            return (int)(n >> 1) ^ -(int)(n & 1);
        }

        public int Count()
        {
            int n = Signed();
            if (n < 0 || n > Remaining) throw Error($"invalid element count {n}");
            return n;
        }

        public string String()
        {
            uint length = UInt();
            if (length > Remaining) throw Error($"truncated {length}-byte string");
            try
            {
                string s = Utf8.GetString(data.Slice(offset, (int)length));
                offset += (int)length;
                return s;
            }
            catch (DecoderFallbackException) { throw Error("invalid UTF-8"); }
        }
    }
}
