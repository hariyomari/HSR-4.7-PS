using March7thHoney.GameServer.Server.ClientLua;
using March7thHoney.Internationalization;

namespace March7thHoney.Command.Command.Cmd;

[CommandInfo("windy", "Game.Command.Windy.Desc", "Game.Command.Windy.Usage", permission: CommandPermissions.Windy)]
public class CommandWindy : ICommand
{
    [CommandDefault]
    public async ValueTask Windy(CommandArg arg)
    {
        var luaRoot = ClientLuaDirectory.Root;
        var luaFiles = GetLuaFiles(luaRoot);

        if (arg.Args.Count == 0 || arg.Args[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            await ShowLuaList(arg, luaFiles);
            return;
        }

        if (arg.Args[0].Equals("help", StringComparison.OrdinalIgnoreCase) || arg.Args[0] == "?")
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.Usage"));
            return;
        }

        if (arg.Args[0].Equals("loaded", StringComparison.OrdinalIgnoreCase) ||
            arg.Args[0].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            await ShowLoadedLua(arg);
            return;
        }

        if (arg.Target == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        var input = arg.Args[0].Equals("load", StringComparison.OrdinalIgnoreCase)
            ? string.Join(" ", arg.Args.Skip(1))
            : string.Join(" ", arg.Args);
        if (string.IsNullOrWhiteSpace(input))
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.Usage"));
            return;
        }

        if (!TrySelectLuaFile(input, luaFiles, out var relativePath, out var error))
        {
            await arg.SendMsg(error ?? I18NManager.Translate("Game.Command.Windy.ReadError", input.Replace("\\", "/")));
            return;
        }

        try
        {
            var fileBytes = await HoyoToonLuaPayloadBuilder.BuildAsync(luaRoot, relativePath);
            await arg.Target.ClientLua.SendScriptAsync(relativePath, fileBytes);
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.Loaded", relativePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.ReadError", relativePath));
        }
    }

    private static List<string> GetLuaFiles(string luaRoot)
    {
        if (!Directory.Exists(luaRoot)) return [];

        try
        {
            return Directory.EnumerateFiles(luaRoot, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetRelativePath(luaRoot, path).Replace("\\", "/", StringComparison.Ordinal))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static async ValueTask ShowLuaList(CommandArg arg, IReadOnlyList<string> luaFiles)
    {
        if (luaFiles.Count == 0)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.NoFiles"));
            return;
        }

        var loaded = arg.Target?.ClientLua.GetSentScripts().ToHashSet(StringComparer.OrdinalIgnoreCase) ??
                     new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.FoundFiles", luaFiles.Count.ToString()));
        for (var i = 0; i < luaFiles.Count; i++)
        {
            var marker = loaded.Contains(luaFiles[i])
                ? I18NManager.Translate("Game.Command.Windy.LoadedMarker")
                : "";
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.FileListItem",
                (i + 1).ToString(), luaFiles[i], marker));
        }

        await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.ListHint"));
    }

    private static async ValueTask ShowLoadedLua(CommandArg arg)
    {
        if (arg.Target == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        var loaded = arg.Target.ClientLua.GetSentScripts();
        if (loaded.Count == 0)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.NoLoadedFiles"));
            return;
        }

        await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.LoadedFiles"));
        for (var i = 0; i < loaded.Count; i++)
            await arg.SendMsg(I18NManager.Translate("Game.Command.Windy.FileListItem",
                (i + 1).ToString(), loaded[i], ""));
    }

    private static bool TrySelectLuaFile(string input, IReadOnlyList<string> luaFiles, out string relativePath,
        out string? error)
    {
        relativePath = "";
        error = null;
        input = input.Trim().Replace("\\", "/", StringComparison.Ordinal).TrimStart('/');

        if (int.TryParse(input, out var choice))
        {
            if (choice < 1 || choice > luaFiles.Count)
            {
                error = I18NManager.Translate("Game.Command.Windy.InvalidChoice", luaFiles.Count.ToString());
                return false;
            }

            relativePath = luaFiles[choice - 1];
            return true;
        }

        var inputWithExtension = input.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
            ? input
            : input + ".lua";
        var exact = luaFiles.FirstOrDefault(path => path.Equals(input, StringComparison.OrdinalIgnoreCase)) ??
                    luaFiles.FirstOrDefault(path => path.Equals(inputWithExtension, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            relativePath = exact;
            return true;
        }

        if (input.Contains('/')) return false;

        var matches = luaFiles
            .Where(path => Path.GetFileName(path).Equals(inputWithExtension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
        {
            relativePath = matches[0];
            return true;
        }

        if (matches.Count > 1)
            error = I18NManager.Translate("Game.Command.Windy.AmbiguousFile", string.Join(", ", matches));
        return false;
    }
}
