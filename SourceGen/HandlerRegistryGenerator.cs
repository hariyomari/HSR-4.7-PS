using System.Linq;
using Microsoft.CodeAnalysis;

namespace March7thHoney.SourceGen;

/// <summary>
///     Emits <c>GeneratedHandlerRegistry</c>, which maps each cmdId to an instance of the class annotated
///     <c>[Opcode(cmdId)]</c> for it. Packet dispatch needs no reflection, so it works under NativeAOT.
/// </summary>
[Generator]
public sealed class HandlerRegistryGenerator : IIncrementalGenerator
{
    private const string OpcodeAttribute = "March7thHoney.GameServer.Server.Packet.Opcode";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = context.SyntaxProvider.ForAttributeWithMetadataName(
                OpcodeAttribute,
                static (node, _) => node.IsConcreteClass(),
                static (ctx, _) =>
                {
                    var arguments = ctx.Attributes[0].ConstructorArguments;
                    var cmdId = arguments.Length > 0 && arguments[0].Value is int value ? value : 0;
                    var type = (INamedTypeSymbol)ctx.TargetSymbol;
                    return new Handler(cmdId, type.FullName(), SourceLocation.Of(type));
                })
            .Collect()
            .Select(static (items, _) => items.ToEquatableArray())
            .WithTrackingName(Symbols.ModelStep);

        context.RegisterSourceOutput(handlers, static (output, handlers) =>
        {
            if (handlers.Count == 0) return;

            var registered = handlers.GroupBy(h => h.CmdId).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
            foreach (var group in registered)
            foreach (var duplicate in group.Skip(1))
                output.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicatePacketHandler,
                    duplicate.Location?.ToLocation(), group[0].TypeName, duplicate.TypeName, duplicate.CmdId));

            var code = new CodeWriter();
            code.Line("namespace March7thHoney.GameServer.Server.Packet;");
            code.Line();
            code.Line("internal static class GeneratedHandlerRegistry");
            using (code.Block())
            {
                code.Line("public static global::System.Collections.Generic.Dictionary<int, Handler> Build()");
                using (code.Block())
                {
                    code.Line($"var d = new global::System.Collections.Generic.Dictionary<int, Handler>({registered.Count});");
                    foreach (var group in registered)
                        code.Line($"d[{group[0].CmdId}] = new {group[0].TypeName}();");
                    code.Line("return d;");
                }
            }

            output.AddSource("GeneratedHandlerRegistry.g.cs", code.ToString());
        });
    }

    private sealed record Handler(int CmdId, string TypeName, SourceLocation? Location);
}
