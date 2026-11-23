using System.Net;
using March7thHoney.Database.Account;
using March7thHoney.Util;
using March7thHoney.WebServer.Http;
using March7thHoney.WebServer.Objects;
using March7thHoney.WebServer.Pages;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Http;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Handler;

public class AccountRecoveryHandler
{
    private const string VerificationSentMessage = "If the account exists, a verification email has been sent.";
    private const string ResetSentMessage = "If the account exists, a reset email has been sent.";
    private const string PasswordResetRequiresVerifiedEmailMessage =
        "Please verify your email before requesting a password reset.";

    public async Task<IResult> SendVerificationEmail(SendVerificationEmailReqJson req)
    {
        var account = ResolveAccount(req.account, req.username, req.email);
        if (account is { IsEmailVerified: false })
        {
            await SendVerificationEmailForAccount(account);
        }

        return Ok(VerificationSentMessage);
    }

    public static async Task<bool> SendVerificationEmailForAccount(AccountData account, bool markUnverified = false)
    {
        if (string.IsNullOrWhiteSpace(account.Email))
            return false;

        if (markUnverified)
        {
            account.SetEmail(account.Email, false);
            account.ClearLoginTokens();
        }

        var token = account.GenerateEmailVerificationToken();
        return await AccountEmailSender.SendVerificationEmailAsync(account, token);
    }

    public IResult VerifyEmail(string? uid, string? token)
    {
        if (!int.TryParse(uid, out var parsedUid) || string.IsNullOrWhiteSpace(token))
            return VerificationPage("Email verification failed", "The verification link is invalid or expired.", 400);

        var account = AccountData.GetAccountByUid(parsedUid);
        if (account == null || !account.ValidateEmailVerificationToken(token))
            return VerificationPage("Email verification failed", "The verification link is invalid or expired.", 400);

        account.MarkEmailVerified();
        return VerificationPage("Email verified", "You can return to the game login screen.");
    }

    public async Task<IResult> ForgotPassword(ForgotPasswordReqJson req)
    {
        var username = (req.username ?? req.account ?? string.Empty).Trim();
        var email = (req.email ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
            return Error("Username and email are required");

        var account = AccountData.GetAccountByUserName(username);
        var normalizedEmail = AccountData.NormalizeEmail(email);
        var accountEmailMatches = account != null &&
                                  string.Equals(account.NormalizedEmail ?? AccountData.NormalizeEmail(account.Email),
                                      normalizedEmail, StringComparison.OrdinalIgnoreCase);

        if (accountEmailMatches && !account!.IsEmailVerified)
        {
            account.ClearPasswordResetToken();
            return Error(PasswordResetRequiresVerifiedEmailMessage, WebConstants.RetcodeInvalidData);
        }

        if (accountEmailMatches)
        {
            var token = account!.GeneratePasswordResetToken();
            await AccountEmailSender.SendPasswordResetEmailAsync(account, token);
        }

        return Ok(ResetSentMessage);
    }

    public IResult ResetPassword(ResetPasswordReqJson req)
    {
        if (!int.TryParse(req.uid, out var uid) || string.IsNullOrWhiteSpace(req.token))
            return Error("Invalid or expired reset token");

        var password = req.password ?? string.Empty;
        var confirmation = req.confirm_password ?? req.confirmPassword ?? req.confirm;
        if (!string.IsNullOrEmpty(confirmation) && !string.Equals(password, confirmation, StringComparison.Ordinal))
            return Error("Passwords do not match");

        var account = AccountData.GetAccountByUid(uid);
        if (account != null && !account.IsEmailVerified)
            account.ClearPasswordResetToken();

        if (account == null || !account.IsEmailVerified || !account.ValidatePasswordResetToken(req.token))
            return Error("Invalid or expired reset token");

        try
        {
            AccountHelper.SetPassword(account, password);
            account.ClearPasswordResetToken(false);
            account.ClearLoginTokens();
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        return Ok("Password updated");
    }

    private static AccountData? ResolveAccount(string? account, string? username, string? email)
    {
        if (!string.IsNullOrWhiteSpace(username))
            return AccountData.GetAccountByUserName(username);

        if (!string.IsNullOrWhiteSpace(email))
            return AccountData.GetAccountByEmail(email);

        if (!string.IsNullOrWhiteSpace(account))
            return AccountData.GetAccountByLoginIdentifier(account);

        return null;
    }

    private static IResult VerificationPage(string title, string message, int statusCode = 200)
    {
        var success = statusCode == 200;
        var serverName = ConfigManager.Config.GameServer.GameServerName;
        var iconMarkup = LoginPlatformPage.RenderIconMarkup(ConfigManager.Config.ServerOption.Auth.RegistrationPageIcon,
            serverName);
        var key = success ? "verify.ok" : "verify.failed";
        var body = $$"""
{{LoginPlatformPage.Brand(iconMarkup, key + ".title", title, serverName)}}
      <div class="status">
        <div class="icon {{(success ? "good" : "bad")}}" aria-hidden="true">{{(success ? "&#10003;" : "!")}}</div>
        <p data-i18n="{{key}}.message">{{WebUtility.HtmlEncode(message)}}</p>
      </div>
      <div class="links">
        <a href="/login-platform/forgot-password.html" data-i18n="lp.forgot_link">Forgot password?</a>
      </div>
""";
        var strings = """
    LP.extend({
      "zh-CN": {
        "verify.ok.title": "邮箱已验证",
        "verify.ok.message": "验证完成，现在可以回到游戏登录界面登录了。",
        "verify.failed.title": "邮箱验证失败",
        "verify.failed.message": "验证链接无效或已过期，请重新发送验证邮件。"
      },
      "en": {
        "verify.ok.title": "Email verified",
        "verify.ok.message": "You can return to the game login screen.",
        "verify.failed.title": "Email verification failed",
        "verify.failed.message": "The verification link is invalid or expired."
      }
    });
""";
        return Results.Text(LoginPlatformPage.Wrap(title, serverName, body, "    LP.init();", strings),
            "text/html; charset=utf-8", null, statusCode);
    }
}
