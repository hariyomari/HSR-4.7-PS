using System.Text.Json;
using March7thHoney.Database.Account;
using March7thHoney.Util;
using March7thHoney.WebServer.Http;
using March7thHoney.WebServer.Objects;
using Microsoft.AspNetCore.Http;

namespace March7thHoney.WebServer.Handler;

public class ComboTokenGranterHandler
{
    private static readonly Logger Logger = new("ComboLoginV2");

    public IResult Handle(int app_id, int channel_id, string data, string device, string sign,
        IReadOnlyCollection<string> identityKeys)
    {
        LoginTokenData? tokenData = null;
        try
        {
            tokenData = JsonSerializer.Deserialize(data ?? "", WebJsonContext.Default.LoginTokenData);
        }
        catch (JsonException)
        {
            // Malformed client payload — treated as missing (handled as null below).
        }
        var res = new ComboTokenResJson();
        if (tokenData == null)
        {
            Logger.Debug($"Failed to deserialize combo login payload. raw_len={data?.Length ?? 0}");
            res.retcode = WebConstants.RetcodeInvalidData;
            res.message = "Invalid login data";
            return Results.Json(res);
        }

        if (!int.TryParse(tokenData.uid, out var uid))
        {
            Logger.Debug($"Invalid combo login uid. uid={tokenData.uid ?? "<null>"} token_len={tokenData.token?.Length ?? 0}");
            res.retcode = WebConstants.RetcodeInvalidData;
            res.message = "Invalid login data";
            return Results.Json(res);
        }

        // Every combo login must present a token issued by this server.
        var account = AccountData.GetAccountByUid(uid);

        if (account == null)
        {
            Logger.Debug(
                $"Combo login cache validation failed. uid={uid} token_len={tokenData.token?.Length ?? 0} account_exists=False");
            res.retcode = WebConstants.RetcodeFailed;
            res.message = "Game account cache information error";
            return Results.Json(res);
        }

        var isDispatchToken = account.ValidateDispatchToken(tokenData.token);
        var isComboToken = account.ValidateComboToken(tokenData.token);
        if (!isDispatchToken && !isComboToken)
        {
            Logger.Debug(
                $"Combo login cache validation failed. uid={uid} token_len={tokenData.token?.Length ?? 0} account_exists=True");
            res.retcode = WebConstants.RetcodeFailed;
            res.message = "Game account cache information error";
            return Results.Json(res);
        }

        if (!LoginSessionFactory.CanAuthenticate(account, identityKeys, out var failure, "combo_login"))
        {
            Logger.Debug($"Combo login blocked. uid={uid} retcode={failure.Retcode}");
            res.retcode = failure.Retcode;
            res.message = failure.Message;
            return Results.Json(res);
        }

        account.AddKnownIdentityKeys(identityKeys);
        Logger.Debug(
            $"Combo login validated. uid={uid} token_type={(isDispatchToken ? "dispatch" : "combo")} token_len={tokenData.token?.Length ?? 0}");
        res.message = "OK";
        res.data = new ComboTokenResJson.LoginData(account.Uid.ToString(), account.GenerateComboToken());
        return Results.Json(res);
    }
}

public class LoginTokenData
{
    public string? uid { get; set; }
    public string? token { get; set; }
    public bool guest { get; set; }
}
