using March7thHoney.Database;
using March7thHoney.Database.Friend;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Command;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.GameServer.Server.Packet.Send.Chat;
using March7thHoney.GameServer.Server.Packet.Send.Friend;
using March7thHoney.Kcp;
using March7thHoney.Proto;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Friend;

public partial class FriendManager(PlayerInstance player) : BasePlayerManager(player)
{
    public FriendData FriendData { get; set; } =
        DatabaseHelper.Instance!.GetInstanceOrCreateNew<FriendData>(player.Uid);

    /// <summary>Drops the development records (shown on the friend profile) that are a week old or older.</summary>
    public void PruneExpiredDevelopmentRecords()
    {
        if (Player.FriendRecordData is not { } records) return;
        foreach (var friendDevelopmentInfoPb in records.DevelopmentInfos.ToArray())
            if (Extensions.GetUnixSec() - friendDevelopmentInfoPb.Time >=
                TimeSpan.TicksPerDay * 7 / TimeSpan.TicksPerSecond)
                records.DevelopmentInfos.Remove(friendDevelopmentInfoPb);
    }

    public async ValueTask<Retcode> AddFriend(int targetUid)
    {
        if (targetUid == Player.Uid) return Retcode.RetSucc; // Cannot add self
        // The command bot and Global Chat contacts are permanent synthetic friends, never real apply targets.
        if (ConfigManager.Config.ServerOption.IsSystemProfileUid(targetUid)) return Retcode.RetFriendAlreadyIsFriend;
        if (FriendData.FriendDetailList.ContainsKey(targetUid)) return Retcode.RetFriendAlreadyIsFriend;
        if (FriendData.BlackList.Contains(targetUid)) return Retcode.RetFriendInBlacklist;
        if (FriendData.SendApplyList.Contains(targetUid)) return Retcode.RetSucc; // Already send apply

        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null) return Retcode.RetFriendPlayerNotFound;
        if (target.BlackList.Contains(Player.Uid)) return Retcode.RetFriendInTargetBlacklist;
        if (target.ReceiveApplyList.Contains(targetUid)) return Retcode.RetSucc; // Already receive apply

        FriendData.SendApplyList.Add(targetUid);
        target.ReceiveApplyList.Add(Player.Uid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncApplyFriendScNotify(Player.Data));

