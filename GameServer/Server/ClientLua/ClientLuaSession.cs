using System.Text;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.ClientLua;

/// <summary>
///     The client-Lua side of one connection: pushes Lua chunks to the client and keeps the set of named scripts sent.
///     The client protocol does not acknowledge execution, so the set deliberately records server sends rather than
///     claiming the scripts ran.
/// </summary>
public sealed class ClientLuaSession(Connection connection)
{
    private readonly Lock _sentScriptsLock = new();
    private readonly HashSet<string> _sentScripts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pushes an anonymous Lua chunk; it is not recorded as a sent script.</summary>
    public Task SendAsync(byte[] lua)
    {
        return connection.SendPacket(new PacketClientDownloadDataScNotify(lua));
    }

    /// <summary>Pushes the script at <paramref name="relativePath" /> (under the Lua folder) and records it.</summary>
    public async Task SendScriptAsync(string relativePath, byte[] lua)
    {
        await SendAsync(lua);
        RecordSentScript(relativePath);
    }

    public void RecordSentScript(string relativePath)
    {
        var normalized = relativePath.Replace("\\", "/", StringComparison.Ordinal).TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized)) return;

        lock (_sentScriptsLock)
            _sentScripts.Add(normalized);
    }

    public IReadOnlyList<string> GetSentScripts()
    {
        lock (_sentScriptsLock)
            return _sentScripts.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Repaints the version overlay, for example after the player renamed themselves.</summary>
    public async Task SendWatermarkAsync()
    {
        if (connection.Player is not { } player) return;
        var lua = WatermarkLua.Build(player);
        if (lua != null) await SendAsync(Encoding.UTF8.GetBytes(lua));
    }

    /// <summary>
    ///     Sends the watermark and the network status overlay as one chunk. The watermark runs inside its own pcall so
    ///     that an error raised there cannot keep the status overlay from being applied or reset.
    /// </summary>
    public async Task SendClientUiAsync()
    {
        if (connection.Player is not { } player) return;
        var watermark = WatermarkLua.Build(player);
        var signal = NetStatusLua.Build(ConfigManager.Config.ServerOption.NetStatus);
        var lua = watermark == null ? signal : $"pcall(function()\n{watermark}\nend)\n{signal}";
        await SendAsync(Encoding.UTF8.GetBytes(lua));
    }
}
