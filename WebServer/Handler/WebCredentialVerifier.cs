using March7thHoney.Database.Account;
using March7thHoney.WebServer.Services;
using Microsoft.AspNetCore.Http;

namespace March7thHoney.WebServer.Handler;

/// <summary>
///     The single uid+password check for web endpoints outside the dispatch login (player console, sr-tools
///     import/export). Same rules as <see cref="LoginSessionFactory" />: passwordless accounts never authenticate,
///     a client under an identity block or login rate-limit block is refused before any password is checked,
///     every miss counts towards the login limit, and banned accounts are refused.
/// </summary>
internal static class WebCredentialVerifier
{
    public const string InvalidCredentialsMessage = "Invalid password for this uid.";

    /// <param name="Response">A ready response (rate-limit or identity block) the caller returns as-is, if set.</param>
    /// <param name="StatusCode">HTTP status for <paramref name="Message" /> when <paramref name="Response" /> is null.</param>
    internal readonly record struct Failure(IResult? Response, int StatusCode, string Message);

    /// <param name="attemptCounted">The caller already counted this request against the login limit.</param>
    public static bool TryVerify(HttpContext ctx, AccountData account, string? password, string stage,
        out Failure failure, bool attemptCounted = false)
    {
        failure = default;
        var identityKeys = LoginIdentityContextFactory.Build(ctx.Request);
        var accountKey = account.Uid.ToString();

        if (AuthRateLimiter.TryRejectBlocked(ctx, AuthRateLimitAction.Login, identityKeys, accountKey,
                out var blocked))
        {
            failure = new Failure(blocked, 0, "");
            return false;
        }

        // Passwordless accounts must be provisioned by an administrator; a uid alone never authenticates.
        if (!account.HasPassword() || string.IsNullOrEmpty(password) || !account.VerifyPassword(password))
        {
            failure = !attemptCounted && AuthRateLimiter.TryReject(ctx, AuthRateLimitAction.Login, identityKeys,
                accountKey, out var limited)
                ? new Failure(limited, 0, "")
                : new Failure(null, StatusCodes.Status401Unauthorized, InvalidCredentialsMessage);
            return false;
        }

        if (AccountBanHelper.TryGetActiveBan(account, identityKeys, out var banStatus, stage))
        {
            failure = new Failure(null, StatusCodes.Status403Forbidden, banStatus.FormatLoginMessage());
            return false;
        }

        return true;
    }
}
