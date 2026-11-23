using March7thHoney.GameServer.Game.Progression;
using March7thHoney.GameServer.Server;
using March7thHoney.Internationalization;
using March7thHoney.Kcp;
using March7thHoney.Util;

namespace March7thHoney.Command.Command;

public class CommandManager
{
    private readonly Dictionary<string, GeneratedCommand> _generated = new(StringComparer.OrdinalIgnoreCase);
    public static CommandManager? Instance { get; private set; }
    public Dictionary<string, ICommand> Commands { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, CommandInfoAttribute> CommandInfo { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CommandAlias { get; } = new(StringComparer.OrdinalIgnoreCase); // alias -> command
    public Logger Logger { get; } = new("CommandManager");
    public Connection? Target { get; set; }

    public void RegisterCommands()
    {
        Instance = this;
        // Populated by the source generator (GeneratedCommandRegistry) from every [CommandInfo] class — no reflection.
        foreach (var gen in GeneratedCommandRegistry.Build())
        {
            Commands[gen.Info.Name] = gen.Instance;
            CommandInfo[gen.Info.Name] = gen.Info;
            _generated[gen.Info.Name] = gen;
            foreach (var alias in gen.Info.Alias)
                CommandAlias[alias] = gen.Info.Name;
        }

        Logger.Info(I18NManager.Translate("Server.ServerInfo.RegisterItem", Commands.Count.ToString(),
            I18NManager.Translate("Word.Command")));
    }

    /// <summary>
    ///     Snapshot of every registered command for the admin console's command reference. Descriptions and
    ///     usages are translated with the server language; subcommands are derived from the method conditions.
    /// </summary>
    public IReadOnlyList<CommandCatalogEntry> BuildCatalog()
    {
        var entries = new List<CommandCatalogEntry>();
        foreach (var (name, gen) in _generated.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var info = gen.Info;
            var subcommands = gen.Methods
                .Select(method => string.Join(" ", method.Conditions
                    .OrderBy(condition => condition.Index)
                    .Select(condition => condition.ShouldBe)
                    .Where(value => !string.IsNullOrWhiteSpace(value))))
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            entries.Add(new CommandCatalogEntry(
                name,
                I18NManager.Translate(info.Description),
                I18NManager.Translate(info.Usage),
                info.Permission,
                info.Alias,
                subcommands));
        }

        return entries;
    }

    public void HandleCommand(string input, ICommandSender sender)
    {
        _ = ObserveCommandTask(HandleCommandAsync(input, sender), sender, input, nameof(HandleCommandAsync));
    }

    public async Task HandleCommandAsync(string input, ICommandSender sender)
    {
        // Localize every reply (notices + command output) to the sender's language for this command only.
        var previousLanguage = I18NManager.LanguageOverride;
        I18NManager.LanguageOverride = sender.Language;
        try
        {
            var split = CommandArg.Tokenize(input);
            if (split.Count == 0) return;

            var cmd = split[0].TrimStart('/');

            // Only the local terminal owns the persistent target; HTTP requests are independent.
            var isLocalConsole = sender is ConsoleCommandSender;
            var tempTarget = isLocalConsole ? Target : null;
            var isTargetExplicit = tempTarget != null;
            if (!sender.IsConsole || sender.GetSender() != 0)
            {
                tempTarget = Listener.GetActiveConnection(sender.GetSender());
                if (tempTarget == null)
                {
                    await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.TargetNotFound",
                        sender.GetSender().ToString()));
                    return;
                }

                isTargetExplicit = sender.IsConsole;
            }

            if (sender.IsConsole)
            {
                if (IsTargetToken(cmd))
                {
                    // IsTargetToken already established the suffix parses as an int uid.
                    var target = cmd[1..];
                    if (Listener.GetPlayerConnection(int.Parse(target)) is { } con)
                    {
                        if (split.Count == 1)
                        {
                            if (isLocalConsole)
                            {
                                Target = con;
                                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.TargetFound", target,
                                    con.Player!.Data.Name!));
                            }
                            else
                            {
                                // A standalone @uid cannot persist a selection between HTTP requests.
                                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.InvalidArguments"));
                            }
                            return;
                        }

                        tempTarget = con;
                        isTargetExplicit = true;
                        split.RemoveAt(0);
                        cmd = split[0].TrimStart('/');
                    }
                    else
                    {
                        // offline or not exist
                        await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.TargetNotFound", target));
                        return;
                    }
                }
            }
            if (tempTarget is { IsOnline: false })
            {
                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.TargetOffline",
                    tempTarget.Player!.Uid.ToString(), tempTarget.Player!.Data.Name!));
                tempTarget = null;
            }

            // find the command
            if (CommandAlias.TryGetValue(cmd, out var realCmd)) cmd = realCmd;

            if (Commands.TryGetValue(cmd, out var command))
            {
                split.RemoveAt(0);
                for (var i = split.Count - 1; i >= 0; i--)
                {
                    if (!IsTargetToken(split[i])) continue;
                    var target = split[i][1..];
                    if (Listener.GetPlayerConnection(int.Parse(target)) is { } con)
                    {
                        tempTarget = con;
                        isTargetExplicit = true;
                        split.RemoveAt(i);
                        continue;
                    }

                    await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.TargetNotFound", target));
                    return;
                }

                var arg = new CommandArg(split, split.JoinFormat(" ", ""), sender, tempTarget, isTargetExplicit);

                // judge permission
                if (arg.Target?.Player?.Uid != sender.GetSender() && !sender.HasPermission(CommandPermissions.TargetOthers))
                {
                    await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.NoPermission"));
                    return;
                }

                // dispatch via the generated method table — no reflection
                var info = CommandInfo[cmd];
                var gen = _generated[cmd];

                if (!sender.HasPermission(info.Permission))
                {
                    await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.NoPermission"));
                    return;
                }

                foreach (var method in gen.Methods)
                {
                    var canRun = true;
                    foreach (var condition in method.Conditions)
                    {
                        if (split.Count <= condition.Index)
                        {
                            canRun = false;
                            break;
                        }

                        if (split[condition.Index].Equals(condition.ShouldBe, StringComparison.OrdinalIgnoreCase))
                            continue;
                        canRun = false;
                        break;
                    }

                    if (!canRun) continue;
                    await InvokeCommandMethod(method, command, arg, sender, cmd);
                    return;
                }

                if (gen.Default != null)
                {
                    await InvokeCommandMethod(gen.Default, command, arg, sender, cmd);
                    return;
                }

                await sender.SendMsg(I18NManager.Translate(info.Usage));
            }
            else
            {
                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.CommandNotFound"));
            }
        }
        catch (Exception e)
        {
            Logger.Error(I18NManager.Translate("Game.Command.Notice.InternalError"), e);
            await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.InternalError"));
        }
        finally
        {
            I18NManager.LanguageOverride = previousLanguage;
        }
    }

    private static bool IsTargetToken(string token)
    {
        return token.Length > 1 && token[0] == '@' && int.TryParse(token[1..], out _);
    }

    private async Task InvokeCommandMethod(GeneratedCommandMethod method, ICommand command, CommandArg arg,
        ICommandSender sender, string commandName)
    {
        // Missions a command finishes pay the progression bonus only when the server allows it.
        using var rewardScope = ProgressionRewards.EnterCommand();
        var result = method.Invoke(command, arg);
        switch (result)
        {
            case Task task:
                await ObserveCommandTask(task, sender, commandName, method.Name);
                break;
            case ValueTask valueTask:
                await ObserveCommandTask(valueTask.AsTask(), sender, commandName, method.Name);
                break;
        }
    }

    private async Task ObserveCommandTask(Task task, ICommandSender sender, string commandName, string methodName)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Logger.Error(I18NManager.Translate("Server.Log.CommandFailed", $"{commandName}.{methodName}"), e);
            try
            {
                await sender.SendMsg(I18NManager.Translate("Game.Command.Notice.InternalError"));
            }
            catch (Exception sendException)
            {
                Logger.Debug($"Failed to send command error message for {commandName}.{methodName}", sendException);
            }
        }
    }
}
