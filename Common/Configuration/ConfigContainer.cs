using March7thHoney.Command;
using March7thHoney.Util;
using System.Text.Json.Serialization;

namespace March7thHoney.Configuration;

public class ConfigContainer
{
    public HttpServerConfig HttpServer { get; set; } = new();
    public KeyStoreConfig KeyStore { get; set; } = new();
    public GameServerConfig GameServer { get; set; } = new();
    public PathConfig Path { get; set; } = new();
    public DatabaseConfig Database { get; set; } = new();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LifecycleNotificationConfig? LifecycleNotification { get; set; }
    public ServerOption ServerOption { get; set; } = new();
    public MuipServerConfig MuipServer { get; set; } = new();
    public WebSecurityConfig WebSecurity { get; set; } = new();
}

public class LifecycleNotificationConfig
{
    public bool Enabled { get; set; } = false;
    public int ControlApiPort { get; set; } = 3101;
    public string ControlApiToken { get; set; } = "";
}

public class HttpServerConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public string PublicAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 21000;
    public bool UseSSL { get; set; } = false;
    public bool SendHotfix { get; set; } = true;
    public bool UseFetchRemoteHotfix { get; set; } = true;
    // Only these reverse proxies may supply X-Forwarded-For/Proto. Empty means direct connections.
    public List<string> TrustedProxies { get; set; } = ["127.0.0.1", "::1"];

    public string GetDisplayAddress()
    {
        return (UseSSL ? "https" : "http") + "://" + PublicAddress + ":" + Port;
    }

    public string GetBindDisplayAddress()
    {
        return (UseSSL ? "https" : "http") + "://" + BindAddress + ":" + Port;
    }
}

public class KeyStoreConfig
{
    public string KeyStorePath { get; set; } = "certificate.p12";
    public string KeyStorePassword { get; set; } = "123456";
}

public class GameServerConfig
{
    public string BindAddress { get; set; } = "0.0.0.0";
    public string PublicAddress { get; set; } = "127.0.0.1";
    public uint Port { get; set; } = 23301;
    public string GameServerId { get; set; } = "March7th_Honey";
    public string GameServerName { get; set; } = "March7thHoney";
    public string GameServerDescription { get; set; } = "A re-implementation of StarRail server";
    public int EnvType { get; set; } = 2;
    public bool UsePacketEncryption { get; set; } = true;

    // Registered connections include active and unauthenticated handshakes. Nonpositive values use safe defaults.
    public int MaxConnections { get; set; } = 4096;
    public int MaxPendingConnections { get; set; } = 256;
    // Only simultaneous not-yet-active connections count here, not all players behind a NAT.
    public int MaxPendingConnectionsPerIp { get; set; } = 16;

    public string GetDisplayAddress()
    {
        return PublicAddress + ":" + Port;
    }
}

public class PathConfig
{
    public string ResourcePath { get; set; } = "Resources";
    public List<string> ExcelOutputDirs { get; set; } = ["ExcelOutput", "ExcelOutputGameCore"];
    public string ConfigPath { get; set; } = "Config";
    public string DatabasePath { get; set; } = "Config/Database";
    public string LogPath { get; set; } = "Config/Logs";
    public string LanguagePath { get; set; } = "Config/Languages";
}

public class DatabaseConfig
{
    public string DatabaseType { get; set; } = "sqlite";
    public string DatabaseName { get; set; } = "March7thHoney.db";
    public string MySqlHost { get; set; } = "127.0.0.1";
    public int MySqlPort { get; set; } = 3306;
    public string MySqlUser { get; set; } = "root";
    public string MySqlPassword { get; set; } = "123456";
    public string MySqlDatabase { get; set; } = "March7thHoney";
}

public class ServerOption
{
    public string DefaultNickname { get; set; } = "Trailblazer";
    public int StartTrailblazerLevel { get; set; } = 1;
    public bool AutoUpgradeWorldLevel { get; set; } = true;
    public bool EnableMission { get; set; } = false; // experimental
    public bool EnableQuest { get; set; } = true; // experimental
    public bool AutoLightSection { get; set; } = true;

