namespace March7thHoney.Util;

public static class HttpNetwork
{
    // one pooled client for the process; the per-call timeout is applied through a linked cancellation source
    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async ValueTask<(int, string?)> SendGetRequest(string url, int timeout = 30)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var response = await Client.GetAsync(url, cts.Token);
            var content = await response.Content.ReadAsStringAsync(cts.Token);
            return ((int)response.StatusCode, content);
        }
        catch (Exception ex)
        {
            return (500, ex.Message);
        }
    }

    public static async ValueTask<(int, string?)> SendPostFormRequest(string url, Dictionary<string, string> form,
        Dictionary<string, string>? headers = null, int timeout = 30)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(form)
            };

            if (headers != null)
            {
                foreach (var (key, value) in headers)
                    request.Headers.TryAddWithoutValidation(key, value);
            }

            using var response = await Client.SendAsync(request, cts.Token);
            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);
            return ((int)response.StatusCode, responseContent);
        }
        catch (Exception ex)
        {
            return (500, ex.Message);
        }
    }
}
