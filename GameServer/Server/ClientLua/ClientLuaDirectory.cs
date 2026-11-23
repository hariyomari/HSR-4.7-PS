namespace March7thHoney.GameServer.Server.ClientLua;

/// <summary>
///     The operator's folder of client Lua scripts: the login auto-run scripts, the tutorial welcome script and the
///     files /windy and /htl send. It is resolved against the current directory on every use, because the regression
///     runners switch the current directory to a scratch folder before they run commands.
/// </summary>
public static class ClientLuaDirectory
{
    public const string Name = "Lua";

    public static string Root => Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, Name));
}