    // Every login returns the player to the default spawn and abandons the raid/challenge run behind the saved
    // location (skipped while a story line is active). Off keeps the saved location and resumable runs.
    public bool ResetLocationOnLogin { get; set; } = false;
    public string Language { get; set; } = "EN";
    public string FallbackLanguage { get; set; } = "EN";
    public string DefaultPermissionRole { get; set; } = CommandRoles.User;
    public Dictionary<string, HashSet<string>> PermissionRoles { get; set; } = CommandRoles.DefaultRoles();
    public HashSet<string> DefaultPermissions { get; set; } = ["*"];
    public ServerAnnounce ServerAnnounce { get; set; } = new();
    public ServerProfile ServerProfile { get; set; } = new();
    public GlobalChatConfig GlobalChat { get; set; } = new();
    public WatermarkConfig Watermark { get; set; } = new();
    public NetStatusConfig NetStatus { get; set; } = new();
    public ChallengePeakOption ChallengePeak { get; set; } = new();
    public RogueTournOption RogueTourn { get; set; } = new();
    public AuthOption Auth { get; set; } = new();
    public bool AutoCreateUser { get; set; } = false;
    public LogOption LogOption { get; set; } = new();
    public ServerConfig ServerConfig { get; set; } = new();
    public int FarmingDropRate { get; set; } = 1;
    public bool UseCache { get; set; } = true;

    // Load Excel tables for paused gameplay modules (MatchThree / Marble / Cake).
    public bool LoadPausedModuleData { get; set; } = false;

    public bool EnableMonthCard { get; set; } = true;

    // Show the Oneiric Shard tiers and grant them instantly instead of running the real SDK payment.
    public bool EnableFakeRecharge { get; set; } = true;
    public ServerTimeOption ServerTime { get; set; } = new();
    public WarpScheduleOption Warps { get; set; } = new();
    public WelcomeMailConfig WelcomeMail { get; set; } = new();

    // Local hour (server time zone, the one PlayerLoginScRsp advertises) at which daily training rolls over.
    public int DailyResetHour { get; set; } = 4;

    public int ValidFarmingDropRate()
    {
        return Math.Max(Math.Min(FarmingDropRate, 999), 1);
    }

    /// <summary>
    ///     Uid of the Global Chat contact, or null when the feature is disabled or its uid is unusable (non-positive or
    ///     colliding with <see cref="ServerProfile" />). Every call site goes through this so a disabled contact
    ///     disappears everywhere at once.
    /// </summary>
    public int? GetGlobalChatUid()
    {
        if (!GlobalChat.Enabled || GlobalChat.Uid <= 0 || GlobalChat.Uid == ServerProfile.Uid) return null;
        return GlobalChat.Uid;
    }

    /// <summary>True for the synthetic friend-list contacts (command bot and Global Chat) that have no player data.</summary>
    public bool IsSystemProfileUid(int uid)
    {
        return uid == ServerProfile.Uid || uid == GetGlobalChatUid();
    }

    /// <summary>Display avatars of a synthetic contact, or null when the uid is not one.</summary>
    public List<ServerAssistInfo>? GetSystemProfileAssistInfo(int uid)
    {
        if (uid == ServerProfile.Uid) return ServerProfile.AssistInfo;
        if (uid == GetGlobalChatUid()) return GlobalChat.AssistInfo;
        return null;
    }

    public int ValidDailyResetHour()
    {
        return Math.Clamp(DailyResetHour, 0, 23);
    }
}

public class NetStatusConfig
{
    public bool Enabled { get; set; } = false;
    public int LatencyMs { get; set; } = 37;
    public string Color { get; set; } = "#FEC6DF";

    public bool TryGetColor(out int rgb)
    {
        rgb = 0;
        var color = Color?.Trim();
        return LatencyMs >= 0 && color is { Length: 7 } && color[0] == '#' &&
               int.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.AllowHexSpecifier,
                   System.Globalization.CultureInfo.InvariantCulture, out rgb);
    }
}

