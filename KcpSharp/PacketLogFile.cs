namespace March7thHoney.Kcp;

/// <summary>
///     A connection's own packet log (<c>LogOption.SavePersonalDebugFile</c>). Sends on several async flows log at
///     the same time, so writes are serialised. The file is opened on the first write and closed with the connection;
///     anything logged after that is dropped.
/// </summary>
internal sealed class PacketLogFile : IDisposable
{
    private readonly Lock _lock = new();
    private string _path = "";
    private StreamWriter? _writer;
    private bool _disposed;

    /// <summary>Where the log goes; nothing is written until a path is set.</summary>
    public void SetPath(string path)
    {
        lock (_lock)
            _path = path;
    }

    public void WriteLine(string line)
    {
        lock (_lock)
        {
            if (_disposed || _path == "") return;
            if (_writer == null)
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                _writer = new StreamWriter(_path, true);
            }

            _writer.WriteLine(line);
            _writer.Flush();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
