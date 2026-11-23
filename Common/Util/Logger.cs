using System.Diagnostics;
using System.Threading.Channels;
using March7thHoney.Internationalization;
using Spectre.Console;

namespace March7thHoney.Util;

public class Logger(string moduleName)
{
    private static FileInfo? _logFile;
    private static FileInfo? _debugLogFile;
    private static readonly object Lock = new();
    private static readonly Channel<ConsoleLogEntry> ConsoleLog = CreateConsoleLog();
    private static int _consolePending; // queued or being written
    private static volatile bool _consoleDirect;

    private readonly record struct ConsoleLogEntry(string Time, string Module, LoggerLevel Level, string Message);

    public void Log(string message, LoggerLevel level)
    {
        // Debug lines are diagnostics for developers; the console shows them only when the owner asks for it.
        Log(message, level, level != LoggerLevel.DEBUG || ShowDebugInConsole());
    }

    private void Log(string message, LoggerLevel level, bool toConsole)
    {
        lock (Lock)
        {
            var time = DateTime.Now.ToString("HH:mm:ss");
            if (toConsole) QueueConsole(new ConsoleLogEntry(time, moduleName, level, message));

            var logMessage = $"[{time}][{moduleName}][{level}] {message}";
            try
            {
                PluginEventCommon.InvokeOnLogEntry(moduleName, level, message);
            }
            catch
            {
                // A misbehaving subscriber must never break logging.
            }

            if (level == LoggerLevel.DEBUG)
            {
                WriteToDebugFile(logMessage);
                return;
            }

            WriteToFile(logMessage);
        }
    }

    private static bool ShowDebugInConsole()
    {
        try
        {
            return ConfigManager.Config.ServerOption.LogOption.ShowDebugInConsole;
        }
        catch
        {
            // Lines logged while the configuration is still being initialised fall back to the default.
            return false;
        }
    }

    private static void QueueConsole(ConsoleLogEntry entry)
    {
        if (_consoleDirect)
        {
            WriteConsole(entry);
            return;
        }

        Interlocked.Increment(ref _consolePending);
        if (!ConsoleLog.Writer.TryWrite(entry)) Interlocked.Decrement(ref _consolePending);
    }

