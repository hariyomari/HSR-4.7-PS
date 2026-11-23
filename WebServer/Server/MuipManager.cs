using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using March7thHoney.Command;
using March7thHoney.Data;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Avatar;
using March7thHoney.Database.Lineup;
using March7thHoney.Database.Player;
using March7thHoney.Database.Quests;
using March7thHoney.Enums;
using March7thHoney.GameServer.Server;
using March7thHoney.Internationalization;
using March7thHoney.Util;
using March7thHoney.WebServer.Response;
using March7thHoney.WebServer.Services;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.X509;

namespace March7thHoney.WebServer.Server;

public static class MuipManager
{
    public delegate Task ExecuteCommandDelegate(string message, MuipCommandSender sender);

    private static readonly Logger logger = Logger.GetByClassName();
    private static ExecuteCommandDelegate? _commandHandler;

    public static string RsaPublicKey { get; private set; } = "";
    public static string RsaPrivateKey { get; private set; } = "";

    public static ConcurrentDictionary<string, MuipSession> Sessions { get; } = [];
    private static readonly object SessionAdmissionLock = new();
    private const int MaxSessions = 1024;
    private const int MaxSessionsPerClient = 32;
    // Expiration must not depend on the creator ever using the ID again. The static timer is retained
    // for the server lifetime; admission also prunes synchronously before checking capacity.
    private static readonly Timer SessionCleanupTimer = new(_ => CleanupExpiredSessions(), null,
        TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

    internal static string GetAdminKeyFingerprint() => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(ConfigManager.Config.MuipServer.AdminKey ?? "")));

    private static bool IsCurrent(MuipSession session) =>
        session.ExpireTimeStamp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
        (!session.IsAdmin || (AdminSessionManager.IsEnabled && string.Equals(session.AdminKeyFingerprint,
            GetAdminKeyFingerprint(), StringComparison.Ordinal)));

    private static void CleanupExpiredSessions()
    {
        foreach (var (id, session) in Sessions)
            if (!IsCurrent(session))
                Sessions.TryRemove(id, out _);
    }

    public static int RevokeAdminSessions()
    {
        var revoked = 0;
        foreach (var (id, session) in Sessions)
            if (session.IsAdmin && Sessions.TryRemove(id, out _))
                revoked++;
        return revoked;
    }

    /// <summary>
    ///     Supplies the registered command list to the admin console. Wired by the entry point because the
    ///     Command project sits above WebServer in the dependency graph.
    /// </summary>
    public static Func<IReadOnlyList<CommandCatalogEntry>>? CommandCatalogProvider { get; set; }

    /// <summary>
    ///     Runs decoded commands, wired by the entry point for the same reason as <see cref="CommandCatalogProvider" />.
    ///     It holds exactly one handler: a combined delegate (for example from a second <c>+=</c>) is refused, because
    ///     it would run every command once per registration.
    /// </summary>
    public static ExecuteCommandDelegate? CommandHandler
    {
        get => _commandHandler;
        set
        {
            if (value is { HasSingleTarget: false })
                throw new ArgumentException("MUIP accepts a single command handler.", nameof(value));
            _commandHandler = value;
        }
    }

    public static CreateSessionResponse CreateSession(string keyType, string? clientKey = null)
    {
        lock (SessionAdmissionLock)
        {
            if (!AdminSessionManager.IsEnabled)
                return new CreateSessionResponse(1, "This function is not enabled in this server!", null);
            CleanupExpiredSessions();
            if (Sessions.Count >= MaxSessions || (!string.IsNullOrWhiteSpace(clientKey) &&
                Sessions.Values.Count(session => session.ClientKey == clientKey) >= MaxSessionsPerClient))
                return new CreateSessionResponse(2, "Too many sessions. Try again later.", null);

            var session = new MuipSession
            {
                SessionId = Guid.NewGuid().ToString(),
                RsaPublicKey = GetRsaKeyPair().Item1,
                ExpireTimeStamp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(),
                IsAdmin = true,
                ClientKey = clientKey
            };
            if (keyType == "PEM")
                session.RsaPublicKey = XMLToPEM_Pub(session.RsaPublicKey);
            Sessions.TryAdd(session.SessionId, session);
            return new CreateSessionResponse(0, "Created!", new CreateSessionData
            {
                RsaPublicKey = session.RsaPublicKey,
                SessionId = session.SessionId,
                ExpireTimeStamp = session.ExpireTimeStamp
            });
        }
    }

    public static AuthAdminKeyResponse AuthAdmin(string sessionId, string key, string? clientKey = null)
    {
        if (Sessions.TryGetValue(sessionId, out var value))
        {
            var session = value;
            if (!IsCurrent(session))
            {
                Sessions.TryRemove(sessionId, out _);
                return new AuthAdminKeyResponse(1, "Session has expired!", null);
            }

            // A wrong key consumes the session so every guess costs a new one, on top of the shared
            // per-client failure counter.
            var keyStr = DecodeWithRsaFallback(key);
            if (!AdminSessionManager.TryVerifyAdminKey(keyStr, clientKey ?? session.ClientKey, out var failure,
                    out var blocked))
            {
                Sessions.TryRemove(sessionId, out _);
                return blocked
                    ? new AuthAdminKeyResponse(3, failure, null)
                    : new AuthAdminKeyResponse(2, "Admin key is invalid!", null);
            }

            session.IsAuthorized = true;

            var data = new AuthAdminKeyData
            {
                SessionId = session.SessionId,
                ExpireTimeStamp = session.ExpireTimeStamp
            };

            return new AuthAdminKeyResponse(0, "Authorized admin key successfully!", data);
        }

        return new AuthAdminKeyResponse(4, "Session not found!", null);
    }

    public static MuipSession? GetSession(string sessionId)
    {
        if (Sessions.TryGetValue(sessionId, out var value))
        {
            var session = value;
            if (!IsCurrent(session))
            {
                Sessions.TryRemove(sessionId, out _);
                return null;
            }

            return session;
        }

        return null;
    }

    public static ExecuteCommandResponse ExecuteCommand(string sessionId, string command, int targetUid)
    {
        return ExecuteCommandAsync(sessionId, command, targetUid).GetAwaiter().GetResult();
    }

    public static async Task<ExecuteCommandResponse> ExecuteCommandAsync(string sessionId, string command, int targetUid)
    {
        if (Sessions.TryGetValue(sessionId, out var value))
        {
            var session = value;
            if (!IsCurrent(session))
            {
                Sessions.TryRemove(sessionId, out _);
                return new ExecuteCommandResponse(1, "Session has expired!");
            }

            if (!session.IsAuthorized)
                return new ExecuteCommandResponse(4, "Not authorized!");

            var commandStr = DecodeWithRsaFallback(command);
            if (string.IsNullOrWhiteSpace(commandStr))
                return new ExecuteCommandResponse(3, "Wrong encrypted key");

            var result = await RunCommandAsync(session, commandStr, targetUid).ConfigureAwait(false);
            if (result.Code != 0)
                return new ExecuteCommandResponse(result.Code, result.Message);

            return new ExecuteCommandResponse(0, "Success", new ExecuteCommandData
            {
                SessionId = sessionId,
                Message = Convert.ToBase64String(Encoding.UTF8.GetBytes(result.Output))
            });
        }

        return new ExecuteCommandResponse(2, "Session not found!");
    }

    /// <summary>
    ///     Builds the session for a web caller that authenticated the request itself (the admin page or the player
    ///     console). It is never added to <see cref="Sessions" />, so it cannot be looked up by ID and does not count
    ///     toward the admission limits.
    /// </summary>
    internal static MuipSession CreateLocalSession(string idPrefix, bool isAdmin, AccountData? account = null)
    {
        return new MuipSession
        {
            SessionId = $"{idPrefix}-{Guid.NewGuid():N}",
            ExpireTimeStamp = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds(),
            IsAdmin = isAdmin,
            IsAuthorized = true,
            Account = account
        };
    }

    /// <summary>
    ///     Runs a plain-text command for <paramref name="session" /> and collects its output. The session rules still
    ///     apply, so an administrator session stops working once the admin key is disabled, wherever it came from.
    /// </summary>
    internal static async Task<CommandResult> RunCommandAsync(MuipSession session, string command, int targetUid)
    {
        if (!IsCurrent(session))
            return new CommandResult(1, "Session has expired!", "");

        if (!session.IsAuthorized)
            return new CommandResult(4, "Not authorized!", "");

        var handler = CommandHandler;
        if (handler == null)
            return new CommandResult(5, "Command handler is not initialized!", "");

        logger.Info(I18NManager.Translate("Server.Log.MuipCommand", session.SessionId, targetUid.ToString(), command));
        var output = new StringBuilder();
        var sender = new MuipCommandSender(session, msg =>
        {
            lock (output) output.Append(msg).Append("\r\n");
        }) { SenderUid = targetUid };

        await handler(command, sender).ConfigureAwait(false);
        return new CommandResult(0, "Success", output.ToString());
    }

    /// <summary>Outcome of <see cref="RunCommandAsync" />; <see cref="Output" /> is plain text, one line per message.</summary>
    internal readonly record struct CommandResult(int Code, string Message, string Output);

    public static ServerInformationResponse GetInformation(string sessionId)
    {
        if (Sessions.TryGetValue(sessionId, out var value))
        {
            var session = value;
            if (!IsCurrent(session))
            {
                Sessions.TryRemove(sessionId, out _);
                return new ServerInformationResponse(1, "Session has expired!");
            }

            if (!session.IsAuthorized)
                return new ServerInformationResponse(3, "Not authorized!");

            var currentProcess = Process.GetCurrentProcess();

            var currentProcessMemory = currentProcess.WorkingSet64;

            var (totalMemory, availableMemory) = HostMetrics.GetMachineMemoryStrict();

            // One entry per uid, even while the listener still holds an old connection of a reconnecting player.
            var onlinePlayers = new Dictionary<int, PlayerData>();
            foreach (var (_, player) in Listener.GetPlayerConnections())
                onlinePlayers.TryAdd(player.Uid, player.Data);

            return new ServerInformationResponse(0, "Success", new ServerInformationData
            {
                ServerTime = DateTime.Now.ToUnixSec(),
                MaxMemory = totalMemory,
                ProgramUsedMemory = currentProcessMemory / 1024 / 1024,
                UsedMemory = totalMemory - availableMemory,
                OnlinePlayers = onlinePlayers.Values.Select(x => new SimplePlayerInformationData
                {
                    Name = x.Name ?? "",
                    HeadIconId = x.HeadIcon,
                    Uid = x.Uid
                }).ToList()
            });
        }

        return new ServerInformationResponse(2, "Session not found!");
    }

    public static PlayerInformationResponse GetPlayerInformation(string sessionId, int uid)
    {
        if (Sessions.TryGetValue(sessionId, out var value))
        {
            var session = value;
            if (!IsCurrent(session))
            {
                Sessions.TryRemove(sessionId, out _);
                return new PlayerInformationResponse(1, "Session has expired!");
            }

            if (!session.IsAuthorized)
                return new PlayerInformationResponse(4, "Not authorized!");

            var player = DatabaseHelper.Instance?.GetInstance<PlayerData>(uid);
            if (player == null) return new PlayerInformationResponse(2, "Player not exist!");

            var (status, subStatus) = Listener.GetPlayerConnection(player.Uid)?.Player?.GetOnlineStatus() ??
                                      (PlayerStatusEnum.Offline, PlayerSubStatusEnum.None);

            var avatarData = DatabaseHelper.Instance!.GetInstance<AvatarData>(player.Uid)!;
            var lineupData = DatabaseHelper.Instance!.GetInstance<LineupData>(player.Uid)!;
            var missionData = DatabaseHelper.Instance!.GetInstance<MissionData>(player.Uid)!;

            var curLineupAvatars = new List<int>();
            var index = lineupData.CurExtraLineup > 0 ? lineupData.CurExtraLineup : lineupData.CurLineup;

            lineupData.Lineups.TryGetValue(index, out var lineup);

            if (lineup != null)
                foreach (var avatar in lineup.BaseAvatars ?? [])
                {
                    GameData.AvatarConfigData.TryGetValue(avatar.BaseAvatarId, out var excel);
                    if (excel != null) curLineupAvatars.Add(avatar.BaseAvatarId);
                }

            Dictionary<int, List<int>> missionDict = [];
            foreach (var subId in missionData.RunningSubMissionIds)
            {
                var subMission = GameData.SubMissionInfoData.GetValueOrDefault(subId);
                if (subMission == null) continue;

                if (missionDict.ContainsKey(subMission.MainMissionId))
                    missionDict[subMission.MainMissionId].Add(subMission.MissionId);
                else
                    missionDict[subMission.MainMissionId] = [subMission.MissionId];
            }

            return new PlayerInformationResponse(0, "Success", new PlayerInformationData
            {
                Uid = player.Uid,
                Name = player.Name ?? "",
                Signature = player.Signature ?? "",
                Stamina = player.Stamina,
                RecoveryStamina = (int)player.StaminaReserve,
                HeadIconId = player.HeadIcon,
                CurFloorId = player.FloorId,
                CurPlaneId = player.PlaneId,
                AssistAvatarList = avatarData.AssistAvatars,
                DisplayAvatarList = avatarData.DisplayAvatars,
                AcceptedMissionList = missionDict,
                FinishedMainMissionIdList = missionData.FinishedMainMissionIds,
                FinishedSubMissionIdList = missionData.FinishedSubMissionIds,
                PlayerStatus = status,
                PlayerSubStatus = subStatus,
                Credit = player.Scoin,
                Jade = player.Hcoin,
                LineupBaseAvatarIdList = curLineupAvatars
            });
        }

        return new PlayerInformationResponse(3, "Session not found!");
    }

    #region Tools

    private static string DecodeWithRsaFallback(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Prefer RSA decrypt first.
        try
        {
            var rsa = new RSACryptoServiceProvider();
            rsa.FromXmlString(GetRsaKeyPair().Item2);
            var decrypted = rsa.Decrypt(Convert.FromBase64String(text), RSAEncryptionPadding.Pkcs1);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            // fallback below
        }

        // Fallback 1: treat payload as base64 plain UTF-8.
        try
        {
            var plainBytes = Convert.FromBase64String(text);
            var plain = Encoding.UTF8.GetString(plainBytes);
            if (!string.IsNullOrWhiteSpace(plain))
                return plain;
        }
        catch
        {
            // fallback below
        }

        // Fallback 2: direct plain text.
        return text;
    }

    /// <summary>
    ///     get rsa key pair
    /// </summary>
    /// <returns>item 1 is public key, item 2 is private key</returns>
    public static (string, string) GetRsaKeyPair()
    {
        if (string.IsNullOrEmpty(RsaPublicKey) || string.IsNullOrEmpty(RsaPrivateKey))
        {
            var rsa = new RSACryptoServiceProvider(2048);
            RsaPublicKey = rsa.ToXmlString(false);
            RsaPrivateKey = rsa.ToXmlString(true);
        }

        return (RsaPublicKey, RsaPrivateKey);
    }


    public static string XMLToPEM_Pub(string xmlpubkey)
    {
        var rsa = new RSACryptoServiceProvider();
        rsa.FromXmlString(xmlpubkey);
        var p = rsa.ExportParameters(false);
        var key = new RsaKeyParameters(false, new BigInteger(1, p.Modulus), new BigInteger(1, p.Exponent));
        var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(key);
        var serializedPublicBytes = publicKeyInfo.ToAsn1Object().GetDerEncoded();
        var publicKey = Convert.ToBase64String(serializedPublicBytes);
        return Format(publicKey, true);
    }


    private static string Format(string key, bool type)
    {
        var result = string.Empty;

        var length = key.Length / 64;
        for (var i = 0; i < length; i++)
        {
            var start = i * 64;
            result = result + key.Substring(start, 64) + "\r\n";
        }

        result = result + key.Substring(length * 64);
        if (type)
        {
            result = result.Insert(0, "-----BEGIN PUBLIC KEY-----\r\n");
            result += "\r\n-----END PUBLIC KEY-----";
        }
        else
        {
            result = result.Insert(0, "-----BEGIN PRIVATE KEY-----\r\n");
            result += "\r\n-----END PRIVATE KEY-----";
        }

        return result;
    }

    #endregion
}
