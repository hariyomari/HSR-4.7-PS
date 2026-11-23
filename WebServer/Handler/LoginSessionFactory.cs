using March7thHoney.Database.Account;
using March7thHoney.Util;
using System.Net.Mail;
using System.Text.RegularExpressions;
using March7thHoney.WebServer.Http;

namespace March7thHoney.WebServer.Handler;

internal static class LoginSessionFactory
{
    public const string InvalidCredentialsMessage = "Incorrect account or password";
    public static string EmailVerificationRequiredMessage =>
        ConfigManager.Config.ServerOption.Auth.EmailVerificationRequiredMessage;

    public static bool TryCreateSession(string? account, string? password, out LoginSession session, out LoginFailure failure)
    {
        return TryCreateSession(account, password, [], out session, out failure);
    }

    public static bool TryCreateSession(string? account, string? password, IReadOnlyCollection<string> identityKeys,
        out LoginSession session, out LoginFailure failure)
    {
        session = default;
        failure = LoginFailure.InvalidCredentials();
        var autoCreateUser = ConfigManager.Config.ServerOption.AutoCreateUser;

        var normalizedAccount = (account ?? string.Empty).Trim();
        var normalizedPassword = password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAccount) || string.IsNullOrWhiteSpace(normalizedPassword))
            return false;

        var accountData = AccountData.GetAccountByLoginIdentifier(normalizedAccount);
        if (accountData == null && autoCreateUser)
        {
            try
            {
                var autoEmail = BuildAutoCreateEmail(normalizedAccount);
                accountData = AccountHelper.CreatePublicAccount(normalizedAccount, autoEmail, normalizedPassword, identityKeys);
            }
            catch (Exception ex)
            {
                failure = new LoginFailure(WebConstants.RetcodeFailed, ex.Message);
                return false;
            }
        }

        if (accountData == null)
            return false;

        var canonicalAccount = AccountData.GetAccountByUid(accountData.Uid) ?? accountData;
        // Passwordless accounts must be provisioned by an administrator; first login cannot claim them.
        if (!canonicalAccount.HasPassword() || !canonicalAccount.VerifyPassword(normalizedPassword))
            return false;

        if (!CanAuthenticate(canonicalAccount, identityKeys, out failure, "password_login"))
            return false;

        var username = canonicalAccount.Username ?? normalizedAccount;
        canonicalAccount.AddKnownIdentityKeys(identityKeys);
        canonicalAccount.SyncPermissionsWithRole();
        var dispatchToken = canonicalAccount.GenerateDispatchToken();
        session = new LoginSession(
            canonicalAccount.Uid.ToString(),
            username,
            canonicalAccount.Email ?? string.Empty,
            canonicalAccount.IsEmailVerified,
            dispatchToken);
        return true;
    }

    public static bool CanAuthenticate(AccountData account, out LoginFailure failure)
    {
        return CanAuthenticate(account, [], out failure);
    }

    public static bool CanAuthenticate(AccountData account, IReadOnlyCollection<string> identityKeys,
        out LoginFailure failure, string stage = "authentication")
    {
        failure = LoginFailure.InvalidCredentials();

        if (AccountBanHelper.TryGetActiveBan(account, identityKeys, out var banStatus, stage))
        {
            account.ClearLoginTokens();
            failure = LoginFailure.Banned(banStatus);
            return false;
        }

        if (account.IsDeleting || !ReferenceEquals(AccountData.GetAccountByUid(account.Uid), account))
            return false;

        if (account.CanLogin())
            return true;

        account.ClearLoginTokens();
        failure = LoginFailure.EmailVerificationRequired();
        return false;
    }

    public static bool TryCreateSession(string? account, string? password, out LoginSession session, out string errorMessage)
    {
        LoginFailure failure;
        var succeeded = TryCreateSession(account, password, out session, out failure);
        errorMessage = failure.Message;
        return succeeded;
    }

    internal readonly record struct LoginFailure(int Retcode, string Message)
    {
        public static LoginFailure InvalidCredentials()
        {
            return new LoginFailure(WebConstants.RetcodeFailed, InvalidCredentialsMessage);
        }

        public static LoginFailure EmailVerificationRequired()
        {
            return new LoginFailure(WebConstants.RetcodeInvalidData, EmailVerificationRequiredMessage);
        }

        public static LoginFailure Banned(AccountBanStatus banStatus)
        {
            return new LoginFailure(WebConstants.RetcodeBlocked, banStatus.FormatLoginMessage());
        }
    }

    internal readonly record struct LoginSession(
        string Uid,
        string Username,
        string Email,
        bool IsEmailVerified,
        string DispatchToken);

    private static string BuildAutoCreateEmail(string account)
    {
        if (LooksLikeValidEmail(account))
            return account.Trim();

        var seed = Regex.Replace(account.Trim().ToLowerInvariant(), "[^a-z0-9._-]", "-");
        if (string.IsNullOrWhiteSpace(seed))
            seed = "user";

        return $"{seed}@autocreate.local";
    }

    private static bool LooksLikeValidEmail(string value)
    {
        try
        {
            var address = new MailAddress(value.Trim());
            return string.Equals(address.Address, value.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
