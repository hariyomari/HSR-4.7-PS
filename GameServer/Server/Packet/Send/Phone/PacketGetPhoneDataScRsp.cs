using March7thHoney.GameServer.Game.Player;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Send.Phone;

public class PacketGetPhoneDataScRsp : BasePacket
{
    public PacketGetPhoneDataScRsp(PlayerInstance player) : base(CmdIds.GetPhoneDataScRsp)
    {
        var proto = new GetPhoneDataScRsp
        {
            CurChatBubble = (uint)player.Data.ChatBubble,
            CurPhoneTheme = (uint)player.Data.PhoneTheme,
            CurPhoneCase = (uint)player.Data.PhoneCase
        };

        // The proto generated from the decompiled client has the names of these two list fields swapped; the values
        // are assigned the way official captures show.
        foreach (var item in player.PlayerUnlockData!.PhoneThemes) proto.OwnedChatBubbles.Add((uint)item);
        foreach (var item in player.PlayerUnlockData!.ChatBubbles) proto.OwnedPhoneThemes.Add((uint)item);
        foreach (var item in player.PlayerUnlockData!.PhoneCases) proto.OwnedPhoneCases.Add((uint)item);

        SetData(proto);
    }
}
