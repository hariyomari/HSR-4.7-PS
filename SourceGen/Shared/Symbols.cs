using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace March7thHoney.SourceGen;

internal static class Symbols
{
    /// <summary>Names a step whose output the regression inspects to confirm it is reused after an unrelated edit.</summary>
    public const string ModelStep = "Model";

    /// <summary>A class the generated code can construct: every registry holds instances, never abstract types.</summary>
    public static bool IsConcreteClass(this SyntaxNode node) =>
        node is ClassDeclarationSyntax declaration && !declaration.Modifiers.Any(SyntaxKind.AbstractKeyword);

    /// <summary>The name generated code uses for a type, <c>global::</c>-qualified so no using can change its meaning.</summary>
    public static string FullName(this ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public static bool HasAttribute(this ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == metadataName);
}
