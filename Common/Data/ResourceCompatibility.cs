using System.Text.Json;
using System.Text.Json.Serialization;
using March7thHoney.Data.Excel;
using March7thHoney.Enums.Item;

namespace March7thHoney.Data;

internal static class ResourceCompatibility
{
    private static readonly Lazy<ResourceCompatibilityData> Data = new(Load);

    private static ResourceCompatibilityData Load()
    {
        using var stream = typeof(ResourceCompatibility).Assembly
            .GetManifestResourceStream("March7thHoney.ResourceCompatibility.json")
            ?? throw new InvalidOperationException("Embedded resource compatibility data is missing.");
        return JsonSerializer.Deserialize(stream, ResourceCompatibilityJsonContext.Default.ResourceCompatibilityData)
            ?? throw new InvalidDataException("Embedded resource compatibility data is empty.");
    }

    internal static bool ContainsOverlayKey(string memberName, int id) =>
        (memberName is nameof(GameDataSnapshot.AvatarSkinData) or nameof(GameDataSnapshot.ItemConfigData)) &&
        Data.Value.AvatarSkins.Any(row => row.SkinId == id);

    internal static void Apply(string? resourceVersion)
    {
        if (string.IsNullOrEmpty(resourceVersion)) return;

        foreach (var row in Data.Value.AvatarSkins)
        {
            if (!string.Equals(resourceVersion, row.ResourceVersion, StringComparison.Ordinal)) continue;
            if (!GameData.AvatarConfigData.ContainsKey(row.AvatarId)) continue;
            if (GameData.AvatarSkinData.TryGetValue(row.SkinId, out var skin) && skin.AvatarID != row.AvatarId)
                continue;
            if (GameData.ItemConfigData.TryGetValue(row.SkinId, out var item) &&
                (item.ItemMainType != row.ItemMainType || item.ItemSubType != row.ItemSubType))
                continue;

            GameData.AvatarSkinData.TryAdd(row.SkinId, new AvatarSkinExcel { ID = row.SkinId, AvatarID = row.AvatarId });
            GameData.ItemConfigData.TryAdd(row.SkinId, new ItemConfigExcel
            {
                ID = row.SkinId,
                ItemName = new HashName { Hash = row.ItemNameHash },
                ItemMainType = row.ItemMainType,
                ItemSubType = row.ItemSubType,
                Rarity = row.Rarity,
                PileLimit = row.PileLimit
            });
        }
    }
}

internal sealed class ResourceCompatibilityData
{
    public required AvatarSkinCompatibilityRow[] AvatarSkins { get; init; }
}

internal sealed class AvatarSkinCompatibilityRow
{
    public required string ResourceVersion { get; init; }
    public required int AvatarId { get; init; }
    public required int SkinId { get; init; }
    public required ulong ItemNameHash { get; init; }
    public required ItemMainTypeEnum ItemMainType { get; init; }
    public required ItemSubTypeEnum ItemSubType { get; init; }
    public required ItemRarityEnum Rarity { get; init; }
    public required int PileLimit { get; init; }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(ResourceCompatibilityData))]
internal partial class ResourceCompatibilityJsonContext : JsonSerializerContext;
