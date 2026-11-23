namespace March7thHoney.SourceGen.Database;

/// <summary>How a column is stored.</summary>
internal enum ColumnKind
{
    /// <summary>A value the database stores natively: a number, text, an enum as its number, or raw bytes.</summary>
    Scalar,

    /// <summary>Any other type, serialized with MemoryPack into a BLOB.</summary>
    MemoryPack,

    /// <summary>A protobuf message, serialized into a BLOB.</summary>
    Protobuf,
}

/// <summary>
///     The CLR type of a scalar column. Every member up to <see cref="Decimal" /> is named after the
///     <c>System.Convert.To…</c> method that reads it back from the database.
/// </summary>
internal enum ScalarType
{
    None,
    String,
    Boolean,
    SByte,
    Byte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Decimal,
    Enum,
    Bytes,
}

/// <summary>One persisted property of an entity.</summary>
/// <param name="TypeName">The property type as declared.</param>
/// <param name="ValueTypeName">The same without <c>Nullable&lt;T&gt;</c> around a value type.</param>
internal sealed record Column(
    string Name,
    string TypeName,
    string ValueTypeName,
    ColumnKind Kind,
    ScalarType Scalar,
    string SqlType,
    bool Nullable);

/// <summary>One concrete subclass of <c>BaseDatabaseDataHelper</c> and the table it is stored in.</summary>
/// <param name="IsPlayerData">False when the class or a base class carries <c>[NotPlayerData]</c>.</param>
/// <param name="FormatterRegistrations">
///     Expressions that create the MemoryPack formatters its columns need registered ahead of time.
/// </param>
internal sealed record Entity(
    string TypeName,
    string ClassName,
    string Table,
    EquatableArray<Column> Columns,
    EquatableArray<string> FormatterRegistrations,
    bool IsPlayerData,
    SourceLocation? Location);
