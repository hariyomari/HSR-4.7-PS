namespace March7thHoney.Util;

public static class PluginEventCommon
{
    #region Common

    public delegate void OnLogEntryHandler(string module, LoggerLevel level, string message);

    #endregion

    #region Event

    /// <summary>Every log entry, with module, level and message kept separate.</summary>
    public static event OnLogEntryHandler? OnLogEntry;

    #endregion

    public static void InvokeOnLogEntry(string module, LoggerLevel level, string message)
    {
        OnLogEntry?.Invoke(module, level, message);
    }
}