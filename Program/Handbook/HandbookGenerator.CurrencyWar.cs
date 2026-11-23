using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using March7thHoney.Data;
using March7thHoney.Enums.GridFight;
using March7thHoney.GameServer.Game.GridFight;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using Newtonsoft.Json.Linq;

namespace March7thHoney.Program.Handbook;

// Since 4.6.51, tag each row by season so launcher searches distinguish reused avatar names.
// Resolve names missing from the cache against the Excel JSON next to the TextMap.
public static partial class HandbookGenerator
{
    private static readonly Regex RichTextTag = new("<[^>]+>", RegexOptions.Compiled);

    private static void GenerateCurrencyWar(StringBuilder builder, string lang, Dictionary<BigInteger, string> map,
        Dictionary<BigInteger, string> fallback)
    {
        var text = new CurrencyWarText(lang, map, fallback);
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarRole",
            () => GenerateCurrencyWarRole(builder, text));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarTrait",
            () => GenerateCurrencyWarTrait(builder, text));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarEquipment",
            () => GenerateCurrencyWarItems(builder, text, GameData.GridFightEquipmentData.Values
                .Select(row => (row.ID, (string?)row.EquipCategory.ToString()))));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarConsumable",
            () => GenerateCurrencyWarItems(builder, text, GameData.GridFightConsumablesData.Values
                .Select(row => (row.ID, (string?)null))));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarOrb",
            () => GenerateCurrencyWarOrb(builder, text));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarPortal",
            () => GenerateCurrencyWarPortal(builder, text));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarAugment",
            () => GenerateCurrencyWarAugment(builder, text));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarPetPal",
            () => GenerateCurrencyWarPetPal(builder, text));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarDivision",
            () => GenerateCurrencyWarDivision(builder, text));
        AppendSection(builder, lang, "Server.ServerInfo.HandbookSectionCurrencyWarRoute",
            () => GenerateCurrencyWarRoute(builder, text));
    }

    private static void AppendSection(StringBuilder builder, string lang, string titleKey, Action body)
    {
        builder.AppendLine();
        builder.AppendLine("#" + I18NManager.TranslateAsCertainLang(lang, titleKey));
        builder.AppendLine();
        body();
    }

    private static void GenerateCurrencyWarRole(StringBuilder builder, CurrencyWarText text)
    {
        var traitNames = text.NamesById("GridFightTraitBasicInfo", "ID", "TraitName");
        foreach (var role in GameData.GridFightRoleBasicInfoData.Values
                     .OrderBy(row => row.SeasonID).ThenBy(row => row.Rarity).ThenBy(row => row.ID))
        {
            if (!GameData.AvatarConfigData.TryGetValue((int)role.AvatarID, out var avatar)) continue;
            var tags = new List<string> { SeasonTag(role.SeasonID, text), text.T("HandbookCurrencyWarCost", role.Rarity) };
            if (role.TraitList.Count > 0)
                tags.Add(text.T("HandbookCurrencyWarTraits") + ": " +
                         string.Join("/", role.TraitList.Select(id => traitNames.GetValueOrDefault(id, id.ToString()))));
            if (!role.IsInPool) tags.Add(text.T("HandbookCurrencyWarNotInPool"));
            builder.AppendLine($"{role.ID}: {text.Name(avatar.AvatarName.Hash)} [{string.Join(" · ", tags)}]");
        }
    }

    private static void GenerateCurrencyWarTrait(StringBuilder builder, CurrencyWarText text)
    {
        foreach (var row in text.Rows("GridFightTraitBasicInfo").OrderBy(row => Int(row, "SeasonID")).ThenBy(row => Int(row, "ID")))
        {
            var tags = new List<string> { SeasonTag(Int(row, "SeasonID"), text) };
            if (row.Value<string>("TraitType") is { Length: > 0 } type) tags.Add(text.T("HandbookCurrencyWarTrait" + type));
            builder.AppendLine($"{Int(row, "ID")}: {text.Name(row, "TraitName")} [{string.Join(" · ", tags)}]");
        }
    }

    private static void GenerateCurrencyWarItems(StringBuilder builder, CurrencyWarText text,
        IEnumerable<(uint Id, string? Category)> items)
    {
        foreach (var (id, category, seasons) in items
                     .Select(item => (item.Id, item.Category, Seasons: GameData.GridFightSeasonItemData.GetValueOrDefault(item.Id) ?? []))
                     .OrderBy(item => item.Seasons.Count == 0 ? uint.MaxValue : item.Seasons.Min())
                     .ThenBy(item => item.Id))
        {
            if (!GameData.GridFightItemsData.TryGetValue(id, out var item)) continue;
            var tags = new List<string> { SeasonsTag(seasons, text) };
            if (category != null) tags.Add(category);
            builder.AppendLine($"{id}: {text.Name(item.ItemName.Hash)} [{string.Join(" · ", tags)}]");
        }
    }

    private static void GenerateCurrencyWarOrb(StringBuilder builder, CurrencyWarText text)
    {
        // Only a handful of GridFightOrb rows carry an OrbName; the rest are named after their orb type.
        foreach (var orb in GameData.GridFightOrbData.Values.OrderBy(row => row.OrbID))
        {
            var typeName = text.T("HandbookCurrencyWarOrb" + orb.Type);
            var bonus = $"{text.T("HandbookCurrencyWarBonus")} {orb.BonusID}";
            builder.AppendLine(orb.OrbName.Hash != 0 && text.HasName(orb.OrbName.Hash)
                ? $"{orb.OrbID}: {text.Name(orb.OrbName.Hash)} [{typeName} · {bonus}]"
                : $"{orb.OrbID}: {typeName} [{bonus}]");
        }
    }

    private static void GenerateCurrencyWarPortal(StringBuilder builder, CurrencyWarText text)
    {
        var seasonsById = InvertSeasonLists(GameData.GridFightSeasonPortalData);
        foreach (var row in text.Rows("GridFightPortalBuff").OrderBy(row => Int(row, "ID")))
        {
            var id = Int(row, "ID");
            builder.AppendLine($"{id}: {text.Name(row, "PortalBuffTitle")} [{SeasonsTag(seasonsById.GetValueOrDefault(id) ?? [], text)}]");
        }
    }

    private static void GenerateCurrencyWarAugment(StringBuilder builder, CurrencyWarText text)
    {
        var seasonsById = InvertSeasonLists(GameData.GridFightSeasonAugmentData);
        foreach (var row in text.Rows("GridFightAugment").OrderBy(row => Int(row, "ID")))
        {
            var id = Int(row, "ID");
            var tags = new List<string> { SeasonsTag(seasonsById.GetValueOrDefault(id) ?? [], text) };
            if (GameData.GridFightAugmentData.TryGetValue(id, out var augment) &&
                augment.Quality != GridFightAugmentQualityEnum.None)
                tags.Add(text.T("HandbookCurrencyWarQuality" + augment.Quality));
            builder.AppendLine($"{id}: {text.Name(row, "HexName")} [{string.Join(" · ", tags)}]");
        }
    }

    // Partners exist from season 2 on; GridFightPetPalEff carries the effects (SwitchDesc is the in-run effect).
    private static void GenerateCurrencyWarPetPal(StringBuilder builder, CurrencyWarText text)
    {
        var effects = text.Rows("GridFightPetPalEff").ToDictionary(row => Int(row, "ID"));
        var partnerLabel = text.T("HandbookCurrencyWarPartner");
        foreach (var partner in text.Rows("GridFightPetPal").OrderBy(row => Int(row, "ID")))
        {
            var id = Int(partner, "ID");
            var name = text.Name(partner, "Name");
            var effectIds = (partner["EffIDList"] as JArray)?.Select(token => token.Value<uint>()).ToList() ?? [];
            builder.AppendLine($"{id}: {name} [{partnerLabel} · {text.T("HandbookCurrencyWarEffects")}: " +
                               $"{(effectIds.Count == 0 ? "-" : string.Join("/", effectIds))}]");
            foreach (var effectId in effectIds)
            {
                var effect = effects.GetValueOrDefault(effectId);
                var description = effect == null ? "" : text.Name(effect, "SwitchDesc");
                builder.AppendLine($"  {effectId}: {description} [{partnerLabel} {id} {name}]");
            }
        }
    }

    // GridFightDivisionInfo rarely names its rows; the per-tier name comes from GridFightDivisionLevelShow, whose
    // "#1" placeholder is the progress inside the tier.
    private static void GenerateCurrencyWarDivision(StringBuilder builder, CurrencyWarText text)
    {
        var tierNames = text.Rows("GridFightDivisionLevelShow")
            .GroupBy(row => (Int(row, "SeasonID"), Int(row, "DivisionLevel")))
            .ToDictionary(group => group.Key, group => text.Name(group.First(), "DivisionNameWithNum"));
        foreach (var row in text.Rows("GridFightDivisionInfo")
                     .OrderBy(row => Int(row, "SeasonID")).ThenBy(row => Int(row, "ID")))
        {
            var (season, level, progress) = (Int(row, "SeasonID"), Int(row, "DivisionLevel"), Int(row, "Progress"));
            var name = text.HasName(row, "DivisionName")
                ? text.Name(row, "DivisionName")
                : tierNames.GetValueOrDefault((season, level), "");
            name = name.Replace("#1", progress.ToString());
            var tags = new List<string>
            {
                SeasonTag(season, text),
                level == 0 ? text.T("HandbookCurrencyWarTutorial") : text.T("HandbookCurrencyWarDivisionLevel", level, progress),
            };
            if (row.Value<bool?>("IsPromotion") == true) tags.Add(text.T("HandbookCurrencyWarPromotion"));
            builder.AppendLine($"{Int(row, "ID")}: {name} [{string.Join(" · ", tags)}]");
        }
    }

    private static void GenerateCurrencyWarRoute(StringBuilder builder, CurrencyWarText text)
    {
        var seasons = GameData.GridFightDivisionInfoData.Values.Select(row => row.SeasonID).Where(id => id > 0)
            .Distinct().Order().ToList();
        foreach (var (routeId, rows) in GameData.GridFightStageRouteData.OrderBy(pair => pair.Key))
        {
            var routeSeasons = seasons.Where(season => GridFightSeasonScope.IsRouteInSeason(routeId, season)).ToList();
            var tags = new List<string>
            {
                routeSeasons.Count > 0 ? SeasonsTag(routeSeasons, text) : text.T("HandbookCurrencyWarTutorialRoute"),
                text.T("HandbookCurrencyWarNodes", rows.Count),
            };
            var nodes = rows.Values.OrderBy(row => row.ChapterID).ThenBy(row => row.SectionID).Select(row =>
            {
                var template = GameData.GridFightNodeTemplateData.GetValueOrDefault(row.NodeTemplateID);
                var label = template == null ? "?" : text.T("HandbookCurrencyWarNode" + template.NodeType);
                if (template is { IsAugment: not 0 }) label += "+" + text.T("HandbookCurrencyWarAugmentNode");
                return $"{row.ChapterID}-{row.SectionID} {label}";
            });
            builder.AppendLine($"{routeId}: [{string.Join(" · ", tags)}] {string.Join(" · ", nodes)}");
        }
    }

    private static string SeasonTag(uint season, CurrencyWarText text)
    {
        return season == 0 ? text.T("HandbookCurrencyWarShared") : $"S{season}";
    }

    private static string SeasonsTag(IReadOnlyCollection<uint> seasons, CurrencyWarText text)
    {
        return seasons.Count == 0 ? text.T("HandbookCurrencyWarShared") : string.Join("/", seasons.Order().Select(id => $"S{id}"));
    }

    private static Dictionary<uint, List<uint>> InvertSeasonLists(Dictionary<uint, List<uint>> idsBySeason)
    {
        var seasonsById = new Dictionary<uint, List<uint>>();
        foreach (var (season, ids) in idsBySeason)
        foreach (var id in ids)
        {
            if (!seasonsById.TryGetValue(id, out var seasons)) seasonsById[id] = seasons = [];
            if (!seasons.Contains(season)) seasons.Add(season);
        }
        return seasonsById;
    }

    private static uint Int(JObject row, string field)
    {
        return row.Value<uint?>(field) ?? 0;
    }

    private sealed class CurrencyWarText(
        string lang,
        Dictionary<BigInteger, string> map,
        Dictionary<BigInteger, string> fallback)
    {
        private readonly Dictionary<string, List<JObject>> _tables = [];

        public string T(string key, params object[] args)
        {
            return I18NManager.TranslateAsCertainLang(lang, "Server.ServerInfo." + key,
                args.Select(arg => arg.ToString() ?? "").ToArray());
        }

        public string Name(BigInteger hash)
        {
            return Clean(GetNameFromTextMap(hash, map, fallback));
        }

        public bool HasName(BigInteger hash)
        {
            return map.ContainsKey(hash) || fallback.ContainsKey(hash);
        }

        public bool HasName(JObject row, string field)
        {
            return Hash(row, field) is { } hash && HasName(hash);
        }

        public string Name(JObject row, string field)
        {
            return Hash(row, field) is { } hash ? Name(hash) : "";
        }

        public Dictionary<uint, string> NamesById(string table, string idField, string nameField)
        {
            return Rows(table).GroupBy(row => Int(row, idField))
                .ToDictionary(group => group.Key, group => Name(group.First(), nameField));
        }

        // Rows of ExcelOutput/<table>.json from the configured Excel directories; empty when the table is absent.
        public IReadOnlyList<JObject> Rows(string table)
        {
            if (_tables.TryGetValue(table, out var rows)) return rows;
            rows = [];
            var path = ConfigManager.Config.Path.ExcelOutputDirs
                .Select(directory => Path.Combine(ConfigManager.Config.Path.ResourcePath, directory, table + ".json"))
                .FirstOrDefault(File.Exists);
            if (path != null)
            {
                var token = JToken.Parse(File.ReadAllText(path));
                rows = (token is JArray array ? array.Children() : token.Children().Select(child => child.First!))
                    .OfType<JObject>()
                    .ToList();
            }
            _tables[table] = rows;
            return rows;
        }

        private static BigInteger? Hash(JObject row, string field)
        {
            if (row[field] is not JObject name) return null;
            var value = name["Hash"] ?? name["Hash64"];
            return value != null && BigInteger.TryParse(value.ToString(), out var hash) ? hash : null;
        }

        private static string Clean(string value)
        {
            return RichTextTag.Replace(value, "").Replace("\\n", " ").Replace("\n", " ").Trim();
        }
    }
}
