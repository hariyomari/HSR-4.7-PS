using System.Data.Common;

namespace March7thHoney.Database;

/// <summary>
///     The database operations of one entity type. The source generator writes a store for every entity and
///     lists them in <c>EntityStores.Map</c>; <see cref="DatabaseHelper" /> reaches a table only through
///     this record, so it needs no reflection.
/// </summary>
/// <param name="DeepCopy">
///     The persistence round trip in memory: a copy that shares no object with the source, as a save
///     followed by a reload would produce.
/// </param>
/// <param name="Export">The entity as column name -> text (BLOB columns base64, see <c>DbSchema.Text</c>).</param>
/// <param name="Import">
///     The reverse of <paramref name="Export" />. Unknown keys are ignored; a missing column reads back like
///     one a later schema migration added.
/// </param>
/// <param name="IsPlayerData">False for an entity marked <see cref="NotPlayerDataAttribute" />.</param>
public sealed record EntityStore(
    Type Type,
    string Table,
    Action<DbConnection> EnsureTable,
    Func<DbConnection, List<BaseDatabaseDataHelper>> LoadAll,
    Action<DbConnection, BaseDatabaseDataHelper> Insert,
    Action<DbConnection, BaseDatabaseDataHelper> Update,
    Action<DbConnection, int> Delete,
    Func<BaseDatabaseDataHelper, BaseDatabaseDataHelper> DeepCopy,
    Func<BaseDatabaseDataHelper, Dictionary<string, string?>> Export,
    Func<IReadOnlyDictionary<string, string?>, BaseDatabaseDataHelper> Import,
    bool IsPlayerData);
