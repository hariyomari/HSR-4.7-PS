namespace March7thHoney.WebServer.Http;

/// <summary>Limits, header names and retcodes that more than one web endpoint relies on.</summary>
internal static class WebConstants
{
    /// <summary>Longest command the admin page or the player console accepts.</summary>
    public const int MaxCommandLength = 4096;

    /// <summary>Longest announcement text, whether sent from the admin page or saved as the configured banner.</summary>
    public const int MaxAnnouncementLength = 4096;

    /// <summary>Header carrying an admin page session token when no Bearer header is sent.</summary>
    public const string AdminSessionHeader = "X-Admin-Session";

    /// <summary>Header carrying a player console session token when no Bearer header is sent.</summary>
    public const string ConsoleSessionHeader = "X-Console-Session";

    /// <summary>
    ///     Generic failure: wrong credentials, a failed validation or a missing account. The SDK login pages and the
    ///     admin page show the message.
    /// </summary>
    public const int RetcodeFailed = -201;

    /// <summary>Malformed login data, or an account that has to verify its email address first.</summary>
    public const int RetcodeInvalidData = -202;

    /// <summary>The caller is refused outright: an account ban, an identity block or the rate limit.</summary>
    public const int RetcodeBlocked = -203;
}
