using March7thHoney.Database;
using March7thHoney.Database.Account;
using March7thHoney.GameServer.Server;
using March7thHoney.Kcp;
using March7thHoney.Util;

namespace March7thHoney.GameServer.Game.Player;

/// <summary>
///     Deletes an account with its save, leaving a tombstone so the uid stays unused. Also ends the life of a
///     throwaway test account (<see cref="AccountData.DeleteOnLogout" />) once its player logs off.
/// </summary>
public static class AccountDeletion
{
    private static readonly Logger Logger = new("Account");

    public static async System.Threading.Tasks.Task DeleteAsync(AccountData account)
    {
        var database = DatabaseHelper.Instance ?? throw new InvalidOperationException("Database is not initialized");
        if (!DatabaseHelper.LoadAllData) throw new InvalidOperationException("Database is still loading; retry deletion shortly");
        Claim(account);
        try
        {
            account.ClearLoginTokens();
            var connections = March7thHoneyListener.GetSnapshot().OfType<Connection>()
                .Where(c => c.Player?.Uid == account.Uid).ToList();
            await System.Threading.Tasks.Task.WhenAll(connections.Select(ConnectionDisconnectHelper.KickByGmAsync));
            if (connections.Any(c => c.IsOnline))
                throw new InvalidOperationException("Session could not be stopped; retry deletion");
            database.DeleteUidData(account.Uid);
            AccountSnapshotService.DeleteUndoPoint(account.Uid);
        }
        finally
        {
            if (!DatabaseHelper.IsUidDeleted(account.Uid)) account.IsDeleting = false;
        }
    }

    /// <summary>
    ///     Called when a session of <paramref name="uid" /> ends. A test account marked delete-on-logout goes with it,
    ///     unless a tool took the save (an editor kick is not the player's logoff) or the player is logging in again.
    /// </summary>
    public static void OnSessionEnded(int uid, bool replacedByLogin)
    {
        if (replacedByLogin || OfflinePlayerSession.IsHeld(uid)) return;
        var account = AccountData.GetAccountByUid(uid);
        if (account is not { DeleteOnLogout: true } || DatabaseHelper.Instance == null) return;
        if (March7thHoneyListener.GetSnapshot().OfType<Connection>().Any(c => c.Player?.Uid == uid && c.IsOnline)) return;

        try
        {
            Claim(account);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            account.ClearLoginTokens();
            DatabaseHelper.Instance.DeleteUidData(uid);
            AccountSnapshotService.DeleteUndoPoint(uid);
            Logger.Info($"Test account {account.Username} (uid {uid}) deleted on logoff");
        }
        catch (Exception e)
        {
            Logger.Warn($"Test account uid {uid} could not be deleted on logoff", e);
        }
        finally
        {
            if (!DatabaseHelper.IsUidDeleted(uid)) account.IsDeleting = false;
        }
    }

    private static void Claim(AccountData account)
    {
        lock (account)
        {
            if (account.IsDeleting) throw new InvalidOperationException("Account deletion is already in progress");
            account.IsDeleting = true;
        }
    }
}
