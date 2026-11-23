using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using March7thHoney.Database;
using March7thHoney.Database.Friend;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.GameServer.Server.Packet.Send.Chat;
using March7thHoney.Internationalization;
using March7thHoney.Kcp;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Friend;

public enum GlobalChatRelayResultEnum
{
    Relayed,
    Ignored,
    Disabled,
    TooLong,
    RateLimited
}

/// <summary>
///     Server-wide chat through the synthetic Global Chat contact (<c>ServerOption.GlobalChat</c>). A message sent to
///     the contact is relayed to every online player as a private message from that contact and kept in each
///     recipient's own conversation history with it; nothing is stored server-wide except the in-memory rate limit.
/// </summary>
public static class GlobalChatManager
{
    private const int DefaultMaxHistory = 15;
    private const int DiscordContentLimit = 2000;

    private static readonly Logger Logger = new("GlobalChat");
    private static readonly HttpClient WebhookClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly object RateLimitLock = new();
    private static readonly Dictionary<int, Queue<DateTime>> SendLog = [];

    public static bool IsGlobalChatUid(int uid)
    {
        return ConfigManager.Config.ServerOption.GetGlobalChatUid() == uid;
    }

    /// <summary>Relays a message from <paramref name="sender" /> to every online ACTIVE player.</summary>
    public static ValueTask<GlobalChatRelayResultEnum> HandleMessageAsync(PlayerInstance sender, string? text)
    {
        return RelayAsync(sender, text, GetOnlinePlayers(), DateTime.UtcNow);
    }

    public static List<PlayerInstance> GetOnlinePlayers()
    {
        return Listener.GetActivePlayers().ToList();
    }

    /// <summary>
    ///     Validates, rate-limits and relays one message to <paramref name="recipients" /> (the sender always receives
    ///     it as well). Rejections are answered to the sender only, in the sender's language, and are not stored.
    /// </summary>
    public static async ValueTask<GlobalChatRelayResultEnum> RelayAsync(PlayerInstance sender, string? text,
        IEnumerable<PlayerInstance> recipients, DateTime now)
    {
        var options = ConfigManager.Config.ServerOption;
        if (options.GetGlobalChatUid() is not { } globalUid) return GlobalChatRelayResultEnum.Disabled;

        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return GlobalChatRelayResultEnum.Ignored;

        var config = options.GlobalChat;
        if (config.MaxLength > 0 && new StringInfo(text).LengthInTextElements > config.MaxLength)
        {
            await NotifySenderAsync(sender, globalUid, I18NManager.TranslateAsCertainLang(sender.EffectiveLanguage,
                "Game.GlobalChat.TooLong", config.MaxLength.ToString()));
            return GlobalChatRelayResultEnum.TooLong;
        }

        if (!TryConsumeRateLimit(sender.Uid, now))
        {
            await NotifySenderAsync(sender, globalUid, I18NManager.TranslateAsCertainLang(sender.EffectiveLanguage,
                "Game.GlobalChat.RateLimited", config.RateLimitCount.ToString(),
                config.RateLimitWindowSeconds.ToString()));
            return GlobalChatRelayResultEnum.RateLimited;
        }

        var relay = $"[{sender.Data.Name} / {sender.Uid}]: {text}";
        var sendTime = Extensions.GetUnixSec();
        var maxHistory = config.MaxHistory > 0 ? config.MaxHistory : DefaultMaxHistory;

        foreach (var recipient in recipients.Prepend(sender).DistinctBy(player => player.Uid))
            await DeliverAsync(recipient, globalUid, relay, sendTime, maxHistory);

        Logger.Debug($"{sender.Uid}: {text}");
        QueueWebhook(relay);
        return GlobalChatRelayResultEnum.Relayed;
    }

    /// <summary>
    ///     Sliding-window limit: at most RateLimitCount accepted messages per RateLimitWindowSeconds per sender.
    ///     Records the message when it is accepted. Thread-safe; in-memory only.
    /// </summary>
    public static bool TryConsumeRateLimit(int uid, DateTime now)
    {
        var config = ConfigManager.Config.ServerOption.GlobalChat;
        if (config.RateLimitCount <= 0 || config.RateLimitWindowSeconds <= 0) return true;

        var window = TimeSpan.FromSeconds(config.RateLimitWindowSeconds);
        lock (RateLimitLock)
        {
            if (!SendLog.TryGetValue(uid, out var stamps))
            {
                PruneIdleSenders(now, window);
                SendLog[uid] = stamps = new Queue<DateTime>();
            }

            while (stamps.Count > 0 && now - stamps.Peek() >= window) stamps.Dequeue();
            if (stamps.Count >= config.RateLimitCount) return false;

            stamps.Enqueue(now);
            return true;
        }
    }

