using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using March7thHoney.Util;
using March7thHoney.WebServer.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static void MapAnnounceRoutes(IEndpointRouteBuilder api)
    {
        api.MapPost("/announce", SendAnnounce);
        api.MapGet("/announce/config", GetAnnounceConfig);
        api.MapPut("/announce/config", UpdateAnnounceConfig);
    }

    private static async Task<IResult> SendAnnounce(AdminAnnounceRequest? request)
    {
        request ??= new AdminAnnounceRequest();
        var content = (request.content ?? string.Empty).Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(content))
            return Error("Announcement content cannot be empty");
        if (content.Length > WebConstants.MaxAnnouncementLength)
            return Error("Announcement content is too long");

        var config = ConfigManager.Config.ServerOption.ServerAnnounce;
        var durationSeconds = config.GetDurationSeconds();
        if (!string.IsNullOrWhiteSpace(request.duration))
        {
            if (!DurationParser.TryParseSeconds(request.duration, out durationSeconds) || durationSeconds <= 0)
                return Error("Invalid duration (examples: 30m, 2h, 7d)");
            durationSeconds = Math.Clamp(durationSeconds, 1, DurationParser.MaxDurationSeconds);
        }

        long frequencySeconds = config.GetBannerFrequencySeconds();
        if (!string.IsNullOrWhiteSpace(request.frequency))
        {
            if (!DurationParser.TryParseSeconds(request.frequency, out frequencySeconds) || frequencySeconds <= 0)
                return Error("Invalid banner frequency (examples: 10m, 1h)");
            frequencySeconds = Math.Clamp(frequencySeconds, 1, uint.MaxValue);
        }

        IEnumerable<Connection> recipients;
        if (request.target_uid.GetValueOrDefault() > 0)
        {
            var connection = Listener.GetActiveConnection(request.target_uid!.Value);
            if (connection == null)
                return Error("Target player is not online", statusCode: StatusCodes.Status404NotFound);
            recipients = [connection];
        }
        else
        {
            recipients = Listener.GetConnections();
        }

        var sent = await AnnounceBroadcaster.SendAsync(recipients, content, durationSeconds,
            (uint)frequencySeconds);

        return Success(new AnnounceSendData(sent, durationSeconds, frequencySeconds));
    }

    private static IResult GetAnnounceConfig()
    {
        return Success(BuildAnnounceConfig());
    }

    private static IResult UpdateAnnounceConfig(AdminAnnounceConfigRequest? request)
    {
        request ??= new AdminAnnounceConfigRequest();
        var config = ConfigManager.Config.ServerOption.ServerAnnounce;

        if (request.duration != null)
        {
            var duration = request.duration.Trim();
            if (!DurationParser.TryParseSeconds(duration, out var seconds) || seconds <= 0)
                return Error("Invalid duration (examples: 30m, 2h, 7d)");
            config.Duration = duration;
        }

        if (request.frequency != null)
        {
            var frequency = request.frequency.Trim();
            if (!DurationParser.TryParseSeconds(frequency, out var seconds) || seconds <= 0)
                return Error("Invalid banner frequency (examples: 10m, 1h)");
            config.BannerFrequency = frequency;
        }

        if (request.content != null)
        {
            var content = request.content.Replace("\r\n", "\n").Trim();
            if (content.Length > WebConstants.MaxAnnouncementLength)
                return Error("Announcement content is too long");
            config.AnnounceContent = content;
        }

        if (request.enabled.HasValue)
            config.EnableAnnounce = request.enabled.Value;

        ConfigManager.SaveConfig();
        return Success(BuildAnnounceConfig());
    }

    private static AnnounceConfigData BuildAnnounceConfig()
    {
        var config = ConfigManager.Config.ServerOption.ServerAnnounce;
        return new AnnounceConfigData(
            config.EnableAnnounce,
            config.AnnounceContent,
            config.Duration,
            config.BannerFrequency,
            config.GetDurationSeconds(),
            config.GetBannerFrequencySeconds());
    }

    public class AdminAnnounceRequest
    {
        public string? content { get; set; }
        public string? duration { get; set; }
        public string? frequency { get; set; }
        public int? target_uid { get; set; }
    }

    public class AdminAnnounceConfigRequest
    {
        public bool? enabled { get; set; }
        public string? content { get; set; }
        public string? duration { get; set; }
        public string? frequency { get; set; }
    }
}
