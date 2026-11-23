using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace March7thHoney.SourceGen;

/// <summary>
///     Emits <c>GeneratedCommandRegistry</c> with command attributes, instances and method delegates
///     for reflection-free dispatch by <c>CommandManager</c>.
/// </summary>
[Generator]
public sealed class CommandRegistryGenerator : IIncrementalGenerator
{
    private const string CommandInfoAttribute = "March7thHoney.Command.CommandInfoAttribute";
    private const string CommandMethodAttribute = "March7thHoney.Command.CommandMethodAttribute";
    private const string CommandDefaultAttribute = "March7thHoney.Command.CommandDefaultAttribute";

    private const string ICommand = "global::March7thHoney.Command.ICommand";
    private const string CommandArg = "global::March7thHoney.Command.Command.CommandArg";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var commands = context.SyntaxProvider.ForAttributeWithMetadataName(
                CommandInfoAttribute,
                static (node, _) => node.IsConcreteClass(),
                static (ctx, _) => Parse((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0]))
            .Collect()
            .Select(static (items, _) => items.ToEquatableArray())
            .WithTrackingName(Symbols.ModelStep);

        context.RegisterSourceOutput(commands, static (output, commands) =>
        {
            if (commands.Count == 0) return;
            output.AddSource("GeneratedCommandRegistry.g.cs", Emit(commands));
        });
    }

    private static Command Parse(INamedTypeSymbol type, AttributeData info)
    {
        var methods = new List<Method>();
        Method? defaultMethod = null;
        foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
        foreach (var attribute in method.GetAttributes())
            switch (attribute.AttributeClass?.ToDisplayString())
            {
                // The condition is a string in source; the constructor that takes a CommandCondition
                // cannot be written in an attribute.
                case CommandMethodAttribute when attribute.ConstructorArguments.Length == 1 &&
                                                 attribute.ConstructorArguments[0].Value is string condition:
                    methods.Add(new Method(method.Name, method.ReturnsVoid, Literal(condition)));
                    break;
                case CommandDefaultAttribute:
                    defaultMethod = new Method(method.Name, method.ReturnsVoid, "");
                    break;
            }

        return new Command(type.FullName(), InfoArguments(info), methods.ToEquatableArray(), defaultMethod);
    }

    /// <summary>
    ///     Normalizes either attribute constructor to the overload with aliases.
    /// </summary>
    private static string InfoArguments(AttributeData info)
    {
        var values = new Dictionary<string, TypedConstant>(StringComparer.OrdinalIgnoreCase);
        if (info.AttributeConstructor is { } constructor)
            for (var i = 0; i < constructor.Parameters.Length && i < info.ConstructorArguments.Length; i++)
                values[constructor.Parameters[i].Name] = info.ConstructorArguments[i];
        foreach (var named in info.NamedArguments)
            if (named.Value.Value is string)
                values[named.Key] = named.Value;

        string Text(string name) =>
            Literal(values.TryGetValue(name, out var value) ? value.Value as string ?? "" : "");

        var alias = values.TryGetValue("alias", out var array) && array.Kind == TypedConstantKind.Array
            ? array.Values.Select(v => Literal(v.Value as string ?? "")).ToList()
            : [];
        var aliasExpression = alias.Count == 0
            ? "global::System.Array.Empty<string>()"
            : "new string[]{" + string.Join(", ", alias) + "}";

        return string.Join(", ",
            Text("name"), Text("description"), Text("usage"), aliasExpression, Text("keyword"), Text("permission"));
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);

    private static string Emit(EquatableArray<Command> commands)
    {
        var code = new CodeWriter();
        code.Line("namespace March7thHoney.Command.Command;");
        code.Line();
        code.Line("internal static class GeneratedCommandRegistry");
        using (code.Block())
        {
            code.Line("public static GeneratedCommand[] Build()");
            using (code.Block())
            {
                code.Line("return new GeneratedCommand[]");
                using (code.Block("};"))
                {
                    foreach (var command in commands) EmitCommand(code, command);
                }
            }
        }

        return code.ToString();
    }

    private static void EmitCommand(CodeWriter code, Command command)
    {
        code.Line("new GeneratedCommand(");
        using (code.Indent())
        {
            code.Line($"new global::March7thHoney.Command.CommandInfoAttribute({command.InfoArguments}),");
            code.Line($"new {command.TypeName}(),");
            if (command.Methods.Count == 0)
            {
                code.Line("global::System.Array.Empty<GeneratedCommandMethod>(),");
            }
            else
            {
                code.Line("new GeneratedCommandMethod[]");
                using (code.Block("},"))
                {
                    foreach (var method in command.Methods)
                    {
                        code.Line("new GeneratedCommandMethod(");
                        using (code.Indent())
                        {
                            code.Line($"{Literal(method.Name)},");
                            code.Line($"new global::March7thHoney.Command.CommandMethodAttribute({method.Condition}).Conditions,");
                            code.Line($"{Invoker(command.TypeName, method)}),");
                        }
                    }
                }
            }

            if (command.Default == null)
            {
                code.Line("null),");
            }
            else
            {
                code.Line("new GeneratedCommandMethod(");
                using (code.Indent())
                {
                    code.Line($"{Literal(command.Default.Name)},");
                    code.Line("new global::System.Collections.Generic.List<global::March7thHoney.Command.CommandCondition>(),");
                    code.Line($"{Invoker(command.TypeName, command.Default)})),");
                }
            }
        }
    }

    /// <summary>A delegate that calls the method on the command instance and returns its result, if any.</summary>
    private static string Invoker(string typeName, Method method)
    {
        var call = $"(({typeName})c).{method.Name}(a)";
        return method.ReturnsVoid
            ? $"static ({ICommand} c, {CommandArg} a) => {{ {call}; return null; }}"
            : $"static ({ICommand} c, {CommandArg} a) => {call}";
    }

    private sealed record Method(string Name, bool ReturnsVoid, string Condition);

    private sealed record Command(string TypeName, string InfoArguments, EquatableArray<Method> Methods, Method? Default);
}
