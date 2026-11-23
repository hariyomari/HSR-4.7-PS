using March7thHoney.Database;
using March7thHoney.GameServer.Game.Freesr;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using System.Text;

namespace March7thHoney.Command.Command.Cmd;

[CommandInfo("json", "Game.Command.Json.Desc", "Game.Command.Json.Usage", permission: CommandPermissions.Json)]
public class CommandJson : ICommand
{
    [CommandDefault]
    public async ValueTask Import(CommandArg arg)
    {
        var player = arg.Target?.Player;
        if (player == null)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Notice.PlayerNotFound"));
            return;
        }

        var input = arg.Args.Count > 0 && !string.IsNullOrEmpty(arg.Args[0])? arg.Args[0] : null;
        if (string.IsNullOrWhiteSpace(input))
        {
            await ShowFileList(arg);
            return;
        }

        if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
        {
            var (changedAvatars, removedItems) = await FreesrShared.ClearRelicAndEquipment(player);
            if (changedAvatars.Count > 0)
                await player.SendPacket(new PacketPlayerSyncScNotify(changedAvatars));
            if (removedItems.Count > 0)
                await player.SendPacket(new PacketPlayerSyncScNotify(removedItems));

            DatabaseHelper.MarkDirty(player.Uid);
            await arg.SendMsg(I18NManager.Translate("Game.Command.Json.ClearInventory"));
            return;
        }

        if (input.Equals("export", StringComparison.OrdinalIgnoreCase))
        {
            bool geshihua = false;
            string? customName = null;
            if (arg.Args.Count == 2)
            {
                bool bushigeshihua = bool.TryParse(arg.Args[1], out geshihua);
                if (!bushigeshihua)
                {
                    customName = arg.Args[1];
                    geshihua = true;
                }
            }
            else if (arg.Args.Count == 3)
            {
                customName = arg.Args[1];
                bool bushigeshihua = bool.TryParse(arg.Args[2], out geshihua);
                if (!bushigeshihua)
                {
                    geshihua = true;
                }
            }

            if (customName != null && !IsPlainFileName(customName))
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Json.InvalidFileName", customName));
                return;
            }

