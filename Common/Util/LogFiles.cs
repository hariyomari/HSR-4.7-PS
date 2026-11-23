using System.IO.Compression;

namespace March7thHoney.Util;

/// <summary>The server's main and debug log files in the configured log directory.</summary>
public static class LogFiles
{
    /// <summary>
    ///     Packs every earlier run's <c>.log</c> and its <c>-debug.log</c> into one zip, then points <see cref="Logger" />
    ///     at a new <c>yyyy-MM-dd-N.log</c> and <c>yyyy-MM-dd-N-debug.log</c> whose number no log or zip uses yet.
    /// </summary>
    public static void RotateAndOpen(string logPath)
    {
        PackPreviousRuns(logPath);

        var counter = 0;
        FileInfo file;
        while (true)
        {
            file = new FileInfo(Path.Combine(logPath, $"{DateTime.Now:yyyy-MM-dd}-{++counter}.log"));
            var zip = new FileInfo(Path.Combine(logPath, $"{DateTime.Now:yyyy-MM-dd}-{counter}.zip"));
            if (file is not { Exists: false, Directory: not null }) continue;
            if (zip is not { Exists: false, Directory: not null }) continue;
            file.Directory.Create();
            break;
        }

        var debugFile = new FileInfo(Path.Combine(logPath, $"{DateTime.Now:yyyy-MM-dd}-{counter}-debug.log"));
        Logger.SetLogFile(file);
        Logger.SetDebugLogFile(debugFile);
    }

    private static void PackPreviousRuns(string logPath)
    {
        var logDirectory = new DirectoryInfo(logPath);
        if (!logDirectory.Exists) return;

        List<string> packed = [];
        foreach (var oldFile in logDirectory.GetFiles().ToArray())
        {
            if (!oldFile.Name.EndsWith(".log")) continue;
            if (oldFile.Name.EndsWith("-debug.log")) continue;
            if (packed.Contains(oldFile.Name)) continue;

            var fileName = oldFile.Name.Replace(".log", "");
            var debugFileName = fileName + "-debug";
            var oldDebugFile = logDirectory.GetFiles(debugFileName + ".log").FirstOrDefault();

            if (!oldFile.Exists) continue;

            var zipFile = new FileInfo(Path.Combine(logPath, fileName + ".zip"));
            if (zipFile.Exists) zipFile.Delete();
            using (var zip = ZipFile.Open(zipFile.FullName, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(oldFile.FullName, oldFile.Name);
                if (oldDebugFile is { Exists: true })
                    zip.CreateEntryFromFile(oldDebugFile.FullName, oldDebugFile.Name);
            }

            oldFile.Delete();
            oldDebugFile?.Delete();
            packed.Add(oldFile.Name);
            packed.Add(oldDebugFile?.Name ?? "");
        }
    }
}