    private static Channel<ConsoleLogEntry> CreateConsoleLog()
    {
        // A paused Windows console or a full stdout pipe must not block request handlers: lines are queued and
        // written by one background reader. The file logs and the log subscribers get every entry even when the
        // display falls behind and the oldest queued lines are dropped.
        var queue = Channel.CreateBounded<ConsoleLogEntry>(new BoundedChannelOptions(512)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        }, _ => Interlocked.Decrement(ref _consolePending));
        _ = Task.Run(async () =>
        {
            await foreach (var entry in queue.Reader.ReadAllAsync())
            {
                WriteConsole(entry);
                Interlocked.Decrement(ref _consolePending);
            }
        });
        // The reader ends with the process. At exit, give the queued lines (the details of an unhandled exception
        // are the usual case) a moment to appear, then write directly so later shutdown messages are not lost.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            var waited = Stopwatch.StartNew();
            while (Volatile.Read(ref _consolePending) > 0 && waited.ElapsedMilliseconds < 2000) Thread.Sleep(10);
            _consoleDirect = true;
        };
        return queue;
    }

    private static void WriteConsole(ConsoleLogEntry entry)
    {
        var redirected = $"[{entry.Time}][{entry.Module}] {entry.Message}";
        try
        {
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine(redirected);
                return;
            }

            var levelColor = entry.Level switch
            {
                LoggerLevel.INFO => "#7AA8DD",
                LoggerLevel.WARN => "#F6C177",
                LoggerLevel.ERROR => "#EB6F92",
                LoggerLevel.FATAL => "#C73E1D",
                LoggerLevel.DEBUG => "#9CCFD8",
                _ => "#9992C7"
            };
            var colored = $"[grey70][[{entry.Time}]][/] [#60BAEE][[{Markup.Escape(entry.Module)}]][/] " +
                          $"[{levelColor}]{entry.Level}[/] {Markup.Escape(entry.Message)}";
            var plain = $"[{entry.Time}] [{entry.Module}] {entry.Level} {entry.Message}";
            ServerConsole.WriteExternalLine(() => AnsiConsole.MarkupLine(colored), plain);
        }
        catch
        {
            try
            {
                Console.WriteLine(redirected);
            }
            catch
            {
                // A detached or closed terminal must not stop the reader; the file log already has the entry.
            }
        }
    }

    public void Info(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.INFO);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.INFO);
            Log(e.StackTrace ?? string.Empty, LoggerLevel.INFO);
        }
    }

    public void InfoI18n(string key, params string[] args)
    {
        Info(I18NManager.Translate(key, args));
    }

    public void Warn(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.WARN);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.WARN);
            Log(e.StackTrace ?? string.Empty, LoggerLevel.WARN);
        }
    }

    public void WarnI18n(string key, params string[] args)
    {
        Warn(I18NManager.Translate(key, args));
    }

    public void Error(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.ERROR);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.ERROR);
            Log(e.StackTrace ?? string.Empty, LoggerLevel.ERROR);
        }
    }

    public void ErrorI18n(string key, params string[] args)
    {
        Error(I18NManager.Translate(key, args));
    }

    public void Fatal(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.FATAL);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.FATAL);
            Log(e.StackTrace ?? string.Empty, LoggerLevel.FATAL);
        }
    }

    public void Debug(string message, Exception? e = null)
    {
        Log(message, LoggerLevel.DEBUG);
        if (e != null)
        {
            Log(e.Message, LoggerLevel.DEBUG);
            Log(e.StackTrace ?? string.Empty, LoggerLevel.DEBUG);
        }
    }

    /// <summary>
    ///     Writes a game packet line (<c>LogOption.LogPacketToConsole</c>). It is recorded at debug level in the debug
    ///     log, but always reaches the console because the owner turned the packet log on to watch it there.
    /// </summary>
    public void Packet(string message)
    {
        Log(message, LoggerLevel.DEBUG, true);
    }

    public static void SetLogFile(FileInfo file)
    {
        lock (Lock)
        {
            _logFile = file;
            Flush(file, PendingFileLines);
        }
    }

    public static void SetDebugLogFile(FileInfo file)
    {
        lock (Lock)
        {
            _debugLogFile = file;
            Flush(file, PendingDebugFileLines);
        }
    }

    public static FileInfo? CurrentLogFile => _logFile;
    public static FileInfo? CurrentDebugLogFile => _debugLogFile;

    // The log directory is configured in Config.json, which is read after the first lines are logged. Those lines are
    // held here (a bounded number, for tools that never open a log file) and written once the file is set.
    private const int MaxPendingFileLines = 1000;
    private static readonly List<string> PendingFileLines = [];
    private static readonly List<string> PendingDebugFileLines = [];

    public static void WriteToFile(string message)
    {
        lock (Lock)
        {
            if (_logFile == null) Hold(PendingFileLines, message);
            else Append(_logFile, message);
        }
    }

    public static void WriteToDebugFile(string message)
    {
        lock (Lock)
        {
            if (_debugLogFile == null) Hold(PendingDebugFileLines, message);
            else Append(_debugLogFile, message);
        }
    }

    private static void Hold(List<string> pending, string message)
    {
        if (pending.Count < MaxPendingFileLines) pending.Add(message);
    }

    private static void Flush(FileInfo file, List<string> pending)
    {
        foreach (var message in pending) Append(file, message);
        pending.Clear();
    }

    private static void Append(FileInfo file, string message)
    {
        try
        {
            file.Directory?.Create();
            using var sw = file.AppendText();
            sw.WriteLine(message);
        }
        catch
        {
            // An unwritable log file must not break the caller; the console and the log subscribers still get the line.
        }
    }

    public static Logger GetByClassName()
    {
        return new Logger(new StackTrace().GetFrame(1)?.GetMethod()?.ReflectedType?.Name ?? string.Empty);
    }
}

public enum LoggerLevel
{
    INFO,
    WARN,
    ERROR,
    FATAL,
    DEBUG
}