public class RogueTournOption
{
    // RogueTournWeeklyChallenge row the Divergent Universe weekly panel shows. The official rotates it every week
    // (4.5 reported 105, 4.6 reports 108); a zero/unknown id leaves the panel without a resolvable challenge.
    public int WeekChallengeId { get; set; } = 108;
}

public class ChallengePeakOption
{
    public bool RequireKnightClearsForNormalBoss { get; set; } = false;
}

public class WatermarkConfig
{
    public bool Enabled { get; set; } = true;
    public string Line1Template { get; set; } = "{nickname}｜{serverName}";
    public string Line2Template { get; set; } = "{version}｜ {uid}";
    public double FontSize { get; set; } = 76.0;
    public bool UseGradient { get; set; } = true;
    public string GradientStartColor { get; set; } = "#54C3F7";
    public string GradientEndColor { get; set; } = "#C673AA";
}

public class AuthOption
{
    public int MinimumPasswordLength { get; set; } = 8;
    public int PasswordHashIterations { get; set; } = 600000;
    public int DispatchTokenExpireMinutes { get; set; } = 43200;
    public int ComboTokenExpireMinutes { get; set; } = 43200;
    public bool EnableRegistration { get; set; } = true;
    public string RegistrationPageIcon { get; set; } = "M7";
    public bool SendVerificationEmailOnRegister { get; set; } = true;
    public bool RequireVerifiedEmailForLogin { get; set; } = false;
    public string EmailVerificationRequiredMessage { get; set; } =
        "Email verification required. Please check your inbox and verify your account before entering the server.";
    public int EmailVerificationTokenExpireMinutes { get; set; } = 1440;
    public int PasswordResetTokenExpireMinutes { get; set; } = 60;
    public EmailOption Email { get; set; } = new();
    public AntiAbuseOption AntiAbuse { get; set; } = new();

    // When public registration is off, the built-in register page redirects here (e.g. the website
    // that provisions accounts through WebAccountApi). Empty keeps the "registration closed" page.
    public string ExternalRegistrationUrl { get; set; } = "";
    public WebAccountApiOption WebAccountApi { get; set; } = new();
}

/// <summary>
///     Server-to-server API that lets a trusted website create and manage game accounts
///     (/external/web-account/v1/*). Requests need the shared secret in the
///     X-March7thHoney-Web-Secret header and must come from one of <see cref="AllowedAddresses" />
///     (checked after trusted-proxy forwarding, so traffic relayed by a reverse proxy is not local).
/// </summary>
public class WebAccountApiOption
{
    public bool Enabled { get; set; } = false;
    public string Secret { get; set; } = "";
    public List<string> AllowedAddresses { get; set; } = ["127.0.0.1", "::1"];
}

public class AntiAbuseOption
{
    public bool RateLimitEnabled { get; set; } = true;
    public int RateLimitMaxRequests { get; set; } = 10;
    public int RateLimitWindowSeconds { get; set; } = 60;
    public int RateLimitBlockSeconds { get; set; } = 900;
    public bool LimitRegistrationPerIdentity { get; set; } = true;
    public string RegistrationIdentityMode { get; set; } = "DeviceOrIp";
    public bool RequireRegistrationIdentity { get; set; } = false;
    public bool LoginRateLimitEnabled { get; set; } = true;
    public int LoginRateLimitMaxRequests { get; set; } = 8;
    public int LoginRateLimitWindowSeconds { get; set; } = 900;
    public int LoginRateLimitBlockSeconds { get; set; } = 900;
    public bool EmailRateLimitEnabled { get; set; } = true;
    public int EmailRateLimitMaxRequests { get; set; } = 3;
    public int EmailRateLimitWindowSeconds { get; set; } = 3600;
    public int EmailRateLimitBlockSeconds { get; set; } = 900;
}

