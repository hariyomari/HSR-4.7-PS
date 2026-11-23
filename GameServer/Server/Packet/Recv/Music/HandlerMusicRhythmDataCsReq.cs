using March7thHoney.GameServer.Server.Packet.Send.Music;
using March7thHoney.Kcp;

namespace March7thHoney.GameServer.Server.Packet.Recv.Music;

[Opcode(CmdIds.MusicRhythmDataCsReq)]
public class HandlerMusicRhythmDataCsReq : Handler
{
    public override async Task OnHandle(Connection connection, byte[] header, byte[] data)
    {
        await connection.SendPacket(new PacketMusicRhythmDataScRsp());

        // Unlock max difficulty level
        await connection.SendPacket(CmdIds.MusicRhythmMaxDifficultyLevelsUnlockNotify);

        // TODO: the unlock notifies for songs, song effects and tracks are not sent; their fields are unknown.
    }
}
