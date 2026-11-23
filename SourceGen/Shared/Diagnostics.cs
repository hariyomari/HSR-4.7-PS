using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace March7thHoney.SourceGen;

/// <summary>What the generators report instead of emitting code that silently drops a declaration.</summary>
internal static class Diagnostics
{
    private const string Category = "March7thHoney.SourceGen";

    public static readonly DiagnosticDescriptor DuplicatePacketHandler = new(
        "M7H001",
        "Duplicate packet handler",
        "'{0}' and '{1}' both handle cmdId {2}; a cmdId can have only one handler",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor DuplicateMissionHandler = new(
        "M7H002",
        "Duplicate mission handler",
        "'{0}' and '{1}' are both registered for {2}; a value can have only one handler",
        Category,
        DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor DuplicateEntityName = new(
        "M7H003",
        "Database entities share a class name",
        "'{0}' and '{1}' are both named {2}; the name of an entity class names its generated store and must be unique",
        Category,
        DiagnosticSeverity.Error,
        true);
}

/// <summary>
///     Where a declaration is, in a form a pipeline model can hold: a <see cref="Location" /> keeps its whole
///     syntax tree alive and does not compare by value.
/// </summary>
internal readonly record struct SourceLocation(string Path, TextSpan Span, LinePositionSpan Lines)
{
    public static SourceLocation? Of(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        return location == null
            ? null
            : new SourceLocation(location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public Location ToLocation() => Location.Create(Path, Span, Lines);
}
