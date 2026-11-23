using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using March7thHoney.WebServer.Request;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

public static class GateServerRoutes
{
    internal const string RogueTournBuildRefStaticPath = "/rpgsimulator/0/";
    internal const string RogueTournBuildRefFile = "rpg_lineup_list.json";
    private const string RogueTournBuildRefPayload = "{\"lineups\":[],\"version\":249}";

    public static void MapGateServerRoutes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/query_gateway", (HttpContext ctx) =>
        {
            if (!ConfigManager.Config.ServerOption.ServerConfig.RunGateway)
                return Results.StatusCode(404);

            // Read the parameters from the query string by hand; a missing one becomes an empty string. Minimal API's
            // [AsParameters] treats non-nullable string properties as required and silently answers 400 when the
            // client omits any of them, so this reads as leniently as QueryDispatch does.
            var q = ctx.Request.Query;
            var req = new GateWayRequest
            {
                version = q["version"].ToString(),
                t = q["t"].ToString(),
                uid = q["uid"].ToString(),
                language_type = q["language_type"].ToString(),
                platform_type = q["platform_type"].ToString(),
                dispatch_seed = q["dispatch_seed"].ToString(),
                channel_id = q["channel_id"].ToString(),
                sub_channel_id = q["sub_channel_id"].ToString(),
                is_need_url = q["is_need_url"].ToString(),
                is_new_format = q["is_new_format"].ToString(),
                game_version = q["game_version"].ToString(),
                account_type = q["account_type"].ToString(),
                account_uid = q["account_uid"].ToString(),
            };

            var publicBaseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}{ctx.Request.PathBase}";
            var handler = new QueryGatewayHandler(req, publicBaseUrl);
            return Results.Text(handler.Data, "text/plain");
        });

        // These are exact routes because the prefix is under our control and the client appends the
        // known file name. The second path is the API fallback used when CDN lookup is unavailable.
        app.MapGet(RogueTournBuildRefStaticPath + RogueTournBuildRefFile,
            () => Results.Text(RogueTournBuildRefPayload, "application/json; charset=utf-8"));
        app.MapGet("/event/rpgsimulator/game/tourn/lineup/cdn_content",
            () => Results.Text(RogueTournBuildRefPayload, "application/json; charset=utf-8"));
    }
}
