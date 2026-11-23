using System.Text;
using System.Text.Json.Nodes;
using March7thHoney.Database.Account;
using March7thHoney.GameServer.Game.Freesr;
using March7thHoney.GameServer.Server;
using March7thHoney.Internationalization;
using March7thHoney.Kcp;
using March7thHoney.Util;
using March7thHoney.WebServer.Handler;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace March7thHoney.WebServer.Controllers;

public static class JsonRoutes
{
    public static void MapJsonRoutes(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sr-tools", (Delegate)SrTools);
        app.MapPost("/sr-tools-export", (Delegate)DownloadFreedata);
        // The export used to take the password in the query string, where proxies and logs keep it.
        app.MapGet("/sr-tools-export", () => Results.Text(
            "Use POST with a JSON body: {\"username\":\"<uid>\",\"password\":\"<password>\"}.",
            "text/plain", null, StatusCodes.Status405MethodNotAllowed));
    }

    private static async Task<IResult> SrTools(HttpContext ctx)
    {
        var request = ctx.Request;
        try
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            string body = await reader.ReadToEndAsync();

            var obj = JsonNode.Parse(body);
            string username = obj?["username"]?.ToString() ?? "";
            string password = obj?["password"]?.ToString() ?? "";
            string dataJson = obj?["data"]?.ToJsonString() ?? "";

            if (!int.TryParse(username, out var uid))
                return Results.Text("Username field must be the player's UID (integer).", "text/plain", null, 400);

            var acc = AccountData.GetAccountByUid(uid);
            if (acc == null)
                return Results.Text("uid not found on server", "text/plain", null, 400);

            // RSA only controls login transport decryption, not permission to overwrite a save.
            if (!WebCredentialVerifier.TryVerify(ctx, acc, password, "sr_tools_import", out var failure))
                return CredentialFailure(failure);

            var con = Listener.GetPlayerConnection(acc.Uid);

            if (con?.Player == null)
                return Results.Text("player offline", "text/plain", null, 400);

            if (dataJson != string.Empty)
            {
                var player = con.Player;
                await FreesrShared.ImportJson(dataJson, player, async msg =>
                {
                    if (msg[0] == "Game.Command.Json.ImportSummary" || msg[0] == "Game.Command.Json.AvatarExcelNotFound")
                        return;
                    throw new Exception(I18NManager.Translate(msg[0], msg.Skip(1).ToArray()));
                });
            }

            return Results.Text("Successfully synced data from website!", "application/json");
        }
        catch (Exception ex)
        {
            return Results.Text(ex.Message, "text/plain", null, 500);
        }
    }

    private static async Task<IResult> DownloadFreedata(HttpContext ctx)
    {
        JsonObject? body;
        try
        {
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            body = JsonNode.Parse(await reader.ReadToEndAsync()) as JsonObject;
        }
        catch (Exception)
        {
            body = null;
        }

        var uidText = (body?["username"] ?? body?["uid"])?.ToString() ?? "";
        if (!int.TryParse(uidText, out var uid) || uid == 0)
            return Results.Text("Username field must be the player's UID (integer).", "text/plain", null, 400);

        var acc = AccountData.GetAccountByUid(uid);
        if (acc == null)
            return Results.Text("uid not found on server", "text/plain", null, 400);

        if (!WebCredentialVerifier.TryVerify(ctx, acc, body?["password"]?.ToString(), "sr_tools_export",
                out var failure))
            return CredentialFailure(failure);

        var json = await FreesrShared.ExportPlayerDataAsync(uid, true);

        ctx.Response.Headers["Content-Disposition"] =
            "attachment; filename=freesr-data.json; filename*=UTF-8''freesr-data.json";
        return Results.Text(json, "application/octet-stream");
    }

    private static IResult CredentialFailure(WebCredentialVerifier.Failure failure) =>
        failure.Response ?? Results.Text(failure.Message, "text/plain", null, failure.StatusCode);
}
