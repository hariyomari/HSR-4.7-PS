using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

internal static class AdminAuthChecks
{
    private const string NotAuthorizedBody = "{\"retcode\":-201,\"message\":\"Not authorized\",\"data\":null}";

    // The admin endpoints that answer without a session, with the exact response each gives an anonymous caller.
    private static readonly Dictionary<string, (HttpStatusCode Status, string Body)> SessionEndpoints = new()
    {
        ["POST /admin/api/login"] = (HttpStatusCode.Unauthorized,
            "{\"retcode\":-201,\"message\":\"Admin key is invalid\",\"data\":null}"),
        ["POST /admin/api/logout"] = (HttpStatusCode.OK, "{\"retcode\":0,\"message\":\"OK\",\"data\":{}}"),
        ["GET /admin/api/session"] = (HttpStatusCode.Unauthorized, NotAuthorizedBody)
    };

    /// <summary>
    ///     Sends every /admin/api endpoint in the routing table a request without a session. The list comes from the
    ///     application, so an endpoint added later is covered without editing this check.
    /// </summary>
    public static async Task RunAnonymous(string address, EndpointDataSource endpoints, Action<string, bool> check)
    {
        using var anonymous = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) };
        var adminEndpoints = endpoints.Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => (Path: SamplePath(endpoint.RoutePattern),
                Methods: endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? []))
            .Where(endpoint => endpoint.Path.StartsWith("/admin/api/", StringComparison.Ordinal))
            .SelectMany(endpoint => endpoint.Methods.Select(method => (Method: method, endpoint.Path)))
            .ToList();

        var protectedCount = 0;
        var wrong = new List<string>();
        foreach (var (method, path) in adminEndpoints)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (method != HttpMethods.Get)
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            using var response = await anonymous.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            var key = $"{method} {path}";
            if (SessionEndpoints.TryGetValue(key, out var expected))
            {
                check($"{key} answers an anonymous caller as before",
                    response.StatusCode == expected.Status && body == expected.Body);
                continue;
            }

            protectedCount++;
            if (response.StatusCode != HttpStatusCode.Unauthorized || body != NotAuthorizedBody)
                wrong.Add($"{key} -> {(int)response.StatusCode} {body}");
        }

        Console.WriteLine($"MEASURE admin API endpoints: {adminEndpoints.Count} ({protectedCount} behind the session check)");        check("the routing table lists the admin session endpoints",
            SessionEndpoints.Keys.All(key => adminEndpoints.Any(endpoint => $"{endpoint.Method} {endpoint.Path}" == key)));
        check($"every other admin endpoint refuses a missing session{(wrong.Count == 0 ? "" : ": " + string.Join("; ", wrong))}",
            protectedCount >= 40 && wrong.Count == 0);
    }

    /// <summary>The token is accepted from the header and the query parameter as well as from a Bearer header.</summary>
    public static async Task RunWithToken(string address, string token, Action<string, bool> check)
    {
        using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) };
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/overview"))
        {
            request.Headers.Add("X-Admin-Session", token);
            using var response = await client.SendAsync(request);
            check("admin API accepts the X-Admin-Session header", response.StatusCode == HttpStatusCode.OK);
        }

        using (var response = await client.GetAsync($"/admin/api/logs/files?token={Uri.EscapeDataString(token)}"))
            check("admin API accepts the token query parameter", response.StatusCode == HttpStatusCode.OK);

        using (var request = new HttpRequestMessage(HttpMethod.Get, "/admin/api/overview"))
        {
            request.Headers.Add("X-Admin-Session", "not-a-real-session");
            using var response = await client.SendAsync(request);
            check("admin API rejects an unknown session token",
                response.StatusCode == HttpStatusCode.Unauthorized &&
                await response.Content.ReadAsStringAsync() == NotAuthorizedBody);
        }
    }

    // Route parameters are all ints or free names here, so "1" satisfies every constraint.
    private static string SamplePath(RoutePattern pattern)
    {
        return "/" + string.Join("/", pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part =>
            part switch
            {
                RoutePatternLiteralPart literal => literal.Content,
                RoutePatternSeparatorPart separator => separator.Content,
                RoutePatternParameterPart => "1",
                _ => ""
            }))));
    }
}