        DatabaseHelper.MarkDirty(targetUid);
        return Retcode.RetSucc;
    }

    public async ValueTask<PlayerData?> ConfirmAddFriend(int targetUid)
    {
        if (targetUid == Player.Uid) return null; // Cannot add self
        if (ConfigManager.Config.ServerOption.IsSystemProfileUid(targetUid)) return null;
        if (FriendData.FriendDetailList.ContainsKey(targetUid)) return null;
        if (FriendData.BlackList.Contains(targetUid)) return null;

        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        var targetData = PlayerData.GetPlayerByUid(targetUid);
        if (target == null || targetData == null) return null;
        if (target.FriendDetailList.ContainsKey(Player.Uid)) return null;
        if (target.BlackList.Contains(Player.Uid)) return null;
        if (!HasPendingApplyFrom(targetUid, target)) return null;

        FriendData.ReceiveApplyList.Remove(targetUid);
        FriendData.FriendDetailList.Add(targetUid, new FriendDetailData());
        target.SendApplyList.Remove(Player.Uid);
        target.FriendDetailList.Add(Player.Uid, new FriendDetailData());

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncHandleFriendScNotify((uint)Player.Uid, true, Player.Data));

        DatabaseHelper.MarkDirty(targetUid);
        return targetData;
    }

    public async ValueTask RefuseAddFriend(int targetUid)
    {
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null || !HasPendingApplyFrom(targetUid, target)) return;

        FriendData.ReceiveApplyList.Remove(targetUid);
        target.SendApplyList.Remove(Player.Uid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncHandleFriendScNotify((uint)Player.Uid, false, Player.Data));

        DatabaseHelper.MarkDirty(targetUid);
    }

    /// <summary>
    ///     The uid in a HandleFriendCsReq comes from the client, so only an apply recorded on both sides may be handled.
    ///     A one-sided leftover entry in this player's received list is dropped, since it can never be accepted.
    /// </summary>
    private bool HasPendingApplyFrom(int targetUid, FriendData target)
    {
        if (!FriendData.ReceiveApplyList.Contains(targetUid)) return false;
        if (target.SendApplyList.Contains(Player.Uid)) return true;

        FriendData.ReceiveApplyList.Remove(targetUid);
        return false;
    }

    public async ValueTask<PlayerData?> AddBlackList(int targetUid)
    {
        if (ConfigManager.Config.ServerOption.IsSystemProfileUid(targetUid)) return null;
        var blackInfo = GetFriendPlayerData([targetUid]).FirstOrDefault(x => x.Uid == targetUid);
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (blackInfo == null || target == null) return null;

        FriendData.FriendDetailList.Remove(targetUid);
        target.FriendDetailList.Remove(Player.Uid);
        if (!FriendData.BlackList.Contains(targetUid))
            FriendData.BlackList.Add(targetUid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncAddBlacklistScNotify(Player.Uid));

        DatabaseHelper.MarkDirty(targetUid);
        return blackInfo;
    }

    public void RemoveBlackList(int targetUid)
    {
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null) return;
        FriendData.BlackList.Remove(targetUid);
    }

    public async ValueTask<int?> RemoveFriend(int targetUid)
    {
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(targetUid);
        if (target == null) return null;

        FriendData.FriendDetailList.Remove(targetUid);
        target.FriendDetailList.Remove(Player.Uid);

        var targetPlayer = Listener.GetActiveConnection(targetUid);
        if (targetPlayer != null)
            await targetPlayer.SendPacket(new PacketSyncDeleteFriendScNotify(Player.Uid));

        DatabaseHelper.MarkDirty(targetUid);
        return targetUid;
    }

    /// <summary>Upper bound on distinct recipients of one SendMsgCsReq; the client sends one per private chat.</summary>
    public const int MaxMessageTargets = 10;

    /// <summary>Messages kept per conversation; older ones are dropped so stored history stays bounded.</summary>
    public const int MaxConversationHistory = 100;

    /// <summary>
    ///     Sends one message to every target of a request. Returns the first rejection, or
    ///     <see cref="Retcode.RetFriendTargetNumberLimit" /> without sending anything when there are too many targets.
    /// </summary>
    public async ValueTask<Retcode> SendMessage(IEnumerable<uint> targetUids, string? message = null,
        int? extraId = null)
    {
        var targets = targetUids.Distinct().ToList();
        if (targets.Count > MaxMessageTargets) return Retcode.RetFriendTargetNumberLimit;

        var result = Retcode.RetSucc;
        foreach (var targetUid in targets)
        {
            var ret = await SendMessage((int)targetUid, message, extraId);
            if (result == Retcode.RetSucc) result = ret;
        }

        return result;
    }

    public async ValueTask<Retcode> SendMessage(int recvUid, string? message = null, int? extraId = null)
    {
        var sendUid = Player.Uid;
        if (GlobalChatManager.IsGlobalChatUid(recvUid))
        {
            // Relayed to every online player instead of being delivered as a private message; text only.
            if (extraId is null or 0)
                await GlobalChatManager.HandleMessageAsync(Player, message);
            return Retcode.RetSucc;
        }

        var data = BuildChatData(sendUid, recvUid, message, extraId);

        if (recvUid == ConfigManager.Config.ServerOption.ServerProfile.Uid)
        {
            await PushPrivateMessageAsync(recvUid, (uint)sendUid, data);
            if (message?.StartsWith('/') == true)
                CommandExecutor.ExecuteCommand(new PlayerCommandSender(Player), message[1..]);
            return Retcode.RetSucc;
        }

        // Private messages need a mutual friendship and no block on either side.
        if (FriendData.BlackList.Contains(recvUid)) return Retcode.RetFriendInBlacklist;
        if (recvUid == sendUid || !FriendData.FriendDetailList.ContainsKey(recvUid))
            return Retcode.RetFriendIsNotFriend;
        var target = DatabaseHelper.Instance!.GetInstance<FriendData>(recvUid);
        if (target == null) return Retcode.RetFriendPlayerNotFound;
        if (target.BlackList.Contains(sendUid)) return Retcode.RetFriendInTargetBlacklist;
        if (!target.FriendDetailList.ContainsKey(sendUid)) return Retcode.RetFriendIsNotFriend;

        await PushPrivateMessageAsync(recvUid, (uint)sendUid, data);

        var recvManager = Listener.GetActiveConnection(recvUid)?.Player?.FriendManager;
        if (recvManager != null)
        {
            await recvManager.ReceiveMessage(sendUid, recvUid, message, extraId);
        }
        else
        {
            AppendHistory(target.ChatHistory, sendUid, data);
            DatabaseHelper.MarkDirty(recvUid);
        }

        return Retcode.RetSucc;
    }

    public async ValueTask SendInviteMessage(int sendUid, int recvUid, object info)
    {
        await ValueTask.CompletedTask;
    }

    public async ValueTask ReceiveMessage(int sendUid, int recvUid, string? message = null, int? extraId = null)
    {
        var data = BuildChatData(sendUid, recvUid, message, extraId);
        await PushPrivateMessageAsync(sendUid, (uint)sendUid, data);
    }

    public async ValueTask ReceiveInviteMessage(int sendUid, int recvUid, object info)
    {
        await ValueTask.CompletedTask;
    }

    public FriendDetailData? GetFriendDetailData(int uid)
    {
        if (ConfigManager.Config.ServerOption.IsSystemProfileUid(uid))
            return new FriendDetailData { IsMark = true };

        if (!FriendData.FriendDetailList.TryGetValue(uid, out var friend)) return null;

        return friend;
    }

    public List<ChatMessageData> GetHistoryInfo(int uid)
    {
        if (!FriendData.ChatHistory.TryGetValue(uid, out var history)) return [];

        var info = new List<ChatMessageData>();

        foreach (var chat in history.MessageList)
        {
            var chatData = new ChatData();
            if (chat.ExtraId > 0)
                chatData.ExtraId = (uint)chat.ExtraId;
            else
                chatData.MessageText = chat.Message;

            info.Add(new ChatMessageData
            {
                CreateTime = (ulong)chat.SendTime,
                CKHPFFENOBE = new EKNABKLPEEL
                {
                    KPOBMNLKLOK = HCMEILLLKBD.Jdoaipkbipe,
                    RoleId = (uint)chat.SendUid
                },
                MessageDatas =
                {
                    new MessageChatData
                    {
                        MessageType = chat.ExtraId > 0 ? MsgType.Emoji : MsgType.CustomText,
                        ChatData = chatData
                    }
                }
            });
        }

        info.Reverse();

        return info;
    }

    private static FriendChatData BuildChatData(int sendUid, int recvUid, string? message, int? extraId)
    {
        return new FriendChatData
        {
            SendUid = sendUid,
            ReceiveUid = recvUid,
            Message = message ?? "",
            ExtraId = extraId ?? 0,
            SendTime = Extensions.GetUnixSec()
        };
    }

    private static void AppendHistory(Dictionary<int, FriendChatHistory> chatHistory, int historyKeyUid,
        FriendChatData data)
    {
        lock (chatHistory)
        {
            if (!chatHistory.TryGetValue(historyKeyUid, out var history))
                chatHistory[historyKeyUid] = history = new FriendChatHistory();

            history.MessageList.Add(data);
            if (history.MessageList.Count > MaxConversationHistory)
                history.MessageList.RemoveRange(0, history.MessageList.Count - MaxConversationHistory);
        }
    }

    private async ValueTask PushPrivateMessageAsync(int historyKeyUid, uint sourceUid, FriendChatData data)
    {
        AppendHistory(FriendData.ChatHistory, historyKeyUid, data);

        await Player.SendPacket(new PacketRevcMsgScNotify(
            sourceUid,
            (uint)historyKeyUid,
            data.ExtraId > 0 ? null : data.Message,
            (uint)data.ExtraId,
            data.SendTime));
    }

    public List<PlayerData> GetFriendPlayerData(List<int>? uids = null)
    {
        var list = new List<PlayerData>();
        uids ??= [.. FriendData.FriendDetailList.Keys];

        var serverOption = ConfigManager.Config.ServerOption;
        foreach (var friend in uids)
        {
            if (serverOption.IsSystemProfileUid(friend)) continue; // appended below
            var player = PlayerData.GetPlayerByUid(friend);
            if (player != null) list.Add(player);
        }

        var serverProfile = serverOption.ServerProfile;
        list.Add(new PlayerData
        {
            Uid = serverProfile.Uid,
            HeadIcon = serverProfile.HeadIcon,
            Signature = serverProfile.Signature,
            Level = serverProfile.Level,
            WorldLevel = 0,
            Name = serverProfile.Name,
            ChatBubble = serverProfile.ChatBubbleId,
            PersonalCard = serverProfile.PersonalCardId
        });

        if (serverOption.GetGlobalChatUid() is { } globalChatUid)
        {
            var globalChat = serverOption.GlobalChat;
            list.Add(new PlayerData
            {
                Uid = globalChatUid,
                HeadIcon = globalChat.HeadIcon,
                Signature = globalChat.Signature,
                Level = globalChat.Level,
                WorldLevel = 0,
                Name = globalChat.Name,
                ChatBubble = globalChat.ChatBubbleId,
                PersonalCard = globalChat.PersonalCardId
            });
        }

        return list;
    }

    public List<PlayerData> GetBlackList()
    {
        List<PlayerData> list = [];

        foreach (var friend in FriendData.BlackList)
        {
            var player = PlayerData.GetPlayerByUid(friend);

            if (player != null) list.Add(player);
        }

        return list;
    }

    public List<PlayerData> GetSendApplyList()
    {
        List<PlayerData> list = [];

        foreach (var friend in FriendData.SendApplyList)
        {
            var player = PlayerData.GetPlayerByUid(friend);

            if (player != null) list.Add(player);
        }

        return list;
    }

    public List<PlayerData> GetReceiveApplyList()
    {
        List<PlayerData> list = [];

        foreach (var friend in FriendData.ReceiveApplyList)
        {
            var player = PlayerData.GetPlayerByUid(friend);

            if (player != null) list.Add(player);
        }

        return list;
    }

    public List<PlayerData> GetRandomFriend()
    {
        var list = new List<PlayerData>();

        foreach (var player in Listener.GetActivePlayers())
        {
            if (player.Uid == Player.Uid) continue;
            var data = player.Data;
            if (data == null) continue;
            list.Add(data);
        }

        return list.Take(20).ToList();
    }

    public void RemarkFriendName(int uid, string remarkName)
    {
        if (!FriendData.FriendDetailList.TryGetValue(uid, out var friend)) return;
        friend.RemarkName = remarkName;
    }

    public void MarkFriend(int uid, bool isMark)
    {
        if (!FriendData.FriendDetailList.TryGetValue(uid, out var friend)) return;
        friend.IsMark = isMark;
    }

    public GetFriendListInfoScRsp ToProto()
    {
        var proto = new GetFriendListInfoScRsp();

        foreach (var player in GetFriendPlayerData())
        {
            var status = Listener.GetActiveConnection(player.Uid) == null
                ? FriendOnlineStatus.Offline
                : FriendOnlineStatus.Online;
            var friend = GetFriendDetailData(player.Uid) ?? new FriendDetailData();

            proto.FriendList.Add(new FriendSimpleInfo
            {
                PlayerInfo = player.ToSimpleProto(status),
                IsMarked = friend.IsMark,
                RemarkName = friend.RemarkName
            });
        }

        foreach (var player in GetBlackList())
        {
            var status = Listener.GetActiveConnection(player.Uid) == null
                ? FriendOnlineStatus.Offline
                : FriendOnlineStatus.Online;
            proto.BlackList.Add(player.ToSimpleProto(status));
        }

        return proto;
    }

    public GetFriendApplyListInfoScRsp ToApplyListProto()
    {
        GetFriendApplyListInfoScRsp proto = new();

        foreach (var player in GetSendApplyList()) proto.SendApplyList.Add((uint)player.Uid);

        foreach (var player in GetReceiveApplyList())
        {
            var status = Listener.GetActiveConnection(player.Uid) == null
                ? FriendOnlineStatus.Offline
                : FriendOnlineStatus.Online;
            proto.ReceiveApplyList.Add(new FriendApplyInfo
            {
                PlayerInfo = player.ToSimpleProto(status)
            });
        }

        return proto;
    }
}
