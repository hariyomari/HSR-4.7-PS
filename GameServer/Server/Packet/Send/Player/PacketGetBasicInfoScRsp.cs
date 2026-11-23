using March7thHoney.GameServer.Game.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Server.Packet.Send.Player;

public class PacketGetBasicInfoScRsp : BasePacket
{
    public PacketGetBasicInfoScRsp(PlayerInstance player) : base(CmdIds.GetBasicInfoScRsp)
    {
        var proto = new GetBasicInfoScRsp
        {
            CurDay = 1,
            NextRecoverTime = player.Data.NextStaminaRecover / 1000,
            GameplayBirthday = (uint)player.Data.Birthday,
            PlayerSettingInfo = player.Data.PrivacySettings.ToSettingProto(),
            Gender = (uint)player.Data.CurrentGender,
            // Story mode reports the gender as unset until the prompt (AvatarManager.IsHeroGenderPending).
            IsGenderSet = player.AvatarManager?.IsHeroGenderPending != true
        };

        SetData(proto);
    }
}
