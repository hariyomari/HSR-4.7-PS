using March7thHoney.GameServer.Game.Lobby;
using March7thHoney.GameServer.Game.Mission;

namespace March7thHoney.GameServer.Server;

public static class ServerUtils
{
    // Server-wide singleton for the multiplayer lobby (no reflection — NativeAOT safe).
    public static LobbyServerManager LobbyServerManager { get; } = new();

    public static void InitializeHandlers()
    {
        // Mission finish-action / finish-type handlers, populated by the source-generated
        // GeneratedMissionHandlers (MissionHandlerRegistryGenerator) — no reflection (NativeAOT safe).
        GeneratedMissionHandlers.Register();
    }
}
