namespace March7thHoney.GameServer.Server.Packet;

public static class HandlerManager
{
    /// <summary>cmdId → handler table, populated by the source generator at <see cref="Init" />.</summary>
    public static IReadOnlyDictionary<int, Handler> Handlers { get; private set; } =
        new Dictionary<int, Handler>();

    public static void Init()
    {
        // Populated by the source generator (GeneratedHandlerRegistry) from every [Opcode] class — no reflection.
        Handlers = GeneratedHandlerRegistry.Build();
    }

    public static Handler? GetHandler(int cmdId)
    {
        return Handlers.GetValueOrDefault(cmdId);
    }
}
