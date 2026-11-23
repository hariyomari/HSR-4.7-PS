using System.Data.Common;
using System.Globalization;
using March7thHoney.Internationalization;

namespace March7thHoney.Database;

/// <summary>
///     Schema changes the generated stores apply when they open their table, and the text form of a column
///     the stores use for save snapshots.
/// </summary>
internal static class DbSchema
{
    /// <summary>Adds the columns an entity declares that a table created by an older build does not have.</summary>
    public static void EnsureColumns(DbConnection connection, string table, (string Name, string Sql)[] columns)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "select * from \"" + table + "\" limit 0";
            using var reader = command.ExecuteReader();
            for (var i = 0; i < reader.FieldCount; i++) existing.Add(reader.GetName(i));
        }

        foreach (var (name, sql) in columns)
        {
            if (existing.Contains(name)) continue;
            using var alter = connection.CreateCommand();
            alter.CommandText = "alter table \"" + table + "\" add column \"" + name + "\" " + sql;
            alter.ExecuteNonQuery();
            DatabaseHelper.logger.Info(I18NManager.Translate("Server.Log.DatabaseColumnAdded", table, name));
        }
    }

    /// <summary>
    ///     The snapshot text of one row column: base64 for a BLOB-backed column, invariant text for a scalar
    ///     (an enum as its number).
    /// </summary>
    public static string? Text(object? value) => value switch
    {
        null => null,
        byte[] bytes => Convert.ToBase64String(bytes),
        string text => text,
        Enum number => Convert.ToInt64(number, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
