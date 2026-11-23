using March7thHoney.GameServer.Game.Tutorial;
using March7thHoney.GameServer.Server.ClientLua;
using March7thHoney.GameServer.Server.Packet.Send.Tutorial;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.Packet.Recv.Tutorial;

[Opcode(CmdIds.GetTutorialCsReq)]
public class HandlerGetTutorialCsReq : PlayerHandler
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player)
    {
        await SendPlayerData(connection);
        if (!player.MissionEnabled) return; // story off: the client keeps its tutorial module idle

        // A fresh account reports the six system tutorials the official server pre-finishes; story tutorials
        // (1xxx) are opened by the client itself and persisted from then on.
        TutorialHelper.SeedFreshAccount(player);

        // Auto-repair (ServerOption.TutorialInputGuard on): a persisted UNLOCK the server can never finish would hold
        // player input forever (tutorials 2012/3006/4003 froze players until logout) - report and persist FINISH.
        var tutorials = player.TutorialData!.Tutorials;
        foreach (var id in tutorials.Where(kv => kv.Value == TutorialStatus.TutorialUnlock).Select(kv => kv.Key).ToList())
            if (!TutorialHelper.MayStayOpen(player, id))
                tutorials[id] = TutorialStatus.TutorialFinish;

        await connection.SendPacket(new PacketGetTutorialScRsp(player));
    }

    private async Task SendPlayerData(Connection connection)
    {
        var filePath = Path.Combine(ClientLuaDirectory.Root, "welcome.lua");
        if (File.Exists(filePath))
        {
            var fileBytes = await File.ReadAllBytesAsync(filePath);
            await connection.ClientLua.SendScriptAsync("welcome.lua", fileBytes);
        }
    }
}
