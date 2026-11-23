using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using Microsoft.AspNetCore.Http;
using static March7thHoney.WebServer.Http.WebResults;

namespace March7thHoney.WebServer.Controllers;

public static partial class AdminRoutes
{
    private static IResult GetIdentityBlocks()
    {
        return Success(new IdentityBlocksData(IdentityBlockHelper.GetSnapshot().Select(ToIdentityBlock).ToList()));
    }

    private static async Task<IResult> BlockIdentity(AdminIdentityBlockRequest? request)
    {
        if (request == null || request.kind is not ("ip" or "device")) return Error("Kind must be ip or device");
        if (string.IsNullOrWhiteSpace(request.value) == string.IsNullOrWhiteSpace(request.identity_key))
            return Error("Supply either a value or an identity key");
        if (!TryResolveBanExpireAt(request, out var expireAt, out var error)) return Error(error);
        try
        {
            var isKey = !string.IsNullOrWhiteSpace(request.identity_key);
            var block = IdentityBlockHelper.Block(request.kind, isKey ? request.identity_key! : request.value!, request.reason,
                expireAt, isKey);
            var connections = Listener.GetConnections().ToList();
            var affected = GetAllAccounts().Where(a => AccountBanHelper.ParseIdentityKeys(a.KnownIdentityKeys)
                .Contains(block.IdentityKey)).Select(a => a.Uid).ToHashSet();
            foreach (var connection in connections)
                if (connection.Player != null && AccountBanHelper.CreateIpIdentityKey(connection.RemoteEndPoint.Address.ToString()) == block.IdentityKey)
                    affected.Add(connection.Player.Uid);
            foreach (var uid in affected) AccountData.GetAccountByUid(uid)?.ClearLoginTokens();
            await Task.WhenAll(connections.Where(c => (c.Player != null && affected.Contains(c.Player.Uid)) ||
                    AccountBanHelper.CreateIpIdentityKey(c.RemoteEndPoint.Address.ToString()) == block.IdentityKey)
                .Select(c => ConnectionDisconnectHelper.KickBannedAsync(c, block.GetStatus())));
            return Success(new IdentityBlockWrap(ToIdentityBlock(block), affected.Count));
        }
        catch (ArgumentException ex) { return Error(ex.Message); }
    }

    private static IResult UnblockIdentity(int id)
    {
        if (!IdentityBlockHelper.Remove(id)) return Error("Block not found", statusCode: 404);
        return Success(new EmptyData());
    }

    private static IdentityBlockSummary ToIdentityBlock(IdentityBlockData block) => new(
        block.Uid, block.Kind, block.Value, block.IdentityKey, block.Reason, block.CreatedAt, block.ExpireAt,
        block.GetStatus().IsActive);

    public class AdminIdentityBlockRequest : AdminBanAccountRequest
    {
        public string? kind { get; set; }
        public string? value { get; set; }
        public string? identity_key { get; set; }
    }
}