public class EmailOption
{
    public bool Enabled { get; set; } = false;
    public string PublicBaseUrl { get; set; } = "";
    public string LogoUrl { get; set; } = "https://img.hoyotoon.com/raw/hoyotoonlogolong.png";
    public SmtpOption Smtp { get; set; } = new();
    public string VerificationSubject { get; set; } = "Verify your {serverName} account";
    public string VerificationBody { get; set; } =
        "Hi {username},\n\nYou are adding email security verification for {serverName}.\n\nVerify your email here:\n{link}\n\nPlease complete the account verification process in {minutes} minutes.\n\nThis is an automated email. Please do not reply.";
    public string PasswordResetSubject { get; set; } = "Reset your {serverName} password";
    public string PasswordResetBody { get; set; } =
        "Hi {username},\n\nWe received a request to reset the password for your {serverName} account.\n\nReset your password here:\n{link}\n\nPlease complete the password reset process in {minutes} minutes.\n\nThis is an automated email. Please do not reply.";
}

public class SmtpOption
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "March7thHoney";
}

public class WelcomeMailConfig
{
    public bool Enabled { get; set; } = true;
    public string Sender { get; set; } = "March7thHoney";
    public string Title { get; set; } = "Welcome";
    public string Content { get; set; } = "Welcome, {username}!";
    public int TemplateId { get; set; } = 0;
    public int ExpireDays { get; set; } = 30;
    public bool IsStar { get; set; } = false;
    public List<WelcomeMailItemConfig> Items { get; set; } = [];
}

public class WelcomeMailItemConfig
{
    public int ItemId { get; set; }
    public int Count { get; set; }
    public int Rank { get; set; }
    public int Level { get; set; } = 1;
    public int Promotion { get; set; } = 0;
    public int TalentLevel { get; set; }
}

/// <summary>
///     Which limited warp banners the server offers. "Static" serves Config/Custom/Banners.json as written (the
///     historical behaviour). "Rotation" and "Manual" serve the permanent pools of Banners.json plus limited banners
///     from the history pool in Config/Custom/Banners.history.json, stamped with fresh time windows.
/// </summary>
public class WarpScheduleOption
{
    // Static | Rotation | Manual
    public string Mode { get; set; } = "Static";

    // Length of one rotation period; manual banners without an explicit end also roll over with it.
    public int RotationDays { get; set; } = 7;

    // yyyy-MM-dd, server-local midnight. Period 0 of the rotation starts here; empty = 1970-01-01.
    public string RotationStart { get; set; } = "";

    // Phase key ("4.7-1") that runs in period 0; empty = the oldest phase in the pool.
    public string RotationStartPhase { get; set; } = "";

    // Active limited banners in Manual mode. Replaced as a whole on every change (never mutated in place),
    // because game threads read it while the admin panel writes it.
    public List<WarpManualEntry> Manual { get; set; } = [];
}

public class WarpManualEntry
{
    // Gacha id of a banner in the history pool.
    public int GachaId { get; set; }

    // Unix seconds; 0 = follow the current rotation period, so the banner never runs out.
    public long BeginTime { get; set; }
    public long EndTime { get; set; }

    // null = the history entry's featured items.
    public List<int>? RateUpItems5 { get; set; }
    public List<int>? RateUpItems4 { get; set; }

    // Light cone banner shown as this character banner's partner in the admin panel; 0 = none.
    public int PairedGachaId { get; set; }
}

public class ServerTimeOption
{
    public bool EnableFakeServerTime { get; set; } = false;
    // yyyy-MM-dd or "today"
    public string FixedDate { get; set; } = "2026-05-12";
}

public class ServerConfig
{
    public bool RunDispatch { get; set; } = true;
    public string FromDispatchBaseUrl { get; set; } = "";
    public string ServerExchangeSecret { get; set; } = "";
    public bool RunGateway { get; set; } = true; // if run gateway, also run game server
    public List<ServerRegion> Regions { get; set; } = [];
}

public class ServerRegion
{
    public string GateWayAddress { get; set; } = "";
    public string GameServerName { get; set; } = "";
    public string GameServerId { get; set; } = "";
    public int EnvType { get; set; } = 21;
}