            string outputFileName = customName != null
                ? customName + ".json"
                : $"freesr-data_{SanitizeFileName(player.Data.Name ?? "")}_{player.Data.Uid}_{DateTime.Now:yyMMddHHmmss}.json";
            var dir = FreesrShared.GetJsonDirectories(createIfMissing: true)[0];
            string combinedPath = Path.GetFullPath(Path.Combine(dir.FullName, outputFileName));
            if (!IsUnderDirectory(dir, combinedPath))
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Json.InvalidFileName", outputFileName));
                return;
            }

            string outputjson = await FreesrShared.ExportPlayerDataAsync(player.Uid, geshihua);
            await File.WriteAllTextAsync(combinedPath, outputjson, Encoding.UTF8);

            await arg.SendMsg(I18NManager.Translate("Game.Command.Json.ExportSuccess", outputFileName));
            return;
        }

        // Only the server terminal and the admin console may import from outside Config/Json.
        var selectedPath = ResolveInputPath(input, arg.Sender.IsConsole, out var pathError);
        if (selectedPath == null)
        {
            if (!string.IsNullOrWhiteSpace(pathError))
                await arg.SendMsg(pathError);
            return;
        }

        if (!File.Exists(selectedPath))
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Json.FileNotFound", selectedPath));
            return;
        }

        string json = string.Empty;
        try
        {
            json = await File.ReadAllTextAsync(selectedPath);
        }
        catch (Exception e)
        {
            await arg.SendMsg(I18NManager.Translate("Game.Command.Json.ReadOrParseFailed", e.Message));
            return;
        }

        await FreesrShared.ImportJson(json, player, async (msg) =>
        {
            if (msg[0] == "Game.Command.Json.ImportSummary")
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Json.ImportSummary", Path.GetFileName(selectedPath),
                    msg[1], msg[2], msg[3], msg[4], msg[5]));
            } else
            {
                await arg.SendMsg(I18NManager.Translate(msg[0], msg.Skip(1).ToArray()));
            }
        });
    }

    private static string? ResolveInputPath(string input, bool allowOutsideJsonDirectory, out string? error)
    {
        error = null;
        input = input.Trim();
        if (input.Length >= 2 && input.StartsWith('"') && input.EndsWith('"'))
            input = input[1..^1];

        if (int.TryParse(input, out var choice))
        {
            var files = FreesrShared.GetJsonFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
            if (files.Count == 0)
            {
                error = I18NManager.Translate("Game.Command.Json.NoFileFoundWithHint");
                return null;
            }

            if (choice < 1 || choice > files.Count)
            {
                error = I18NManager.Translate("Game.Command.Json.InvalidChoice", files.Count.ToString());
                return null;
            }

            return files[choice - 1].FullName;
        }

        var jsonDirs = FreesrShared.GetJsonDirectories(createIfMissing: true);
        var looksLikePath = input.Contains('/') || input.Contains('\\') || Path.IsPathRooted(input);
        if (looksLikePath)
        {
            var fullPath = Path.GetFullPath(input);
            if (allowOutsideJsonDirectory || jsonDirs.Any(dir => IsUnderDirectory(dir, fullPath)))
                return fullPath;

            error = I18NManager.Translate("Game.Command.Json.PathOutsideJsonDirectory", input);
            return null;
        }

        if (!IsPlainFileName(input))
        {
            error = I18NManager.Translate("Game.Command.Json.InvalidFileName", input);
            return null;
        }

        // Treat as filename under configured json directories.
        var fileName = input.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? input : input + ".json";
        foreach (var jsonDir in jsonDirs)
        {
            var candidate = Path.Combine(jsonDir.FullName, input);
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(jsonDir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        // Fallback to primary directory.
        return Path.Combine(jsonDirs[0].FullName, fileName);
    }

    /// <summary>
    ///     A single path segment: no directory separators, drive or volume prefix, "." / ".." or invalid characters,
    ///     so joining it to a directory can never leave that directory.
    /// </summary>
    private static bool IsPlainFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") return false;
        if (name.IndexOfAny(['/', '\\', ':']) >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
        return !Path.IsPathRooted(name) && Path.GetFileName(name) == name;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => c is '/' or '\\' or ':' || invalid.Contains(c) ? '_' : c).ToArray();
        var sanitized = new string(chars).Replace("..", "_");
        return string.IsNullOrWhiteSpace(sanitized) ? "player" : sanitized;
    }

    private static bool IsUnderDirectory(DirectoryInfo directory, string fullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(directory.FullName) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return Path.GetFullPath(fullPath).StartsWith(root, comparison);
    }


    private static async ValueTask ShowFileList(CommandArg arg)
    {
        var files = FreesrShared.GetJsonFiles().OrderBy(f => f.LastWriteTimeUtc).ToList();
        if (files.Count == 0)
        {
            var searched = FreesrShared.GetJsonDirectories(createIfMissing: true).Select(x => x.FullName).ToList();
            await arg.SendMsg(I18NManager.Translate("Game.Command.Json.NoFileFound"));
            if (searched.Count > 0)
            {
                await arg.SendMsg(I18NManager.Translate("Game.Command.Json.SearchedDirectories"));
                foreach (var s in searched)
                    await arg.SendMsg(I18NManager.Translate("Game.Command.Json.SearchedDirectoryItem", s));
            }
            return;
        }

        await arg.SendMsg(I18NManager.Translate("Game.Command.Json.FoundFiles"));
        for (var i = 0; i < files.Count; i++)
            await arg.SendMsg(I18NManager.Translate("Game.Command.Json.FileListItem", (i + 1).ToString(), files[i].Name));
        await arg.SendMsg(I18NManager.Translate("Game.Command.Json.UsageSelectHint"));
    }

}
