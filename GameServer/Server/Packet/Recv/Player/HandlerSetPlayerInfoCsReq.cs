using March7thHoney.GameServer.Game.Avatar;
using March7thHoney.GameServer.Server.Packet.Send.Player;
using March7thHoney.GameServer.Server.Packet.Send.PlayerSync;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Player;

[Opcode(CmdIds.SetPlayerInfoCsReq)]
public class HandlerSetPlayerInfoCsReq : Handler<SetPlayerInfoCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, SetPlayerInfoCsReq req)
    {
        if (req == null) return;
        player.Data.Name = req.Nickname;
        if (req.Gender == Gender.None)
        {
            await connection.SendPacket(new PacketSetPlayerInfoScRsp(player, req.IsModify));
            await connection.SendPacket(new PacketPlayerSyncScNotify(player.ToProto()));
            await connection.SendWatermarkLuaAsync();
            return;
        }

        await player.AvatarManager!.SetHeroGenderAtCreation(req.Gender);

        var heroAvatarId = player.Data.CurrentGender == Gender.Woman ? 8002 : 8001;
        await player.LineupManager!.AddAvatarToRealTeam(heroAvatarId);
        await player.LineupManager!.AddAvatarToRealTeam(1001);
        await player.MissionManager!.FinishSubMission(AvatarManager.NameAndGenderSubMissionId);

        await connection.SendPacket(new PacketSetPlayerInfoScRsp(player, req.IsModify));
        await connection.SendPacket(new PacketPlayerSyncScNotify(player.ToProto()));
        await connection.SendWatermarkLuaAsync();
    }
}