    private static void PruneIdleSenders(DateTime now, TimeSpan window)
    {
        if (SendLog.Count < 256) return;
        foreach (var uid in SendLog.Where(pair => pair.Value.Count == 0 || now - pair.Value.Last() >= window)
                     .Select(pair => pair.Key).ToList())
            SendLog.Remove(uid);
    }

    private static async ValueTask DeliverAsync(PlayerInstance recipient, int globalUid, string relay, long sendTime,
        int maxHistory)
    {
        var friendManager = recipient.FriendManager;
        if (friendManager == null) return;

        var history = friendManager.FriendData.ChatHistory;
        lock (history)
        {
            if (!history.TryGetValue(globalUid, out var conversation))
                history[globalUid] = conversation = new FriendChatHistory();

            conversation.MessageList.Add(new FriendChatData
            {
                SendUid = globalUid,
                ReceiveUid = recipient.Uid,
                Message = relay,
                SendTime = sendTime
            });
            if (conversation.MessageList.Count > maxHistory)
                conversation.MessageList.RemoveRange(0, conversation.MessageList.Count - maxHistory);
        }

        DatabaseHelper.MarkDirty(recipient.Uid);

        try
        {
            await recipient.SendPacket(new PacketRevcMsgScNotify((uint)globalUid, (uint)recipient.Uid, relay, 0,
                sendTime));
        }
        catch (Exception e)
        {
            // One broken session must not stop the relay to everyone else.
            Logger.Debug($"Failed to deliver a global chat message to {recipient.Uid}", e);
        }
    }

    private static async ValueTask NotifySenderAsync(PlayerInstance sender, int globalUid, string message)
    {
        await sender.SendPacket(new PacketRevcMsgScNotify((uint)globalUid, (uint)sender.Uid, message, 0,
            Extensions.GetUnixSec()));
    }

    private static void QueueWebhook(string relay)
    {
        var url = ConfigManager.Config.ServerOption.GlobalChat.Webhook;
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps &&
                                                                         uri.Scheme != Uri.UriSchemeHttp))
        {
            Logger.Warn(I18NManager.Translate("Server.Log.GlobalChatWebhookInvalid"));
            return;
        }

        // Same shape as the Go/Grasscutter relay: the server name only prefixes the webhook copy.
        var serverName = ConfigManager.Config.GameServer.GameServerName;
        var content = string.IsNullOrEmpty(serverName) ? relay : $"[{serverName}]{relay}";
        if (content.Length > DiscordContentLimit) content = content[..DiscordContentLimit];

        // Fire-and-forget: a slow or failing webhook must never delay the in-game relay. The request is bounded
        // by the client timeout and every failure is logged inside PostWebhookAsync.
        _ = PostWebhookAsync(uri, content);
    }

    private static async Task PostWebhookAsync(Uri uri, string content)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new GlobalChatWebhookPayload { Content = content },
                GlobalChatJsonContext.Default.GlobalChatWebhookPayload);
            using var body = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await WebhookClient.PostAsync(uri, body).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                Logger.Warn(I18NManager.Translate("Server.Log.GlobalChatWebhookRejected", ((int)response.StatusCode).ToString()));
        }
        catch (Exception e)
        {
            Logger.Warn(I18NManager.Translate("Server.Log.GlobalChatWebhookFailed"), e);
        }
    }
}

internal sealed class GlobalChatWebhookPayload
{
    [JsonPropertyName("content")] public string Content { get; init; } = "";

    // Player text is relayed verbatim; never let it ping @everyone, roles or users in the Discord channel.
    [JsonPropertyName("allowed_mentions")] public GlobalChatWebhookMentions AllowedMentions { get; init; } = new();
}

internal sealed class GlobalChatWebhookMentions
{
    [JsonPropertyName("parse")] public List<string> Parse { get; init; } = [];
}

[JsonSerializable(typeof(GlobalChatWebhookPayload))]
internal partial class GlobalChatJsonContext : JsonSerializerContext;
