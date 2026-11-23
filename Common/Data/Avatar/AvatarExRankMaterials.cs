using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using March7thHoney.Enums.Item;
using March7thHoney.Data.Excel;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Data.Avatar;

/// <summary>
/// ExRank (奇缘星魂) material/configuration overlay from Config/Custom/AvatarExRankMaterials.json.
/// Activation (CmdID 400, field 5 = ExRank config ID; response 395, field 2 = retcode) and the
/// activated-ID fields were checked against the 4.6.53 client by the contributor.
/// This overlay is not part of MemoryPack's Resource.bin or player save layouts.
/// </summary>
public static class AvatarExRankMaterials
{
    private static readonly Logger Logger = new("AvatarExRank");
    public static IReadOnlyDictionary<int, AvatarExRankDefinition> Definitions { get; private set; } =
        new ReadOnlyDictionary<int, AvatarExRankDefinition>(new Dictionary<int, AvatarExRankDefinition>());
    private static HashSet<int> _materialIds = [];

    public static bool IsMaterial(int itemId) => _materialIds.Contains(itemId);

    public static void Apply()
    {
        var path = Path.Combine(ConfigManager.Config.Path.ConfigPath, "Custom", "AvatarExRankMaterials.json");
        if (!File.Exists(path))
        {
            Logger.Warn(I18NManager.Translate("Server.Log.ExRankConfigMissing", path));
            return;
        }
        try
        {
            ApplyDocument(JObject.Parse(File.ReadAllText(path)));
            Logger.Debug($"Loaded {_materialIds.Count} ExRank materials and {Definitions.Count} ExRank definitions.");
        }
        catch (Exception error)
        {
            // The staged document is applied only after full validation, so a bad file leaves ExRank disabled.
            Logger.Error(I18NManager.Translate("Server.Log.ExRankConfigInvalid", path), error);
        }
    }

    public static void ApplyDocument(JObject document)
    {
        var materials = document["Materials"] as JArray ?? throw new InvalidDataException("Missing ExRank materials");
        var ranks = document["Ranks"] as JArray ?? throw new InvalidDataException("Missing ExRank configuration");
        var names = document["MaterialNames"] as JObject ?? throw new InvalidDataException("Missing ExRank material names");
        Dictionary<int, ItemConfigExcel> stagedItems = [];
        Dictionary<int, AvatarExRankDefinition> stagedRanks = [];
        foreach (var material in materials.OfType<JObject>())
        {
            if (material.Value<string>("ItemMainType") != "Material" ||
                material.Value<string>("ItemSubType") != "AvatarExRankMaterial" ||
                material.Value<string>("Rarity") != "SuperRare" || material.Value<int>("PileLimit") != 1)
                throw new InvalidDataException("Unsupported ExRank material configuration");
            // Only the *name* of the new client subtype has been exported, not its numeric enum value.
            // Use existing generic-material storage internally; keep the exact original subtype in JSON.
            // Item sync transmits item ID/count, not this internal subtype. Do not guess a wire enum.
            // Built field by field: JToken.ToObject would need the generic SafeStringEnumConverter
            // instantiations, which the NativeAOT build does not contain on a cache boot.
            var nameHash = material["ItemName"]?["Hash64"] ?? material["ItemName"]?["Hash"];
            var item = new ItemConfigExcel
            {
                ID = material.Value<int>("ID"),
                ItemName = new HashName
                    { Hash = nameHash == null ? BigInteger.Zero : BigInteger.Parse(nameHash.ToString(), CultureInfo.InvariantCulture) },
                ItemMainType = ItemMainTypeEnum.Material,
                ItemSubType = ItemSubTypeEnum.Material,
                Rarity = ItemRarityEnum.SuperRare,
                PileLimit = 1
            };
            item.Name = names.Value<string>(item.ID.ToString());
            if (item.ID <= 0 || string.IsNullOrWhiteSpace(item.Name) || !stagedItems.TryAdd(item.ID, item))
                throw new InvalidDataException("Invalid or duplicate ExRank item ID/name");
        }
        foreach (var rank in ranks.OfType<JObject>())
        {
            var id = rank.Value<int>("PHFMCACHFIJ");
            var avatarId = rank.Value<int>("ACCJKGEKHKP");
            var cost = rank["PKBNJLOLPBM"] as JObject ?? throw new InvalidDataException("Missing ExRank cost");
            var materialId = cost.Value<int>("ItemID");
            var count = cost.Value<int>("ItemNum");
            var abilityKeys = (rank["NNFEGHLLEPH"] as JArray)?.Select(key => key.Type == JTokenType.String ? key.Value<string>() ?? "" : "").ToArray() ?? [];
            if (id <= 0 || avatarId <= 0 || count != 1 || !stagedItems.ContainsKey(materialId) ||
                abilityKeys.Length == 0 || abilityKeys.Any(string.IsNullOrWhiteSpace) ||
                !stagedRanks.TryAdd(id, new AvatarExRankDefinition(id, avatarId, materialId, count,
                    Array.AsReadOnly(abilityKeys))))
                throw new InvalidDataException("Invalid or duplicate ExRank definition");
        }
        if (stagedItems.Count == 0 || stagedItems.Count != materials.Count || stagedRanks.Count != ranks.Count ||
            stagedRanks.Values.Select(rank => rank.MaterialId).Distinct().Count() != stagedItems.Count)
            throw new InvalidDataException("Incomplete ExRank configuration");

        // Validate the entire file before changing live definitions; replace only these material IDs.
        foreach (var (id, item) in stagedItems) GameData.ItemConfigData[id] = item;
        Definitions = new ReadOnlyDictionary<int, AvatarExRankDefinition>(stagedRanks);
        _materialIds = stagedItems.Keys.ToHashSet();
    }
}

public sealed record AvatarExRankDefinition(int Id, int AvatarId, int MaterialId, int MaterialCount,
    IReadOnlyList<string> AbilityKeys);
