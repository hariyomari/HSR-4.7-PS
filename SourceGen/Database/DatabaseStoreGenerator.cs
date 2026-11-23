using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace March7thHoney.SourceGen.Database;

/// <summary>
///     Emits the database access code of every entity, a concrete subclass of
///     <c>BaseDatabaseDataHelper</c>: see <see cref="StoreEmitter" /> for what is written. The code uses
///     ADO.NET directly, with no ORM and no reflection, so it works under NativeAOT.
/// </summary>
[Generator]
public sealed class DatabaseStoreGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entities = context.SyntaxProvider.CreateSyntaxProvider(
                // An entity is recognized by its base class, not by an attribute, so every concrete class
                // that names a base is a candidate.
                static (node, _) => node.IsConcreteClass() && ((ClassDeclarationSyntax)node).BaseList != null,
                static (ctx, _) => EntityParser.Parse(ctx))
            .Where(static entity => entity != null)
            .Select(static (entity, _) => entity!)
            .Collect()
            // A partial class yields the same entity once per declaration.
            .Select(static (items, _) =>
                items.Distinct().OrderBy(e => e.ClassName, StringComparer.Ordinal).ToEquatableArray())
            .WithTrackingName(Symbols.ModelStep);

        context.RegisterSourceOutput(entities, static (output, entities) =>
        {
            if (entities.Count == 0) return;

            var stores = new List<Entity>();
            foreach (var sameName in entities.GroupBy(e => e.ClassName).Select(g => g.ToList()))
            {
                foreach (var duplicate in sameName.Skip(1))
                    output.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateEntityName,
                        duplicate.Location?.ToLocation(), sameName[0].TypeName, duplicate.TypeName,
                        duplicate.ClassName));
                stores.Add(sameName[0]);
            }

            output.AddSource("DbStores.g.cs", StoreEmitter.Emit(stores));
        });
    }
}
