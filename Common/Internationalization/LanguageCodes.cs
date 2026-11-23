using March7thHoney.Proto;

namespace March7thHoney.Internationalization;

/// <summary>
///     The single table relating the client <see cref="LanguageType" />, the server language-file code
///     (Config/Languages/{code}.json), the UI cultures the first-run default is derived from, and the alternative
///     spellings under which a language appears in resource trees.
/// </summary>
public static class LanguageCodes
{
    private const string DefaultCode = "EN";

    // the client names simplified Chinese "CN" in its text maps; the server and older res trees name it "CHS"
    private sealed record Entry(string Code, LanguageType Type, string? Culture, string[] Aliases);

    private static readonly Entry[] Entries =
    [
        new("CHS", LanguageType.LanguageSc, "zh-CN", ["CHS", "CN"]),
        new("CHT", LanguageType.LanguageTc, "zh-TW", ["CHT"]),
        new("EN", LanguageType.LanguageEn, null, ["EN"]),
        new("KR", LanguageType.LanguageKr, null, ["KR"]),
        new("JP", LanguageType.LanguageJp, "ja-JP", ["JP"]),
        new("FR", LanguageType.LanguageFr, null, ["FR"]),
        new("DE", LanguageType.LanguageDe, null, ["DE"]),
        new("ES", LanguageType.LanguageEs, null, ["ES"]),
        new("PT", LanguageType.LanguagePt, null, ["PT"]),
        new("RU", LanguageType.LanguageRu, null, ["RU"]),
        new("TH", LanguageType.LanguageTh, null, ["TH"]),
        new("VI", LanguageType.LanguageVi, null, ["VI"]),
        new("ID", LanguageType.LanguageId, null, ["ID"])
    ];

    /// <summary>The server language code for a client language, or null when the client sent none or an unknown one.</summary>
    public static string? FromClientType(LanguageType type)
    {
        return Entries.FirstOrDefault(e => e.Type == type)?.Code;
    }

    /// <summary>The default server language code for a UI culture name such as "zh-CN"; EN when unmapped.</summary>
    public static string FromCulture(string cultureName)
    {
        return Entries.FirstOrDefault(e => e.Culture == cultureName)?.Code ?? DefaultCode;
    }

    /// <summary>
    ///     Every spelling of the language (case-insensitive); the first is the server-side one, which i18n and
    ///     the handbook filename both use. An unknown code yields itself.
    /// </summary>
    public static string[] AliasesOf(string lang)
    {
        var entry = Entries.FirstOrDefault(e => e.Aliases.Contains(lang, StringComparer.OrdinalIgnoreCase));
        return entry?.Aliases ?? [lang];
    }
}
