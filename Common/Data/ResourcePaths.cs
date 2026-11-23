using March7thHoney.Util;

namespace March7thHoney.Data;

/// <summary>Locates files in the raw resource tree (<c>Config.Path.ResourcePath</c>).</summary>
public static class ResourcePaths
{
    /// <summary>Absolute path of a file inside the resource tree.</summary>
    /// <remarks>
    ///     Paths read from excel and level data may be null or empty, and a leading separator is stripped so a
    ///     stray "/Config/..." still resolves inside the resource tree instead of escaping it.
    /// </remarks>
    public static string Resource(string? relative) =>
        Path.Combine(ConfigManager.Config.Path.ResourcePath, (relative ?? string.Empty).TrimStart('/', '\\'));

    /// <summary>
    ///     The excel table <paramref name="fileName" /> from the first of <c>Config.Path.ExcelOutputDirs</c> that holds
    ///     it, or null when none does.
    /// </summary>
    public static FileInfo? FindExcel(string fileName) =>
        ConfigManager.Config.Path.ExcelOutputDirs
            .Select(dir => new FileInfo(Path.Combine(ConfigManager.Config.Path.ResourcePath, dir, fileName)))
            .FirstOrDefault(file => file.Exists);
}
