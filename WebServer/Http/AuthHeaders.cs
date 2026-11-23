using Microsoft.AspNetCore.Http;

namespace March7thHoney.WebServer.Http;

/// <summary>Reads session tokens that clients send in request headers.</summary>
internal static class AuthHeaders
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>
    ///     Returns true when the request has an <c>Authorization: Bearer</c> header (scheme matched case-insensitively);
    ///     <paramref name="token" /> is the trimmed remainder, which may be empty.
    /// </summary>
    public static bool TryGetBearerToken(HttpRequest request, out string token)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            token = authorization[BearerPrefix.Length..].Trim();
            return true;
        }

        token = string.Empty;
        return false;
    }
}
