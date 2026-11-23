using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using March7thHoney.Database.Account;
using March7thHoney.Util;
using MimeKit;

/// <summary>
///     Account emails carry reset and verification tokens, so their links must use Email.PublicBaseUrl and never
///     the Host header of the request that triggered them.
/// </summary>
internal static class EmailLinkRegression
{
    public static async Task Run(HttpClient client, string password, Action<string, bool> check)
    {
        var auth = ConfigManager.Config.ServerOption.Auth;
        var email = auth.Email;
        var emailRateLimit = auth.AntiAbuse.EmailRateLimitEnabled;
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var stop = new CancellationTokenSource();
        var received = new List<MimeMessage>();
        var smtp = FakeSmtp(listener, received, stop.Token);
        try
        {
            auth.AntiAbuse.EmailRateLimitEnabled = false;
            email.Enabled = true;
            email.Smtp.Host = "127.0.0.1";
            email.Smtp.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            email.Smtp.EnableSsl = false;
            email.Smtp.Username = "";
            email.Smtp.FromAddress = "noreply@example.test";

            var account = AccountHelper.CreateAccount("MailVictim", "mail.victim@example.test", password, 0, null,
                identityKeys: AccountBanHelper.BuildIdentityKeys(["mail-victim-device"], "192.0.2.250"));
            account.MarkEmailVerified();

            async Task<int> Forgot()
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/account/password/forgot")
                {
                    Content = JsonContent.Create(new { username = "MailVictim", email = "mail.victim@example.test" })
                };
                request.Headers.Host = "evil.example";
                using var response = await client.SendAsync(request);
                lock (received) return received.Count;
            }

            email.PublicBaseUrl = "";
            check("password reset email is refused while Email.PublicBaseUrl is empty", await Forgot() == 0);
            email.PublicBaseUrl = "evil.example";
            check("password reset email is refused while Email.PublicBaseUrl is not an absolute URL", await Forgot() == 0);
            email.PublicBaseUrl = "javascript:alert(1)";
            check("password reset email is refused for a non-http(s) Email.PublicBaseUrl", await Forgot() == 0);

            email.PublicBaseUrl = "https://sr.example.test/";
            var sent = await Forgot() == 1;
            MimeMessage? message;
            lock (received) message = received.LastOrDefault();
            var expected = $"https://sr.example.test/login-platform/reset-password.html?uid={account.Uid}&";
            check("password reset link uses Email.PublicBaseUrl instead of the request Host",
                sent && message != null &&
                (message.TextBody ?? "").Contains(expected, StringComparison.Ordinal) &&
                (message.HtmlBody ?? "").Contains(expected.Replace("&", "&amp;"), StringComparison.Ordinal) &&
                !(message.TextBody + message.HtmlBody).Contains("evil.example", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            email.Enabled = false;
            email.PublicBaseUrl = "";
            email.Smtp.Host = "";
            auth.AntiAbuse.EmailRateLimitEnabled = emailRateLimit;
            stop.Cancel();
            listener.Stop();
            try { await smtp; } catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }
    }

    // Minimal SMTP endpoint that records each message instead of delivering it.
    private static async Task FakeSmtp(TcpListener listener, List<MimeMessage> received, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            using var tcp = await listener.AcceptTcpClientAsync(token);
            await using var stream = tcp.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII);
            await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
            await writer.WriteLineAsync("220 localhost");
            while (await reader.ReadLineAsync(token) is { } line)
            {
                var verb = line.Split(' ')[0].ToUpperInvariant();
                if (verb == "DATA")
                {
                    await writer.WriteLineAsync("354 end with .");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync(token) is { } body && body != ".")
                        data.Append(body.StartsWith("..", StringComparison.Ordinal) ? body[1..] : body).Append("\r\n");
                    var message = await MimeMessage.LoadAsync(new MemoryStream(Encoding.ASCII.GetBytes(data.ToString())), token);
                    lock (received) received.Add(message);
                    await writer.WriteLineAsync("250 queued");
                }
                else if (verb == "QUIT")
                {
                    await writer.WriteLineAsync("221 bye");
                    break;
                }
                else
                {
                    await writer.WriteLineAsync("250 ok");
                }
            }
        }
    }
}
