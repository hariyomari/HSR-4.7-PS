using March7thHoney.Util;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static void MapLogRoutes(IEndpointRouteBuilder api)
    {
        api.MapGet("/logs", GetLogs);
        api.MapGet("/logs/files", GetLogFiles);
        api.MapGet("/logs/files/{name}", DownloadLogFile);
    }

    private static IResult GetLogs(long? after, int? limit, string? levels, string? search)
    {
        var levelList = (levels ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        var entries = AdminLogBuffer.Query(after.GetValueOrDefault(), limit.GetValueOrDefault(300), levelList,
            search, out var truncated);

        return Success(new LogsData(
            entries.Select(entry => new LogEntry(entry.Seq, entry.TimeMs, entry.Module, entry.Level, entry.Message))
                .ToList(),
            AdminLogBuffer.LatestSeq,
            AdminLogBuffer.OldestSeq,
            AdminLogBuffer.Count,
            truncated));
    }

    private static IResult GetLogFiles()
    {
        var directory = new DirectoryInfo(ConfigManager.Config.Path.LogPath);
        if (!directory.Exists)
            return Success(new LogFilesData([], directory.FullName));

        var activeName = Logger.CurrentLogFile?.Name;
        var activeDebugName = Logger.CurrentDebugLogFile?.Name;
        var files = directory.GetFiles()
            .Where(file => IsLogFileName(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => new LogFileInfo(
                file.Name,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeSeconds(),
                string.Equals(file.Name, activeName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file.Name, activeDebugName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return Success(new LogFilesData(files, directory.FullName));
    }

    private static IResult DownloadLogFile(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (!IsLogFileName(name) || name != Path.GetFileName(name))
            return Error("Invalid log file name");

        var directory = new DirectoryInfo(ConfigManager.Config.Path.LogPath);
        var path = Path.Combine(directory.FullName, name);
        if (!File.Exists(path))
            return Error("Log file not found", statusCode: StatusCodes.Status404NotFound);

        // The active log is appended to concurrently; share the handle so the download never blocks logging.
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var contentType = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            ? "application/zip"
            : "text/plain; charset=utf-8";
        return Results.Stream(stream, contentType, name);
    }

    private static bool IsLogFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128) return false;
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..")) return false;
        return name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }
}
