using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using March7thHoney.Command;

namespace March7thHoney.Command
{
    public interface ICommand;

    public class CommandCondition
    {
        public int Index { get; set; }
        public string ShouldBe { get; set; } = "";
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CommandInfoAttribute(
        string name,
        string description,
        string usage,
        string keyword = "",
        string permission = "")
        : Attribute
    {
        public CommandInfoAttribute(string name, string description, string usage, string[] alias,
            string keyword = "", string permission = "") : this(name, description, usage, keyword, permission)
        {
            Alias = alias;
        }

        public string Name { get; } = name;
        public string Description { get; } = description;
        public string Usage { get; } = usage;
        public string Keyword { get; } = keyword;
        public string Permission { get; } = permission;
        public string[] Alias { get; } = [];
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class CommandMethodAttribute(CommandCondition conditions) : Attribute
    {
        public CommandMethodAttribute(string condition) : this(new CommandCondition { ShouldBe = condition })
        {
        }

        public List<CommandCondition> Conditions { get; } = [conditions];
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class CommandDefaultAttribute : Attribute;
}

namespace March7thHoney.Command.Command
{
    public class CommandArg;

    public sealed class GeneratedCommandMethod(
        string name,
        List<CommandCondition> conditions,
        Func<ICommand, CommandArg, object?> invoke)
    {
        public string Name { get; } = name;
        public List<CommandCondition> Conditions { get; } = conditions;
        public Func<ICommand, CommandArg, object?> Invoke { get; } = invoke;
    }

    public sealed class GeneratedCommand(
        CommandInfoAttribute info,
        ICommand instance,
        GeneratedCommandMethod[] methods,
        GeneratedCommandMethod? @default)
    {
        public CommandInfoAttribute Info { get; } = info;
        public ICommand Instance { get; } = instance;
        public GeneratedCommandMethod[] Methods { get; } = methods;
        public GeneratedCommandMethod? Default { get; } = @default;
    }
}

namespace Sample.Commands
{
    using March7thHoney.Command.Command;

    // Methods that return nothing, a task, and a default method.
    [CommandInfo("ping", "Sample.Ping.Desc", "Sample.Ping.Usage")]
    public class CommandPing : ICommand
    {
        [CommandMethod("now")]
        public void Now(CommandArg arg)
        {
        }

        [CommandMethod("after 0")]
        public Task After(CommandArg arg) => Task.CompletedTask;

        [CommandDefault]
        public ValueTask Default(CommandArg arg) => default;

        public void NotACommand(CommandArg arg)
        {
        }
    }

    // Aliases, keyword and permission; no methods and no default. The description needs escaping.
    [CommandInfo("give", "Gives an \"item\"", "Sample.Give.Usage", new[] { "g", "grant" }, "item", "command.give")]
    public class CommandGive : ICommand;

    // An abstract command cannot be constructed and is left out.
    [CommandInfo("shared", "", "")]
    public abstract class CommandShared : ICommand;
}
