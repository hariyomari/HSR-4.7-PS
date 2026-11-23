using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace March7thHoney.SourceGen;

/// <summary>
///     Registers assembly-visible, non-generic <c>[MemoryPackable]</c> types at module initialization.
///     Running their static constructors prevents collection deserialization from using MemoryPack's
///     reflection fallback, which fails under NativeAOT.
/// </summary>
[Generator]
public sealed class MemoryPackRegistrarGenerator : IIncrementalGenerator
{
    private const string MemoryPackableAttribute = "MemoryPack.MemoryPackableAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.ForAttributeWithMetadataName(
                MemoryPackableAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) =>
                {
                    var type = (INamedTypeSymbol)ctx.TargetSymbol;
                    // Include abstract union bases, but skip types the registrar cannot name.
                    // Inaccessible nested types register themselves when serialized directly.
                    if (type.IsGenericType || type.IsStatic) return null;
                    if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) return null;
                    return IsVisibleInAssembly(type) ? type.FullName() : null;
                })
            .Where(static name => name != null)
            .Select(static (name, _) => name!)
            .Collect()
            .Select(static (names, _) => names.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToEquatableArray())
            .WithTrackingName(Symbols.ModelStep);

        context.RegisterSourceOutput(types, static (output, types) =>
        {
            if (types.Count == 0) return;

            var code = new CodeWriter();
            code.Line("using System.Runtime.CompilerServices;");
            code.Line();
            code.Line("namespace March7thHoney.Data.Generated;");
            code.Line();
            code.Line("internal static class MemoryPackRegistrar");
            using (code.Block())
            {
                code.Line("private static bool _done;");
                code.Line();
                code.Line("[ModuleInitializer]");
                code.Line("internal static void RegisterAll()");
                using (code.Block())
                {
                    code.Line("if (_done) return;");
                    code.Line("_done = true;");
                    foreach (var type in types)
                        code.Line($"RuntimeHelpers.RunClassConstructor(typeof({type}).TypeHandle);");
                }
            }

            output.AddSource("MemoryPackRegistrar.g.cs", code.ToString());
        });
    }

    /// <summary>Public, internal or protected internal at every level of nesting.</summary>
    private static bool IsVisibleInAssembly(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.ContainingType)
            if (current.DeclaredAccessibility is not
                (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal))
                return false;
        return true;
    }
}