public class LogOption
{
#if DEBUG
    public bool EnableGamePacketLog { get; set; } = true;
#else
    public bool EnableGamePacketLog { get; set; } = false;
#endif
    public bool LogPacketToConsole { get; set; } = true;
    public bool DisableLogDetailPacket { get; set; } = false;
    public bool SavePersonalDebugFile { get; set; } = false;
    public bool LogResourceCacheLoading { get; set; } = true;
}

public class ServerAnnounce
{
    private const long DefaultDurationSeconds = 86400L * 30;
    private const uint DefaultBannerFrequencySeconds = 3600;

    public bool EnableAnnounce { get; set; } = false;
    public string AnnounceContent { get; set; } = "Welcome to March7thHoney!";
    public string Duration { get; set; } = "30d";
    public string BannerFrequency { get; set; } = "1h";

    public long GetDurationSeconds()
    {
        if (!DurationParser.TryParseSeconds(Duration, out var seconds))
            return DefaultDurationSeconds;

        return Math.Clamp(seconds, 1, DurationParser.MaxDurationSeconds);
    }

    public uint GetBannerFrequencySeconds()
    {
        if (!DurationParser.TryParseSeconds(BannerFrequency, out var seconds))
            return DefaultBannerFrequencySeconds;

        return (uint)Math.Clamp(seconds, 1, uint.MaxValue);
    }
}

public class ServerProfile
{
    public string Name { get; set; } = "HyacineLover";
    public int Uid { get; set; } = 5201314;
    public string Signature { get; set; } = "Type /help for a list of commands";
    public int Level { get; set; } = 70;
    public int HeadIcon { get; set; } = 200139;
    public int ChatBubbleId { get; set; } = 220008;
    public int PersonalCardId { get; set; } = 253001;

    public List<ServerAssistInfo> AssistInfo { get; set; } =
    [
        new() { AvatarId = 1409, Level = 80 }
    ];
}

/// <summary>
///     Second synthetic friend-list contact acting as a server-wide chat channel. Text sent to it is relayed to every
///     online player as a private message from this contact. Uid must differ from <see cref="ServerProfile.Uid" /> and
///     from real player uids (accounts start at 10001); a colliding uid disables the contact.
/// </summary>
public class GlobalChatConfig
{
    public bool Enabled { get; set; } = true;
    public int Uid { get; set; } = 98;
    public string Name { get; set; } = "Global Chat";
    public string Signature { get; set; } = "Say hi to everyone on this server";
    public int Level { get; set; } = 70;
    public int HeadIcon { get; set; } = 200101;
    public int ChatBubbleId { get; set; } = 220000;
    public int PersonalCardId { get; set; } = 253001;

    public List<ServerAssistInfo> AssistInfo { get; set; } =
    [
        new() { AvatarId = 1001, Level = 80 }
    ];

    // Shown at the top of the Global Chat conversation; empty disables it.
    public string WelcomeMessage { get; set; } =
        "Welcome to Global Chat! Messages here reach every online player. Commands do not work here; use the other bot's chat for /help.";

    // Relayed messages kept per player for this conversation.
    public int MaxHistory { get; set; } = 15;

    // At most RateLimitCount messages per RateLimitWindowSeconds per player; 0 in either disables the limit.
    public int RateLimitCount { get; set; } = 3;
    public int RateLimitWindowSeconds { get; set; } = 60;

    // Longer messages are rejected; 0 disables the check.
    public int MaxLength { get; set; } = 200;

    // Optional Discord webhook URL that receives every relayed message; empty disables it.
    public string Webhook { get; set; } = "";
}


public class ServerAssistInfo
{
    public int AvatarId { get; set; }
    public int Level { get; set; }
    public int SkinId { get; set; }
}

public class MuipServerConfig
{
    public string AdminKey { get; set; } = "None";
}

public class WebSecurityConfig
{
    public bool EnableRsaLoginDecrypt { get; set; } = true;
    public string RsaPublicKeyPem { get; set; } = "";
    public string RsaPrivateKeyPem { get; set; } = "";
}
