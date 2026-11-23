using March7thHoney.Util;

namespace March7thHoney.WebServer.Services;

/// <summary>
///     In-memory ring buffer of recent log lines, fed by <see cref="PluginEventCommon.OnLogEntry" />, so the admin
///     console can tail the server log without touching the log files. Attached by the entry point before the
///     first log line is written; safe to call from any thread.
/// </summary>
public static class AdminLogBuffer
{
    public readonly record struct Entry(long Seq, long TimeMs, string Module, string Level, string Message);

    private const int Capacity = 4000;
    private static readonly object Lock = new();
    private static readonly Entry[] Ring = new Entry[Capacity];
    private static long _nextSeq = 1;
    private static int _count;
    private static bool _attached;

    public static void Attach()
    {
        lock (Lock)
        {
            if (_attached) return;
            _attached = true;
        }

        PluginEventCommon.OnLogEntry += Append;
    }

    public static long LatestSeq
    {
        get
        {
            lock (Lock) return _nextSeq - 1;
        }
    }

    public static long OldestSeq
    {
        get
        {
            lock (Lock) return _count == 0 ? 0 : _nextSeq - _count;
        }
    }

    public static int Count
    {
        get
        {
            lock (Lock) return _count;
        }
    }

    /// <summary>
    ///     Entries newer than <paramref name="afterSeq" /> that match the optional level list and search text,
    ///     oldest first. When more than <paramref name="limit" /> lines match only the newest ones are returned
    ///     and <paramref name="truncated" /> is set.
    /// </summary>
    public static List<Entry> Query(long afterSeq, int limit, IReadOnlyCollection<string>? levels, string? search,
        out bool truncated)
    {
        limit = Math.Clamp(limit, 1, Capacity);
        var matches = new List<Entry>();
        var levelSet = levels is { Count: > 0 }
            ? new HashSet<string>(levels, StringComparer.OrdinalIgnoreCase)
            : null;
        var needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        lock (Lock)
        {
            var oldest = _count == 0 ? _nextSeq : _nextSeq - _count;
            var start = Math.Max(oldest, afterSeq + 1);
            for (var seq = start; seq < _nextSeq; seq++)
            {
                var entry = Ring[(int)((seq - 1) % Capacity)];
                if (levelSet != null && !levelSet.Contains(entry.Level)) continue;
                if (needle != null &&
                    !entry.Message.Contains(needle, StringComparison.OrdinalIgnoreCase) &&
                    !entry.Module.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    continue;

                matches.Add(entry);
            }
        }

        truncated = matches.Count > limit;
        return truncated ? matches.GetRange(matches.Count - limit, limit) : matches;
    }

    private static void Append(string module, LoggerLevel level, string message)
    {
        try
        {
            lock (Lock)
            {
                var seq = _nextSeq++;
                Ring[(int)((seq - 1) % Capacity)] = new Entry(
                    seq,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    module,
                    level.ToString(),
                    message);
                if (_count < Capacity) _count++;
            }
        }
        catch
        {
            // Never let diagnostics interfere with logging.
        }
    }
}
