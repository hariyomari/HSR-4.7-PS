using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace March7thHoney.SourceGen.Database;

/// <summary>Reads an entity class into the model the store is emitted from.</summary>
internal static class EntityParser
{
    public const string BaseType = "March7thHoney.Database.BaseDatabaseDataHelper";
    private const string TableAttribute = "March7thHoney.Database.DbTableAttribute";
    private const string IgnoreAttribute = "March7thHoney.Database.DbIgnoreAttribute";
    private const string NotPlayerDataAttribute = "March7thHoney.Database.NotPlayerDataAttribute";
    private const string ProtobufMessage = "Google.Protobuf.IMessage";

    private static readonly Dictionary<SpecialType, (ScalarType Scalar, string Sql)> ScalarTypes = new()
    {
        [SpecialType.System_String] = (ScalarType.String, "TEXT"),
        [SpecialType.System_Boolean] = (ScalarType.Boolean, "INTEGER"),
        [SpecialType.System_SByte] = (ScalarType.SByte, "INTEGER"),
        [SpecialType.System_Byte] = (ScalarType.Byte, "INTEGER"),
        [SpecialType.System_Int16] = (ScalarType.Int16, "INTEGER"),
        [SpecialType.System_UInt16] = (ScalarType.UInt16, "INTEGER"),
        [SpecialType.System_Int32] = (ScalarType.Int32, "INTEGER"),
        [SpecialType.System_UInt32] = (ScalarType.UInt32, "INTEGER"),
        [SpecialType.System_Int64] = (ScalarType.Int64, "INTEGER"),
        [SpecialType.System_UInt64] = (ScalarType.UInt64, "INTEGER"),
        [SpecialType.System_Single] = (ScalarType.Single, "REAL"),
        [SpecialType.System_Double] = (ScalarType.Double, "REAL"),
        [SpecialType.System_Decimal] = (ScalarType.Decimal, "REAL"),
    };

    /// <summary>The model of a class, or null when the class is not an entity.</summary>
    public static Entity? Parse(GeneratorSyntaxContext context)
    {
        // Stores are generated where the entity base class is declared: that assembly's DatabaseHelper is the
        // only consumer. Elsewhere this returns before any symbol of the class is bound.
        if (context.SemanticModel.Compilation.Assembly.GetTypeByMetadataName(BaseType) == null) return null;
        if (context.SemanticModel.GetDeclaredSymbol(context.Node) is not INamedTypeSymbol type) return null;
        if (!DerivesFromBase(type)) return null;

        var table = type.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == TableAttribute && a.ConstructorArguments.Length == 1)
            .Select(a => a.ConstructorArguments[0].Value as string)
            .LastOrDefault(name => name != null) ?? type.Name;

        var columns = new List<Column>();
        var formatters = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var property in PersistedProperties(type))
        {
            if (columns.Any(c => c.Name == property.Name)) continue;
            columns.Add(ParseColumn(property));
            CollectFormatters(property.Type, formatters);
        }

        return new Entity(type.FullName(), type.Name, table, columns.ToEquatableArray(),
            formatters.ToEquatableArray(), !IsMarkedNotPlayerData(type), SourceLocation.Of(type));
    }

    private static bool IsMarkedNotPlayerData(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (current.HasAttribute(NotPlayerDataAttribute))
                return true;
        return false;
    }

    private static bool DerivesFromBase(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current != null; current = current.BaseType)
            if (current.ToDisplayString() == BaseType)
                return true;
        return false;
    }

    /// <summary>Read-write instance properties without <c>[DbIgnore]</c>, the base class's first so Uid leads.</summary>
    private static IEnumerable<IPropertySymbol> PersistedProperties(INamedTypeSymbol type)
    {
        var hierarchy = new Stack<INamedTypeSymbol>();
        for (var current = type; current is { SpecialType: not SpecialType.System_Object }; current = current.BaseType)
            hierarchy.Push(current);

        return hierarchy
            .SelectMany(t => t.GetMembers().OfType<IPropertySymbol>())
            .Where(p => !p.IsStatic && !p.IsIndexer && p.GetMethod != null && p.SetMethod != null)
            .Where(p => !p.HasAttribute(IgnoreAttribute));
    }

    private static Column ParseColumn(IPropertySymbol property)
    {
        var type = property.Type;
        var isNullableValue = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
        var valueType = isNullableValue ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
        var nullable = isNullableValue || type.NullableAnnotation == NullableAnnotation.Annotated;

        var (kind, scalar, sql) = valueType switch
        {
            IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } =>
                (ColumnKind.Scalar, ScalarType.Bytes, "BLOB"),
            { TypeKind: TypeKind.Enum } =>
                (ColumnKind.Scalar, ScalarType.Enum, "INTEGER"),
            _ when ScalarTypes.TryGetValue(valueType.SpecialType, out var known) =>
                (ColumnKind.Scalar, known.Scalar, known.Sql),
            _ when valueType.AllInterfaces.Any(i => i.ToDisplayString() == ProtobufMessage) =>
                (ColumnKind.Protobuf, ScalarType.None, "BLOB"),
            _ => (ColumnKind.MemoryPack, ScalarType.None, "BLOB"),
        };

        return new Column(property.Name, type.FullName(), valueType.FullName(), kind, scalar, sql, nullable);
    }

    /// <summary>
    ///     Records the formatter of every collection and enum inside a column type. A column is serialized as
    ///     the top-level value, so nothing else registers the formatters of the types nested in it (entities
    ///     are not <c>[MemoryPackable]</c>), and under NativeAOT the provider cannot create them by reflection.
    /// </summary>
    private static void CollectFormatters(ITypeSymbol type, ISet<string> formatters)
    {
        const string Formatters = "global::MemoryPack.Formatters";

        if (type.TypeKind == TypeKind.Enum)
        {
            formatters.Add($"new {Formatters}.DangerousUnmanagedFormatter<{type.FullName()}>()");
            return;
        }

        switch (type)
        {
            case IArrayTypeSymbol array:
                CollectFormatters(array.ElementType, formatters);
                return;
            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable:
                CollectFormatters(nullable.TypeArguments[0], formatters);
                return;
            case INamedTypeSymbol { IsGenericType: true } generic:
                var arguments = generic.TypeArguments;
                var isSystemCollection =
                    generic.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic";
                switch (generic.Name)
                {
                    case "List" when isSystemCollection && arguments.Length == 1:
                        formatters.Add($"new {Formatters}.ListFormatter<{arguments[0].FullName()}>()");
                        break;
                    case "HashSet" when isSystemCollection && arguments.Length == 1:
                        formatters.Add(
                            $"new {Formatters}.GenericSetFormatter<{generic.FullName()}, {arguments[0].FullName()}>()");
                        break;
                    case "Dictionary" when isSystemCollection && arguments.Length == 2:
                        formatters.Add(
                            $"new {Formatters}.DictionaryFormatter<{arguments[0].FullName()}, {arguments[1].FullName()}>()");
                        break;
                }

                foreach (var argument in arguments) CollectFormatters(argument, formatters);
                return;
        }
    }
}
