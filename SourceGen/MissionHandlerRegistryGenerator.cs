using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace March7thHoney.SourceGen;

/// <summary>
///     Emits <c>GeneratedMissionHandlers.Register</c>, which fills <c>MissionManager.ActionHandlers</c> and
///     <c>MissionManager.FinishTypeHandlers</c> with an instance of every class annotated
///     <c>[MissionFinishAction(...)]</c> or <c>[MissionFinishType(...)]</c>, without reflection.
/// </summary>
[Generator]
public sealed class MissionHandlerRegistryGenerator : IIncrementalGenerator
{
    private const string ActionAttribute =
        "March7thHoney.GameServer.Game.Mission.FinishAction.MissionFinishActionAttribute";

    private const string TypeAttribute =
        "March7thHoney.GameServer.Game.Mission.FinishType.MissionFinishTypeAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var handlers = Handlers(context, ActionAttribute).Combine(Handlers(context, TypeAttribute));

        context.RegisterSourceOutput(handlers, static (output, handlers) =>
        {
            var (actions, types) = handlers;
            if (actions.Count == 0 && types.Count == 0) return;

            var code = new CodeWriter();
            code.Line("namespace March7thHoney.GameServer.Game.Mission;");
            code.Line();
            code.Line("internal static class GeneratedMissionHandlers");
            using (code.Block())
            {
                code.Line("public static void Register()");
                using (code.Block())
                {
                    foreach (var handler in Registered(output, actions))
                        code.Line($"MissionManager.ActionHandlers[({handler.EnumType}){handler.Value}] = new {handler.TypeName}();");
                    foreach (var handler in Registered(output, types))
                        code.Line($"MissionManager.FinishTypeHandlers[({handler.EnumType}){handler.Value}] = new {handler.TypeName}();");
                }
            }

            output.AddSource("GeneratedMissionHandlers.g.cs", code.ToString());
        });
    }

    private static IncrementalValueProvider<EquatableArray<Handler>> Handlers(
        IncrementalGeneratorInitializationContext context, string attribute)
    {
        return context.SyntaxProvider.ForAttributeWithMetadataName(
                attribute,
                static (node, _) => node.IsConcreteClass(),
                static (ctx, _) =>
                {
                    var arguments = ctx.Attributes[0].ConstructorArguments;
                    var key = arguments.Length > 0 ? arguments[0] : default;
                    var type = (INamedTypeSymbol)ctx.TargetSymbol;
                    return new Handler(
                        key.Type?.FullName() ?? "",
                        key.Value is { } value ? Convert.ToInt64(value) : 0L,
                        type.FullName(),
                        SourceLocation.Of(type));
                })
            .Collect()
            .Select(static (items, _) => items.ToEquatableArray())
            .WithTrackingName(Symbols.ModelStep);
    }

    /// <summary>One handler per enum value, in value order; a second handler for a value is reported.</summary>
    private static IEnumerable<Handler> Registered(SourceProductionContext output, EquatableArray<Handler> handlers)
    {
        foreach (var group in handlers.GroupBy(h => h.Value).OrderBy(g => g.Key).Select(g => g.ToList()))
        {
            foreach (var duplicate in group.Skip(1))
                output.ReportDiagnostic(Diagnostic.Create(Diagnostics.DuplicateMissionHandler,
                    duplicate.Location?.ToLocation(), group[0].TypeName, duplicate.TypeName,
                    $"({duplicate.EnumType}){duplicate.Value}"));
            yield return group[0];
        }
    }

    private sealed record Handler(string EnumType, long Value, string TypeName, SourceLocation? Location);
}
