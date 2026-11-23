using System.Security.Cryptography;
using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.Database.Player;
using March7thHoney.GameServer.Game.Player;
using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static readonly Logger AccountsLogger = new("AdminAccounts");

    private static void MapAccountRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/api/accounts", GetAccounts);
        app.MapPost("/admin/api/accounts", CreateAccount);
        app.MapPost("/admin/api/accounts/bulk-delete", BulkDeleteAccounts);
        app.MapGet("/admin/api/identity-blocks", GetIdentityBlocks);
        app.MapPost("/admin/api/identity-blocks", BlockIdentity);
        app.MapDelete("/admin/api/identity-blocks/{id:int}", UnblockIdentity);
        app.MapPut("/admin/api/accounts/{uid:int}", UpdateAccount);
        app.MapPut("/admin/api/accounts/{uid:int}/password", SetPassword);
        app.MapPost("/admin/api/accounts/{uid:int}/email/verification", SendAccountVerificationEmail);
        app.MapPost("/admin/api/accounts/{uid:int}/ban", BanAccount);
        app.MapDelete("/admin/api/accounts/{uid:int}/ban", UnbanAccount);
        app.MapPost("/admin/api/accounts/{uid:int}/tokens/revoke", RevokeAccountTokens);
        app.MapPost("/admin/api/accounts/{uid:int}/session/kick", KickAccountSession);
        app.MapPost("/admin/api/accounts/{uid:int}/gameplay/reset", ResetAccountGameplay);
        app.MapDelete("/admin/api/accounts/{uid:int}/identity-keys", ClearIdentityKeys);
        app.MapDelete("/admin/api/accounts/{uid:int}", DeleteAccount);
        app.MapPut("/admin/api/accounts/{uid:int}/delete-on-logout", SetDeleteOnLogout);
        app.MapPost("/admin/api/accounts/{uid:int}/clone", CloneAccount);
        app.MapPost("/admin/api/accounts/{uid:int}/snapshot/restore", RestoreAccountSnapshot);
        app.MapGet("/admin/api/snapshots", GetSnapshots);
        app.MapPost("/admin/api/snapshots", SaveSnapshot);
        app.MapPost("/admin/api/snapshots/{id}/spawn", SpawnSnapshot);
        app.MapDelete("/admin/api/snapshots/{id}", DeleteSnapshot);
    }

    private static IResult GetAccounts(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var accounts = GetAllAccounts()
            .Select(ToAccountSummary)
            .OrderBy(account => account.uid)
            .ToList();

        return Success(new AccountsData(accounts, accounts.Count, GetRoleNames(), NormalizeRole(null)));
    }

    private static IResult CreateAccount(HttpContext ctx, AdminCreateAccountRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        request ??= new AdminCreateAccountRequest();
        var username = (request.username ?? string.Empty).Trim();
        var email = (request.email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username))
            return Error("Username cannot be empty");

        if (string.IsNullOrWhiteSpace(email))
            return Error("Email cannot be empty");

        if (string.IsNullOrWhiteSpace(request.password))
            return Error("Password cannot be empty");

        var uid = request.uid.GetValueOrDefault();
        try
        {
            var role = NormalizeRole(request.role);
            AccountHelper.CreateAccount(username, email, request.password, uid, role,
                request.email_verified.GetValueOrDefault());
            var account = AccountData.GetAccountByUserName(username);
            if (account == null)
                return Error("Failed to create account");

            account.SyncPermissionsWithRole();
            account.SetPermissionOverrides(request.permissions);

            return Success(new AccountWrap(ToAccountSummary(account)));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static async Task<IResult> UpdateAccount(HttpContext ctx, int uid, AdminUpdateAccountRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        request ??= new AdminUpdateAccountRequest();
        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        var username = (request.username ?? string.Empty).Trim();
        var email = (request.email ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username))
            return Error("Username cannot be empty");

        if (string.IsNullOrWhiteSpace(email))
            return Error("Email cannot be empty");

        var existing = AccountData.GetAccountByUserName(username);
        if (existing != null && existing.Uid != uid)
            return Error("Username already exists");

        var existingEmail = AccountData.GetAccountByEmail(email);
        if (existingEmail != null && existingEmail.Uid != uid)
            return Error("Email already exists");

        var previousNormalizedEmail = account.NormalizedEmail;
        if (string.IsNullOrWhiteSpace(previousNormalizedEmail))
            previousNormalizedEmail = AccountData.NormalizeEmail(account.Email);
        var previousEmailVerified = account.IsEmailVerified;
        var requestedEmailVerified = request.email_verified.GetValueOrDefault(account.IsEmailVerified);

        try
        {
            account.Username = username;
            AccountHelper.SetEmail(account, email, requestedEmailVerified);
            account.SetRole(request.role);
            account.SetPermissionOverrides(request.permissions);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        var emailSent = false;
        var emailChanged = !string.Equals(previousNormalizedEmail, account.NormalizedEmail, StringComparison.OrdinalIgnoreCase);
        var changedFromVerifiedToUnverified = previousEmailVerified && !account.IsEmailVerified;
        if (!account.IsEmailVerified && (emailChanged || changedFromVerifiedToUnverified))
            emailSent = await AccountRecoveryHandler.SendVerificationEmailForAccount(account, ctx.Request);

        return Success(new AccountEmailWrap(ToAccountSummary(account), emailSent));
    }

    private static IResult SetPassword(HttpContext ctx, int uid, AdminSetPasswordRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        request ??= new AdminSetPasswordRequest();
        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        try
        {
            AccountHelper.SetPassword(account, request.password ?? string.Empty);
            if (request.revoke_tokens.GetValueOrDefault())
                account.ClearLoginTokens();
            return Success(new AccountWrap(ToAccountSummary(account)));
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static async Task<IResult> SendAccountVerificationEmail(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        if (string.IsNullOrWhiteSpace(account.Email))
            return Error("Account does not have an email address");

        var emailSent = await AccountRecoveryHandler.SendVerificationEmailForAccount(account, ctx.Request,
            markUnverified: true);
        return Success(new AccountEmailWrap(ToAccountSummary(account), emailSent));
    }

    private static IResult BanAccount(HttpContext ctx, int uid, AdminBanAccountRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        request ??= new AdminBanAccountRequest();
        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        if (!TryResolveBanExpireAt(request, out var expireAt, out var error))
            return Error(error);

        account.AddKnownIdentityKeys(GetActiveConnectionIdentityKeys(account.Uid));
        account.Ban(request.reason, expireAt);
        ConnectionDisconnectHelper.KickBanned(Listener.GetActiveConnection(uid), account.GetBanStatus());

        return Success(new AccountWrap(ToAccountSummary(account)));
    }

    private static IResult UnbanAccount(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        account.ClearBan();
        return Success(new AccountWrap(ToAccountSummary(account)));
    }

    private static IResult RevokeAccountTokens(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        account.ClearLoginTokens();

        return Success(new AccountWrap(ToAccountSummary(account)));
    }

    private static IResult KickAccountSession(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        var activeConnection = Listener.GetActiveConnection(uid);
        ConnectionDisconnectHelper.KickByGm(activeConnection);

        return Success(new AccountOnlineWrap(ToAccountSummary(account), activeConnection != null));
    }

    private static async Task<IResult> ResetAccountGameplay(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        if (DatabaseHelper.Instance == null)
            return Error("Database is not initialized");

        var wasOnline = await PlayerResetHelper.ResetGameplayAsync(uid);

        return Success(new AccountOnlineWrap(ToAccountSummary(account), wasOnline));
    }

    private static IResult ClearIdentityKeys(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        account.KnownIdentityKeys = null;
        account.KnownDeviceIds = [];
        account.KnownIpAddresses = [];
        account.LastIp = null;
        DatabaseHelper.MarkDirty(uid);
        DatabaseHelper.SaveDatabaseType(account);

        return Success(new AccountWrap(ToAccountSummary(account)));
    }

    private static async Task<IResult> DeleteAccount(HttpContext ctx, int uid)
    {
        if (!IsAuthorized(ctx, out var unauthorized))
            return unauthorized;

        var account = AccountData.GetAccountByUid(uid);
        if (account == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        await DeleteAccountData(account);
        return Success(new DeletedUid(uid));
    }

    private static Task DeleteAccountData(AccountData account) => AccountDeletion.DeleteAsync(account);

    private static IResult SetDeleteOnLogout(HttpContext ctx, int uid, AdminRegistrationRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        var account = AccountData.GetAccountByUid(uid);
        if (account == null) return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        account.DeleteOnLogout = request?.enabled ?? false;
        DatabaseHelper.SaveDatabaseType(account);
        return Success(new AccountWrap(ToAccountSummary(account)));
    }

    private static async Task<IResult> BulkDeleteAccounts(HttpContext ctx, AdminBulkDeleteRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (request?.confirm != "DELETE") return Error("Deletion confirmation is required");
        if (request.uids == null || request.uids.Count is < 1 or > 500 || request.uids.Any(uid => uid <= 0))
            return Error("Select between 1 and 500 positive UIDs");
        if (DatabaseHelper.Instance == null) return Error("Database is not initialized");

        var deleted = new List<int>();
        var missing = new List<int>();
        var failed = new List<AccountDeleteFailure>();
        foreach (var uid in request.uids.Distinct())
        {
            var account = AccountData.GetAccountByUid(uid);
            if (account == null) { missing.Add(uid); continue; }
            try { await DeleteAccountData(account); deleted.Add(uid); }
            catch (Exception ex) { failed.Add(new AccountDeleteFailure(uid, ex.Message)); }
        }
        return Success(new BulkDeleteData(deleted, missing, failed));
    }

    public class AdminBulkDeleteRequest
    {
        public List<int>? uids { get; set; }
        public string? confirm { get; set; }
    }

    private static async Task<IResult> CloneAccount(HttpContext ctx, int uid, AdminTestAccountRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (DatabaseHelper.Instance == null) return Error("Database is not initialized");

        request ??= new AdminTestAccountRequest();
        var source = AccountData.GetAccountByUid(uid);
        if (source == null) return Error("Account not found", statusCode: StatusCodes.Status404NotFound);
        if (DatabaseHelper.Instance.GetInstance<PlayerData>(uid) == null) return Error($"UID {uid} has no gameplay data");

        AccountData account;
        string? password;
        try
        {
            account = CreateTestAccount(request, $"{source.Username}_clone", out password);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        try
        {
            await AccountSnapshotService.CloneGameplayAsync(uid, account.Uid, request.nickname);
        }
        catch (Exception ex)
        {
            await DiscardTestAccount(account);
            return SnapshotError(ex);
        }

        return Success(new TestAccountData(ToAccountSummary(account), password));
    }

    private static async Task<IResult> RestoreAccountSnapshot(HttpContext ctx, int uid, AdminSnapshotRestoreRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (DatabaseHelper.Instance == null) return Error("Database is not initialized");

        var account = AccountData.GetAccountByUid(uid);
        if (account == null) return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        try
        {
            var wasOnline = await AccountSnapshotService.RestoreAsync(request?.id ?? "", uid, request?.nickname);
            return Success(new AccountOnlineWrap(ToAccountSummary(account), wasOnline));
        }
        catch (Exception ex)
        {
            return SnapshotError(ex);
        }
    }

    private static IResult GetSnapshots(HttpContext ctx)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        return Success(new SnapshotsData(AccountSnapshotService.List().Select(ToSnapshotView).ToList()));
    }

    private static IResult SaveSnapshot(HttpContext ctx, AdminSaveSnapshotRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        request ??= new AdminSaveSnapshotRequest();
        if (AccountData.GetAccountByUid(request.uid) == null)
            return Error("Account not found", statusCode: StatusCodes.Status404NotFound);

        try
        {
            var meta = AccountSnapshotService.Save(request.uid, request.name ?? "", request.description);
            return Success(new SnapshotWrap(ToSnapshotView(meta)));
        }
        catch (Exception ex)
        {
            return SnapshotError(ex);
        }
    }

    private static async Task<IResult> SpawnSnapshot(HttpContext ctx, string id, AdminTestAccountRequest? request)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        if (DatabaseHelper.Instance == null) return Error("Database is not initialized");

        request ??= new AdminTestAccountRequest();
        try
        {
            AccountSnapshotService.Get(id);
        }
        catch (Exception ex)
        {
            return SnapshotError(ex);
        }

        AccountData account;
        string? password;
        try
        {
            account = CreateTestAccount(request, $"snap_{id}", out password);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        try
        {
            await AccountSnapshotService.RestoreAsync(id, account.Uid, request.nickname);
        }
        catch (Exception ex)
        {
            await DiscardTestAccount(account);
            return SnapshotError(ex);
        }

        return Success(new TestAccountData(ToAccountSummary(account), password));
    }

    private static IResult DeleteSnapshot(HttpContext ctx, string id)
    {
        if (!IsAuthorized(ctx, out var unauthorized)) return unauthorized;
        try
        {
            return AccountSnapshotService.Delete(id)
                ? Success(new DeletedSnapshot(id))
                : Error($"Snapshot {id} not found", statusCode: StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            return SnapshotError(ex);
        }
    }

    /// <summary>
    ///     Creates the account a clone, snapshot spawn or story provision lands on. An empty username gets a random
    ///     suffix on <paramref name="usernamePrefix" />, an empty password is generated (and returned once), an empty
    ///     email defaults to <c>username@local.test</c>.
    /// </summary>
    private static AccountData CreateTestAccount(AdminTestAccountRequest request, string usernamePrefix,
        out string? generatedPassword)
    {
        var username = request.username?.Trim();
        if (string.IsNullOrEmpty(username))
        {
            var prefix = usernamePrefix.Length > 48 ? usernamePrefix[..48] : usernamePrefix;
            username = $"{prefix}_{RandomNumberGenerator.GetHexString(6, true)}";
        }

        var email = string.IsNullOrWhiteSpace(request.email) ? $"{username}@local.test" : request.email.Trim();
        generatedPassword = string.IsNullOrWhiteSpace(request.password)
            ? RandomNumberGenerator.GetString("ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789", 16)
            : null;
        var account = AccountHelper.CreateAccount(username, email, generatedPassword ?? request.password,
            request.uid.GetValueOrDefault(), NormalizeRole(request.role));
        account.SyncPermissionsWithRole();
        if (request.delete_on_logout == true)
        {
            account.DeleteOnLogout = true;
            DatabaseHelper.SaveDatabaseType(account);
        }

        return account;
    }

    // Retires an account whose save could not be written, so a failed request leaves no half-made account behind.
    private static async Task DiscardTestAccount(AccountData account)
    {
        try
        {
            await DeleteAccountData(account);
        }
        catch (Exception ex)
        {
            AccountsLogger.Warn($"Could not remove test account {account.Uid} after a failed copy: {ex.Message}");
        }
    }

    private static IResult SnapshotError(Exception ex)
    {
        return ex is AccountSnapshotException { NotFound: true }
            ? Error(ex.Message, statusCode: StatusCodes.Status404NotFound)
            : Error(ex.Message);
    }

    private static SnapshotView ToSnapshotView(AccountSnapshotMeta meta)
    {
        return new SnapshotView(meta.Id, meta.Name, meta.Description, meta.CreatedAt, meta.SourceUid,
            meta.GameVersion, meta.PlayerLevel, meta.FrontierMainMission, meta.FloorId, meta.TableCount);
    }

    public class AdminTestAccountRequest
    {
        public string? username { get; set; }
        public string? email { get; set; }
        public string? password { get; set; }
        public int? uid { get; set; }
        public string? role { get; set; }
        public string? nickname { get; set; }

        /// <summary>Delete the account with its save once its player logs off (a throwaway test account).</summary>
        public bool? delete_on_logout { get; set; }
    }

    public class AdminSnapshotRestoreRequest
    {
        public string? id { get; set; }
        public string? nickname { get; set; }
    }

    public class AdminSaveSnapshotRequest
    {
        public int uid { get; set; }
        public string? name { get; set; }
        public string? description { get; set; }
    }

    private static bool TryResolveBanExpireAt(AdminBanAccountRequest request, out long expireAt, out string error)
    {
        expireAt = 0;
        error = string.Empty;

        if (request.permanent.GetValueOrDefault(true))
            return true;

        var now = Extensions.GetUnixSec();
        if (request.expire_at.HasValue)
        {
            expireAt = request.expire_at.Value;
            if (expireAt <= now || expireAt > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            {
                error = "Ban expiry must be in the future";
                return false;
            }

            return true;
        }

        if (request.duration_seconds.HasValue)
        {
            if (request.duration_seconds.Value <= 0)
            {
                error = "Ban duration must be greater than zero";
                return false;
            }

            expireAt = now + request.duration_seconds.Value;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(request.duration))
        {
            if (!DurationParser.TryParseSeconds(request.duration, out var seconds) || seconds <= 0)
            {
                error = "Invalid ban duration";
                return false;
            }

            expireAt = now + seconds;
            return true;
        }

        error = "Timed bans require an expiry or duration";
        return false;
    }

    private static IReadOnlyCollection<string> GetActiveConnectionIdentityKeys(int uid)
    {
        var activeConnection = Listener.GetActiveConnection(uid);
        return activeConnection == null
            ? []
            : AccountBanHelper.BuildIdentityKeys([], activeConnection.RemoteEndPoint.Address.ToString());
    }

    public class AdminCreateAccountRequest
    {
        public string? username { get; set; }
        public string? email { get; set; }
        public string? password { get; set; }
        public int? uid { get; set; }
        public string? role { get; set; }
        public string? permissions { get; set; }
        public bool? email_verified { get; set; }
    }

    public class AdminUpdateAccountRequest
    {
        public string? username { get; set; }
        public string? email { get; set; }
        public string? role { get; set; }
        public string? permissions { get; set; }
        public bool? email_verified { get; set; }
    }

    public class AdminSetPasswordRequest
    {
        public string? password { get; set; }
        public bool? revoke_tokens { get; set; }
    }

    public class AdminBanAccountRequest
    {
        public string? reason { get; set; }
        public bool? permanent { get; set; }
        public long? expire_at { get; set; }
        public int? duration_seconds { get; set; }
        public string? duration { get; set; }
    }
}
