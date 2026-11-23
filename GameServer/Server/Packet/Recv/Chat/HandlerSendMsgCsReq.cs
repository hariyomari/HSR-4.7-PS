using March7thHoney.GameServer.Server.Packet.Send.Chat;
using March7thHoney.Kcp;
using March7thHoney.Proto;

namespace March7thHoney.GameServer.Server.Packet.Recv.Chat;

[Opcode(CmdIds.SendMsgCsReq)]
public class HandlerSendMsgCsReq : Handler<SendMsgCsReq>
{
    protected override async Task OnHandle(Connection connection, PlayerInstance player, SendMsgCsReq req)
    {
        var nestedChatData = req.MessageDatas?.ChatData;

        string? text = null;
        if (!string.IsNullOrEmpty(nestedChatData?.MessageText))
            text = nestedChatData.MessageText;

        text = text?.Trim('\0').Trim();

        var extraId = nestedChatData?.ExtraId ?? 0;

        var msgType = MsgType.None;
        if (req.MessageDatas != null && req.MessageDatas.MessageType != MsgType.None)
            msgType = req.MessageDatas.MessageType;
        else if (!string.IsNullOrWhiteSpace(text))
            msgType = MsgType.CustomText;
        else if (extraId != 0)
            msgType = MsgType.Emoji;

        if (req.TargetList.Count == 0)
        {
            await connection.SendPacket(CmdIds.SendMsgScRsp);
            return;
        }

        var retcode = msgType switch
        {
            MsgType.CustomText => await player.FriendManager!.SendMessage(req.TargetList, text),
            MsgType.Emoji => await player.FriendManager!.SendMessage(req.TargetList, null, (int)extraId),
            _ => Retcode.RetSucc
        };

        await connection.SendPacket(new PacketSendMsgScRsp(retcode));
    }
}
